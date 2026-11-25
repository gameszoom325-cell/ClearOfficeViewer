using System.Windows;

namespace ClearOfficeViewer.PdfEngine;

public sealed record PdfPageMetadata(
    int PageIndex,
    int PageNumber,
    double WidthDip,
    double HeightDip);

public sealed record PdfWordBox(
    string Text,
    double NormX,
    double NormY,
    double NormWidth,
    double NormHeight)
{
    public Rect ToPixelRect(double pageWidth, double pageHeight) =>
        new(NormX * pageWidth, NormY * pageHeight, NormWidth * pageWidth, NormHeight * pageHeight);
}

public sealed record PdfSearchMatch(
    int PageIndex,
    int PageNumber,
    IReadOnlyList<PdfWordBox> Boxes,
    string ContextSnippet);
