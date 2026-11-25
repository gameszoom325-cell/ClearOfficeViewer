using System.Windows;
using System.Windows.Input;
using ClearOfficeViewer.PresentationEngine;

namespace ClearOfficeViewer.UI.Controls;

public partial class PresentationFullscreenWindow : Window
{
    private readonly ParsedPresentation _presentation;
    public int CurrentIndex { get; private set; }

    public PresentationFullscreenWindow(ParsedPresentation presentation, int startIndex)
    {
        InitializeComponent();
        _presentation = presentation;
        CurrentIndex = Math.Clamp(startIndex, 0, Math.Max(0, presentation.Slides.Count - 1));
        RenderSlide();
    }

    private void RenderSlide()
    {
        if (_presentation.Slides.Count == 0) return;
        var slide = _presentation.Slides[CurrentIndex];
        FullscreenViewbox.Child = SlideRenderer.RenderSlideCanvas(slide, _presentation.SlideWidthDip, _presentation.SlideHeightDip);
        CounterText.Text = $"Slide {CurrentIndex + 1} / {_presentation.Slides.Count}";
    }

    private void Advance(int delta)
    {
        int next = CurrentIndex + delta;
        if (next < 0) next = 0;
        if (next >= _presentation.Slides.Count)
        {
            Close();
            return;
        }
        CurrentIndex = next;
        RenderSlide();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right:
            case Key.Down:
            case Key.Space:
            case Key.Enter:
            case Key.PageDown:
                Advance(1);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Up:
            case Key.Back:
            case Key.PageUp:
                Advance(-1);
                e.Handled = true;
                break;
            case Key.Home:
                CurrentIndex = 0;
                RenderSlide();
                e.Handled = true;
                break;
            case Key.End:
                CurrentIndex = Math.Max(0, _presentation.Slides.Count - 1);
                RenderSlide();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void FullscreenViewbox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Advance(1);
    private void Prev_Click(object sender, RoutedEventArgs e) => Advance(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => Advance(1);
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
