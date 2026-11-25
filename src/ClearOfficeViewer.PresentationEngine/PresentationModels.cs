namespace ClearOfficeViewer.PresentationEngine;

public enum SlideShapeKind
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    Line
}

public enum SlideTextAlignment
{
    Left,
    Center,
    Right,
    Justify
}

public sealed class ParsedPresentation
{
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SourceFormatLabel { get; set; } = "PPTX";
    public double SlideWidthDip { get; set; } = 960;
    public double SlideHeightDip { get; set; } = 540;
    public List<ParsedSlide> Slides { get; } = [];
}

public sealed class ParsedSlide
{
    public int SlideNumber { get; set; }
    public string BackgroundHex { get; set; } = "#FFFFFF";
    public byte[]? BackgroundImageBytes { get; set; }
    public string? NotesText { get; set; }
    public List<SlideElement> Elements { get; } = [];

    public string SummaryTitle
    {
        get
        {
            foreach (var el in Elements.OfType<SlideTextShapeElement>())
            {
                string firstLine = string.Concat(el.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text)).Trim();
                if (!string.IsNullOrWhiteSpace(firstLine))
                    return firstLine.Length > 48 ? firstLine[..45] + "..." : firstLine;
            }
            return $"Slide {SlideNumber}";
        }
    }
}

public abstract class SlideElement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class SlideTextShapeElement : SlideElement
{
    public SlideShapeKind ShapeKind { get; set; } = SlideShapeKind.Rectangle;
    public string? FillHex { get; set; }
    public string? StrokeHex { get; set; }
    public double StrokeThicknessDip { get; set; }
    public List<SlideParagraph> Paragraphs { get; } = [];
}

public sealed class SlideImageElement : SlideElement
{
    public byte[] ImageBytes { get; set; } = [];
}

public sealed class SlideTableElement : SlideElement
{
    public List<double> ColumnWidthsDip { get; } = [];
    public List<SlideTableRow> Rows { get; } = [];
}

public sealed class SlideTableRow
{
    public double HeightDip { get; set; }
    public List<SlideTableCell> Cells { get; } = [];
}

public sealed class SlideTableCell
{
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public string? FillHex { get; set; }
    public List<SlideParagraph> Paragraphs { get; } = [];
}

public sealed class SlideParagraph
{
    public SlideTextAlignment Alignment { get; set; } = SlideTextAlignment.Left;
    public string? BulletPrefix { get; set; }
    public int Level { get; set; }
    public List<SlideTextRun> Runs { get; } = [];
}

public sealed class SlideTextRun
{
    public string Text { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string? FontFamily { get; set; }
    public double FontSizePt { get; set; } = 18;
    public string ColorHex { get; set; } = "#1F2937";
}
