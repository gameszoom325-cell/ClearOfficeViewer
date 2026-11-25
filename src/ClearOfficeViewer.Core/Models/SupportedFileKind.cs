using System.IO;

namespace ClearOfficeViewer.Core.Models;

public enum SupportedFileKind
{
    Unknown = 0,
    Pdf,
    WordLegacy,
    WordOpenXml,
    PowerPointLegacy,
    PowerPointOpenXml,
    Html
}

public static class FileFormatDetector
{
    public static readonly string[] SupportedExtensions =
    [
        ".pdf",
        ".doc",
        ".docx",
        ".ppt",
        ".pptx",
        ".html",
        ".htm"
    ];

    public const string OpenFileDialogFilter =
        "All Supported Files|*.pdf;*.doc;*.docx;*.ppt;*.pptx;*.html;*.htm|" +
        "PDF Documents (*.pdf)|*.pdf|" +
        "Word Documents (*.docx;*.doc)|*.docx;*.doc|" +
        "PowerPoint Presentations (*.pptx;*.ppt)|*.pptx;*.ppt|" +
        "Web Pages (*.html;*.htm)|*.html;*.htm|" +
        "All Files (*.*)|*.*";

    public static bool IsSupportedExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string ext = Path.GetExtension(path).ToLowerInvariant();
        return Array.Exists(SupportedExtensions, e => e == ext);
    }

    public static SupportedFileKind DetectByExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return SupportedFileKind.Unknown;

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => SupportedFileKind.Pdf,
            ".doc" => SupportedFileKind.WordLegacy,
            ".docx" => SupportedFileKind.WordOpenXml,
            ".ppt" => SupportedFileKind.PowerPointLegacy,
            ".pptx" => SupportedFileKind.PowerPointOpenXml,
            ".html" or ".htm" => SupportedFileKind.Html,
            _ => SupportedFileKind.Unknown
        };
    }

    public static SupportedFileKind DetectFromFile(string path)
    {
        var extKind = DetectByExtension(path);
        if (!File.Exists(path))
            return extKind;

        try
        {
            Span<byte> header = stackalloc byte[8];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int read = fs.Read(header);
            if (read >= 5 && header[0] == (byte)'%' && header[1] == (byte)'P' && header[2] == (byte)'D' && header[3] == (byte)'F' && header[4] == (byte)'-')
            {
                return SupportedFileKind.Pdf;
            }
        }
        catch
        {
            // Fall back to extension classification if header read is locked or fails.
        }

        return extKind;
    }

    public static string GetDisplayTypeName(SupportedFileKind kind) => kind switch
    {
        SupportedFileKind.Pdf => "PDF Document",
        SupportedFileKind.WordLegacy => "Word 97-2003 Document",
        SupportedFileKind.WordOpenXml => "Word Document",
        SupportedFileKind.PowerPointLegacy => "PowerPoint 97-2003 Presentation",
        SupportedFileKind.PowerPointOpenXml => "PowerPoint Presentation",
        SupportedFileKind.Html => "HTML Web Document",
        _ => "Document"
    };

    public static string GetShortBadge(SupportedFileKind kind) => kind switch
    {
        SupportedFileKind.Pdf => "PDF",
        SupportedFileKind.WordLegacy => "DOC",
        SupportedFileKind.WordOpenXml => "DOCX",
        SupportedFileKind.PowerPointLegacy => "PPT",
        SupportedFileKind.PowerPointOpenXml => "PPTX",
        SupportedFileKind.Html => "HTML",
        _ => "FILE"
    };
}
