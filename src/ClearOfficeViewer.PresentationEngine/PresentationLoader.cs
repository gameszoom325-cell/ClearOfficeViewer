using System.IO;
using ClearOfficeViewer.Core.Models;

namespace ClearOfficeViewer.PresentationEngine;

public static class PresentationLoader
{
    public static ParsedPresentation Load(string filePath)
    {
        var kind = FileFormatDetector.DetectFromFile(filePath);
        return kind switch
        {
            SupportedFileKind.PowerPointLegacy => LegacyPptParser.Parse(filePath),
            SupportedFileKind.PowerPointOpenXml => PptxParser.Parse(filePath),
            _ => Path.GetExtension(filePath).Equals(".ppt", StringComparison.OrdinalIgnoreCase)
                ? LegacyPptParser.Parse(filePath)
                : PptxParser.Parse(filePath)
        };
    }
}
