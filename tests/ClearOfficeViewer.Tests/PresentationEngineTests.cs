using System.Buffers.Binary;
using System.IO;
using System.Text;
using ClearOfficeViewer.PresentationEngine;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using OpenMcdf;
using A = DocumentFormat.OpenXml.Drawing;

namespace ClearOfficeViewer.Tests;

public class PresentationEngineTests
{
    private static readonly byte[] SamplePng1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public void PptxParser_PreservesSlideOrderTextImagesAndNotes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"deck_{Guid.NewGuid():N}.pptx");
        try
        {
            CreateSamplePptx(path);
            var pres = PresentationLoader.Load(path);

            Assert.Equal("PPTX", pres.SourceFormatLabel);
            Assert.Equal(2, pres.Slides.Count);
            Assert.Equal(1, pres.Slides[0].SlideNumber);
            Assert.Equal(2, pres.Slides[1].SlideNumber);
            Assert.Contains("Annual Product Roadmap", pres.Slides[0].SummaryTitle);
            Assert.Contains(pres.Slides[1].Elements.OfType<SlideImageElement>(), img => img.ImageBytes.Length > 0);
            Assert.Contains("Explain Q3 milestones", pres.Slides[1].NotesText ?? string.Empty);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LegacyPptParser_ParsesOle2BinaryPptSlides()
    {
        string path = Path.Combine(Path.GetTempPath(), $"legacy_{Guid.NewGuid():N}.ppt");
        try
        {
            CreateSampleOle2Ppt(path);
            var pres = PresentationLoader.Load(path);

            Assert.Equal("PPT", pres.SourceFormatLabel);
            Assert.Equal(2, pres.Slides.Count);
            Assert.Contains("Welcome Slide", pres.Slides[0].SummaryTitle);
            Assert.Contains("Architecture Summary", pres.Slides[1].SummaryTitle);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void CreateSamplePptx(string path)
    {
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presPart = doc.AddPresentationPart();
        presPart.Presentation = new Presentation(
            new SlideIdList(),
            new SlideSize { Cx = 12192000, Cy = 6858000 });

        AddSlide(presPart, 256, "Annual Product Roadmap", "2026 Engineering Goals", null, includeImage: false);
        AddSlide(presPart, 257, "Architecture Highlights", "Fast native rendering", "Explain Q3 milestones during demo.", includeImage: true);
    }

    private static void AddSlide(PresentationPart presPart, uint slideId, string title, string body, string? notes, bool includeImage)
    {
        var slidePart = presPart.AddNewPart<SlidePart>();
        var spTree = new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1, Name = "" },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new A.TransformGroup()));

        spTree.AppendChild(CreateTextBox(2, "Title", 600000, 400000, 10000000, 1100000, title, 3200));
        spTree.AppendChild(CreateTextBox(3, "Body", 600000, 1800000, 10000000, 3600000, body, 2000));

        if (includeImage)
        {
            var imgPart = slidePart.AddImagePart(ImagePartType.Png);
            using (var ms = new MemoryStream(SamplePng1x1)) imgPart.FeedData(ms);
            string relId = slidePart.GetIdOfPart(imgPart);
            spTree.AppendChild(new Picture(
                new NonVisualPictureProperties(
                    new NonVisualDrawingProperties { Id = 4, Name = "Img" },
                    new NonVisualPictureDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new BlipFill(new A.Blip { Embed = relId }, new A.Stretch(new A.FillRectangle())),
                new ShapeProperties(new A.Transform2D(
                    new A.Offset { X = 7000000, Y = 2200000 },
                    new A.Extents { Cx = 1800000, Cy = 1800000 }))));
        }

        slidePart.Slide = new Slide(new CommonSlideData(spTree));

        if (!string.IsNullOrEmpty(notes))
        {
            var notesPart = slidePart.AddNewPart<NotesSlidePart>();
            notesPart.NotesSlide = new NotesSlide(
                new CommonSlideData(
                    new ShapeTree(
                        new NonVisualGroupShapeProperties(
                            new NonVisualDrawingProperties { Id = 1, Name = "" },
                            new NonVisualGroupShapeDrawingProperties(),
                            new ApplicationNonVisualDrawingProperties()),
                        new GroupShapeProperties(new A.TransformGroup()),
                        CreateTextBox(2, "Notes", 0, 0, 1000000, 1000000, notes, 1200))));
        }

        string id = presPart.GetIdOfPart(slidePart);
        presPart.Presentation.SlideIdList!.AppendChild(new SlideId { Id = slideId, RelationshipId = id });
    }

    private static Shape CreateTextBox(uint id, string name, long x, long y, long cx, long cy, string text, int fontSize) =>
        new(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = name },
                new NonVisualShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new ShapeProperties(
                new A.Transform2D(new A.Offset { X = x, Y = y }, new A.Extents { Cx = cx, Cy = cy }),
                new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle }),
            new TextBody(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.Run(new A.RunProperties { FontSize = fontSize }, new A.Text(text)))));

    private static void CreateSampleOle2Ppt(string path)
    {
        static byte[] MakeRecord(ushort ver, ushort inst, ushort type, byte[] payload)
        {
            byte[] buf = new byte[8 + payload.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0, 2), (ushort)((inst << 4) | (ver & 0x0F)));
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(2, 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), (uint)payload.Length);
            payload.CopyTo(buf, 8);
            return buf;
        }

        byte[] docAtomPayload = new byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(docAtomPayload.AsSpan(0, 4), 5760);
        BinaryPrimitives.WriteInt32LittleEndian(docAtomPayload.AsSpan(4, 4), 3240);
        byte[] docAtom = MakeRecord(0x1, 0, 0x03E9, docAtomPayload);

        byte[] persist = new byte[20];
        byte[] s1 = [
            .. MakeRecord(0x0, 0, 0x03F3, persist),
            .. MakeRecord(0x0, 0, 0x0F9F, BitConverter.GetBytes(0)),
            .. MakeRecord(0x0, 0, 0x0FA0, Encoding.Unicode.GetBytes("Welcome Slide")),
            .. MakeRecord(0x0, 0, 0x03F3, persist),
            .. MakeRecord(0x0, 0, 0x0F9F, BitConverter.GetBytes(0)),
            .. MakeRecord(0x0, 0, 0x0FA0, Encoding.Unicode.GetBytes("Architecture Summary"))
        ];

        byte[] slwt = MakeRecord(0xF, 0, 0x0FF0, s1);
        byte[] docRec = MakeRecord(0xF, 0, 0x03E8, [.. docAtom, .. slwt]);

        using var root = RootStorage.Create(path);
        using (var s = root.CreateStream("PowerPoint Document"))
            s.Write(docRec, 0, docRec.Length);
    }
}
