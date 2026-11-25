using System.Buffers.Binary;
using System.IO;
using System.Text;
using ClearOfficeViewer.DocumentEngine;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OpenMcdf;

namespace ClearOfficeViewer.Tests;

public class DocumentEngineTests
{
    private static readonly byte[] SamplePng1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public void DocxParser_ParsesHeadingsParagraphsTablesAndImages()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.docx");
        try
        {
            CreateSampleDocx(path);
            var parsed = DocumentLoader.Load(path);

            Assert.Equal("DOCX", parsed.SourceFormatLabel);
            Assert.True(parsed.Blocks.Count >= 3);
            Assert.Contains(parsed.Blocks.OfType<DocParagraph>(), p => p.HeadingLevel == 1);
            Assert.Contains(parsed.Blocks.OfType<DocTable>(), t => t.Rows.Count == 2 && t.Rows[0].Cells.Count == 2);
            Assert.Contains(parsed.Blocks.OfType<DocParagraph>().SelectMany(p => p.Inlines), i => i.ImageBytes is { Length: > 0 });
            Assert.True(parsed.WordCount >= 6);

            Exception? ex = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var flow = DocumentLoader.ToFlowDocument(parsed);
                    Assert.True(flow.Blocks.Count >= 3);
                }
                catch (Exception e)
                {
                    ex = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(ex);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LegacyDocParser_ParsesRtfAndOle2BinaryDoc()
    {
        string rtfPath = Path.Combine(Path.GetTempPath(), $"rtf_{Guid.NewGuid():N}.doc");
        string olePath = Path.Combine(Path.GetTempPath(), $"ole_{Guid.NewGuid():N}.doc");
        try
        {
            File.WriteAllText(rtfPath, @"{\rtf1\ansi {\b Legacy RTF Document}\par Body text line.}");
            var rtfDoc = DocumentLoader.Load(rtfPath);
            Assert.NotNull(rtfDoc.RawRtfBytes);

            CreateSampleOle2Doc(olePath, "Quarterly Summary\rRevenue increased by 18 percent.\r");
            var oleDoc = DocumentLoader.Load(olePath);
            string fullText = string.Join("\n", oleDoc.Blocks.OfType<DocParagraph>().SelectMany(p => p.Inlines).Select(i => i.Text));
            Assert.Contains("Quarterly Summary", fullText);
            Assert.Contains("Revenue increased by 18 percent.", fullText);
        }
        finally
        {
            if (File.Exists(rtfPath)) File.Delete(rtfPath);
            if (File.Exists(olePath)) File.Delete(olePath);
        }
    }

    private static void CreateSampleDocx(string path)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new Style(new StyleName { Val = "heading 1" }) { Type = StyleValues.Paragraph, StyleId = "Heading1" });

        var body = mainPart.Document.Body!;
        body.AppendChild(new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
            new Run(new Text("Executive Overview"))));

        body.AppendChild(new Paragraph(
            new Run(new RunProperties(new Bold()), new Text("ClearOffice")),
            new Run(new Text(" renders modern Word documents cleanly."))));

        var tbl = new Table(
            new TableRow(
                new TableCell(new Paragraph(new Run(new Text("Metric")))),
                new TableCell(new Paragraph(new Run(new Text("Value"))))),
            new TableRow(
                new TableCell(new Paragraph(new Run(new Text("Latency")))),
                new TableCell(new Paragraph(new Run(new Text("12 ms"))))));
        body.AppendChild(tbl);

        var imgPart = mainPart.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(SamplePng1x1))
            imgPart.FeedData(ms);
        string relId = mainPart.GetIdOfPart(imgPart);

        var drawing = new Drawing(
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent { Cx = 914400, Cy = 914400 },
                new DocumentFormat.OpenXml.Drawing.Graphic(
                    new DocumentFormat.OpenXml.Drawing.GraphicData(
                        new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                            new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                                new DocumentFormat.OpenXml.Drawing.Blip { Embed = relId })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })));
        body.AppendChild(new Paragraph(new Run(drawing)));
    }

    private static void CreateSampleOle2Doc(string path, string unicodeText)
    {
        byte[] wordStream = new byte[4096];
        BinaryPrimitives.WriteUInt16LittleEndian(wordStream.AsSpan(0, 2), 0xA5EC); // wIdent
        BinaryPrimitives.WriteUInt16LittleEndian(wordStream.AsSpan(10, 2), 0x0200); // fWhichTblStm = 1Table
        BinaryPrimitives.WriteInt32LittleEndian(wordStream.AsSpan(76, 4), unicodeText.Length); // ccpText

        int textOffset = 1024;
        byte[] textBytes = Encoding.Unicode.GetBytes(unicodeText);
        Array.Copy(textBytes, 0, wordStream, textOffset, textBytes.Length);

        // Build Clx with PlcPcd in 1Table
        // Clx: 0x02 (Pcdt), uint32 lcbPcdt = 16 (2 CPs * 4 + 1 PCD * 8), CP[0]=0, CP[1]=len, PCD(8 bytes with fc=textOffset)
        byte[] tableStream = new byte[64];
        tableStream[0] = 0x02;
        BinaryPrimitives.WriteInt32LittleEndian(tableStream.AsSpan(1, 4), 16);
        BinaryPrimitives.WriteInt32LittleEndian(tableStream.AsSpan(5, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(tableStream.AsSpan(9, 4), unicodeText.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(tableStream.AsSpan(13 + 2, 4), (uint)textOffset);

        BinaryPrimitives.WriteUInt32LittleEndian(wordStream.AsSpan(418, 4), 0); // fcClx
        BinaryPrimitives.WriteUInt32LittleEndian(wordStream.AsSpan(422, 4), 21); // lcbClx

        using var root = RootStorage.Create(path);
        using (var ws = root.CreateStream("WordDocument"))
            ws.Write(wordStream, 0, wordStream.Length);
        using (var ts = root.CreateStream("1Table"))
            ts.Write(tableStream, 0, tableStream.Length);
    }
}
