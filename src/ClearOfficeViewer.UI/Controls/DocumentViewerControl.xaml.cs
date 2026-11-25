using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ClearOfficeViewer.Core.Models;
using ClearOfficeViewer.DocumentEngine;

namespace ClearOfficeViewer.UI.Controls;

public partial class DocumentViewerControl : UserControl, IViewerControl
{
    private ParsedDocument? _parsed;
    private DefaultZoomBehavior? _pendingDefaultZoom;
    private double _zoom = 1.0;
    private int _totalPages = 1;
    private int _currentPage = 1;
    private readonly List<TextRange> _searchHits = [];
    private int _activeHitIndex = -1;

    public event EventHandler? StatusChanged;

    public string StatusPrimaryText => _parsed == null
        ? "No document loaded"
        : $"Page {_currentPage} / {_totalPages}  ·  {_parsed.WordCount:N0} words";

    public double ZoomPercent => Math.Round(_zoom * 100.0);
    public bool SupportsFind => true;
    public bool SupportsPrint => true;
    public bool SupportsPresentation => false;

    public DocumentViewerControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            if (_pendingDefaultZoom.HasValue && OuterScrollViewer.ActualWidth > 120)
            {
                var dz = _pendingDefaultZoom.Value;
                _pendingDefaultZoom = null;
                ApplyDefaultZoom(dz);
            }
        };
    }

    public async Task LoadAsync(string filePath, DefaultZoomBehavior defaultZoom)
    {
        var parsed = await Task.Run(() => DocumentLoader.Load(filePath));
        _parsed = parsed;

        var flow = DocumentLoader.ToFlowDocument(parsed, forPrint: false);
        DocRichTextBox.Document = flow;
        DocRichTextBox.Width = parsed.PageWidthDip;
        PaperSheetBorder.Width = parsed.PageWidthDip + 2;
        PaperSheetBorder.MinHeight = parsed.PageHeightDip;

        // Compute deterministic page count via FlowDocument paginator
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = new Size(parsed.PageWidthDip, parsed.PageHeightDip);
        paginator.ComputePageCount();
        _totalPages = Math.Max(1, paginator.PageCount);
        _currentPage = 1;

        _searchHits.Clear();
        _activeHitIndex = -1;

        if (OuterScrollViewer.ActualWidth > 120)
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

    public void ZoomIn() => SetZoomPercent(ZoomPercent + 15);
    public void ZoomOut() => SetZoomPercent(ZoomPercent - 15);

    public void FitWidth()
    {
        if (_parsed == null) return;
        double avail = Math.Max(300, OuterScrollViewer.ActualWidth - 72);
        double target = Math.Clamp((avail / Math.Max(1, _parsed.PageWidthDip)) * 100.0, 50, 150);
        SetZoomPercent(target);
    }

    public void FitPage()
    {
        if (_parsed == null) return;
        double availW = Math.Max(300, OuterScrollViewer.ActualWidth - 72);
        double availH = Math.Max(300, OuterScrollViewer.ActualHeight - 48);
        double scale = Math.Min(availW / Math.Max(1, _parsed.PageWidthDip), availH / Math.Max(1, _parsed.PageHeightDip));
        SetZoomPercent(scale * 100.0);
    }

    public void SetZoomPercent(double percent)
    {
        double clamped = Math.Clamp(percent, 25, 350);
        _zoom = clamped / 100.0;
        PaperScale.ScaleX = _zoom;
        PaperScale.ScaleY = _zoom;
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
        if (_parsed == null) return;
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() == true)
        {
            var printDoc = DocumentLoader.ToFlowDocument(_parsed, forPrint: true);
            printDoc.PageWidth = dlg.PrintableAreaWidth;
            printDoc.PageHeight = dlg.PrintableAreaHeight;
            var paginator = ((IDocumentPaginatorSource)printDoc).DocumentPaginator;
            dlg.PrintDocument(paginator, System.IO.Path.GetFileName(_parsed.FilePath));
        }
    }

    public void StartPresentation() { }

    private void OuterScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_parsed == null || _totalPages <= 1) return;
        double maxScroll = Math.Max(1, OuterScrollViewer.ScrollableHeight);
        double progress = OuterScrollViewer.VerticalOffset / maxScroll;
        int estimatedPage = Math.Clamp((int)Math.Floor(progress * (_totalPages - 1)) + 1, 1, _totalPages);
        if (estimatedPage != _currentPage)
        {
            _currentPage = estimatedPage;
            RaiseStatusChanged();
        }
    }

    private void OuterScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Delta > 0) ZoomIn();
            else ZoomOut();
            e.Handled = true;
        }
    }

    private void FindTextBox_TextChanged(object sender, TextChangedEventArgs e) => ExecuteFind();

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateHit((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFindBar();
            e.Handled = true;
        }
    }

    private void ExecuteFind()
    {
        ClearSearchHighlights();
        string query = FindTextBox.Text;
        if (string.IsNullOrWhiteSpace(query) || DocRichTextBox.Document == null)
        {
            FindCountText.Text = "0 / 0";
            return;
        }

        TextPointer? pos = DocRichTextBox.Document.ContentStart;
        while (pos != null && pos.CompareTo(DocRichTextBox.Document.ContentEnd) < 0)
        {
            if (pos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                string runText = pos.GetTextInRun(LogicalDirection.Forward);
                int idx = 0;
                while (idx < runText.Length)
                {
                    int found = runText.IndexOf(query, idx, StringComparison.OrdinalIgnoreCase);
                    if (found < 0) break;

                    var start = pos.GetPositionAtOffset(found);
                    var end = pos.GetPositionAtOffset(found + query.Length);
                    if (start != null && end != null)
                    {
                        var range = new TextRange(start, end);
                        range.ApplyPropertyValue(TextElement.BackgroundProperty, new SolidColorBrush(Color.FromArgb(140, 250, 204, 21)));
                        _searchHits.Add(range);
                    }
                    idx = found + query.Length;
                }
            }
            pos = pos.GetNextContextPosition(LogicalDirection.Forward);
        }

        _activeHitIndex = _searchHits.Count > 0 ? 0 : -1;
        UpdateActiveHit();
    }

    private void NavigateHit(int delta)
    {
        if (_searchHits.Count == 0) return;
        _activeHitIndex = (_activeHitIndex + delta + _searchHits.Count) % _searchHits.Count;
        UpdateActiveHit();
    }

    private void UpdateActiveHit()
    {
        FindCountText.Text = _searchHits.Count == 0 ? "0 / 0" : $"{_activeHitIndex + 1} / {_searchHits.Count}";
        for (int i = 0; i < _searchHits.Count; i++)
        {
            var brush = i == _activeHitIndex
                ? new SolidColorBrush(Color.FromArgb(180, 234, 88, 12))
                : new SolidColorBrush(Color.FromArgb(140, 250, 204, 21));
            _searchHits[i].ApplyPropertyValue(TextElement.BackgroundProperty, brush);
        }

        if (_activeHitIndex >= 0 && _activeHitIndex < _searchHits.Count)
        {
            var hit = _searchHits[_activeHitIndex];
            DocRichTextBox.Selection.Select(hit.Start, hit.End);
            var rect = hit.Start.GetCharacterRect(LogicalDirection.Forward);
            double targetY = rect.Top * _zoom;
            OuterScrollViewer.ScrollToVerticalOffset(Math.Max(0, targetY - 120));
        }
    }

    private void ClearSearchHighlights()
    {
        foreach (var hit in _searchHits)
            hit.ApplyPropertyValue(TextElement.BackgroundProperty, DependencyProperty.UnsetValue);
        _searchHits.Clear();
        _activeHitIndex = -1;
    }

    private void CloseFindBar()
    {
        FindBar.Visibility = Visibility.Collapsed;
        ClearSearchHighlights();
    }

    private void FindPrev_Click(object sender, RoutedEventArgs e) => NavigateHit(-1);
    private void FindNext_Click(object sender, RoutedEventArgs e) => NavigateHit(1);
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFindBar();
    private void OnFindExecuted(object sender, ExecutedRoutedEventArgs e) => ShowFind();
    private void OnPrintExecuted(object sender, ExecutedRoutedEventArgs e) => Print();

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _parsed = null;
        _searchHits.Clear();
    }
}
