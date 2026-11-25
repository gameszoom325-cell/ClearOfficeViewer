using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ClearOfficeViewer.PresentationEngine;

namespace ClearOfficeViewer.UI.Controls;

public static class SlideRenderer
{
    public static Canvas RenderSlideCanvas(ParsedSlide slide, double slideWidthDip, double slideHeightDip)
    {
        var canvas = new Canvas
        {
            Width = slideWidthDip,
            Height = slideHeightDip,
            ClipToBounds = true,
            Background = ParseBrush(slide.BackgroundHex, Brushes.White)
        };

        if (slide.BackgroundImageBytes is { Length: > 0 })
        {
            var bgBmp = LoadBitmap(slide.BackgroundImageBytes);
            if (bgBmp != null)
            {
                var bgImg = new Image
                {
                    Source = bgBmp,
                    Width = slideWidthDip,
                    Height = slideHeightDip,
                    Stretch = Stretch.UniformToFill
                };
                canvas.Children.Add(bgImg);
            }
        }

        foreach (var element in slide.Elements)
        {
            switch (element)
            {
                case SlideImageElement imgEl:
                {
                    var bmp = LoadBitmap(imgEl.ImageBytes);
                    if (bmp == null) break;
                    var img = new Image
                    {
                        Source = bmp,
                        Width = Math.Max(1, imgEl.Width),
                        Height = Math.Max(1, imgEl.Height),
                        Stretch = Stretch.Uniform
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    Canvas.SetLeft(img, imgEl.X);
                    Canvas.SetTop(img, imgEl.Y);
                    canvas.Children.Add(img);
                    break;
                }

                case SlideTextShapeElement shapeEl:
                {
                    var container = RenderShapeElement(shapeEl);
                    Canvas.SetLeft(container, shapeEl.X);
                    Canvas.SetTop(container, shapeEl.Y);
                    canvas.Children.Add(container);
                    break;
                }

                case SlideTableElement tblEl:
                {
                    var tblControl = RenderTableElement(tblEl);
                    Canvas.SetLeft(tblControl, tblEl.X);
                    Canvas.SetTop(tblControl, tblEl.Y);
                    canvas.Children.Add(tblControl);
                    break;
                }
            }
        }

        return canvas;
    }

    private static FrameworkElement RenderShapeElement(SlideTextShapeElement el)
    {
        double w = Math.Max(8, el.Width);
        double h = Math.Max(8, el.Height);

        Brush? fill = !string.IsNullOrEmpty(el.FillHex) ? ParseBrush(el.FillHex, Brushes.Transparent) : null;
        Brush? stroke = !string.IsNullOrEmpty(el.StrokeHex) ? ParseBrush(el.StrokeHex, Brushes.Transparent) : null;

        if (el.ShapeKind == SlideShapeKind.Line)
        {
            return new Line
            {
                X1 = 0,
                Y1 = 0,
                X2 = w,
                Y2 = h,
                Stroke = stroke ?? new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                StrokeThickness = Math.Max(1.5, el.StrokeThicknessDip)
            };
        }

        var grid = new Grid { Width = w, Height = h };

        if (el.ShapeKind == SlideShapeKind.Ellipse)
        {
            grid.Children.Add(new Ellipse
            {
                Fill = fill ?? Brushes.Transparent,
                Stroke = stroke,
                StrokeThickness = el.StrokeThicknessDip
            });
        }
        else if (fill != null || stroke != null)
        {
            grid.Children.Add(new Border
            {
                Background = fill ?? Brushes.Transparent,
                BorderBrush = stroke,
                BorderThickness = new Thickness(el.StrokeThicknessDip),
                CornerRadius = el.ShapeKind == SlideShapeKind.RoundedRectangle ? new CornerRadius(10) : new CornerRadius(0)
            });
        }

        if (el.Paragraphs.Count > 0)
        {
            var stack = new StackPanel
            {
                Margin = new Thickness(8, 6, 8, 6),
                VerticalAlignment = VerticalAlignment.Top
            };

            foreach (var p in el.Paragraphs)
            {
                var tb = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(p.Level * 18, 2, 0, 4),
                    TextAlignment = p.Alignment switch
                    {
                        SlideTextAlignment.Center => TextAlignment.Center,
                        SlideTextAlignment.Right => TextAlignment.Right,
                        SlideTextAlignment.Justify => TextAlignment.Justify,
                        _ => TextAlignment.Left
                    }
                };

                if (!string.IsNullOrEmpty(p.BulletPrefix))
                {
                    var firstRun = p.Runs.FirstOrDefault();
                    tb.Inlines.Add(new Run(p.BulletPrefix)
                    {
                        FontSize = (firstRun?.FontSizePt ?? 18) * (96.0 / 72.0),
                        Foreground = ParseBrush(firstRun?.ColorHex ?? "#1F2937", Brushes.Black),
                        FontWeight = FontWeights.SemiBold
                    });
                }

                foreach (var r in p.Runs)
                {
                    var run = new Run(r.Text)
                    {
                        FontSize = Math.Max(8, r.FontSizePt * (96.0 / 72.0)),
                        FontWeight = r.Bold ? FontWeights.Bold : FontWeights.Normal,
                        FontStyle = r.Italic ? FontStyles.Italic : FontStyles.Normal,
                        Foreground = ParseBrush(r.ColorHex, Brushes.Black)
                    };
                    if (!string.IsNullOrWhiteSpace(r.FontFamily))
                        run.FontFamily = new FontFamily(r.FontFamily);
                    if (r.Underline)
                        run.TextDecorations = TextDecorations.Underline;

                    tb.Inlines.Add(run);
                }

                if (tb.Inlines.Count > 0)
                    stack.Children.Add(tb);
            }

            grid.Children.Add(stack);
        }

        return grid;
    }

