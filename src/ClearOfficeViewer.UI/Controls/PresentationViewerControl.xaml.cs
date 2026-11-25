using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ClearOfficeViewer.Core.Models;
using ClearOfficeViewer.PresentationEngine;

namespace ClearOfficeViewer.UI.Controls;

public partial class PresentationViewerControl : UserControl, IViewerControl
{
    private ParsedPresentation? _presentation;
    private DefaultZoomBehavior? _pendingDefaultZoom;
    private int _currentIndex;
    private double _zoom = 1.0;

    public event EventHandler? StatusChanged;

    public string StatusPrimaryText => _presentation == null || _presentation.Slides.Count == 0
        ? "No presentation loaded"
        : $"Slide {_currentIndex + 1} / {_presentation.Slides.Count}";

    public double ZoomPercent => Math.Round(_zoom * 100.0);
    public bool SupportsFind => false;
    public bool SupportsPrint => true;
    public bool SupportsPresentation => _presentation != null && _presentation.Slides.Count > 0;

    public PresentationViewerControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            if (_pendingDefaultZoom.HasValue && SlideScrollViewer.ActualWidth > 120)
            {
                var dz = _pendingDefaultZoom.Value;
                _pendingDefaultZoom = null;
                if (dz == DefaultZoomBehavior.ActualSize) SetZoomPercent(100);
                else FitPage();
                return;
            }
            ApplyStageSize();
        };
    }

    public async Task LoadAsync(string filePath, DefaultZoomBehavior defaultZoom)
    {
        var pres = await Task.Run(() => PresentationLoader.Load(filePath));
        _presentation = pres;
        _currentIndex = 0;

        BuildThumbnails();
        if (SlideThumbList.Items.Count > 0)
            SlideThumbList.SelectedIndex = 0;

        if (SlideScrollViewer.ActualWidth > 120)
        {
            _pendingDefaultZoom = null;
            if (defaultZoom == DefaultZoomBehavior.ActualSize)
                SetZoomPercent(100);
            else
                FitPage();
        }
        else
        {
            _pendingDefaultZoom = defaultZoom;
            SetZoomPercent(85);
        }

        RenderCurrentSlide();
        RaiseStatusChanged();
    }

    private void BuildThumbnails()
    {
        SlideThumbList.Items.Clear();
        if (_presentation == null) return;

        foreach (var slide in _presentation.Slides)
        {
            var miniCanvas = SlideRenderer.RenderSlideCanvas(slide, _presentation.SlideWidthDip, _presentation.SlideHeightDip);
            var viewbox = new Viewbox
            {
                Child = miniCanvas,
                Stretch = Stretch.Uniform
            };

            var thumbBorder = new Border
            {
                Height = 82,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Child = viewbox
            };

            var label = new TextBlock
            {
                Text = $"{slide.SlideNumber}. {slide.SummaryTitle}",
                FontSize = 11,
                Margin = new Thickness(2, 4, 2, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var stack = new StackPanel();
            stack.Children.Add(thumbBorder);
            stack.Children.Add(label);

            SlideThumbList.Items.Add(stack);
        }
    }

    private void RenderCurrentSlide()
    {
        if (_presentation == null || _presentation.Slides.Count == 0)
        {
            SlideStageViewbox.Child = null;
            NotesBar.Visibility = Visibility.Collapsed;
            return;
        }

        _currentIndex = Math.Clamp(_currentIndex, 0, _presentation.Slides.Count - 1);
        var slide = _presentation.Slides[_currentIndex];
        SlideStageViewbox.Child = SlideRenderer.RenderSlideCanvas(slide, _presentation.SlideWidthDip, _presentation.SlideHeightDip);

        if (!string.IsNullOrWhiteSpace(slide.NotesText))
        {
            NotesTextBlock.Text = "Notes: " + slide.NotesText;
            NotesBar.Visibility = Visibility.Visible;
        }
        else
        {
            NotesBar.Visibility = Visibility.Collapsed;
        }

        ApplyStageSize();
    }

    private void ApplyStageSize()
    {
        if (_presentation == null) return;
        SlideStageFrame.Width = _presentation.SlideWidthDip * _zoom;
        SlideStageFrame.Height = _presentation.SlideHeightDip * _zoom;
    }

    public void SelectSlide(int index)
    {
        if (_presentation == null || _presentation.Slides.Count == 0) return;
        _currentIndex = Math.Clamp(index, 0, _presentation.Slides.Count - 1);
        if (SlideThumbList.SelectedIndex != _currentIndex)
            SlideThumbList.SelectedIndex = _currentIndex;
        RenderCurrentSlide();
        RaiseStatusChanged();
    }

    public void NextSlide() => SelectSlide(_currentIndex + 1);
    public void PreviousSlide() => SelectSlide(_currentIndex - 1);

    public void ZoomIn() => SetZoomPercent(ZoomPercent + 15);
    public void ZoomOut() => SetZoomPercent(ZoomPercent - 15);

    public void FitWidth()
    {
        if (_presentation == null) return;
        double availW = Math.Max(300, SlideScrollViewer.ActualWidth - 56);
        SetZoomPercent((availW / Math.Max(1, _presentation.SlideWidthDip)) * 100.0);
    }

    public void FitPage()
    {
        if (_presentation == null) return;
        double availW = Math.Max(300, SlideScrollViewer.ActualWidth - 56);
        double availH = Math.Max(220, SlideScrollViewer.ActualHeight - 56);
        double scale = Math.Min(availW / Math.Max(1, _presentation.SlideWidthDip), availH / Math.Max(1, _presentation.SlideHeightDip));
        SetZoomPercent(scale * 100.0);
    }

    public void SetZoomPercent(double percent)
    {
        _zoom = Math.Clamp(percent, 25, 300) / 100.0;
        ApplyStageSize();
        RaiseStatusChanged();
    }

    public void ShowFind() { }

    public void Print()
    {
        if (_presentation == null || _presentation.Slides.Count == 0) return;
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() == true)
        {
            var paginator = new PresentationPrintPaginator(_presentation, new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight));
            dlg.PrintDocument(paginator, System.IO.Path.GetFileName(_presentation.FilePath));
        }
    }

    public void StartPresentation()
    {
        if (_presentation == null || _presentation.Slides.Count == 0) return;
        var win = new PresentationFullscreenWindow(_presentation, _currentIndex)
        {
            Owner = Window.GetWindow(this)
        };
        win.ShowDialog();
        SelectSlide(win.CurrentIndex);
    }

    private void SlideThumbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlideThumbList.SelectedIndex >= 0 && SlideThumbList.SelectedIndex != _currentIndex)
        {
            _currentIndex = SlideThumbList.SelectedIndex;
            RenderCurrentSlide();
            RaiseStatusChanged();
        }
    }

    private void SlideScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Delta > 0) ZoomIn();
            else ZoomOut();
            e.Handled = true;
        }
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right:
            case Key.Down:
            case Key.PageDown:
                NextSlide();
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Up:
            case Key.PageUp:
                PreviousSlide();
                e.Handled = true;
                break;
            case Key.F5:
                StartPresentation();
                e.Handled = true;
                break;
        }
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _presentation = null;
        SlideThumbList.Items.Clear();
        SlideStageViewbox.Child = null;
    }

    private sealed class PresentationPrintPaginator(ParsedPresentation pres, Size pageSize) : DocumentPaginator
    {
        private Size _pageSize = pageSize;
        public override bool IsPageCountValid => true;
        public override int PageCount => pres.Slides.Count;
        public override Size PageSize { get => _pageSize; set => _pageSize = value; }
        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            var slide = pres.Slides[pageNumber];
            var canvas = SlideRenderer.RenderSlideCanvas(slide, pres.SlideWidthDip, pres.SlideHeightDip);
            var viewbox = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
            double margin = 24;
            double w = Math.Max(100, _pageSize.Width - margin * 2);
            double h = Math.Max(100, _pageSize.Height - margin * 2);
            viewbox.Measure(new Size(w, h));
            viewbox.Arrange(new Rect(margin, margin, w, h));
            return new DocumentPage(viewbox, _pageSize, new Rect(_pageSize), new Rect(_pageSize));
        }
    }
}
