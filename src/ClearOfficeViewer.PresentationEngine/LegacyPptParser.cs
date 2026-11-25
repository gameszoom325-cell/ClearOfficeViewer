using System.Buffers.Binary;
using System.IO;
using System.Text;
using ClearOfficeViewer.Core.Services;
using OpenMcdf;

namespace ClearOfficeViewer.PresentationEngine;

/// <summary>
/// Parses legacy Microsoft PowerPoint (.ppt) binary OLE2 files (MS-PPT record stream + Pictures OfficeArt BLIPs).
/// </summary>
public static class LegacyPptParser
{
    private const ushort RT_Document = 0x03E8;
    private const ushort RT_DocumentAtom = 0x03E9;
    private const ushort RT_Slide = 0x03EE;
    private const ushort RT_SlidePersistAtom = 0x03F3;
    private const ushort RT_TextHeaderAtom = 0x0F9F;
    private const ushort RT_TextCharsAtom = 0x0FA0;
    private const ushort RT_TextBytesAtom = 0x0FA8;
    private const ushort RT_SlideListWithText = 0x0FF0;
    private const ushort RT_DgContainer = 0xF002;
    private const ushort RT_SpgrContainer = 0xF003;
    private const ushort RT_SpContainer = 0xF004;
    private const ushort RT_Opt = 0xF00B;
    private const ushort RT_ClientAnchor = 0xF010;
    private const ushort RT_Notes = 0x03F0;

    // Master units = 576 DPI => DIPs = masterUnits * (96 / 576) = masterUnits / 6.0
    private const double MasterToDip = 96.0 / 576.0;

    public static ParsedPresentation Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("PPT file not found.", filePath);

        var pres = new ParsedPresentation
        {
            FilePath = Path.GetFullPath(filePath),
            Title = Path.GetFileNameWithoutExtension(filePath),
            SourceFormatLabel = "PPT"
        };

        using var root = RootStorage.OpenRead(filePath);
        if (!root.TryOpenStream("PowerPoint Document", out var pptStream))
            throw new InvalidDataException("Invalid .ppt file: 'PowerPoint Document' stream is missing.");

        byte[] docBytes = ReadAllBytes(pptStream);
        var pictures = new List<byte[]>();
        if (root.TryOpenStream("Pictures", out var picStream))
        {
            pictures = ExtractPictures(ReadAllBytes(picStream));
        }

        // First extract slide dimensions from DocumentAtom and slides from SlideContainers / SlideListWithText
        var slideTextsFromList = new List<List<(int textType, string text)>>();
        var slideContainers = new List<(int start, int end)>();

        WalkTopLevelRecords(docBytes, 0, docBytes.Length, (recVer, recInst, recType, payloadStart, payloadEnd) =>
        {
            if (recType == RT_Document)
            {
                WalkTopLevelRecords(docBytes, payloadStart, payloadEnd, (_, _, childType, cStart, cEnd) =>
                {
                    if (childType == RT_DocumentAtom && cEnd - cStart >= 8)
                    {
                        int wMaster = BinaryPrimitives.ReadInt32LittleEndian(docBytes.AsSpan(cStart, 4));
                        int hMaster = BinaryPrimitives.ReadInt32LittleEndian(docBytes.AsSpan(cStart + 4, 4));
                        if (wMaster > 0 && hMaster > 0)
                        {
                            pres.SlideWidthDip = Math.Clamp(wMaster * MasterToDip, 320, 1920);
                            pres.SlideHeightDip = Math.Clamp(hMaster * MasterToDip, 240, 1200);
                        }
                    }
                    else if (childType == RT_SlideListWithText)
                    {
                        ParseSlideListWithText(docBytes, cStart, cEnd, slideTextsFromList);
                    }
                });
            }
            else if (recType == RT_Slide)
            {
                slideContainers.Add((payloadStart, payloadEnd));
            }
        });

        int slideCount = Math.Max(slideContainers.Count, slideTextsFromList.Count);
        if (slideCount == 0 && pictures.Count == 0)
            return pres;

        for (int i = 0; i < slideCount; i++)
        {
            var slide = new ParsedSlide { SlideNumber = i + 1 };

            if (i < slideContainers.Count)
            {
                var (sStart, sEnd) = slideContainers[i];
                ParseSlideShapes(docBytes, sStart, sEnd, pictures, pres.SlideWidthDip, pres.SlideHeightDip, slide);
            }

            // If SlideContainer lacked inline text (common in older PPT files where text lives in SlideListWithText), add SlideListWithText entries
            if (!slide.Elements.OfType<SlideTextShapeElement>().Any() && i < slideTextsFromList.Count)
            {
                double nextY = 56;
                foreach (var (textType, text) in slideTextsFromList[i])
                {
                    bool isTitle = textType is 0 or 6;
                    var shape = new SlideTextShapeElement
                    {
                        X = 64,
                        Y = isTitle ? 48 : nextY,
                        Width = pres.SlideWidthDip - 128,
                        Height = isTitle ? 90 : Math.Max(140, pres.SlideHeightDip - nextY - 48)
                    };

                    foreach (string line in text.Replace("\r\n", "\r").Replace('\n', '\r').Split('\r'))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var p = new SlideParagraph
                        {
                            BulletPrefix = isTitle ? null : "• "
                        };
                        p.Runs.Add(new SlideTextRun
                        {
                            Text = line.Trim(),
                            Bold = isTitle,
                            FontSizePt = isTitle ? 30 : 18,
                            ColorHex = "#111827"
                        });
                        shape.Paragraphs.Add(p);
                    }

                    if (shape.Paragraphs.Count > 0)
                    {
                        slide.Elements.Add(shape);
                        if (isTitle) nextY = 156;
                        else nextY += 120;
                    }
                }
            }

