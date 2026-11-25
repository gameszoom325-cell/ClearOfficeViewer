using System.IO;
using ClearOfficeViewer.Core.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace ClearOfficeViewer.DocumentEngine;

public static class DocxParser
{
    private const double TwipsToDip = 96.0 / 1440.0;
    private const double EmuToDip = 96.0 / 914400.0;

    public static ParsedDocument Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("DOCX file not found.", filePath);

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var wordDoc = WordprocessingDocument.Open(fs, false);

        var result = new ParsedDocument
        {
            FilePath = Path.GetFullPath(filePath),
            Title = Path.GetFileNameWithoutExtension(filePath),
            SourceFormatLabel = "DOCX"
        };

        var mainPart = wordDoc.MainDocumentPart;
        if (mainPart?.Document?.Body == null)
            return result;

        var styles = BuildStyleLookup(mainPart);
        var numberingLookup = BuildNumberingLookup(mainPart);
        var listCounters = new Dictionary<(int numId, int level), int>();

        // Extract section page metrics
        var sectPr = mainPart.Document.Body.Elements<SectionProperties>().LastOrDefault()
                     ?? mainPart.Document.Body.Descendants<SectionProperties>().LastOrDefault();
        if (sectPr != null)
        {
            var pgSz = sectPr.GetFirstChild<PageSize>();
            if (pgSz?.Width?.Value is uint w && w > 0)
                result.PageWidthDip = w * TwipsToDip;
            if (pgSz?.Height?.Value is uint h && h > 0)
                result.PageHeightDip = h * TwipsToDip;

            var pgMar = sectPr.GetFirstChild<PageMargin>();
            if (pgMar != null)
            {
                if (pgMar.Left?.Value is uint ml) result.MarginLeftDip = ml * TwipsToDip;
                if (pgMar.Right?.Value is uint mr) result.MarginRightDip = mr * TwipsToDip;
                if (pgMar.Top?.Value is int mt && mt >= 0) result.MarginTopDip = mt * TwipsToDip;
                if (pgMar.Bottom?.Value is int mb && mb >= 0) result.MarginBottomDip = mb * TwipsToDip;
            }
        }

        // Extract primary header/footer preview text
        result.HeaderText = ExtractHeaderFooterText(mainPart.HeaderParts.Select(hp => hp.Header?.InnerText));
        result.FooterText = ExtractHeaderFooterText(mainPart.FooterParts.Select(fp => fp.Footer?.InnerText));

        foreach (var child in mainPart.Document.Body.ChildElements)
        {
            switch (child)
            {
                case Paragraph p:
                    ParseParagraphIntoBlocks(p, mainPart, styles, numberingLookup, listCounters, result.Blocks);
                    break;
                case Table t:
                    result.Blocks.Add(ParseTable(t, mainPart, styles, numberingLookup, listCounters));
                    break;
            }
        }

