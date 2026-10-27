using System.IO;
using Piko.Models;

namespace Piko.Services;

public interface IStorageClassifier
{
    string ProviderName { get; }
    Task<string> SuggestSubfolderAsync(StorageFileItem item, CancellationToken token = default);
}

public sealed class RuleBasedStorageClassifier : IStorageClassifier
{
    public string ProviderName => "Rule-Based Engine (Deterministic)";

    public Task<string> SuggestSubfolderAsync(StorageFileItem item, CancellationToken token = default)
    {
        var ext = item.Extension.ToLowerInvariant();
        var name = item.FileName.ToLowerInvariant();

        string folder = item.Category switch
        {
            StorageCategoryType.Documents => ext switch
            {
                ".pdf" => "Documents/PDFs",
                ".docx" or ".doc" or ".odt" => "Documents/Word",
                ".xlsx" or ".xls" or ".csv" => "Documents/Spreadsheets",
                ".pptx" or ".ppt" => "Documents/Presentations",
                _ => "Documents/Notes"
            },
            StorageCategoryType.Images => name.Contains("screenshot") || name.Contains("tangkapan")
                ? "Images/Screenshots"
                : "Images/Photos",
            StorageCategoryType.Media => ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a"
                ? "Media/Audio"
                : "Media/Videos",
            StorageCategoryType.Code => "Projects & Code",
            StorageCategoryType.Archives => ext is ".exe" or ".msi"
                ? "Installers"
                : "Archives",
            _ => "Unsorted"
        };

        return Task.FromResult(folder);
    }
}

public sealed class LocalLlmStorageClassifier : IStorageClassifier
{
    public string ProviderName => "Local AI / Ollama (Optional)";

    // Fallback to rule engine if local model is not running
    private readonly RuleBasedStorageClassifier _fallback = new();

    public async Task<string> SuggestSubfolderAsync(StorageFileItem item, CancellationToken token = default)
    {
        // Future extension point for local Ollama / ONNX model inference
        return await _fallback.SuggestSubfolderAsync(item, token);
    }
}

public sealed class StorageOrganizerService
{
    private readonly IStorageClassifier _classifier;

    public StorageOrganizerService(IStorageClassifier? classifier = null)
    {
        _classifier = classifier ?? new RuleBasedStorageClassifier();
    }

    public async Task<List<FileOrganizePlanItem>> GeneratePlanAsync(
        StorageScanResult scanResult,
        string targetBaseFolder,
        CancellationToken token = default)
    {
        var plan = new List<FileOrganizePlanItem>();

        foreach (var file in scanResult.AllFiles)
        {
            token.ThrowIfCancellationRequested();

            var subfolder = await _classifier.SuggestSubfolderAsync(file, token);
            var destDir = Path.Combine(targetBaseFolder, subfolder);
            var destPath = Path.Combine(destDir, file.FileName);

            // Avoid moving if file is already in the target folder
            if (string.Equals(file.FilePath, destPath, StringComparison.OrdinalIgnoreCase))
                continue;

            plan.Add(new FileOrganizePlanItem
            {
                SourcePath = file.FilePath,
                DestinationPath = destPath,
                Category = file.Category,
                SizeBytes = file.SizeBytes,
                Reason = $"Move to {subfolder}",
                Status = PlanItemStatus.Pending,
                IsSelected = true
            });
        }

        return plan;
    }

    public async Task<OperationJournal> ExecutePlanAsync(
        IEnumerable<FileOrganizePlanItem> items,
        IProgress<(int Current, int Total)>? progress,
        CancellationToken token = default)
    {
        var selected = items.Where(i => i.IsSelected && i.Status == PlanItemStatus.Pending).ToList();
        var journal = new OperationJournal
        {
            Description = $"Organized {selected.Count} file(s)",
            ExecutedAt = DateTime.Now
        };

        var total = selected.Count;
        var current = 0;

        await Task.Run(() =>
        {
            foreach (var item in selected)
            {
                token.ThrowIfCancellationRequested();
                current++;
                progress?.Report((current, total));

                try
                {
                    if (!File.Exists(item.SourcePath))
                    {
                        item.Status = PlanItemStatus.Skipped;
                        item.ErrorMessage = "Source file no longer exists";
                        continue;
                    }

                    var destDir = Path.GetDirectoryName(item.DestinationPath)!;
                    var wasCreated = false;
                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                        wasCreated = true;
                    }

                    // Collision safe: resolve destination name collisions
                    var finalDest = item.DestinationPath;
                    if (File.Exists(finalDest))
                    {
                        var dir = Path.GetDirectoryName(finalDest)!;
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(finalDest);
                        var ext = Path.GetExtension(finalDest);
                        var copyIndex = 1;

                        do
                        {
                            finalDest = Path.Combine(dir, $"{nameWithoutExt}_piko{copyIndex}{ext}");
                            copyIndex++;
                        } while (File.Exists(finalDest));
                    }

                    File.Move(item.SourcePath, finalDest);

                    item.DestinationPath = finalDest;
                    item.Status = PlanItemStatus.Moved;

                    journal.Entries.Add(new OperationJournalEntry
                    {
                        SourcePath = item.SourcePath,
                        DestinationPath = finalDest,
                        WasCreatedDirectory = wasCreated
                    });
                }
                catch (Exception ex)
                {
                    item.Status = PlanItemStatus.Failed;
                    item.ErrorMessage = ex.Message;
                }
            }
        }, token);

        return journal;
    }

    public async Task<(int Reverted, int Failed)> UndoJournalAsync(OperationJournal journal, CancellationToken token = default)
    {
        var reverted = 0;
        var failed = 0;

        await Task.Run(() =>
        {
            // Reverse order
            for (int i = journal.Entries.Count - 1; i >= 0; i--)
            {
                token.ThrowIfCancellationRequested();
                var entry = journal.Entries[i];

                try
                {
                    if (File.Exists(entry.DestinationPath))
                    {
                        var originalDir = Path.GetDirectoryName(entry.SourcePath)!;
                        if (!Directory.Exists(originalDir))
                        {
                            Directory.CreateDirectory(originalDir);
                        }

                        // Ensure source path is free
                        var target = entry.SourcePath;
                        if (File.Exists(target))
                        {
                            var dir = Path.GetDirectoryName(target)!;
                            var nameWithoutExt = Path.GetFileNameWithoutExtension(target);
                            var ext = Path.GetExtension(target);
                            target = Path.Combine(dir, $"{nameWithoutExt}_restored{ext}");
                        }

                        File.Move(entry.DestinationPath, target);
                        reverted++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch
                {
                    failed++;
                }
            }
        }, token);

        return (reverted, failed);
    }
}
