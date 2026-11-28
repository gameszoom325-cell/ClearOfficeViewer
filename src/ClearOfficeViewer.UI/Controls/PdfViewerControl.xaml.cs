using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClearOfficeViewer.Core.Models;
using ClearOfficeViewer.Core.Services;
using ClearOfficeViewer.PdfEngine;

namespace ClearOfficeViewer.UI.Controls;

public partial class PdfViewerControl : UserControl, IViewerControl
{
    private PdfDocumentSession? _session;
    private readonly List<PageSlot> _pageSlots = [];
    private readonly HashSet<int> _renderingPages = [];
    private double _zoom = 1.0;
    private DefaultZoomBehavior? _pendingDefaultZoom;
    private int _currentPageNumber = 1;
    private string _selectedText = string.Empty;
    private IReadOnlyList<PdfSearchMatch> _searchMatches = [];
    private int _activeMatchIndex = -1;
    private CancellationTokenSource? _renderCts;
    private bool _isRenderLoopRunning;
    private bool _renderDirty;
    private bool _disposed;

    public event EventHandler? StatusChanged;

    public string StatusPrimaryText => _session == null
        ? "No PDF loaded"
        : $"Page {_currentPageNumber} / {_session.PageCount}";

    public double ZoomPercent => Math.Round(_zoom * 100.0);
    public bool SupportsFind => true;
    public bool SupportsPrint => true;
    public bool SupportsPresentation => false;

    public PdfViewerControl()
    {
        InitializeComponent();
        SizeChanged += OnControlSizeChanged;
    }

