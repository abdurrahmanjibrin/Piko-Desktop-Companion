using System.IO;
using System.Security.Cryptography;
using Piko.Models;

namespace Piko.Services;

public sealed class StorageScannerService
{
    private static readonly HashSet<string> DocumentExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".txt", ".rtf", ".odt", ".csv", ".md", ".epub", ".mobi"
    };

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico", ".tiff", ".psd", ".raw"
    };

    private static readonly HashSet<string> MediaExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".flv", ".wmv", ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg"
    };

    private static readonly HashSet<string> CodeExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".js", ".ts", ".jsx", ".tsx", ".html", ".css", ".scss", ".py", ".json", ".xml", ".yaml", ".yml",
        ".sql", ".java", ".cpp", ".c", ".h", ".rs", ".go", ".sh", ".bat", ".ps1", ".php"
    };

    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".iso", ".exe", ".msi", ".dmg", ".apk"
    };

    public const long LargeFileThresholdBytes = 50 * 1024 * 1024; // 50 MB

    public static StorageCategoryType Categorize(string extension)
    {
        if (DocumentExts.Contains(extension)) return StorageCategoryType.Documents;
        if (ImageExts.Contains(extension)) return StorageCategoryType.Images;
        if (MediaExts.Contains(extension)) return StorageCategoryType.Media;
        if (CodeExts.Contains(extension)) return StorageCategoryType.Code;
        if (ArchiveExts.Contains(extension)) return StorageCategoryType.Archives;
        return StorageCategoryType.Others;
    }

    public async Task<StorageScanResult> ScanFolderAsync(string folderPath, bool recursive, IProgress<int>? progress, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            var result = new StorageScanResult
            {
                RootPath = folderPath,
                ScannedAt = DateTime.Now
            };

            if (!Directory.Exists(folderPath))
                return result;

            var dirInfo = new DirectoryInfo(folderPath);
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            IEnumerable<FileInfo> files;
            try
            {
                files = dirInfo.EnumerateFiles("*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = recursive,
                    AttributesToSkip = FileAttributes.ReparsePoint
                });
            }
            catch
            {
                files = Array.Empty<FileInfo>();
            }

            var categoryCounts = new Dictionary<StorageCategoryType, (int Count, long Bytes)>();
            foreach (var cat in Enum.GetValues<StorageCategoryType>())
            {
                categoryCounts[cat] = (0, 0);
            }

            var sizeGroups = new Dictionary<long, List<StorageFileItem>>();
            var fileList = new List<StorageFileItem>();
            var scannedCount = 0;

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    var ext = file.Extension;
                    var category = Categorize(ext);
                    var isLarge = file.Length >= LargeFileThresholdBytes;

                    var item = new StorageFileItem
                    {
                        FilePath = file.FullName,
                        FileName = file.Name,
                        DirectoryPath = file.DirectoryName ?? folderPath,
                        Extension = ext,
                        SizeBytes = file.Length,
                        LastModified = file.LastWriteTime,
                        Category = category,
                        IsLarge = isLarge
                    };

                    fileList.Add(item);
                    result.TotalFiles++;
                    result.TotalSizeBytes += file.Length;

                    var current = categoryCounts[category];
                    categoryCounts[category] = (current.Count + 1, current.Bytes + file.Length);

                    if (isLarge)
                    {
                        result.LargeFiles.Add(item);
                    }

                    if (file.Length > 1024)
                    {
                        if (!sizeGroups.TryGetValue(file.Length, out var list))
                        {
                            list = new List<StorageFileItem>();
                            sizeGroups[file.Length] = list;
                        }
                        list.Add(item);
                    }

                    scannedCount++;
                    if (scannedCount % 50 == 0)
                    {
                        progress?.Report(scannedCount);
                    }
                }
                catch
                {
                    // Skip files that cannot be accessed
                }
            }

            result.AllFiles = fileList;
            result.LargeFiles = result.LargeFiles.OrderByDescending(f => f.SizeBytes).Take(20).ToList();

            // Detect duplicates by hash comparison for files with identical sizes
            foreach (var group in sizeGroups.Where(g => g.Value.Count > 1))
            {
                token.ThrowIfCancellationRequested();

                var hashDict = new Dictionary<string, List<StorageFileItem>>();
                foreach (var item in group.Value)
                {
                    try
                    {
                        item.ContentHash = ComputeFastHash(item.FilePath);
                        if (!hashDict.TryGetValue(item.ContentHash, out var dupList))
                        {
                            dupList = new List<StorageFileItem>();
                            hashDict[item.ContentHash] = dupList;
                        }
                        dupList.Add(item);
                    }
                    catch
                    {
                        // Ignore unreadable file hash
                    }
                }

                foreach (var hashGroup in hashDict.Where(h => h.Value.Count > 1))
                {
                    result.DuplicateGroups.Add(new DuplicateGroup
                    {
                        SizeBytes = group.Key,
                        Hash = hashGroup.Key,
                        Files = hashGroup.Value
                    });
                }
            }

            result.DuplicateGroups = result.DuplicateGroups
                .OrderByDescending(d => d.SizeBytes * d.Files.Count)
                .Take(20)
                .ToList();

            // Populate category summaries
            result.Categories = new List<StorageCategorySummary>
            {
                new() { Category = StorageCategoryType.Documents, Name = "Documents", Icon = "📄", FileCount = categoryCounts[StorageCategoryType.Documents].Count, TotalBytes = categoryCounts[StorageCategoryType.Documents].Bytes, ColorHex = "#FFA8F07A" },
                new() { Category = StorageCategoryType.Images, Name = "Images", Icon = "🖼️", FileCount = categoryCounts[StorageCategoryType.Images].Count, TotalBytes = categoryCounts[StorageCategoryType.Images].Bytes, ColorHex = "#FF7AE0F0" },
                new() { Category = StorageCategoryType.Media, Name = "Media & Audio", Icon = "🎬", FileCount = categoryCounts[StorageCategoryType.Media].Count, TotalBytes = categoryCounts[StorageCategoryType.Media].Bytes, ColorHex = "#FFF07AB4" },
                new() { Category = StorageCategoryType.Code, Name = "Code & Projects", Icon = "💻", FileCount = categoryCounts[StorageCategoryType.Code].Count, TotalBytes = categoryCounts[StorageCategoryType.Code].Bytes, ColorHex = "#FFF0E67A" },
                new() { Category = StorageCategoryType.Archives, Name = "Archives & Apps", Icon = "📦", FileCount = categoryCounts[StorageCategoryType.Archives].Count, TotalBytes = categoryCounts[StorageCategoryType.Archives].Bytes, ColorHex = "#FFA77AF0" },
                new() { Category = StorageCategoryType.Others, Name = "Other Files", Icon = "📁", FileCount = categoryCounts[StorageCategoryType.Others].Count, TotalBytes = categoryCounts[StorageCategoryType.Others].Bytes, ColorHex = "#FFB0B2C3" }
            };

            return result;
        }, token);
    }

    public static (long TotalBytes, long FreeBytes, long UsedBytes) GetDriveStats(string path)
    {
        try
        {
            var driveInfo = new DriveInfo(Path.GetPathRoot(path) ?? "C:\\");
            if (driveInfo.IsReady)
            {
                var total = driveInfo.TotalSize;
                var free = driveInfo.AvailableFreeSpace;
                var used = total - free;
                return (total, free, used);
            }
        }
        catch
        {
            // Fallback
        }
        return (0, 0, 0);
    }

    private static string ComputeFastHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var buffer = new byte[65536];
        var read = stream.Read(buffer, 0, buffer.Length);
        var hash = sha.ComputeHash(buffer, 0, read);
        return Convert.ToHexString(hash);
    }
}
