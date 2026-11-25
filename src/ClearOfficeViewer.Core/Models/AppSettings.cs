namespace ClearOfficeViewer.Core.Models;

public enum BackdropMaterial
{
    Mica = 0,
    MicaAlt = 1,
    Acrylic = 2,
    Solid = 3
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
    public BackdropMaterial Backdrop { get; set; } = BackdropMaterial.Mica;
    public AppThemePreference Theme { get; set; } = AppThemePreference.System;
    public DefaultZoomBehavior DefaultZoom { get; set; } = DefaultZoomBehavior.FitWidth;
    public int MaxRecentFiles { get; set; } = 30;
    public bool ShowStatusBar { get; set; } = true;
    public List<RecentFileEntry> RecentFiles { get; set; } = [];
    public List<string> FavoritePaths { get; set; } = [];
}
