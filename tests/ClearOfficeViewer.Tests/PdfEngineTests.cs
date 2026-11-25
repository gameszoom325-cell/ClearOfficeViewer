using System.IO;
using System.Text;
using System.Windows;
using ClearOfficeViewer.PdfEngine;

namespace ClearOfficeViewer.Tests;

public class PdfEngineTests
{
    [Fact]
    public async Task PdfDocumentSession_LoadsRendersSearchesAndExtractsText()
    {
        string pdfPath = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.pdf");
        try
        {
            CreateTwoPagePdf(pdfPath);
            using var session = await PdfDocumentSession.OpenAsync(pdfPath);

            Assert.Equal(2, session.PageCount);

            var bmp = await session.RenderPageAsync(0, 400);
            Assert.True(bmp.PixelWidth >= 300);
            Assert.True(bmp.PixelHeight >= 300);

            var page1Words = session.GetPageWords(0);
            Assert.NotEmpty(page1Words);

            var matches = session.Search("Quarterly");
            Assert.Single(matches);
            Assert.Equal(2, matches[0].PageNumber);

            string selected = session.GetSelectedText(0, new Rect(0, 0, 1, 1));
            Assert.Contains("ClearOffice", selected);

            var paginator = session.CreatePrintPaginator(new Size(816, 1056));
            Assert.Equal(2, paginator.PageCount);
            using var page0 = paginator.GetPage(0);
            Assert.NotNull(page0.Visual);
        }
        finally
        {
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
        }
    }

    internal static void CreateTwoPagePdf(string path)
    {
        var sb = new StringBuilder();
        var offsets = new List<int>();

        void AppendObj(string body)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append(body);
        }

        sb.Append("%PDF-1.4\n");
        AppendObj("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        AppendObj("2 0 obj\n<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>\nendobj\n");
        AppendObj("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 7 0 R >> >> >>\nendobj\n");
        AppendObj("4 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 6 0 R /Resources << /Font << /F1 7 0 R >> >> >>\nendobj\n");

        string p1Stream = "BT /F1 20 Tf 72 720 Td (ClearOffice Viewer Report) Tj 0 -32 Td /F1 12 Tf (Lightweight native PDF rendering and search.) Tj ET";
        AppendObj($"5 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(p1Stream)} >>\nstream\n{p1Stream}\nendstream\nendobj\n");

        string p2Stream = "BT /F1 18 Tf 72 720 Td (Quarterly Financial Summary) Tj 0 -28 Td /F1 12 Tf (All local files are processed securely offline.) Tj ET";
        AppendObj($"6 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(p2Stream)} >>\nstream\n{p2Stream}\nendstream\nendobj\n");

        AppendObj("7 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        int xrefOffset = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append("xref\n0 8\n0000000000 65535 f \n");
        foreach (int off in offsets)
        {
            sb.Append(off.ToString("D10")).Append(" 00000 n \n");
        }
        sb.Append("trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n").Append(xrefOffset).Append("\n%%EOF\n");

        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
    }
}