            pres.Slides.Add(slide);
        }

        // Safety: if a PPT only stored Pictures without SlideContainers
        if (pres.Slides.Count == 0 && pictures.Count > 0)
        {
            int n = 1;
            foreach (var pic in pictures)
            {
                var s = new ParsedSlide { SlideNumber = n++ };
                s.Elements.Add(new SlideImageElement
                {
                    X = 48,
                    Y = 48,
                    Width = pres.SlideWidthDip - 96,
                    Height = pres.SlideHeightDip - 96,
                    ImageBytes = pic
                });
                pres.Slides.Add(s);
            }
        }

        return pres;
    }

    private static void ParseSlideListWithText(byte[] data, int start, int end, List<List<(int textType, string text)>> slides)
    {
        List<(int textType, string text)>? currentSlide = null;
        int currentTextType = 1;
        Encoding cp1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;

        WalkTopLevelRecords(data, start, end, (_, _, recType, pStart, pEnd) =>
        {
            if (recType == RT_SlidePersistAtom)
            {
                currentSlide = [];
                slides.Add(currentSlide);
            }
            else if (recType == RT_TextHeaderAtom && pEnd - pStart >= 4)
            {
                currentTextType = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pStart, 4));
            }
            else if (recType == RT_TextCharsAtom && currentSlide != null)
            {
                string txt = Encoding.Unicode.GetString(data, pStart, (pEnd - pStart) & ~1);
                if (!string.IsNullOrWhiteSpace(txt))
                    currentSlide.Add((currentTextType, txt));
            }
            else if (recType == RT_TextBytesAtom && currentSlide != null)
            {
                string txt = cp1252.GetString(data, pStart, pEnd - pStart);
                if (!string.IsNullOrWhiteSpace(txt))
                    currentSlide.Add((currentTextType, txt));
            }
        });
    }

    private static void ParseSlideShapes(
        byte[] data,
        int start,
        int end,
        List<byte[]> pictures,
        double slideW,
        double slideH,
        ParsedSlide slide)
    {
        WalkTopLevelRecords(data, start, end, (recVer, _, recType, pStart, pEnd) =>
        {
            if (recType == RT_DgContainer || recType == RT_SpgrContainer)
            {
                ParseSlideShapes(data, pStart, pEnd, pictures, slideW, slideH, slide);
            }
            else if (recType == RT_SpContainer)
            {
                ParseSpContainer(data, pStart, pEnd, pictures, slideW, slideH, slide);
            }
            else if (recVer == 0xF && recType != RT_Notes)
            {
                ParseSlideShapes(data, pStart, pEnd, pictures, slideW, slideH, slide);
            }
        });
    }

    private static void ParseSpContainer(
        byte[] data,
        int start,
        int end,
        List<byte[]> pictures,
        double slideW,
        double slideH,
        ParsedSlide slide)
    {
        int pibIndex = -1;
        double x = 64, y = 64, w = slideW - 128, h = 160;
        bool hasAnchor = false;
        var texts = new List<string>();
        Encoding cp1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;

        WalkTopLevelRecords(data, start, end, (_, recInst, recType, pStart, pEnd) =>
        {
            if (recType == RT_Opt)
            {
                // Property table: recInst = property count; each prop is 6 bytes (2-byte propId+flags, 4-byte value)
                int count = recInst;
                int offset = pStart;
                for (int i = 0; i < count && offset + 6 <= pEnd; i++, offset += 6)
                {
                    ushort rawId = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
                    ushort propId = (ushort)(rawId & 0x3FFF);
                    uint propVal = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 2, 4));
                    if (propId == 0x0104 && propVal >= 1 && propVal <= pictures.Count)
                    {
                        pibIndex = (int)propVal - 1;
                    }
                }
            }
            else if (recType == RT_ClientAnchor)
            {
                int len = pEnd - pStart;
                if (len == 8)
                {
                    short top = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(pStart, 2));
                    short left = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(pStart + 2, 2));
                    short right = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(pStart + 4, 2));
                    short bottom = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(pStart + 6, 2));
                    x = left * MasterToDip;
                    y = top * MasterToDip;
                    w = Math.Max(16, (right - left) * MasterToDip);
                    h = Math.Max(16, (bottom - top) * MasterToDip);
                    hasAnchor = true;
                }
                else if (len >= 16)
                {
                    int top = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pStart, 4));
                    int left = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pStart + 4, 4));
                    int right = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pStart + 8, 4));
                    int bottom = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pStart + 12, 4));
                    x = left * MasterToDip;
                    y = top * MasterToDip;
                    w = Math.Max(16, (right - left) * MasterToDip);
                    h = Math.Max(16, (bottom - top) * MasterToDip);
                    hasAnchor = true;
                }
            }
            else if (recType == RT_TextCharsAtom)
            {
                string txt = Encoding.Unicode.GetString(data, pStart, (pEnd - pStart) & ~1);
                if (!string.IsNullOrWhiteSpace(txt)) texts.Add(txt);
            }
            else if (recType == RT_TextBytesAtom)
            {
                string txt = cp1252.GetString(data, pStart, pEnd - pStart);
                if (!string.IsNullOrWhiteSpace(txt)) texts.Add(txt);
            }
            else if (recType == 0xF00D) // msofbtClientTextbox
            {
                WalkTopLevelRecords(data, pStart, pEnd, (_, _, tType, tStart, tEnd) =>
                {
                    if (tType == RT_TextCharsAtom)
                    {
                        string txt = Encoding.Unicode.GetString(data, tStart, (tEnd - tStart) & ~1);
                        if (!string.IsNullOrWhiteSpace(txt)) texts.Add(txt);
                    }
                    else if (tType == RT_TextBytesAtom)
                    {
                        string txt = cp1252.GetString(data, tStart, tEnd - tStart);
                        if (!string.IsNullOrWhiteSpace(txt)) texts.Add(txt);
                    }
                });
            }
        });

        if (pibIndex >= 0 && pibIndex < pictures.Count && hasAnchor)
        {
            slide.Elements.Add(new SlideImageElement
            {
                X = Math.Clamp(x, 0, slideW - 16),
                Y = Math.Clamp(y, 0, slideH - 16),
                Width = Math.Min(w, slideW),
                Height = Math.Min(h, slideH),
                ImageBytes = pictures[pibIndex]
            });
        }

        if (texts.Count > 0)
        {
            bool looksLikeHeader = hasAnchor && y < slideH * 0.28;
            var shape = new SlideTextShapeElement
            {
                X = Math.Clamp(x, 24, slideW - 60),
                Y = Math.Clamp(y, 24, slideH - 40),
                Width = Math.Max(120, Math.Min(w, slideW - 48)),
                Height = Math.Max(48, Math.Min(h, slideH - 48))
            };

            foreach (string block in texts)
            {
                foreach (string line in block.Replace("\r\n", "\r").Replace('\n', '\r').Split('\r'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var p = new SlideParagraph();
                    p.Runs.Add(new SlideTextRun
                    {
                        Text = line.Trim(),
                        Bold = looksLikeHeader,
                        FontSizePt = looksLikeHeader ? 28 : 17,
                        ColorHex = "#111827"
                    });
                    shape.Paragraphs.Add(p);
                }
            }

            if (shape.Paragraphs.Count > 0)
                slide.Elements.Add(shape);
        }
    }

    private static List<byte[]> ExtractPictures(byte[] data)
    {
        var list = new List<byte[]>();
        int pos = 0;
        while (pos + 8 <= data.Length)
        {
            ushort verInst = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos, 2));
            ushort recInst = (ushort)(verInst >> 4);
            ushort recType = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 2, 2));
            uint recLen = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4, 4));
            pos += 8;

            if (recLen == 0 || pos + recLen > data.Length)
                break;

            int payloadLen = (int)recLen;
            // OfficeArt BLIP records: JPEG (F01D, F02A), PNG (F01E), DIB (F01F)
            if (recType is 0xF01D or 0xF01E or 0xF02A)
            {
                // 17-byte UID header (or 33-byte if extra primary UID bit is set on instance)
                int headerSize = (recInst & 0x01) != 0 ? 33 : 17;
                if (payloadLen > headerSize)
                {
                    list.Add(data.AsSpan(pos + headerSize, payloadLen - headerSize).ToArray());
                }
            }

            pos += payloadLen;
        }
        return list;
    }

    private delegate void RecordVisitor(ushort recVer, ushort recInst, ushort recType, int payloadStart, int payloadEnd);

    private static void WalkTopLevelRecords(
        byte[] data,
        int start,
        int end,
        RecordVisitor visitor)
    {
        int pos = start;
        while (pos + 8 <= end)
        {
            ushort verInst = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos, 2));
            ushort recVer = (ushort)(verInst & 0x0F);
            ushort recInst = (ushort)(verInst >> 4);
            ushort recType = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 2, 2));
            uint recLen = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4, 4));

            int payloadStart = pos + 8;
            long payloadEndLong = (long)payloadStart + recLen;
            if (payloadEndLong > end || payloadEndLong < payloadStart)
                break;

            int payloadEnd = (int)payloadEndLong;
            visitor(recVer, recInst, recType, payloadStart, payloadEnd);
            pos = payloadEnd;
        }
    }

    private static byte[] ReadAllBytes(CfbStream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