    private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_disposed) return;
        if (_pendingDefaultZoom.HasValue && MainScrollViewer.ActualWidth > 120)
        {
            var dz = _pendingDefaultZoom.Value;
            _pendingDefaultZoom = null;
            ApplyDefaultZoom(dz);
            return;
        }
        QueueVisibleRender(cancelExisting: false);
    }

    public async Task LoadAsync(string filePath, DefaultZoomBehavior defaultZoom, CancellationToken ct = default)
    {
        UnloadSession();

        _session = await PdfDocumentSession.OpenAsync(filePath, password: null, ct);
        if (_disposed || ct.IsCancellationRequested)
        {
            UnloadSession();
            return;
        }

        _currentPageNumber = 1;
        _selectedText = string.Empty;
        _searchMatches = [];
        _activeMatchIndex = -1;

        BuildPageSlots();

        if (MainScrollViewer.ActualWidth > 120)
        {
            _pendingDefaultZoom = null;
            ApplyDefaultZoom(defaultZoom);
        }
        else
        {
            _pendingDefaultZoom = defaultZoom;
            SetZoomPercent(100);
        }

        RaiseStatusChanged();
    }

    private void ApplyDefaultZoom(DefaultZoomBehavior defaultZoom)
    {
        switch (defaultZoom)
        {
            case DefaultZoomBehavior.FitPage:
                FitPage();
                break;
            case DefaultZoomBehavior.ActualSize:
                SetZoomPercent(100);
                break;
            default:
                FitWidth();
                break;
        }
    }

    private void BuildPageSlots()
    {
        PagesPanel.Children.Clear();
        _pageSlots.Clear();
        if (_session == null) return;

        foreach (var meta in _session.Pages)
        {
            var image = new Image
            {
                Stretch = Stretch.Fill,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var overlayCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = true,
                Cursor = Cursors.IBeam
            };

            var grid = new Grid();
            grid.Children.Add(image);
            grid.Children.Add(overlayCanvas);

            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(208, 215, 222)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 0, 14),
                Child = grid
            };

            var slot = new PageSlot(meta, border, image, overlayCanvas);
            AttachSelectionHandlers(slot);
            _pageSlots.Add(slot);
            PagesPanel.Children.Add(border);
        }

        ApplyZoomToSlots();
    }

    private void AttachSelectionHandlers(PageSlot slot)
    {
        Point? dragStart = null;

        slot.Overlay.MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            dragStart = e.GetPosition(slot.Overlay);
            slot.Overlay.CaptureMouse();
            ClearAllSelections();
            e.Handled = true;
        };

        slot.Overlay.MouseMove += (_, e) =>
        {
            if (dragStart == null || !slot.Overlay.IsMouseCaptured || _session == null)
                return;

            Point cur = e.GetPosition(slot.Overlay);
            UpdateSelectionVisual(slot, dragStart.Value, cur);
        };

        slot.Overlay.MouseLeftButtonUp += (_, e) =>
        {
            if (dragStart == null) return;
            Point end = e.GetPosition(slot.Overlay);
            slot.Overlay.ReleaseMouseCapture();
            FinalizeSelection(slot, dragStart.Value, end);
            dragStart = null;
        };
    }

    private void ClearAllSelections()
    {
        _selectedText = string.Empty;
        foreach (var slot in _pageSlots)
        {
            slot.SelectionRect = Rect.Empty;
            RedrawOverlays(slot);
        }
    }

    private void UpdateSelectionVisual(PageSlot slot, Point p1, Point p2)
    {
        double w = Math.Max(1, slot.Overlay.ActualWidth);
        double h = Math.Max(1, slot.Overlay.ActualHeight);

        double x = Math.Clamp(Math.Min(p1.X, p2.X), 0, w);
        double y = Math.Clamp(Math.Min(p1.Y, p2.Y), 0, h);
        double rw = Math.Clamp(Math.Abs(p2.X - p1.X), 1, w - x);
        double rh = Math.Clamp(Math.Abs(p2.Y - p1.Y), 1, h - y);

        slot.SelectionRect = new Rect(x / w, y / h, rw / w, rh / h);
        RedrawOverlays(slot);
    }

    private void FinalizeSelection(PageSlot slot, Point p1, Point p2)
    {
        if (_session == null) return;
        if (Math.Abs(p2.X - p1.X) < 4 && Math.Abs(p2.Y - p1.Y) < 4)
        {
            ClearAllSelections();
            return;
        }

        UpdateSelectionVisual(slot, p1, p2);
        _selectedText = _session.GetSelectedText(slot.Meta.PageIndex, slot.SelectionRect);
    }

    private void RedrawOverlays(PageSlot slot)
    {
        slot.Overlay.Children.Clear();
        double pw = slot.Border.Width;
        double ph = slot.Border.Height;
        if (double.IsNaN(pw) || double.IsNaN(ph) || pw <= 0 || ph <= 0)
            return;

        // 1. Search highlights
        if (_searchMatches.Count > 0)
        {
            for (int mIdx = 0; mIdx < _searchMatches.Count; mIdx++)
            {
                var match = _searchMatches[mIdx];
                if (match.PageIndex != slot.Meta.PageIndex) continue;

                bool isActive = mIdx == _activeMatchIndex;
                var fill = isActive
                    ? new SolidColorBrush(Color.FromArgb(135, 234, 88, 12))
                    : new SolidColorBrush(Color.FromArgb(95, 250, 204, 21));

                foreach (var box in match.Boxes)
                {
                    var pxRect = box.ToPixelRect(pw, ph);
                    var rect = new Rectangle
                    {
                        Width = Math.Max(2, pxRect.Width),
                        Height = Math.Max(2, pxRect.Height),
                        Fill = fill
                    };
                    Canvas.SetLeft(rect, pxRect.X);
                    Canvas.SetTop(rect, pxRect.Y);
                    slot.Overlay.Children.Add(rect);
                }
            }
        }

        // 2. User text selection highlights
        if (!slot.SelectionRect.IsEmpty && _session != null)
        {
            var words = _session.GetPageWords(slot.Meta.PageIndex);
            var selFill = new SolidColorBrush(Color.FromArgb(85, 15, 108, 189));
            bool drewWord = false;
            foreach (var w in words)
            {
                var wr = new Rect(w.NormX, w.NormY, w.NormWidth, w.NormHeight);
                if (!wr.IntersectsWith(slot.SelectionRect)) continue;

                drewWord = true;
                var pxRect = w.ToPixelRect(pw, ph);
                var rect = new Rectangle
                {
                    Width = Math.Max(2, pxRect.Width),
                    Height = Math.Max(2, pxRect.Height),
                    Fill = selFill
                };
                Canvas.SetLeft(rect, pxRect.X);
                Canvas.SetTop(rect, pxRect.Y);
                slot.Overlay.Children.Add(rect);
            }

            if (!drewWord)
            {
                // Fallback marquee rectangle for scanned/image-only pages
                var rect = new Rectangle
                {
                    Width = slot.SelectionRect.Width * pw,
                    Height = slot.SelectionRect.Height * ph,
                    Fill = new SolidColorBrush(Color.FromArgb(45, 15, 108, 189)),
                    Stroke = new SolidColorBrush(Color.FromArgb(160, 15, 108, 189)),
                    StrokeThickness = 1
                };
                Canvas.SetLeft(rect, slot.SelectionRect.X * pw);
                Canvas.SetTop(rect, slot.SelectionRect.Y * ph);
                slot.Overlay.Children.Add(rect);
            }
        }
    }

    private void ApplyZoomToSlots()
    {
        foreach (var slot in _pageSlots)
        {
            double w = slot.Meta.WidthDip * _zoom;
            double h = slot.Meta.HeightDip * _zoom;
            slot.Border.Width = w;
            slot.Border.Height = h;
            slot.Overlay.Width = w;
            slot.Overlay.Height = h;
            slot.RenderedZoom = -1;
            RedrawOverlays(slot);
        }

        QueueVisibleRender(cancelExisting: true);
    }

    private void QueueVisibleRender(bool cancelExisting = false)
    {
        if (_session == null || _pageSlots.Count == 0 || _disposed)
            return;

        if (cancelExisting)
        {
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = null;
            _isRenderLoopRunning = false;
        }

        _renderDirty = true;
        if (_isRenderLoopRunning)
            return;

        _renderCts ??= new CancellationTokenSource();
        var ct = _renderCts.Token;
        _isRenderLoopRunning = true;
        _ = RunRenderLoopAsync(ct);
    }

    private async Task RunRenderLoopAsync(CancellationToken ct)
    {
        try
        {
            while (_renderDirty && !ct.IsCancellationRequested && !_disposed && _session != null)
            {
                _renderDirty = false;
                await RenderVisiblePagesAsync(ct);
            }
        }
        finally
        {
            _isRenderLoopRunning = false;
        }
    }

    private async Task RenderVisiblePagesAsync(CancellationToken ct)
    {
        if (_session == null || _disposed) return;

        // Allow layout pass to settle
        await System.Windows.Threading.Dispatcher.Yield();
        if (ct.IsCancellationRequested || _session == null || _disposed) return;

        double viewportTop = MainScrollViewer.VerticalOffset;
        double viewportHeight = MainScrollViewer.ViewportHeight > 0 ? MainScrollViewer.ViewportHeight : 700;
        double viewportBottom = viewportTop + viewportHeight;

        double yCursor = 0;
        var visible = new List<int>();
        int firstVisiblePage = 1;
        bool foundFirstVisible = false;

        for (int i = 0; i < _pageSlots.Count; i++)
        {
            var slot = _pageSlots[i];
            double h = slot.Border.Height + slot.Border.Margin.Bottom;
            double top = yCursor;
            double bottom = yCursor + h;

            if (bottom >= viewportTop && top <= viewportBottom)
            {
                if (!foundFirstVisible)
                {
                    firstVisiblePage = i + 1;
                    foundFirstVisible = true;
                }
                visible.Add(i);
            }
            else if (bottom >= viewportTop - viewportHeight && top <= viewportBottom + viewportHeight)
            {
                visible.Add(i);
            }

            yCursor = bottom;
        }

        if (foundFirstVisible && _currentPageNumber != firstVisiblePage)
        {
            _currentPageNumber = firstVisiblePage;
            RaiseStatusChanged();
        }

        // Release distant offscreen bitmaps to keep memory footprint low
        if (visible.Count > 0)
        {
            int minKeep = Math.Max(0, visible.Min() - 4);
            int maxKeep = Math.Min(_pageSlots.Count - 1, visible.Max() + 4);
            for (int i = 0; i < _pageSlots.Count; i++)
            {
                if (i < minKeep || i > maxKeep)
                {
                    _pageSlots[i].Image.Source = null;
                    _pageSlots[i].RenderedZoom = -1;
                }
            }
        }

        double dpiScale = PresentationSource.FromVisual(this) != null
            ? VisualTreeHelper.GetDpi(this).DpiScaleX
            : 1.0;

        foreach (int idx in visible)
        {
            if (ct.IsCancellationRequested || _session == null || _disposed) return;
            var slot = _pageSlots[idx];
            if (Math.Abs(slot.RenderedZoom - _zoom) < 0.01 && slot.Image.Source != null)
                continue;

            if (!_renderingPages.Add(idx))
                continue;

            try
            {
                uint targetPx = (uint)Math.Clamp(Math.Round(slot.Meta.WidthDip * _zoom * dpiScale), 240, 2800);
                var bmp = await _session.RenderPageAsync(idx, targetPx, ct);
                if (!ct.IsCancellationRequested && !_disposed)
                {
                    slot.Image.Source = bmp;
                    slot.RenderedZoom = _zoom;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn(nameof(PdfViewerControl), $"Render page {idx + 1} warning: {ex.Message}");
            }
            finally
            {
                _renderingPages.Remove(idx);
            }
        }
    }

    public void ZoomIn() => SetZoomPercent(ZoomPercent + 15);
    public void ZoomOut() => SetZoomPercent(ZoomPercent - 15);

    public void FitWidth()
    {
        if (_session == null || _pageSlots.Count == 0) return;
        double available = Math.Max(300, MainScrollViewer.ActualWidth - 72);
        double maxPageW = _pageSlots.Max(s => s.Meta.WidthDip);
        SetZoomPercent((available / Math.Max(1, maxPageW)) * 100.0);
    }

    public void FitPage()
    {
        if (_session == null || _pageSlots.Count == 0) return;
        double availW = Math.Max(300, MainScrollViewer.ActualWidth - 72);
        double availH = Math.Max(300, MainScrollViewer.ActualHeight - 48);
        var current = _pageSlots[Math.Clamp(_currentPageNumber - 1, 0, _pageSlots.Count - 1)].Meta;
        double scale = Math.Min(availW / Math.Max(1, current.WidthDip), availH / Math.Max(1, current.HeightDip));
        SetZoomPercent(scale * 100.0);
    }

    public void SetZoomPercent(double percent)
    {
        double clamped = Math.Clamp(percent, 25, 400);
        _zoom = clamped / 100.0;
        ApplyZoomToSlots();
        RaiseStatusChanged();
    }

    public void ShowFind()
    {
        FindBar.Visibility = Visibility.Visible;
        FindTextBox.Focus();
        FindTextBox.SelectAll();
    }

    public void Print()
    {
        if (_session == null) return;
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() == true)
        {
            var paginator = _session.CreatePrintPaginator(new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight));
            dlg.PrintDocument(paginator, System.IO.Path.GetFileName(_session.FilePath));
        }
    }

    public void StartPresentation() { }

    private void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        QueueVisibleRender();
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Delta > 0) ZoomIn();
            else ZoomOut();
            e.Handled = true;
        }
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e) => ExecuteSearch();

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                NavigateMatch(-1);
            else
                NavigateMatch(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFindBar();
            e.Handled = true;
        }
    }

    private void ExecuteSearch()
    {
        if (_session == null) return;
        string q = FindTextBox.Text;
        _searchMatches = string.IsNullOrWhiteSpace(q) ? [] : _session.Search(q);
        _activeMatchIndex = _searchMatches.Count > 0 ? 0 : -1;
        UpdateFindUiAndScroll();
    }

    private void NavigateMatch(int delta)
    {
        if (_searchMatches.Count == 0) return;
        _activeMatchIndex = (_activeMatchIndex + delta + _searchMatches.Count) % _searchMatches.Count;
        UpdateFindUiAndScroll();
    }

    private void UpdateFindUiAndScroll()
    {
        FindCountText.Text = _searchMatches.Count == 0
            ? "0 / 0"
            : $"{_activeMatchIndex + 1} / {_searchMatches.Count}";

        foreach (var slot in _pageSlots)
            RedrawOverlays(slot);

        if (_activeMatchIndex >= 0 && _activeMatchIndex < _searchMatches.Count)
        {
            var match = _searchMatches[_activeMatchIndex];
            ScrollToPage(match.PageIndex);
        }
    }

    private void ScrollToPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pageSlots.Count) return;
        double y = 0;
        for (int i = 0; i < pageIndex; i++)
            y += _pageSlots[i].Border.Height + _pageSlots[i].Border.Margin.Bottom;
        MainScrollViewer.ScrollToVerticalOffset(y);
    }

    private void CloseFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        _searchMatches = [];
        _activeMatchIndex = -1;
        foreach (var slot in _pageSlots)
            RedrawOverlays(slot);
    }

    private void FindPrev_Click(object sender, RoutedEventArgs e) => NavigateMatch(-1);
    private void FindNext_Click(object sender, RoutedEventArgs e) => NavigateMatch(1);
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFindBar();

    private void OnCopyCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !string.IsNullOrEmpty(_selectedText);
    private void OnCopyExecuted(object sender, ExecutedRoutedEventArgs e) => CopySelectedText();
    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e) => ShowFind();
    private void OnPrintExecuted(object sender, ExecutedRoutedEventArgs e) => Print();

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e) => CopySelectedText();
    private void ClearSelectionMenuItem_Click(object sender, RoutedEventArgs e) => ClearAllSelections();
    private void FindMenuItem_Click(object sender, RoutedEventArgs e) => ShowFind();
    private void PrintMenuItem_Click(object sender, RoutedEventArgs e) => Print();

    private void CopySelectedText()
    {
        if (string.IsNullOrEmpty(_selectedText))
            return;

        try
        {
            Clipboard.SetDataObject(_selectedText, true);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn(nameof(PdfViewerControl), $"Clipboard copy failed: {ex.Message}");
        }
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private void UnloadSession()
    {
        _renderDirty = false;
        _isRenderLoopRunning = false;
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        _renderingPages.Clear();

        foreach (var slot in _pageSlots)
        {
            if (slot.Overlay.IsMouseCaptured)
                slot.Overlay.ReleaseMouseCapture();
            slot.Image.Source = null;
            slot.Overlay.Children.Clear();
        }

        _session?.Dispose();
        _session = null;
        _searchMatches = [];
        _activeMatchIndex = -1;
        _selectedText = string.Empty;
        _pageSlots.Clear();
        PagesPanel.Children.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SizeChanged -= OnControlSizeChanged;
        UnloadSession();
    }

    private sealed class PageSlot(PdfPageMetadata meta, Border border, Image image, Canvas overlay)
    {
        public PdfPageMetadata Meta { get; } = meta;
        public Border Border { get; } = border;
        public Image Image { get; } = image;
        public Canvas Overlay { get; } = overlay;
        public double RenderedZoom { get; set; } = -1;
        public Rect SelectionRect { get; set; } = Rect.Empty;
    }
}
