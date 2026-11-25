using System.Globalization;
using System.IO;
using ClearOfficeViewer.Core.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace ClearOfficeViewer.PresentationEngine;

public static class PptxParser
{
    private const double EmuToDip = 96.0 / 914400.0;

    public static ParsedPresentation Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("PPTX file not found.", filePath);

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var pptDoc = PresentationDocument.Open(fs, false);

        var pres = new ParsedPresentation
        {
            FilePath = Path.GetFullPath(filePath),
            Title = Path.GetFileNameWithoutExtension(filePath),
            SourceFormatLabel = "PPTX"
        };

        var presPart = pptDoc.PresentationPart;
        var presentation = presPart?.Presentation;
        if (presPart == null || presentation == null)
            return pres;

        if (presentation.SlideSize is SlideSize ssz)
        {
            if (ssz.Cx?.Value is int cx && cx > 0) pres.SlideWidthDip = cx * EmuToDip;
            if (ssz.Cy?.Value is int cy && cy > 0) pres.SlideHeightDip = cy * EmuToDip;
        }

        var themeColors = BuildThemeColorMap(presPart);

        var slideIds = presentation.SlideIdList?.Elements<SlideId>().ToList() ?? [];
        int slideNumber = 1;

        foreach (var sId in slideIds)
        {
            string? relId = sId.RelationshipId?.Value;
            if (string.IsNullOrEmpty(relId)) continue;
            if (presPart.GetPartById(relId) is not SlidePart slidePart) continue;

            var slide = ParseSlide(slidePart, slideNumber++, themeColors);
            pres.Slides.Add(slide);
        }

