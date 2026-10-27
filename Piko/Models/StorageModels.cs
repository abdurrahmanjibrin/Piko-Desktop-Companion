using System.IO;

namespace Piko.Models;

public enum StorageCategoryType
{
    Documents,
    Images,
    Media,
    Code,
    Archives,
    Others
}

public sealed class StorageFileItem
{
    public required string FilePath { get; set; }
    public required string FileName { get; set; }
    public required string DirectoryPath { get; set; }
    public required string Extension { get; set; }
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public StorageCategoryType Category { get; set; }
    public bool IsLarge { get; set; }
    public string? ContentHash { get; set; }

    public string FormattedSize => FormatBytes(SizeBytes);
    public string FormattedDate => LastModified.ToString("dd MMM yyyy, HH:mm");

    public static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
            if (counter == suffixes.Length - 1) break;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

public sealed class StorageCategorySummary
{
    public StorageCategoryType Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public string ColorHex { get; set; } = "#FFA8F07A";
    public string FormattedSize => StorageFileItem.FormatBytes(TotalBytes);
}

public sealed class DuplicateGroup
{
    public long SizeBytes { get; set; }
    public string FormattedSize => StorageFileItem.FormatBytes(SizeBytes);
    public string? Hash { get; set; }
    public List<StorageFileItem> Files { get; set; } = new();
}

public sealed class StorageScanResult
{
    public string RootPath { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public string FormattedTotalSize => StorageFileItem.FormatBytes(TotalSizeBytes);
    public DateTime ScannedAt { get; set; } = DateTime.Now;
    public List<StorageCategorySummary> Categories { get; set; } = new();
    public List<StorageFileItem> LargeFiles { get; set; } = new();
    public List<DuplicateGroup> DuplicateGroups { get; set; } = new();
    public List<StorageFileItem> AllFiles { get; set; } = new();
}

public enum PlanItemStatus
{
    Pending,
    Approved,
    Moved,
    Skipped,
    Failed
}

public sealed class FileOrganizePlanItem
{
    public required string SourcePath { get; set; }
    public required string DestinationPath { get; set; }
    public string FileName => Path.GetFileName(SourcePath);
    public string TargetFolder => Path.GetDirectoryName(DestinationPath) ?? string.Empty;
    public StorageCategoryType Category { get; set; }
    public string Reason { get; set; } = string.Empty;
    public PlanItemStatus Status { get; set; } = PlanItemStatus.Pending;
    public string? ErrorMessage { get; set; }
    public long SizeBytes { get; set; }
    public string FormattedSize => StorageFileItem.FormatBytes(SizeBytes);
    public bool IsSelected { get; set; } = true;
}

public sealed class OperationJournalEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public bool WasCreatedDirectory { get; set; }
}

public sealed class OperationJournal
{
    public Guid BatchId { get; set; } = Guid.NewGuid();
    public DateTime ExecutedAt { get; set; } = DateTime.Now;
    public string Description { get; set; } = string.Empty;
    public List<OperationJournalEntry> Entries { get; set; } = new();
}
