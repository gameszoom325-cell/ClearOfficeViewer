using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ClearOfficeViewer.Core.Services;
using OpenMcdf;

namespace ClearOfficeViewer.DocumentEngine;

/// <summary>
/// Parses legacy Microsoft Word (.doc) files:
/// 1) OLE2 Compound File Binary Format (MS-DOC FIB + Clx Piece Table + Data stream images)
/// 2) RTF-wrapped .doc files ({\rtf1...)
/// 3) HTML/MHT-disguised .doc exports
/// </summary>
public static partial class LegacyDocParser
{
    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static ParsedDocument Parse(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("DOC file not found.", filePath);

        byte[] raw = File.ReadAllBytes(filePath);
        var doc = new ParsedDocument
        {
            FilePath = Path.GetFullPath(filePath),
            Title = Path.GetFileNameWithoutExtension(filePath),
            SourceFormatLabel = "DOC"
        };

        // 1. Check RTF disguised as .doc
        if (raw.Length >= 5 && raw[0] == (byte)'{' && raw[1] == (byte)'\\' && raw[2] == (byte)'r' && raw[3] == (byte)'t' && raw[4] == (byte)'f')
        {
            doc.RawRtfBytes = raw;
            doc.SourceFormatLabel = "DOC (RTF)";
            return doc;
        }

        // 2. Check ZIP/DOCX saved with .doc extension
        if (raw.Length >= 4 && raw[0] == (byte)'P' && raw[1] == (byte)'K' && raw[2] == 0x03 && raw[3] == 0x04)
        {
            var docx = DocxParser.Parse(filePath);
            docx.SourceFormatLabel = "DOC (OOXML)";
            return docx;
        }

        // 3. Check OLE2 Compound File (MS-DOC)
        if (raw.Length >= 8 && raw.AsSpan(0, 8).SequenceEqual(OleSignature))
        {
            return ParseOle2WordDocument(filePath, doc);
        }

        // 4. HTML or plain text disguised as .doc
        string text = Encoding.UTF8.GetString(raw);
        if (text.Contains("<html", StringComparison.OrdinalIgnoreCase) || text.Contains("<body", StringComparison.OrdinalIgnoreCase))
        {
            ParseHtmlFallback(text, doc);
            return doc;
        }

        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var p = new DocParagraph();
            p.Inlines.Add(new DocInline { Text = line });
            doc.Blocks.Add(p);
        }
        return doc;
    }

    private static ParsedDocument ParseOle2WordDocument(string filePath, ParsedDocument doc)
    {
        using var root = RootStorage.OpenRead(filePath);

        if (!root.TryOpenStream("WordDocument", out var wordStream))
            throw new InvalidDataException("Invalid .doc file: 'WordDocument' stream is missing.");

        byte[] wordBytes = ReadAllBytes(wordStream);
        if (wordBytes.Length < 512)
            throw new InvalidDataException("Invalid .doc file: WordDocument stream is truncated.");

        ushort wIdent = BinaryPrimitives.ReadUInt16LittleEndian(wordBytes.AsSpan(0, 2));
        if (wIdent != 0xA5EC && wIdent != 0xA5DC)
            AppLogger.Instance.Warn(nameof(LegacyDocParser), $"Unexpected Word FIB wIdent 0x{wIdent:X4} in '{filePath}'.");

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(wordBytes.AsSpan(10, 2));
        bool use1Table = (flags & 0x0200) != 0;
        string tableStreamName = use1Table ? "1Table" : "0Table";

        byte[]? tableBytes = null;
        if (root.TryOpenStream(tableStreamName, out var tblStream))
        {
            tableBytes = ReadAllBytes(tblStream);
        }
        else if (root.TryOpenStream(use1Table ? "0Table" : "1Table", out var altStream))
        {
            tableBytes = ReadAllBytes(altStream);
        }

        string extractedText = ExtractTextViaPieceTable(wordBytes, tableBytes) ?? ExtractFallbackText(wordBytes);
        PopulateBlocksFromWordText(extractedText, doc);

        // Extract any embedded PNG/JPEG pictures from Data stream
        if (root.TryOpenStream("Data", out var dataStream))
        {
            byte[] dataBytes = ReadAllBytes(dataStream);
            var images = ExtractEmbeddedImages(dataBytes);
            foreach (var img in images)
            {
                var p = new DocParagraph { MarginTopDip = 8, MarginBottomDip = 8 };
                p.Inlines.Add(new DocInline
                {
                    ImageBytes = img,
                    ImageWidthDip = 420,
                    ImageHeightDip = 280
                });
                doc.Blocks.Add(p);
            }
        }

        return doc;
    }

    private static string? ExtractTextViaPieceTable(byte[] wordBytes, byte[]? tableBytes)
    {
        if (tableBytes == null || wordBytes.Length < 430)
            return null;

        try
        {
            // MS-DOC FIB offsets:
            // csw (offset 32, 2 bytes) -> FibRgW97 (28 bytes)
            // cslw (offset 62, 2 bytes) -> FibRgLw97 (88 bytes) -> ccpText at offset 76
            // cbRgFcLcb (offset 152, 2 bytes) -> FibRgFcLcb97 starts at offset 154
            // fcClx is index 33 in FibRgFcLcb97 => offset 154 + 33 * 8 = 418 (fcClx), 422 (lcbClx)
            uint fcClx = BinaryPrimitives.ReadUInt32LittleEndian(wordBytes.AsSpan(418, 4));
            uint lcbClx = BinaryPrimitives.ReadUInt32LittleEndian(wordBytes.AsSpan(422, 4));

            int ccpText = BinaryPrimitives.ReadInt32LittleEndian(wordBytes.AsSpan(76, 4));

            if (lcbClx == 0 || fcClx + lcbClx > tableBytes.Length)
                return null;

            int pos = (int)fcClx;
            int end = pos + (int)lcbClx;

            // Skip optional Prc entries (clxt == 0x01)
            while (pos < end && tableBytes[pos] == 0x01)
            {
                if (pos + 3 > end) return null;
                short cbGrpprl = BinaryPrimitives.ReadInt16LittleEndian(tableBytes.AsSpan(pos + 1, 2));
                pos += 3 + Math.Max(0, (int)cbGrpprl);
            }

            if (pos >= end || tableBytes[pos] != 0x02)
                return null;

            pos++;
            if (pos + 4 > end) return null;
            int lcbPcdt = BinaryPrimitives.ReadInt32LittleEndian(tableBytes.AsSpan(pos, 4));
            pos += 4;
            if (lcbPcdt <= 4 || pos + lcbPcdt > tableBytes.Length)
                return null;

            // PlcPcd: (n + 1) uint32 CPs followed by n 8-byte PCDs => lcbPcdt = 4*(n+1) + 8*n = 12*n + 4
            int n = (lcbPcdt - 4) / 12;
            if (n <= 0)
                return null;

            int cpArrayOffset = pos;
            int pcdArrayOffset = pos + (n + 1) * 4;

            var sb = new StringBuilder();
            Encoding cp1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;

            for (int i = 0; i < n; i++)
            {
                int cpStart = BinaryPrimitives.ReadInt32LittleEndian(tableBytes.AsSpan(cpArrayOffset + i * 4, 4));
                int cpEnd = BinaryPrimitives.ReadInt32LittleEndian(tableBytes.AsSpan(cpArrayOffset + (i + 1) * 4, 4));
                int charCount = cpEnd - cpStart;
                if (charCount <= 0) continue;

                // Stop once we pass main document text (ccpText) so header/footnote internal markers aren't mixed in
                if (ccpText > 0 && cpStart >= ccpText)
                    break;

                int effectiveCount = ccpText > 0 ? Math.Min(charCount, ccpText - cpStart) : charCount;

                int pcdOffset = pcdArrayOffset + i * 8;
                uint fcValue = BinaryPrimitives.ReadUInt32LittleEndian(tableBytes.AsSpan(pcdOffset + 2, 4));

                bool isCompressedAnsi = (fcValue & 0x40000000u) != 0;
                uint rawOffset = fcValue & 0x3FFFFFFFu;

                if (isCompressedAnsi)
                {
                    int byteOffset = (int)(rawOffset / 2);
                    int byteLen = Math.Min(effectiveCount, Math.Max(0, wordBytes.Length - byteOffset));
                    if (byteLen > 0)
                        sb.Append(cp1252.GetString(wordBytes, byteOffset, byteLen));
                }
                else
                {
                    int byteOffset = (int)rawOffset;
                    int byteLen = Math.Min(effectiveCount * 2, Math.Max(0, wordBytes.Length - byteOffset));
                    if (byteLen >= 2)
                        sb.Append(Encoding.Unicode.GetString(wordBytes, byteOffset, byteLen & ~1));
                }
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn(nameof(LegacyDocParser), $"Piece table parse fallback triggered: {ex.Message}");
            return null;
        }
    }

    private static string ExtractFallbackText(byte[] wordBytes)
    {
        if (wordBytes.Length < 0x800) return string.Empty;
        int start = Math.Min(0x800, wordBytes.Length);
        return Encoding.Unicode.GetString(wordBytes, start, (wordBytes.Length - start) & ~1);
    }

    private static void PopulateBlocksFromWordText(string rawText, ParsedDocument doc)
    {
        if (string.IsNullOrEmpty(rawText))
            return;

        // Clean field instructions like HYPERLINK / TOC
        string cleaned = FieldCodeRegex().Replace(rawText, string.Empty);

        // Split by paragraph breaks (\r or \n) while detecting table rows (\x07)
        var lines = cleaned.Split('\r');
        var pendingTableRows = new List<List<string>>();
        var currentRowCells = new List<string>();

        void FlushTable()
        {
            if (pendingTableRows.Count == 0) return;
            var table = new DocTable();
            int maxCols = pendingTableRows.Max(r => r.Count);
            double colW = Math.Max(80, 620.0 / Math.Max(1, maxCols));
            for (int c = 0; c < maxCols; c++) table.ColumnWidthsDip.Add(colW);

            for (int r = 0; r < pendingTableRows.Count; r++)
            {
                var row = new DocTableRow { IsHeader = r == 0 };
                foreach (string cellText in pendingTableRows[r])
                {
                    var cell = new DocTableCell();
                    var p = new DocParagraph { MarginBottomDip = 2 };
                    p.Inlines.Add(new DocInline { Text = SanitizeDisplayText(cellText), Bold = r == 0 });
                    cell.Blocks.Add(p);
                    row.Cells.Add(cell);
                }
                table.Rows.Add(row);
            }
            doc.Blocks.Add(table);
            pendingTableRows.Clear();
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string segment = lines[i].Trim('\n');

            if (segment.Contains('\f'))
            {
                FlushTable();
                string before = SanitizeDisplayText(segment.Replace("\f", ""));
                if (!string.IsNullOrWhiteSpace(before))
                    AddParagraph(doc, before, i == 0);
                doc.Blocks.Add(new DocPageBreak());
                continue;
            }

            if (segment.Contains('\x07'))
            {
                // \x07 marks cell end; an empty segment ending in \x07 marks row end in MS-DOC
                string cellContent = segment.Replace("\x07", "");
                if (currentRowCells.Count > 0 && string.IsNullOrEmpty(cellContent))
                {
                    pendingTableRows.Add([.. currentRowCells]);
                    currentRowCells.Clear();
                }
                else
                {
                    currentRowCells.Add(cellContent);
                }
                continue;
            }

            if (currentRowCells.Count > 0)
            {
                pendingTableRows.Add([.. currentRowCells]);
                currentRowCells.Clear();
            }
            FlushTable();

            string display = SanitizeDisplayText(segment);
            if (!string.IsNullOrEmpty(display))
                AddParagraph(doc, display, isFirstParagraph: doc.Blocks.Count == 0);
        }

        if (currentRowCells.Count > 0)
            pendingTableRows.Add(currentRowCells);
        FlushTable();
    }

    private static void AddParagraph(ParsedDocument doc, string text, bool isFirstParagraph)
    {
        bool looksLikeHeading = isFirstParagraph && text.Length <= 80 && !text.EndsWith('.');
        var p = new DocParagraph
        {
            HeadingLevel = looksLikeHeading ? 1 : 0,
            MarginTopDip = looksLikeHeading ? 10 : 0,
            MarginBottomDip = 8
        };
        p.Inlines.Add(new DocInline { Text = text });
        doc.Blocks.Add(p);
    }

    private static string SanitizeDisplayText(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char ch in input)
        {
            if (ch == '\t')
                sb.Append("    ");
            else if (!char.IsControl(ch))
                sb.Append(ch);
        }
        return sb.ToString().TrimEnd();
    }

    private static List<byte[]> ExtractEmbeddedImages(byte[] data)
    {
        var images = new List<byte[]>();
        // Scan for PNG signatures (89 50 4E 47 0D 0A 1A 0A ... IEND AE 42 60 82)
        int i = 0;
        while (i <= data.Length - 16 && images.Count < 24)
        {
            if (data[i] == 0x89 && data[i + 1] == 0x50 && data[i + 2] == 0x4E && data[i + 3] == 0x47 &&
                data[i + 4] == 0x0D && data[i + 5] == 0x0A && data[i + 6] == 0x1A && data[i + 7] == 0x0A)
            {
                int end = FindBytes(data, [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82], i + 8);
                if (end > i)
                {
                    int len = (end + 8) - i;
                    if (len is > 64 and < 10_000_000)
                    {
                        images.Add(data.AsSpan(i, len).ToArray());
                        i = end + 8;
                        continue;
                    }
                }
            }
            i++;
        }
        return images;
    }

    private static int FindBytes(byte[] haystack, byte[] needle, int start)
    {
        for (int i = start; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        }
        return -1;
    }

    private static void ParseHtmlFallback(string html, ParsedDocument doc)
    {
        string stripped = HtmlTagRegex().Replace(html, "\n");
        string decoded = System.Net.WebUtility.HtmlDecode(stripped);
        foreach (string line in decoded.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            var p = new DocParagraph();
            p.Inlines.Add(new DocInline { Text = trimmed });
            doc.Blocks.Add(p);
        }
    }

    private static byte[] ReadAllBytes(CfbStream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    [GeneratedRegex(@"\x13[^\x14\x15]*(\x14([^\x15]*))?\x15", RegexOptions.Singleline)]
    private static partial Regex FieldCodeRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();
}
