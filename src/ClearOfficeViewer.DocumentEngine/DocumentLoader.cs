using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClearOfficeViewer.Core.Models;
using ClearOfficeViewer.Core.Services;

namespace ClearOfficeViewer.DocumentEngine;

public static class DocumentLoader
{
    public static ParsedDocument Load(string filePath)
    {
        var kind = FileFormatDetector.DetectFromFile(filePath);
        return kind switch
        {
            SupportedFileKind.WordLegacy => LegacyDocParser.Parse(filePath),
            SupportedFileKind.WordOpenXml => DocxParser.Parse(filePath),
            _ => Path.GetExtension(filePath).Equals(".doc", StringComparison.OrdinalIgnoreCase)
                ? LegacyDocParser.Parse(filePath)
                : DocxParser.Parse(filePath)
        };
    }

    public static FlowDocument ToFlowDocument(ParsedDocument parsed, bool forPrint = false)
    {
        var flow = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI, Calibri"),
            FontSize = 14.5,
            Foreground = new SolidColorBrush(Color.FromRgb(28, 28, 30)),
            Background = Brushes.White,
            PageWidth = parsed.PageWidthDip,
            PageHeight = parsed.PageHeightDip,
            PagePadding = new Thickness(
                parsed.MarginLeftDip,
                parsed.MarginTopDip,
                parsed.MarginRightDip,
                parsed.MarginBottomDip),
            ColumnWidth = double.PositiveInfinity
        };