        return pres;
    }

    private static ParsedSlide ParseSlide(SlidePart slidePart, int slideNumber, Dictionary<string, string> themeColors)
    {
        var slide = new ParsedSlide { SlideNumber = slideNumber };

        // Resolve background from Slide -> Layout -> Master
        var bg = slidePart.Slide?.CommonSlideData?.Background
                 ?? slidePart.SlideLayoutPart?.SlideLayout?.CommonSlideData?.Background
                 ?? slidePart.SlideLayoutPart?.SlideMasterPart?.SlideMaster?.CommonSlideData?.Background;

        if (bg?.BackgroundProperties != null)
        {
            var solid = bg.BackgroundProperties.GetFirstChild<A.SolidFill>();
            if (solid != null && TryResolveFillColor(solid, themeColors, out string hex))
                slide.BackgroundHex = hex;

            var blipFill = bg.BackgroundProperties.GetFirstChild<A.BlipFill>();
            string? bgEmbed = blipFill?.Blip?.Embed?.Value;
            if (!string.IsNullOrEmpty(bgEmbed))
            {
                OpenXmlPart? imgSource = slidePart.Slide?.CommonSlideData?.Background != null
                    ? slidePart
                    : slidePart.SlideLayoutPart?.SlideLayout?.CommonSlideData?.Background != null
                        ? slidePart.SlideLayoutPart
                        : slidePart.SlideLayoutPart?.SlideMasterPart;
                if (imgSource != null)
                    slide.BackgroundImageBytes = TryReadImageBytes(imgSource, bgEmbed);
            }
        }

        // Speaker notes
        if (slidePart.NotesSlidePart?.NotesSlide != null)
        {
            var notesTexts = slidePart.NotesSlidePart.NotesSlide
                .Descendants<A.Text>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            if (notesTexts.Count > 0)
                slide.NotesText = string.Join(" ", notesTexts).Trim();
        }

        var spTree = slidePart.Slide?.CommonSlideData?.ShapeTree;
        if (spTree != null)
        {
            ParseShapeTreeChildren(spTree.ChildElements, slidePart, slide.Elements, themeColors, 0, 0, 1, 1);
        }

        return slide;
    }

    private static void ParseShapeTreeChildren(
        OpenXmlElementList children,
        OpenXmlPart hostPart,
        List<SlideElement> target,
        Dictionary<string, string> themeColors,
        double offsetX,
        double offsetY,
        double scaleX,
        double scaleY)
    {
        foreach (var child in children)
        {
            switch (child)
            {
                case Shape sp:
                    var shapeEl = ParseShape(sp, themeColors, offsetX, offsetY, scaleX, scaleY);
                    if (shapeEl != null)
                        target.Add(shapeEl);
                    break;

                case Picture pic:
                    var picEl = ParsePicture(pic, hostPart, offsetX, offsetY, scaleX, scaleY);
                    if (picEl != null)
                        target.Add(picEl);
                    break;

                case GraphicFrame gf:
                    var tblEl = ParseGraphicFrameTable(gf, themeColors, offsetX, offsetY, scaleX, scaleY);
                    if (tblEl != null)
                        target.Add(tblEl);
                    break;

                case GroupShape grp:
                    ParseGroupShape(grp, hostPart, target, themeColors, offsetX, offsetY, scaleX, scaleY);
                    break;
            }
        }
    }

    private static void ParseGroupShape(
        GroupShape grp,
        OpenXmlPart hostPart,
        List<SlideElement> target,
        Dictionary<string, string> themeColors,
        double parentOffsetX,
        double parentOffsetY,
        double parentScaleX,
        double parentScaleY)
    {
        var xfrm = grp.GroupShapeProperties?.TransformGroup;
        double gx = (xfrm?.Offset?.X?.Value ?? 0) * EmuToDip * parentScaleX + parentOffsetX;
        double gy = (xfrm?.Offset?.Y?.Value ?? 0) * EmuToDip * parentScaleY + parentOffsetY;
        double gw = (xfrm?.Extents?.Cx?.Value ?? 0) * EmuToDip * parentScaleX;
        double gh = (xfrm?.Extents?.Cy?.Value ?? 0) * EmuToDip * parentScaleY;

        double chx = (xfrm?.ChildOffset?.X?.Value ?? 0) * EmuToDip;
        double chy = (xfrm?.ChildOffset?.Y?.Value ?? 0) * EmuToDip;
        double chw = (xfrm?.ChildExtents?.Cx?.Value ?? 0) * EmuToDip;
        double chh = (xfrm?.ChildExtents?.Cy?.Value ?? 0) * EmuToDip;

        double sx = chw > 0 && gw > 0 ? gw / chw : parentScaleX;
        double sy = chh > 0 && gh > 0 ? gh / chh : parentScaleY;

        ParseShapeTreeChildren(grp.ChildElements, hostPart, target, themeColors, gx - chx * sx, gy - chy * sy, sx, sy);
    }

    private static SlideTextShapeElement? ParseShape(
        Shape sp,
        Dictionary<string, string> themeColors,
        double offsetX,
        double offsetY,
        double scaleX,
        double scaleY)
    {
        var spPr = sp.ShapeProperties;
        var xfrm = spPr?.Transform2D;

        double x = ((xfrm?.Offset?.X?.Value ?? 0) * EmuToDip) * scaleX + offsetX;
        double y = ((xfrm?.Offset?.Y?.Value ?? 0) * EmuToDip) * scaleY + offsetY;
        double w = ((xfrm?.Extents?.Cx?.Value ?? 0) * EmuToDip) * scaleX;
        double h = ((xfrm?.Extents?.Cy?.Value ?? 0) * EmuToDip) * scaleY;

        // Placeholder shapes sometimes inherit position/size from layout; provide sensible default if missing
        var ph = sp.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;
        if (w <= 0 || h <= 0)
        {
            if (ph != null)
            {
                if (ph.Type?.Value == PlaceholderValues.Title || ph.Type?.Value == PlaceholderValues.CenteredTitle)
                {
                    x = 64; y = 48; w = 832; h = 96;
                }
                else if (ph.Type?.Value == PlaceholderValues.SubTitle)
                {
                    x = 96; y = 160; w = 768; h = 120;
                }
                else
                {
                    x = 64; y = 156; w = 832; h = 320;
                }
            }
            else
            {
                return null;
            }
        }

        var el = new SlideTextShapeElement
        {
            X = x,
            Y = y,
            Width = w,
            Height = h,
            ShapeKind = ResolveShapeKind(spPr?.GetFirstChild<A.PresetGeometry>()?.Preset?.Value)
        };

        var solidFill = spPr?.GetFirstChild<A.SolidFill>();
        if (solidFill != null && TryResolveFillColor(solidFill, themeColors, out string fillHex))
            el.FillHex = fillHex;

        var outline = spPr?.GetFirstChild<A.Outline>();
        var outlineFill = outline?.GetFirstChild<A.SolidFill>();
        if (outlineFill != null && TryResolveFillColor(outlineFill, themeColors, out string strokeHex))
        {
            el.StrokeHex = strokeHex;
            el.StrokeThicknessDip = Math.Max(1, (outline?.Width?.Value ?? 12700) * EmuToDip);
        }

        bool isTitlePlaceholder = ph?.Type?.Value == PlaceholderValues.Title || ph?.Type?.Value == PlaceholderValues.CenteredTitle;
        double defaultFontPt = isTitlePlaceholder ? 32 : (ph?.Type?.Value == PlaceholderValues.SubTitle ? 20 : 18);

        if (sp.TextBody != null)
        {
            int autoNumCounter = 1;
            foreach (var p in sp.TextBody.Elements<A.Paragraph>())
            {
                var slidePara = ParseDrawingParagraph(p, themeColors, defaultFontPt, isTitlePlaceholder, ref autoNumCounter);
                el.Paragraphs.Add(slidePara);
            }
        }

        if (el.FillHex == null && el.StrokeHex == null && el.Paragraphs.All(p => p.Runs.Count == 0))
            return null;

        return el;
    }

    private static SlideParagraph ParseDrawingParagraph(
        A.Paragraph p,
        Dictionary<string, string> themeColors,
        double defaultFontPt,
        bool defaultBold,
        ref int autoNumCounter)
    {
        var para = new SlideParagraph();
        var pPr = p.ParagraphProperties;
        if (pPr != null)
        {
            para.Level = pPr.Level?.Value ?? 0;
            if (pPr.Alignment?.Value is A.TextAlignmentTypeValues al)
            {
                if (al == A.TextAlignmentTypeValues.Center) para.Alignment = SlideTextAlignment.Center;
                else if (al == A.TextAlignmentTypeValues.Right) para.Alignment = SlideTextAlignment.Right;
                else if (al == A.TextAlignmentTypeValues.Justified) para.Alignment = SlideTextAlignment.Justify;
            }

            if (pPr.GetFirstChild<A.NoBullet>() == null)
            {
                var buChar = pPr.GetFirstChild<A.CharacterBullet>();
                var buAuto = pPr.GetFirstChild<A.AutoNumberedBullet>();
                if (buChar != null && !string.IsNullOrEmpty(buChar.Char?.Value))
                    para.BulletPrefix = buChar.Char.Value + " ";
                else if (buAuto != null)
                    para.BulletPrefix = $"{autoNumCounter++}. ";
            }
        }

        foreach (var child in p.ChildElements)
        {
            if (child is A.Run r && !string.IsNullOrEmpty(r.Text?.Text))
            {
                var rPr = r.RunProperties;
                double fontSizePt = rPr?.FontSize?.Value is int sz && sz > 0 ? sz / 100.0 : defaultFontPt;
                bool bold = rPr?.Bold?.Value ?? defaultBold;
                bool italic = rPr?.Italic?.Value ?? false;
                bool underline = rPr?.Underline?.Value != null && rPr.Underline.Value != A.TextUnderlineValues.None;
                string? fontFamily = rPr?.GetFirstChild<A.LatinFont>()?.Typeface?.Value;

                string colorHex = "#1F2937";
                var runFill = rPr?.GetFirstChild<A.SolidFill>();
                if (runFill != null && TryResolveFillColor(runFill, themeColors, out string resolvedColor))
                    colorHex = resolvedColor;

                para.Runs.Add(new SlideTextRun
                {
                    Text = r.Text.Text,
                    Bold = bold,
                    Italic = italic,
                    Underline = underline,
                    FontFamily = fontFamily,
                    FontSizePt = fontSizePt,
                    ColorHex = colorHex
                });
            }
            else if (child is A.Break)
            {
                para.Runs.Add(new SlideTextRun { Text = "\n", FontSizePt = defaultFontPt });
            }
        }

        return para;
    }

    private static SlideImageElement? ParsePicture(
        Picture pic,
        OpenXmlPart hostPart,
        double offsetX,
        double offsetY,
        double scaleX,
        double scaleY)
    {
        string? embedId = pic.BlipFill?.Blip?.Embed?.Value;
        if (string.IsNullOrEmpty(embedId))
            return null;

        byte[]? bytes = TryReadImageBytes(hostPart, embedId);
        if (bytes == null || bytes.Length == 0)
            return null;

        var xfrm = pic.ShapeProperties?.Transform2D;
        double x = ((xfrm?.Offset?.X?.Value ?? 0) * EmuToDip) * scaleX + offsetX;
        double y = ((xfrm?.Offset?.Y?.Value ?? 0) * EmuToDip) * scaleY + offsetY;
        double w = ((xfrm?.Extents?.Cx?.Value ?? 0) * EmuToDip) * scaleX;
        double h = ((xfrm?.Extents?.Cy?.Value ?? 0) * EmuToDip) * scaleY;

        if (w <= 0 || h <= 0)
            return null;

        return new SlideImageElement
        {
            X = x,
            Y = y,
            Width = w,
            Height = h,
            ImageBytes = bytes
        };
    }

    private static SlideTableElement? ParseGraphicFrameTable(
        GraphicFrame gf,
        Dictionary<string, string> themeColors,
        double offsetX,
        double offsetY,
        double scaleX,
        double scaleY)
    {
        var aTbl = gf.Descendants<A.Table>().FirstOrDefault();
        if (aTbl == null) return null;

        var xfrm = gf.Transform;
        double x = ((xfrm?.Offset?.X?.Value ?? 0) * EmuToDip) * scaleX + offsetX;
        double y = ((xfrm?.Offset?.Y?.Value ?? 0) * EmuToDip) * scaleY + offsetY;
        double w = ((xfrm?.Extents?.Cx?.Value ?? 0) * EmuToDip) * scaleX;
        double h = ((xfrm?.Extents?.Cy?.Value ?? 0) * EmuToDip) * scaleY;

        var tblEl = new SlideTableElement { X = x, Y = y, Width = Math.Max(120, w), Height = Math.Max(60, h) };
        var grid = aTbl.TableGrid;
        if (grid != null)
        {
            foreach (var col in grid.Elements<A.GridColumn>())
            {
                tblEl.ColumnWidthsDip.Add((col.Width?.Value ?? 914400) * EmuToDip * scaleX);
            }
        }

        foreach (var row in aTbl.Elements<A.TableRow>())
        {
            var slideRow = new SlideTableRow { HeightDip = (row.Height?.Value ?? 370840) * EmuToDip * scaleY };
            foreach (var cell in row.Elements<A.TableCell>())
            {
                if (cell.HorizontalMerge?.Value == true || cell.VerticalMerge?.Value == true)
                    continue;

                var slideCell = new SlideTableCell
                {
                    ColumnSpan = cell.GridSpan?.Value ?? 1,
                    RowSpan = cell.RowSpan?.Value ?? 1
                };

                var cellFill = cell.TableCellProperties?.GetFirstChild<A.SolidFill>();
                if (cellFill != null && TryResolveFillColor(cellFill, themeColors, out string fillHex))
                    slideCell.FillHex = fillHex;

                int dummyCounter = 1;
                if (cell.TextBody != null)
                {
                    foreach (var p in cell.TextBody.Elements<A.Paragraph>())
                    {
                        slideCell.Paragraphs.Add(ParseDrawingParagraph(p, themeColors, 14, false, ref dummyCounter));
                    }
                }
                slideRow.Cells.Add(slideCell);
            }
            tblEl.Rows.Add(slideRow);
        }

        return tblEl;
    }

    private static SlideShapeKind ResolveShapeKind(A.ShapeTypeValues? preset)
    {
        if (preset == null) return SlideShapeKind.Rectangle;
        if (preset == A.ShapeTypeValues.RoundRectangle) return SlideShapeKind.RoundedRectangle;
        if (preset == A.ShapeTypeValues.Ellipse) return SlideShapeKind.Ellipse;
        if (preset == A.ShapeTypeValues.Line || preset == A.ShapeTypeValues.StraightConnector1) return SlideShapeKind.Line;
        return SlideShapeKind.Rectangle;
    }

    private static byte[]? TryReadImageBytes(OpenXmlPart hostPart, string embedId)
    {
        try
        {
            if (hostPart.GetPartById(embedId) is not ImagePart imgPart)
                return null;
            using var s = imgPart.GetStream();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn(nameof(PptxParser), $"Failed reading slide image '{embedId}': {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, string> BuildThemeColorMap(PresentationPart presPart)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dk1"] = "#000000",
            ["tx1"] = "#000000",
            ["lt1"] = "#FFFFFF",
            ["bg1"] = "#FFFFFF",
            ["dk2"] = "#1F497D",
            ["tx2"] = "#1F497D",
            ["lt2"] = "#EEECE1",
            ["bg2"] = "#EEECE1",
            ["accent1"] = "#0F6CBD",
            ["accent2"] = "#C0504D",
            ["accent3"] = "#9BBB59",
            ["accent4"] = "#8064A2",
            ["accent5"] = "#4BACC6",
            ["accent6"] = "#F79646",
            ["hlink"] = "#0563C1"
        };

        var clrScheme = presPart.ThemePart?.Theme?.ThemeElements?.ColorScheme;
        if (clrScheme == null) return map;

        void ReadColor(string key, A.Color2Type? node)
        {
            if (node == null) return;
            string? val = node.RgbColorModelHex?.Val?.Value;
            if (string.IsNullOrEmpty(val))
                val = node.SystemColor?.LastColor?.Value;
            if (!string.IsNullOrEmpty(val) && val.Length == 6)
                map[key] = "#" + val;
        }

        ReadColor("dk1", clrScheme.Dark1Color);
        ReadColor("lt1", clrScheme.Light1Color);
        ReadColor("dk2", clrScheme.Dark2Color);
        ReadColor("lt2", clrScheme.Light2Color);
        ReadColor("accent1", clrScheme.Accent1Color);
        ReadColor("accent2", clrScheme.Accent2Color);
        ReadColor("accent3", clrScheme.Accent3Color);
        ReadColor("accent4", clrScheme.Accent4Color);
        ReadColor("accent5", clrScheme.Accent5Color);
        ReadColor("accent6", clrScheme.Accent6Color);
        ReadColor("hlink", clrScheme.Hyperlink);

        map["tx1"] = map["dk1"];
        map["bg1"] = map["lt1"];
        map["tx2"] = map["dk2"];
        map["bg2"] = map["lt2"];
        return map;
    }

    private static bool TryResolveFillColor(A.SolidFill fill, Dictionary<string, string> themeColors, out string hex)
    {
        hex = "#000000";
        if (fill.RgbColorModelHex?.Val?.Value is string rgb && rgb.Length == 6)
        {
            hex = "#" + rgb;
            return true;
        }

        if (fill.SchemeColor?.Val?.Value is A.SchemeColorValues scv)
        {
            string key = scv.ToString() ?? "tx1";
            // DocumentFormat.OpenXml SchemeColorValues maps Dark1 -> dk1, etc.
            string mapped = key switch
            {
                "dk1" or "Dark1" => "dk1",
                "lt1" or "Light1" => "lt1",
                "dk2" or "Dark2" => "dk2",
                "lt2" or "Light2" => "lt2",
                "tx1" or "Text1" => "tx1",
                "tx2" or "Text2" => "tx2",
                "bg1" or "Background1" => "bg1",
                "bg2" or "Background2" => "bg2",
                "accent1" or "Accent1" => "accent1",
                "accent2" or "Accent2" => "accent2",
                "accent3" or "Accent3" => "accent3",
                "accent4" or "Accent4" => "accent4",
                "accent5" or "Accent5" => "accent5",
                "accent6" or "Accent6" => "accent6",
                _ => key
            };

            if (themeColors.TryGetValue(mapped, out string? resolved))
            {
                hex = resolved;
                var lumMod = fill.SchemeColor.GetFirstChild<A.LuminanceModulation>()?.Val?.Value;
                var lumOff = fill.SchemeColor.GetFirstChild<A.LuminanceOffset>()?.Val?.Value;
                if (lumMod.HasValue || lumOff.HasValue)
                    hex = ApplyLuminanceTransform(hex, (lumMod ?? 100000) / 100000.0, (lumOff ?? 0) / 100000.0);
                return true;
            }
        }

        return false;
    }

    private static string ApplyLuminanceTransform(string hex, double mod, double off)
    {
        if (!int.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, null, out int r) ||
            !int.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, null, out int g) ||
            !int.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, null, out int b))
            return hex;

        int ClampChannel(int c) => Math.Clamp((int)Math.Round(c * mod + off * 255.0), 0, 255);
        return $"#{ClampChannel(r):X2}{ClampChannel(g):X2}{ClampChannel(b):X2}";
    }
}
