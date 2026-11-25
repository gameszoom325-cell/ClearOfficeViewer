namespace ClearOfficeViewer.Core.Models;

public enum BackdropMaterial
{
    DeepAcrylicGlass = 0,
    UltraAcrylicGlass = 1,
    WindowsAcrylic = 2,
    MicaAlt = 3,
    Mica = 4
}

public enum AppThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum DefaultZoomBehavior
{
    FitWidth = 0,
    FitPage = 1,
    ActualSize = 2
}

public sealed class AppSettings
{
    public BackdropMaterial Backdrop { get; set; } = BackdropMaterial.DeepAcrylicGlass;
    public AppThemePreference Theme { get; set; } = AppThemePreference.Dark;
    public int GlassTintOpacityPercent { get; set; } = 50;
    public DefaultZoomBehavior DefaultZoom { get; set; } = DefaultZoomBehavior.FitWidth;
    public int MaxRecentFiles { get; set; } = 30;
    public bool ShowStatusBar { get; set; } = true;
    public List<RecentFileEntry> RecentFiles { get; set; } = [];
    public List<string> FavoritePaths { get; set; } = [];
}
