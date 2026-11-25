namespace ClearOfficeViewer.DocumentEngine;

public enum DocAlignment
{
    Left,
    Center,
    Right,
    Justify
}

public enum DocListKind
{
    None,
    Bullet,
    Numbered
}

public sealed class ParsedDocument
{
    public string FilePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SourceFormatLabel { get; set; } = "DOCX";
    public double PageWidthDip { get; set; } = 816;   // 8.5 in * 96
    public double PageHeightDip { get; set; } = 1056; // 11 in * 96
    public double MarginLeftDip { get; set; } = 72;
    public double MarginTopDip { get; set; } = 72;
    public double MarginRightDip { get; set; } = 72;
    public double MarginBottomDip { get; set; } = 72;
    public string? HeaderText { get; set; }
    public string? FooterText { get; set; }
    public byte[]? RawRtfBytes { get; set; }
    public List<DocBlock> Blocks { get; } = [];

    public int WordCount
    {
        get
        {
            int count = 0;
            foreach (var block in Blocks)
            {
                count += CountBlockWords(block);
            }
            return count;
        }
    }

    private static int CountBlockWords(DocBlock block)
    {
        if (block is DocParagraph p)
        {
            int w = 0;
            foreach (var inline in p.Inlines)
            {
                if (!string.IsNullOrWhiteSpace(inline.Text))
                    w += inline.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            }
            return w;
        }
        if (block is DocTable t)
        {
            int w = 0;
            foreach (var row in t.Rows)
                foreach (var cell in row.Cells)
                    foreach (var b in cell.Blocks)
                        w += CountBlockWords(b);
            return w;
        }
        return 0;
    }
}

public abstract class DocBlock;

public sealed class DocPageBreak : DocBlock;

public sealed class DocParagraph : DocBlock
{
    public int HeadingLevel { get; set; } // 0 = normal, 1..6 = H1..H6
    public bool IsTitle { get; set; }
    public bool IsSubtitle { get; set; }
    public DocAlignment Alignment { get; set; } = DocAlignment.Left;
    public DocListKind ListKind { get; set; } = DocListKind.None;
    public int ListLevel { get; set; }
    public int ListNumber { get; set; } = 1;
    public double MarginTopDip { get; set; }
    public double MarginBottomDip { get; set; } = 8;
    public double LeftIndentDip { get; set; }
    public string? BackgroundHex { get; set; }
    public bool HasBottomBorder { get; set; }
    public bool HasLeftBorder { get; set; }
    public List<DocInline> Inlines { get; } = [];
}

public sealed class DocInline
{
    public string Text { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strikethrough { get; set; }
    public bool Superscript { get; set; }
    public bool Subscript { get; set; }
    public string? FontFamily { get; set; }
    public double? FontSizePt { get; set; }
    public string? ColorHex { get; set; }
    public string? HighlightHex { get; set; }
    public string? HyperlinkUri { get; set; }
    public bool IsLineBreak { get; set; }
    public byte[]? ImageBytes { get; set; }
    public double ImageWidthDip { get; set; }
    public double ImageHeightDip { get; set; }
}

public sealed class DocTable : DocBlock
{
    public List<double> ColumnWidthsDip { get; } = [];
    public List<DocTableRow> Rows { get; } = [];
    public bool HasBorders { get; set; } = true;
}

public sealed class DocTableRow
{
    public bool IsHeader { get; set; }
    public List<DocTableCell> Cells { get; } = [];
}

public sealed class DocTableCell
{
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;
    public string? BackgroundHex { get; set; }
    public double WidthDip { get; set; }
    public List<DocBlock> Blocks { get; } = [];
}