        return result;
    }

    private static string? ExtractHeaderFooterText(IEnumerable<string?> candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c))
                return c.Trim();
        }
        return null;
    }

    private static void ParseParagraphIntoBlocks(
        Paragraph p,
        MainDocumentPart mainPart,
        Dictionary<string, StyleInfo> styles,
        Dictionary<(int numId, int level), DocListKind> numberingLookup,
        Dictionary<(int numId, int level), int> listCounters,
        List<DocBlock> targetBlocks)
    {
        // Handle explicit page break at paragraph start
        bool hasPageBreakBefore = p.ParagraphProperties?.PageBreakBefore != null;
        if (hasPageBreakBefore && targetBlocks.Count > 0)
            targetBlocks.Add(new DocPageBreak());

        var docPara = new DocParagraph();
        var pPr = p.ParagraphProperties;
        string? styleId = pPr?.ParagraphStyleId?.Val?.Value;

        var resolvedStyle = ResolveStyle(styleId, styles);
        if (resolvedStyle != null)
        {
            docPara.HeadingLevel = resolvedStyle.HeadingLevel;
            docPara.IsTitle = resolvedStyle.IsTitle;
            docPara.IsSubtitle = resolvedStyle.IsSubtitle;
            if (resolvedStyle.Alignment.HasValue)
                docPara.Alignment = resolvedStyle.Alignment.Value;
        }

        if (pPr?.Justification?.Val?.Value is JustificationValues jv)
        {
            if (jv == JustificationValues.Center) docPara.Alignment = DocAlignment.Center;
            else if (jv == JustificationValues.Right) docPara.Alignment = DocAlignment.Right;
            else if (jv == JustificationValues.Both) docPara.Alignment = DocAlignment.Justify;
            else docPara.Alignment = DocAlignment.Left;
        }

        if (pPr?.SpacingBetweenLines is SpacingBetweenLines sp)
        {
            if (double.TryParse(sp.Before?.Value, out double before))
                docPara.MarginTopDip = before * TwipsToDip;
            if (double.TryParse(sp.After?.Value, out double after))
                docPara.MarginBottomDip = after * TwipsToDip;
        }
        else if (docPara.HeadingLevel > 0)
        {
            docPara.MarginTopDip = docPara.HeadingLevel == 1 ? 14 : 10;
            docPara.MarginBottomDip = 4;
        }

        if (pPr?.Indentation?.Left?.Value is string leftStr && double.TryParse(leftStr, out double leftTwips))
            docPara.LeftIndentDip = Math.Max(0, leftTwips * TwipsToDip);

        if (pPr?.Shading?.Fill?.Value is string shdFill && IsValidHexColor(shdFill))
            docPara.BackgroundHex = "#" + shdFill;

        if (pPr?.ParagraphBorders is ParagraphBorders pb)
        {
            docPara.HasBottomBorder = pb.BottomBorder != null && pb.BottomBorder.Val?.Value != BorderValues.None;
            docPara.HasLeftBorder = pb.LeftBorder != null && pb.LeftBorder.Val?.Value != BorderValues.None;
        }

        // Numbering / bullets
        var numPr = pPr?.NumberingProperties;
        if (numPr != null)
        {
            int numId = numPr.NumberingId?.Val?.Value ?? 0;
            int lvl = numPr.NumberingLevelReference?.Val?.Value ?? 0;
            if (numId > 0)
            {
                docPara.ListLevel = lvl;
                docPara.ListKind = numberingLookup.TryGetValue((numId, lvl), out var lk)
                    ? lk
                    : DocListKind.Bullet;

                if (docPara.ListKind == DocListKind.Numbered)
                {
                    var key = (numId, lvl);
                    int next = listCounters.TryGetValue(key, out int cur) ? cur + 1 : 1;
                    listCounters[key] = next;
                    docPara.ListNumber = next;
                }
            }
        }

        // Collect runs & hyperlinks
        foreach (var element in p.ChildElements)
        {
            switch (element)
            {
                case Run r:
                    AppendRunInlines(r, mainPart, resolvedStyle, hyperlinkUri: null, docPara, targetBlocks);
                    break;
                case Hyperlink hl:
                {
                    string? uri = null;
                    if (!string.IsNullOrEmpty(hl.Id?.Value))
                    {
                        var rel = mainPart.HyperlinkRelationships.FirstOrDefault(x => x.Id == hl.Id.Value);
                        uri = rel?.Uri?.ToString();
                    }
                    foreach (var r in hl.Elements<Run>())
                    {
                        AppendRunInlines(r, mainPart, resolvedStyle, uri, docPara, targetBlocks);
                    }
                    break;
                }
            }
        }

        targetBlocks.Add(docPara);
    }

    private static void AppendRunInlines(
        Run r,
        MainDocumentPart mainPart,
        StyleInfo? paraStyle,
        string? hyperlinkUri,
        DocParagraph currentPara,
        List<DocBlock> targetBlocks)
    {
        var rPr = r.RunProperties;
        bool bold = IsToggleOn(rPr?.Bold) || (rPr?.Bold == null && (paraStyle?.Bold ?? false));
        bool italic = IsToggleOn(rPr?.Italic) || (rPr?.Italic == null && (paraStyle?.Italic ?? false));
        bool underline = (rPr?.Underline != null && rPr.Underline.Val?.Value != UnderlineValues.None) || hyperlinkUri != null;
        bool strike = IsToggleOn(rPr?.Strike) || IsToggleOn(rPr?.DoubleStrike);

        bool sup = rPr?.VerticalTextAlignment?.Val?.Value == VerticalPositionValues.Superscript;
        bool sub = rPr?.VerticalTextAlignment?.Val?.Value == VerticalPositionValues.Subscript;

        string? fontFamily = rPr?.RunFonts?.Ascii?.Value
                             ?? rPr?.RunFonts?.HighAnsi?.Value
                             ?? paraStyle?.FontFamily;

        double? fontSizePt = null;
        if (rPr?.FontSize?.Val?.Value is string szStr && double.TryParse(szStr, out double halfPts) && halfPts > 0)
            fontSizePt = halfPts / 2.0;
        else if (paraStyle?.FontSizePt is double styleSz)
            fontSizePt = styleSz;

        string? colorHex = null;
        if (rPr?.Color?.Val?.Value is string c && IsValidHexColor(c))
            colorHex = "#" + c;
        else if (hyperlinkUri != null)
            colorHex = "#0F6CBD";
        else if (paraStyle?.ColorHex != null)
            colorHex = paraStyle.ColorHex;

        string? highlightHex = rPr?.Highlight?.Val?.Value is HighlightColorValues hcv
            ? MapHighlightColor(hcv)
            : null;

        foreach (var child in r.ChildElements)
        {
            switch (child)
            {
                case Text t when !string.IsNullOrEmpty(t.Text):
                    currentPara.Inlines.Add(new DocInline
                    {
                        Text = t.Text,
                        Bold = bold,
                        Italic = italic,
                        Underline = underline,
                        Strikethrough = strike,
                        Superscript = sup,
                        Subscript = sub,
                        FontFamily = fontFamily,
                        FontSizePt = fontSizePt,
                        ColorHex = colorHex,
                        HighlightHex = highlightHex,
                        HyperlinkUri = hyperlinkUri
                    });
                    break;

                case TabChar:
                    currentPara.Inlines.Add(new DocInline { Text = "    " });
                    break;

                case Break br:
                    if (br.Type?.Value == BreakValues.Page)
                    {
                        if (currentPara.Inlines.Count > 0)
                        {
                            targetBlocks.Add(currentPara);
                            currentPara = new DocParagraph { Alignment = currentPara.Alignment };
                        }
                        targetBlocks.Add(new DocPageBreak());
                    }
                    else
                    {
                        currentPara.Inlines.Add(new DocInline { IsLineBreak = true });
                    }
                    break;

                case Drawing drawing:
                    var img = ExtractDrawingImage(drawing, mainPart);
                    if (img != null)
                        currentPara.Inlines.Add(img);
                    break;
            }
        }
    }

    private static DocInline? ExtractDrawingImage(Drawing drawing, MainDocumentPart mainPart)
    {
        try
        {
            var blip = drawing.Descendants<A.Blip>().FirstOrDefault();
            string? embedId = blip?.Embed?.Value;
            if (string.IsNullOrEmpty(embedId))
                return null;

            if (mainPart.GetPartById(embedId) is not ImagePart imagePart)
                return null;

            using var s = imagePart.GetStream();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            if (bytes.Length == 0)
                return null;

            double wDip = 320;
            double hDip = 200;

            var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
            if (extent?.Cx?.Value is long cx && cx > 0)
                wDip = cx * EmuToDip;
            if (extent?.Cy?.Value is long cy && cy > 0)
                hDip = cy * EmuToDip;

            return new DocInline
            {
                ImageBytes = bytes,
                ImageWidthDip = Math.Clamp(wDip, 16, 720),
                ImageHeightDip = Math.Clamp(hDip, 16, 900)
            };
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn(nameof(DocxParser), $"Skipped embedded image: {ex.Message}");
            return null;
        }
    }

    private static DocTable ParseTable(
        Table tbl,
        MainDocumentPart mainPart,
        Dictionary<string, StyleInfo> styles,
        Dictionary<(int numId, int level), DocListKind> numberingLookup,
        Dictionary<(int numId, int level), int> listCounters)
    {
        var docTable = new DocTable();

        var grid = tbl.GetFirstChild<TableGrid>();
        if (grid != null)
        {
            foreach (var col in grid.Elements<GridColumn>())
            {
                if (double.TryParse(col.Width?.Value, out double wTwips) && wTwips > 0)
                    docTable.ColumnWidthsDip.Add(wTwips * TwipsToDip);
            }
        }

        var rawRows = tbl.Elements<TableRow>().ToList();
        var activeRowMergeHeads = new Dictionary<int, DocTableCell>();

        for (int rIdx = 0; rIdx < rawRows.Count; rIdx++)
        {
            var row = rawRows[rIdx];
            var docRow = new DocTableRow
            {
                IsHeader = rIdx == 0 || row.TableRowProperties?.GetFirstChild<TableHeader>() != null
            };

            int colCursor = 0;
            foreach (var cell in row.Elements<TableCell>())
            {
                var tcPr = cell.TableCellProperties;
                int span = tcPr?.GridSpan?.Val?.Value ?? 1;
                var vMerge = tcPr?.VerticalMerge;

                if (vMerge != null && vMerge.Val == null)
                {
                    // Continuation of vertical merge
                    if (activeRowMergeHeads.TryGetValue(colCursor, out var headCell))
                        headCell.RowSpan++;
                    colCursor += span;
                    continue;
                }

                var docCell = new DocTableCell
                {
                    ColumnSpan = Math.Max(1, span)
                };

                if (tcPr?.TableCellWidth?.Width?.Value is string cw && double.TryParse(cw, out double cwVal) && cwVal > 0)
                {
                    if (tcPr.TableCellWidth.Type?.Value == TableWidthUnitValues.Dxa)
                        docCell.WidthDip = cwVal * TwipsToDip;
                }

                if (tcPr?.Shading?.Fill?.Value is string fill && IsValidHexColor(fill))
                    docCell.BackgroundHex = "#" + fill;

                foreach (var cellChild in cell.ChildElements)
                {
                    if (cellChild is Paragraph cp)
                    {
                        ParseParagraphIntoBlocks(cp, mainPart, styles, numberingLookup, listCounters, docCell.Blocks);
                    }
                    else if (cellChild is Table nestedTbl)
                    {
                        docCell.Blocks.Add(ParseTable(nestedTbl, mainPart, styles, numberingLookup, listCounters));
                    }
                }

                if (vMerge?.Val?.Value == MergedCellValues.Restart)
                    activeRowMergeHeads[colCursor] = docCell;
                else
                    activeRowMergeHeads.Remove(colCursor);

                docRow.Cells.Add(docCell);
                colCursor += span;
            }

            docTable.Rows.Add(docRow);
        }

        return docTable;
    }

    private static bool IsToggleOn(OnOffType? val) => val != null && (val.Val == null || val.Val.Value);

    private static bool IsValidHexColor(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) &&
        !string.Equals(hex, "auto", StringComparison.OrdinalIgnoreCase) &&
        hex.Length == 6 &&
        hex.All(Uri.IsHexDigit);

    private static string? MapHighlightColor(HighlightColorValues val)
    {
        if (val == HighlightColorValues.Yellow) return "#FFF59D";
        if (val == HighlightColorValues.Green) return "#C8E6C9";
        if (val == HighlightColorValues.Cyan) return "#B2EBF2";
        if (val == HighlightColorValues.Magenta) return "#F8BBD0";
        if (val == HighlightColorValues.Blue) return "#BBDEFB";
        if (val == HighlightColorValues.Red) return "#FFCDD2";
        if (val == HighlightColorValues.LightGray) return "#E0E0E0";
        return null;
    }

    private sealed class StyleInfo
    {
        public string Id { get; set; } = string.Empty;
        public string? BasedOn { get; set; }
        public int HeadingLevel { get; set; }
        public bool IsTitle { get; set; }
        public bool IsSubtitle { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public string? FontFamily { get; set; }
        public double? FontSizePt { get; set; }
        public string? ColorHex { get; set; }
        public DocAlignment? Alignment { get; set; }
    }

    private static Dictionary<string, StyleInfo> BuildStyleLookup(MainDocumentPart mainPart)
    {
        var map = new Dictionary<string, StyleInfo>(StringComparer.OrdinalIgnoreCase);
        var stylesRoot = mainPart.StyleDefinitionsPart?.Styles;
        if (stylesRoot == null)
            return map;

        foreach (var style in stylesRoot.Elements<Style>())
        {
            string? id = style.StyleId?.Value;
            if (string.IsNullOrEmpty(id))
                continue;

            string name = style.StyleName?.Val?.Value ?? id;
            int headingLevel = DetectHeadingLevel(id, name);

            var rPr = style.StyleRunProperties;
            var pPr = style.StyleParagraphProperties;

            double? fontSizePt = null;
            if (rPr?.FontSize?.Val?.Value is string sz && double.TryParse(sz, out double hp) && hp > 0)
                fontSizePt = hp / 2.0;

            string? colorHex = rPr?.Color?.Val?.Value is string c && IsValidHexColor(c) ? "#" + c : null;

            DocAlignment? align = null;
            if (pPr?.Justification?.Val?.Value is JustificationValues jVal)
            {
                if (jVal == JustificationValues.Center) align = DocAlignment.Center;
                else if (jVal == JustificationValues.Right) align = DocAlignment.Right;
                else if (jVal == JustificationValues.Both) align = DocAlignment.Justify;
            }

            map[id] = new StyleInfo
            {
                Id = id,
                BasedOn = style.BasedOn?.Val?.Value,
                HeadingLevel = headingLevel,
                IsTitle = string.Equals(name, "Title", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "Title", StringComparison.OrdinalIgnoreCase),
                IsSubtitle = string.Equals(name, "Subtitle", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "Subtitle", StringComparison.OrdinalIgnoreCase),
                Bold = rPr?.Bold != null ? IsToggleOn(rPr.Bold) : null,
                Italic = rPr?.Italic != null ? IsToggleOn(rPr.Italic) : null,
                FontFamily = rPr?.RunFonts?.Ascii?.Value ?? rPr?.RunFonts?.HighAnsi?.Value,
                FontSizePt = fontSizePt,
                ColorHex = colorHex,
                Alignment = align
            };
        }

        return map;
    }

    private static int DetectHeadingLevel(string id, string name)
    {
        foreach (var candidate in new[] { name, id })
        {
            if (candidate.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(candidate.AsSpan(8).Trim(), out int lvl) && lvl is >= 1 and <= 6)
                return lvl;

            if (candidate.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
                candidate.Length == 8 && char.IsDigit(candidate[7]))
            {
                int d = candidate[7] - '0';
                if (d is >= 1 and <= 6) return d;
            }
        }
        return 0;
    }

    private static StyleInfo? ResolveStyle(string? styleId, Dictionary<string, StyleInfo> styles)
    {
        if (string.IsNullOrEmpty(styleId) || !styles.TryGetValue(styleId, out var top))
            return null;

        var merged = new StyleInfo
        {
            Id = top.Id,
            HeadingLevel = top.HeadingLevel,
            IsTitle = top.IsTitle,
            IsSubtitle = top.IsSubtitle,
            Bold = top.Bold,
            Italic = top.Italic,
            FontFamily = top.FontFamily,
            FontSizePt = top.FontSizePt,
            ColorHex = top.ColorHex,
            Alignment = top.Alignment
        };

        var cur = top;
        int guard = 0;
        while (!string.IsNullOrEmpty(cur.BasedOn) && guard++ < 8 && styles.TryGetValue(cur.BasedOn, out var parent))
        {
            if (merged.HeadingLevel == 0) merged.HeadingLevel = parent.HeadingLevel;
            if (!merged.IsTitle) merged.IsTitle = parent.IsTitle;
            if (!merged.IsSubtitle) merged.IsSubtitle = parent.IsSubtitle;
            merged.Bold ??= parent.Bold;
            merged.Italic ??= parent.Italic;
            merged.FontFamily ??= parent.FontFamily;
            merged.FontSizePt ??= parent.FontSizePt;
            merged.ColorHex ??= parent.ColorHex;
            merged.Alignment ??= parent.Alignment;
            cur = parent;
        }

        return merged;
    }

    private static Dictionary<(int numId, int level), DocListKind> BuildNumberingLookup(MainDocumentPart mainPart)
    {
        var result = new Dictionary<(int numId, int level), DocListKind>();
        var numbering = mainPart.NumberingDefinitionsPart?.Numbering;
        if (numbering == null)
            return result;

        var abstractMap = new Dictionary<int, Dictionary<int, DocListKind>>();
        foreach (var abs in numbering.Elements<AbstractNum>())
        {
            if (abs.AbstractNumberId?.Value is not int absId) continue;
            var lvlMap = new Dictionary<int, DocListKind>();
            foreach (var lvl in abs.Elements<Level>())
            {
                int lvlIdx = lvl.LevelIndex?.Value ?? 0;
                var fmt = lvl.NumberingFormat?.Val?.Value;
                lvlMap[lvlIdx] = fmt == NumberFormatValues.Bullet ? DocListKind.Bullet : DocListKind.Numbered;
            }
            abstractMap[absId] = lvlMap;
        }

        foreach (var num in numbering.Elements<NumberingInstance>())
        {
            if (num.NumberID?.Value is not int numId) continue;
            if (num.AbstractNumId?.Val?.Value is not int absId) continue;
            if (!abstractMap.TryGetValue(absId, out var lvlMap)) continue;
            foreach (var kv in lvlMap)
            {
                result[(numId, kv.Key)] = kv.Value;
            }
        }

        return result;
    }
}
