using System.IO;
using ClearOfficeViewer.Core.Models;

namespace ClearOfficeViewer.Tests;

public class FileFormatDetectorTests
{
    [Theory]
    [InlineData("report.pdf", SupportedFileKind.Pdf)]
    [InlineData("letter.doc", SupportedFileKind.WordLegacy)]
    [InlineData("spec.docx", SupportedFileKind.WordOpenXml)]
    [InlineData("deck.ppt", SupportedFileKind.PowerPointLegacy)]
    [InlineData("slides.pptx", SupportedFileKind.PowerPointOpenXml)]
    [InlineData("index.html", SupportedFileKind.Html)]
    [InlineData("page.htm", SupportedFileKind.Html)]
    [InlineData("archive.zip", SupportedFileKind.Unknown)]
    public void DetectByExtension_ClassifiesSupportedFormats(string fileName, SupportedFileKind expected)
    {
        Assert.Equal(expected, FileFormatDetector.DetectByExtension(fileName));
    }

    [Fact]
    public void DetectFromFile_IdentifiesPdfByMagicHeader()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"magic_{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempFile, "%PDF-1.4\n%test");
            Assert.Equal(SupportedFileKind.Pdf, FileFormatDetector.DetectFromFile(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