    private static FrameworkElement RenderTableElement(SlideTableElement tbl)
    {
        var grid = new Grid
        {
            Width = Math.Max(60, tbl.Width),
            Background = Brushes.White
        };

        int cols = tbl.ColumnWidthsDip.Count > 0
            ? tbl.ColumnWidthsDip.Count
            : (tbl.Rows.Count > 0 ? tbl.Rows.Max(r => r.Cells.Sum(c => c.ColumnSpan)) : 1);

        for (int c = 0; c < cols; c++)
        {
            double w = c < tbl.ColumnWidthsDip.Count && tbl.ColumnWidthsDip[c] > 0
                ? tbl.ColumnWidthsDip[c]
                : tbl.Width / Math.Max(1, cols);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        }

        for (int r = 0; r < tbl.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int colCursor = 0;
            foreach (var cell in tbl.Rows[r].Cells)
            {
                var stack = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
                foreach (var p in cell.Paragraphs)
                {
                    var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
                    foreach (var runModel in p.Runs)
                    {
                        tb.Inlines.Add(new Run(runModel.Text)
                        {
                            FontSize = Math.Max(9, runModel.FontSizePt * (96.0 / 72.0)),
                            FontWeight = runModel.Bold ? FontWeights.Bold : FontWeights.Normal,
                            Foreground = ParseBrush(runModel.ColorHex, Brushes.Black)
                        });
                    }
                    stack.Children.Add(tb);
                }

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    BorderThickness = new Thickness(0.75),
                    Background = !string.IsNullOrEmpty(cell.FillHex)
                        ? ParseBrush(cell.FillHex, Brushes.White)
                        : (r == 0 ? new SolidColorBrush(Color.FromRgb(241, 245, 249)) : Brushes.White),
                    Child = stack
                };

                Grid.SetRow(border, r);
                Grid.SetColumn(border, Math.Min(colCursor, Math.Max(0, cols - 1)));
                Grid.SetColumnSpan(border, Math.Max(1, cell.ColumnSpan));
                grid.Children.Add(border);
                colCursor += Math.Max(1, cell.ColumnSpan);
            }
        }

        return grid;
    }

    private static BitmapImage? LoadBitmap(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static Brush ParseBrush(string hex, Brush fallback)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c)
            {
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }
        }
        catch
        {
            // Ignore malformed color.
        }
        return fallback;
    }
}
