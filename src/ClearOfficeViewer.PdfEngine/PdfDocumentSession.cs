using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClearOfficeViewer.Core.Services;
using UglyToad.PdfPig.Content;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;
using PigPdfDocument = UglyToad.PdfPig.PdfDocument;
using WinPdfDocument = Windows.Data.Pdf.PdfDocument;

namespace ClearOfficeViewer.PdfEngine;

public sealed class PdfDocumentSession : IDisposable
{
    private readonly string _filePath;
    private readonly WinPdfDocument _winPdf;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly Dictionary<int, IReadOnlyList<PdfWordBox>> _wordCache = [];
    private readonly object _pigLock = new();
    private PigPdfDocument? _pigPdf;
    private bool _disposed;

    public string FilePath => _filePath;
    public int PageCount => (int)_winPdf.PageCount;
    public IReadOnlyList<PdfPageMetadata> Pages { get; }

    private PdfDocumentSession(string filePath, WinPdfDocument winPdf, PigPdfDocument? pigPdf)
    {
        _filePath = filePath;
        _winPdf = winPdf;
        _pigPdf = pigPdf;

        var pages = new List<PdfPageMetadata>(PageCount);
        for (uint i = 0; i < _winPdf.PageCount; i++)
        {
            using var page = _winPdf.GetPage(i);
            var size = page.Size;
            pages.Add(new PdfPageMetadata(
                PageIndex: (int)i,
                PageNumber: (int)i + 1,
                WidthDip: size.Width > 0 ? size.Width : 612,
                HeightDip: size.Height > 0 ? size.Height : 792));
        }
        Pages = pages;
    }

    public static async Task<PdfDocumentSession> OpenAsync(string filePath, string? password = null, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("PDF file not found.", filePath);

        string fullPath = Path.GetFullPath(filePath);
        WinPdfDocument winPdf;
        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(fullPath).AsTask(ct);
            winPdf = string.IsNullOrEmpty(password)
                ? await WinPdfDocument.LoadFromFileAsync(storageFile).AsTask(ct)
                : await WinPdfDocument.LoadFromFileAsync(storageFile, password).AsTask(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallback via stream in case StorageFile path broker has restrictions
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            await fs.CopyToAsync(ms, ct);
            ms.Position = 0;
            var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(ms.ToArray().AsBuffer()).AsTask(ct);
            ras.Seek(0);
            winPdf = string.IsNullOrEmpty(password)
                ? await WinPdfDocument.LoadFromStreamAsync(ras).AsTask(ct)
                : await WinPdfDocument.LoadFromStreamAsync(ras, password).AsTask(ct);
        }

        PigPdfDocument? pigPdf = null;
        try
        {
            var parsingOptions = string.IsNullOrEmpty(password)
                ? new UglyToad.PdfPig.ParsingOptions { SkipMissingFonts = true }
                : new UglyToad.PdfPig.ParsingOptions { Password = password, SkipMissingFonts = true };
            pigPdf = PigPdfDocument.Open(fullPath, parsingOptions);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn(nameof(PdfDocumentSession), $"PdfPig text layer unavailable for '{fullPath}': {ex.Message}");
        }

        return new PdfDocumentSession(fullPath, winPdf, pigPdf);
    }

    public async Task<BitmapSource> RenderPageAsync(int pageIndex, uint destinationWidthPx, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageIndex < 0 || pageIndex >= PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        await _renderGate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var page = _winPdf.GetPage((uint)pageIndex);
            var size = page.Size;
            uint safeWidth = Math.Clamp(destinationWidthPx, 64u, 4096u);
            uint safeHeight = (uint)Math.Max(64, Math.Round(safeWidth * (size.Height / Math.Max(1.0, size.Width))));

            using var ras = new InMemoryRandomAccessStream();
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = safeWidth,
                DestinationHeight = safeHeight,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
            };

            await page.RenderToStreamAsync(ras, options).AsTask(ct);
            ras.Seek(0);