        if (parsed.RawRtfBytes != null && parsed.RawRtfBytes.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(parsed.RawRtfBytes);
                var range = new TextRange(flow.ContentStart, flow.ContentEnd);
                range.Load(ms, DataFormats.Rtf);
                return flow;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn(nameof(DocumentLoader), $"RTF load fallback triggered: {ex.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(parsed.HeaderText))
        {
            flow.Blocks.Add(new Paragraph(new Run(parsed.HeaderText))
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 118)),
                Margin = new Thickness(0, 0, 0, 14),
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 4)
            });
        }

        foreach (var block in parsed.Blocks)
        {
            var wpfBlock = ConvertBlock(block, forPrint);
            if (wpfBlock != null)
                flow.Blocks.Add(wpfBlock);
        }

        if (!string.IsNullOrWhiteSpace(parsed.FooterText))
        {
            flow.Blocks.Add(new Paragraph(new Run(parsed.FooterText))
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 118)),
                Margin = new Thickness(0, 16, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 4, 0, 0)
            });
        }

        if (flow.Blocks.Count == 0)
        {
            flow.Blocks.Add(new Paragraph(new Run("(Document contains no displayable text)")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 128))
            }));
        }

        return flow;
    }

    private static Block? ConvertBlock(DocBlock block, bool forPrint)
    {
        switch (block)
        {
            case DocPageBreak:
                return forPrint
                    ? new Paragraph { BreakPageBefore = true, Margin = new Thickness(0) }
                    : new BlockUIContainer(new Border
                    {
                        Height = 2,
                        Background = new SolidColorBrush(Color.FromRgb(220, 224, 230)),
                        Margin = new Thickness(0, 14, 0, 14)
                    });

            case DocParagraph p:
                return ConvertParagraph(p);

            case DocTable t:
                return ConvertTable(t, forPrint);

            default:
                return null;
        }
    }

    private static Paragraph ConvertParagraph(DocParagraph p)
    {
        var wpfPara = new Paragraph
        {
            TextAlignment = p.Alignment switch
            {
                DocAlignment.Center => TextAlignment.Center,
                DocAlignment.Right => TextAlignment.Right,
                DocAlignment.Justify => TextAlignment.Justify,
                _ => TextAlignment.Left
            },
            Margin = new Thickness(
                p.LeftIndentDip + (p.ListKind != DocListKind.None ? 18 + p.ListLevel * 16 : 0),
                p.MarginTopDip,
                0,
                p.MarginBottomDip),
            LineHeight = double.NaN
        };

        if (p.IsTitle)
        {
            wpfPara.FontSize = 26;
            wpfPara.FontWeight = FontWeights.SemiBold;
            wpfPara.Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39));
        }
        else if (p.IsSubtitle)
        {
            wpfPara.FontSize = 16;
            wpfPara.Foreground = new SolidColorBrush(Color.FromRgb(82, 90, 102));
        }
        else if (p.HeadingLevel is >= 1 and <= 6)
        {
            wpfPara.FontSize = p.HeadingLevel switch
            {
                1 => 22,
                2 => 18,
                3 => 15.5,
                4 => 14.5,
                _ => 13.5
            };
            wpfPara.FontWeight = FontWeights.SemiBold;
            wpfPara.Foreground = p.HeadingLevel <= 2
                ? new SolidColorBrush(Color.FromRgb(15, 76, 129))
                : new SolidColorBrush(Color.FromRgb(30, 41, 59));
        }

        if (p.BackgroundHex != null && TryParseBrush(p.BackgroundHex, out var bgBrush))
        {
            wpfPara.Background = bgBrush;
            wpfPara.Padding = new Thickness(8, 6, 8, 6);
        }

        if (p.HasBottomBorder)
        {
            wpfPara.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            wpfPara.BorderThickness = new Thickness(0, 0, 0, 1);
            wpfPara.Padding = new Thickness(0, 0, 0, 4);
        }
        else if (p.HasLeftBorder)
        {
            wpfPara.BorderBrush = new SolidColorBrush(Color.FromRgb(15, 108, 189));
            wpfPara.BorderThickness = new Thickness(3, 0, 0, 0);
            wpfPara.Padding = new Thickness(10, 2, 0, 2);
        }

        if (p.ListKind == DocListKind.Bullet)
        {
            wpfPara.Inlines.Add(new Run("•  ") { FontWeight = FontWeights.Bold });
        }
        else if (p.ListKind == DocListKind.Numbered)
        {
            wpfPara.Inlines.Add(new Run($"{p.ListNumber}. ") { FontWeight = FontWeights.SemiBold });
        }

        foreach (var inline in p.Inlines)
        {
            if (inline.IsLineBreak)
            {
                wpfPara.Inlines.Add(new LineBreak());
                continue;
            }

            if (inline.ImageBytes != null)
            {
                var imgControl = BuildImageElement(inline.ImageBytes, inline.ImageWidthDip, inline.ImageHeightDip);
                if (imgControl != null)
                    wpfPara.Inlines.Add(new InlineUIContainer(imgControl));
                continue;
            }

            if (string.IsNullOrEmpty(inline.Text))
                continue;

            var run = new Run(inline.Text);
            if (inline.Bold) run.FontWeight = FontWeights.Bold;
            if (inline.Italic) run.FontStyle = FontStyles.Italic;

            var decorations = new TextDecorationCollection();
            if (inline.Underline) decorations.Add(TextDecorations.Underline);
            if (inline.Strikethrough) decorations.Add(TextDecorations.Strikethrough);
            if (decorations.Count > 0) run.TextDecorations = decorations;

            if (!string.IsNullOrWhiteSpace(inline.FontFamily))
                run.FontFamily = new FontFamily(inline.FontFamily);
            if (inline.FontSizePt is double pt && pt > 0)
                run.FontSize = pt * (96.0 / 72.0);
            if (inline.ColorHex != null && TryParseBrush(inline.ColorHex, out var fg))
                run.Foreground = fg;
            if (inline.HighlightHex != null && TryParseBrush(inline.HighlightHex, out var hl))
                run.Background = hl;
            if (inline.Superscript)
                run.BaselineAlignment = BaselineAlignment.Superscript;
            else if (inline.Subscript)
                run.BaselineAlignment = BaselineAlignment.Subscript;

            if (!string.IsNullOrWhiteSpace(inline.HyperlinkUri) && Uri.TryCreate(inline.HyperlinkUri, UriKind.Absolute, out var uri))
            {
                var link = new Hyperlink(run)
                {
                    NavigateUri = uri,
                    ToolTip = uri.ToString()
                };
                link.RequestNavigate += (_, e) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                    }
                    catch
                    {
                        // Ignore invalid system URI handler.
                    }
                    e.Handled = true;
                };
                wpfPara.Inlines.Add(link);
            }
            else
            {
                wpfPara.Inlines.Add(run);
            }
        }

        return wpfPara;
    }

    private static Table ConvertTable(DocTable t, bool forPrint)
    {
        var wpfTable = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 8, 0, 12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1, 1, 0, 0)
        };

        int maxCols = Math.Max(t.ColumnWidthsDip.Count, t.Rows.Count > 0 ? t.Rows.Max(r => r.Cells.Sum(c => c.ColumnSpan)) : 1);
        for (int c = 0; c < maxCols; c++)
        {
            var col = new TableColumn();
            if (c < t.ColumnWidthsDip.Count && t.ColumnWidthsDip[c] > 0)
                col.Width = new GridLength(t.ColumnWidthsDip[c]);
            wpfTable.Columns.Add(col);
        }

        var rowGroup = new TableRowGroup();
        foreach (var r in t.Rows)
        {
            var wpfRow = new TableRow();
            foreach (var c in r.Cells)
            {
                var wpfCell = new TableCell
                {
                    ColumnSpan = Math.Max(1, c.ColumnSpan),
                    RowSpan = Math.Max(1, c.RowSpan),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(8, 6, 8, 6)
                };

                if (c.BackgroundHex != null && TryParseBrush(c.BackgroundHex, out var cellBg))
                    wpfCell.Background = cellBg;
                else if (r.IsHeader)
                    wpfCell.Background = new SolidColorBrush(Color.FromRgb(243, 246, 250));

                foreach (var b in c.Blocks)
                {
                    var converted = ConvertBlock(b, forPrint);
                    if (converted != null)
                        wpfCell.Blocks.Add(converted);
                }

                if (wpfCell.Blocks.Count == 0)
                    wpfCell.Blocks.Add(new Paragraph { Margin = new Thickness(0) });

                wpfRow.Cells.Add(wpfCell);
            }
            rowGroup.Rows.Add(wpfRow);
        }
        wpfTable.RowGroups.Add(rowGroup);
        return wpfTable;
    }

    private static Image? BuildImageElement(byte[] bytes, double widthDip, double heightDip)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            return new Image
            {
                Source = bmp,
                Width = widthDip > 0 ? widthDip : double.NaN,
                Height = heightDip > 0 ? heightDip : double.NaN,
                MaxWidth = 650,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 4, 0, 4)
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseBrush(string hex, out SolidColorBrush brush)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c)
            {
                brush = new SolidColorBrush(c);
                brush.Freeze();
                return true;
            }
        }
        catch
        {
            // Ignore malformed color string.
        }
        brush = Brushes.Black;
        return false;
    }
}
