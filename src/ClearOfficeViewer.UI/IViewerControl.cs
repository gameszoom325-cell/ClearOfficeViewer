namespace ClearOfficeViewer.UI;

public interface IViewerControl : IDisposable
{
    event EventHandler? StatusChanged;

    string StatusPrimaryText { get; }
    double ZoomPercent { get; }
    bool SupportsFind { get; }
    bool SupportsPrint { get; }
    bool SupportsPresentation { get; }

    void ZoomIn();
    void ZoomOut();
    void FitWidth();
    void FitPage();
    void SetZoomPercent(double percent);
    void ShowFind();
    void Print();
    void StartPresentation();
}