            using var netStream = ras.AsStreamForRead();
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = netStream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        finally
        {
            _renderGate.Release();
        }
    }

    public IReadOnlyList<PdfWordBox> GetPageWords(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageIndex < 0 || pageIndex >= PageCount)
            return [];

        lock (_pigLock)
        {
            if (_wordCache.TryGetValue(pageIndex, out var cached))
                return cached;

            if (_pigPdf == null)
            {
                _wordCache[pageIndex] = [];
                return [];
            }

            try
            {
                Page pigPage = _pigPdf.GetPage(pageIndex + 1);
                double pw = pigPage.Width > 0 ? pigPage.Width : 1;
                double ph = pigPage.Height > 0 ? pigPage.Height : 1;

                var list = new List<PdfWordBox>();
                foreach (var word in pigPage.GetWords())
                {
                    if (string.IsNullOrWhiteSpace(word.Text))
                        continue;

                    var r = word.BoundingBox;
                    double nx = Math.Clamp(r.Left / pw, 0, 1);
                    double ny = Math.Clamp(1.0 - (r.Top / ph), 0, 1);
                    double nw = Math.Clamp(r.Width / pw, 0.001, 1 - nx);
                    double nh = Math.Clamp(r.Height / ph, 0.002, 1 - ny);

                    list.Add(new PdfWordBox(word.Text, nx, ny, nw, nh));
                }

                _wordCache[pageIndex] = list;
                return list;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn(nameof(PdfDocumentSession), $"Failed extracting words on page {pageIndex + 1}: {ex.Message}");
                _wordCache[pageIndex] = [];
                return [];
            }
        }
    }

    public string GetSelectedText(int pageIndex, Rect normalizedRect)
    {
        var words = GetPageWords(pageIndex);
        if (words.Count == 0)
            return string.Empty;

        var selected = new List<PdfWordBox>();
        foreach (var w in words)
        {
            var wr = new Rect(w.NormX, w.NormY, w.NormWidth, w.NormHeight);
            if (wr.IntersectsWith(normalizedRect))
            {
                selected.Add(w);
            }
        }

        if (selected.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        double? lastY = null;
        foreach (var w in selected)
        {
            if (lastY.HasValue && Math.Abs(w.NormY - lastY.Value) > Math.Max(w.NormHeight * 0.65, 0.012))
            {
                sb.AppendLine();
            }
            else if (sb.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append(w.Text);
            lastY = w.NormY;
        }
        return sb.ToString();
    }

    public IReadOnlyList<PdfSearchMatch> Search(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        string[] tokens = query.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return [];

        var matches = new List<PdfSearchMatch>();

        for (int p = 0; p < PageCount; p++)
        {
            ct.ThrowIfCancellationRequested();
            var words = GetPageWords(p);
            if (words.Count == 0)
                continue;

            if (tokens.Length == 1)
            {
                string term = tokens[0];
                for (int i = 0; i < words.Count; i++)
                {
                    if (words[i].Text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        string snippet = BuildSnippet(words, i, i);
                        matches.Add(new PdfSearchMatch(p, p + 1, [words[i]], snippet));
                    }
                }
            }
            else
            {
                for (int i = 0; i <= words.Count - tokens.Length; i++)
                {
                    bool allMatch = true;
                    for (int t = 0; t < tokens.Length; t++)
                    {
                        string wText = words[i + t].Text;
                        bool ok = t == 0
                            ? wText.EndsWith(tokens[t], StringComparison.OrdinalIgnoreCase) || wText.Contains(tokens[t], StringComparison.OrdinalIgnoreCase)
                            : t == tokens.Length - 1
                                ? wText.StartsWith(tokens[t], StringComparison.OrdinalIgnoreCase) || wText.Contains(tokens[t], StringComparison.OrdinalIgnoreCase)
                                : string.Equals(wText.Trim('.', ',', ';', ':', '!', '?'), tokens[t], StringComparison.OrdinalIgnoreCase);

                        if (!ok)
                        {
                            allMatch = false;
                            break;
                        }
                    }

                    if (allMatch)
                    {
                        var span = words.Skip(i).Take(tokens.Length).ToArray();
                        matches.Add(new PdfSearchMatch(p, p + 1, span, BuildSnippet(words, i, i + tokens.Length - 1)));
                    }
                }
            }
        }

        return matches;
    }

    public DocumentPaginator CreatePrintPaginator(Size pageSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new PdfPrintPaginator(this, pageSize);
    }

    private static string BuildSnippet(IReadOnlyList<PdfWordBox> words, int startIdx, int endIdx)
    {
        int from = Math.Max(0, startIdx - 3);
        int to = Math.Min(words.Count - 1, endIdx + 3);
        return string.Join(' ', words.Skip(from).Take(to - from + 1).Select(w => w.Text));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_pigLock)
        {
            _pigPdf?.Dispose();
            _pigPdf = null;
        }
        _renderGate.Dispose();
    }

    private sealed class PdfPrintPaginator : DocumentPaginator
    {
        private readonly PdfDocumentSession _session;
        private Size _pageSize;

        public PdfPrintPaginator(PdfDocumentSession session, Size pageSize)
        {
            _session = session;
            _pageSize = pageSize;
        }

        public override bool IsPageCountValid => true;
        public override int PageCount => _session.PageCount;

        public override Size PageSize
        {
            get => _pageSize;
            set => _pageSize = value;
        }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            // Render synchronously on print thread at ~200 DPI
            uint renderWidth = (uint)Math.Clamp(Math.Round(_pageSize.Width / 96.0 * 200.0), 800, 3000);
            var bmp = Task.Run(() => _session.RenderPageAsync(pageNumber, renderWidth)).GetAwaiter().GetResult();

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double scale = Math.Min(_pageSize.Width / bmp.PixelWidth, _pageSize.Height / bmp.PixelHeight);
                double drawW = bmp.PixelWidth * scale;
                double drawH = bmp.PixelHeight * scale;
                double x = (_pageSize.Width - drawW) / 2.0;
                double y = (_pageSize.Height - drawH) / 2.0;
                dc.DrawImage(bmp, new Rect(x, y, drawW, drawH));
            }

            return new DocumentPage(dv, _pageSize, new Rect(_pageSize), new Rect(_pageSize));
        }
    }
}
