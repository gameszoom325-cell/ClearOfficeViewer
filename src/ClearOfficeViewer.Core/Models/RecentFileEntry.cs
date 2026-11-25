namespace ClearOfficeViewer.Core.Models;

public sealed class RecentFileEntry
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public SupportedFileKind FileKind { get; set; }
    public DateTimeOffset LastOpenedUtc { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsFavorite { get; set; }

    public string FormatBadge => FileFormatDetector.GetShortBadge(FileKind);
    public string TypeDisplayName => FileFormatDetector.GetDisplayTypeName(FileKind);

    public string FormattedSize
    {
        get
        {
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
            return $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    public string FormattedLastOpened => LastOpenedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
