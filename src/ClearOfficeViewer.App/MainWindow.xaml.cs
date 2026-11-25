using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClearOfficeViewer.Core.Models;
using ClearOfficeViewer.Core.Services;
using ClearOfficeViewer.UI;
using ClearOfficeViewer.UI.Controls;
using ClearOfficeViewer.UI.Interop;
using Microsoft.Win32;

namespace ClearOfficeViewer.App;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly string? _initialFile;
    private readonly TransparencyEngine _transparencyEngine;
    private IViewerControl? _activeViewer;
    private string? _activeFilePath;
    private bool _initializingControls = true;

    public MainWindow(SettingsService settings, string? initialFile = null)
    {
        _settings = settings;
        _initialFile = initialFile;

        InitializeComponent();

        _transparencyEngine = new TransparencyEngine(this);
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        GlassNoiseLayer.Background = GlassNoiseBrush.Instance;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _transparencyEngine.Attach();
        ApplyBackdropAndSurface();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SyncSettingsControls();
        _initializingControls = false;

        RefreshRecentAndFavoritesUi();
        UpdateAssociationStatusUi();
        UpdateStatusBarVisibility();

        if (!string.IsNullOrEmpty(_initialFile))
        {
            await OpenFileAsync(_initialFile);
        }
        else
        {
            SelectNavTag("Home");
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void ApplyBackdropAndSurface()
    {
        _transparencyEngine.Configure(
            _settings.Current.Backdrop,
            _settings.Current.Theme,
            _settings.Current.BlurIntensityPercent,
            _settings.Current.TintStrengthPercent,
            _settings.Current.BackgroundAdaptation);

        bool isDark = TransparencyEngine.ResolveIsDark(_settings.Current.Theme);
        bool isCompositionGlass = _settings.Current.Backdrop is BackdropMaterial.DeepAcrylic or BackdropMaterial.CustomGlass;

        // Keep WPF layers whisper-thin (zero opaque grey slabs) so Windows.UI.Composition HostBackdrop colors shine through
        if (isDark)
        {
            CommandBarBorder.Background = new SolidColorBrush(Color.FromArgb(14, 8, 12, 18));
            CommandBarBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            StatusBarBorder.Background = new SolidColorBrush(Color.FromArgb(20, 8, 12, 18));
            StatusBarBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            GlassSpecularRim.BorderBrush = new SolidColorBrush(Color.FromArgb(isCompositionGlass ? (byte)58 : (byte)32, 255, 255, 255));
            ContentSurfaceBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(isCompositionGlass ? (byte)40 : (byte)28, 255, 255, 255));
            ContentSurfaceBorder.Background = new SolidColorBrush(
                Color.FromArgb(isCompositionGlass ? (byte)24 : (byte)70, 10, 14, 22));
            GlassNoiseLayer.Opacity = isCompositionGlass ? 0.55 : 0.25;
        }
        else
        {
            CommandBarBorder.Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
            CommandBarBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(24, 0, 0, 0));
            StatusBarBorder.Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            StatusBarBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(24, 0, 0, 0));
            GlassSpecularRim.BorderBrush = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255));
            ContentSurfaceBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(34, 0, 0, 0));
            ContentSurfaceBorder.Background = new SolidColorBrush(
                Color.FromArgb(isCompositionGlass ? (byte)36 : (byte)110, 255, 255, 255));
            GlassNoiseLayer.Opacity = isCompositionGlass ? 0.40 : 0.20;
        }
    }

    private void SyncSettingsControls()
    {
        SelectComboByTag(BackdropCombo, _settings.Current.Backdrop.ToString());
        SelectComboByTag(ThemeCombo, _settings.Current.Theme.ToString());
        SelectComboByTag(DefaultZoomCombo, _settings.Current.DefaultZoom.ToString());

        BlurIntensitySlider.Value = Math.Clamp(_settings.Current.BlurIntensityPercent, 0, 100);
        BlurIntensityValueText.Text = $"{(int)BlurIntensitySlider.Value}%";

        TintStrengthSlider.Value = Math.Clamp(_settings.Current.TintStrengthPercent, 0, 100);
        TintStrengthValueText.Text = $"{(int)TintStrengthSlider.Value}%";

        BackgroundAdaptationCheckBox.IsChecked = _settings.Current.BackgroundAdaptation;
        BackgroundAdaptationCheckBox.Content = _settings.Current.BackgroundAdaptation ? "ON" : "OFF";

        bool usesCustomComp = _settings.Current.Backdrop is BackdropMaterial.DeepAcrylic or BackdropMaterial.CustomGlass;
        BlurIntensitySlider.IsEnabled = usesCustomComp;
        TintStrengthSlider.IsEnabled = usesCustomComp;
        BackgroundAdaptationCheckBox.IsEnabled = usesCustomComp;

        ShowStatusBarCheckBox.IsChecked = _settings.Current.ShowStatusBar;
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                break;
            }
        }
    }

    public async Task OpenFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            MessageBox.Show($"File does not exist:\n{filePath}", "ClearOffice Viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string fullPath = Path.GetFullPath(filePath);
        var kind = FileFormatDetector.DetectFromFile(fullPath);
        if (kind == SupportedFileKind.Unknown)
        {
            MessageBox.Show(
                $"Unsupported file format: {Path.GetExtension(fullPath)}\n\nSupported formats: .pdf, .doc, .docx, .ppt, .pptx, .html, .htm",
                "Unsupported Format",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        LoadingMessageText.Text = $"Opening {Path.GetFileName(fullPath)}...";
        LoadingOverlay.Visibility = Visibility.Visible;

        try
        {
            DisposeActiveViewer();

            IViewerControl newViewer;
            switch (kind)
            {
                case SupportedFileKind.Pdf:
                {
                    var pdfCtrl = new PdfViewerControl();
                    await pdfCtrl.LoadAsync(fullPath, _settings.Current.DefaultZoom);
                    newViewer = pdfCtrl;
                    break;
                }
                case SupportedFileKind.WordLegacy:
                case SupportedFileKind.WordOpenXml:
                {
                    var docCtrl = new DocumentViewerControl();
                    await docCtrl.LoadAsync(fullPath, _settings.Current.DefaultZoom);
                    newViewer = docCtrl;
                    break;
                }
                case SupportedFileKind.PowerPointLegacy:
                case SupportedFileKind.PowerPointOpenXml:
                {
                    var pptCtrl = new PresentationViewerControl();
                    await pptCtrl.LoadAsync(fullPath, _settings.Current.DefaultZoom);
                    newViewer = pptCtrl;
                    break;
                }
                case SupportedFileKind.Html:
                {
                    var htmlCtrl = new HtmlViewerControl();
                    ViewerHost.Content = htmlCtrl;
                    ShowContentSection("Viewer");
                    await htmlCtrl.LoadAsync(fullPath);
                    newViewer = htmlCtrl;
                    break;
                }
                default:
                    throw new NotSupportedException($"Format '{kind}' is not supported.");
            }

            _activeViewer = newViewer;
            _activeViewer.StatusChanged += ActiveViewer_StatusChanged;
            _activeFilePath = fullPath;

            ViewerHost.Content = (UIElement)newViewer;
            ShowContentSection("Viewer");

            _settings.RecordFileOpened(fullPath);
            RefreshRecentAndFavoritesUi();
            UpdateDocumentChrome(kind, fullPath);
            UpdateViewerStatus();

            AppLogger.Instance.Info(nameof(MainWindow), $"Opened '{fullPath}' ({kind}).");
        }
        catch (Exception ex)
        {
            DisposeActiveViewer();
            ClearDocumentChrome();
            AppLogger.Instance.Error(nameof(MainWindow), $"Failed to open '{fullPath}'.", ex);
            MessageBox.Show(
                $"Unable to open '{Path.GetFileName(fullPath)}'.\n\n{ex.Message}",
                "Open File Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void DisposeActiveViewer()
    {
        if (_activeViewer != null)
        {
            _activeViewer.StatusChanged -= ActiveViewer_StatusChanged;
            _activeViewer.Dispose();
            _activeViewer = null;
        }
        ViewerHost.Content = null;
        _activeFilePath = null;
    }

    private void UpdateDocumentChrome(SupportedFileKind kind, string fullPath)
    {
        string fileName = Path.GetFileName(fullPath);
        Title = $"{fileName} - ClearOffice Viewer";
        TitleDocumentText.Text = $"—  {fileName}";

        DocumentActionBar.Visibility = Visibility.Visible;
        ActiveDocSidebarBox.Visibility = Visibility.Visible;
        ActiveDocBadgeText.Text = FileFormatDetector.GetShortBadge(kind);
        ActiveDocNameText.Text = fileName;
        ActiveDocNameText.ToolTip = fullPath;

        FindButton.Visibility = _activeViewer?.SupportsFind == true ? Visibility.Visible : Visibility.Collapsed;
        PrintButton.Visibility = _activeViewer?.SupportsPrint == true ? Visibility.Visible : Visibility.Collapsed;
        PresentButton.Visibility = _activeViewer?.SupportsPresentation == true ? Visibility.Visible : Visibility.Collapsed;
        MenuPrintItem.IsEnabled = _activeViewer?.SupportsPrint == true;
        MenuCloseDocItem.IsEnabled = true;

        UpdateFavoriteButtonState();
        StatusLeftText.Text = $"{FileFormatDetector.GetDisplayTypeName(kind)}  ·  {fullPath}";
    }

    private void ClearDocumentChrome()
    {
        Title = "ClearOffice Viewer";
        TitleDocumentText.Text = string.Empty;
        DocumentActionBar.Visibility = Visibility.Collapsed;
        ActiveDocSidebarBox.Visibility = Visibility.Collapsed;
        MenuPrintItem.IsEnabled = false;
        MenuCloseDocItem.IsEnabled = false;
        StatusLeftText.Text = "Ready";
        StatusRightPrimaryText.Text = string.Empty;
        StatusZoomSeparator.Visibility = Visibility.Collapsed;
        StatusZoomText.Visibility = Visibility.Collapsed;
    }

    private void ActiveViewer_StatusChanged(object? sender, EventArgs e) => UpdateViewerStatus();

    private void UpdateViewerStatus()
    {
        if (_activeViewer == null) return;
        StatusRightPrimaryText.Text = _activeViewer.StatusPrimaryText;
        string zoomStr = $"Zoom {_activeViewer.ZoomPercent:0}%";
        StatusZoomText.Text = zoomStr;
        ToolbarZoomText.Text = $"{_activeViewer.ZoomPercent:0}%";
        StatusZoomSeparator.Visibility = Visibility.Visible;
        StatusZoomText.Visibility = Visibility.Visible;
        PresentButton.Visibility = _activeViewer.SupportsPresentation ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateFavoriteButtonState()
    {
        bool fav = _settings.IsFavorite(_activeFilePath);
        FavoriteGlyph.Text = fav ? "\uE735" : "\uE734"; // Filled star vs outline star
        FavoriteGlyph.Foreground = fav ? (Brush)FindResource("BrandAccentBrush") : Foreground;
    }

    private void RefreshRecentAndFavoritesUi()
    {
        var recents = _settings.Current.RecentFiles;
        HomeRecentList.ItemsSource = null;
        HomeRecentList.ItemsSource = recents.Take(10).ToList();
        HomeEmptyRecentText.Visibility = recents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RecentListView.ItemsSource = null;
        RecentListView.ItemsSource = recents.ToList();

        var favs = _settings.GetFavorites();
        FavoritesListView.ItemsSource = null;
        FavoritesListView.ItemsSource = favs;
        FavoritesEmptyText.Visibility = favs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FavoritesListView.Visibility = favs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        // Populate File -> Recent Files submenu
        FileRecentSubMenu.Items.Clear();
        if (recents.Count == 0)
        {
            FileRecentSubMenu.Items.Add(new MenuItem { Header = "(Empty)", IsEnabled = false });
        }
        else
        {
            foreach (var r in recents.Take(12))
            {
                var item = new MenuItem { Header = $"{r.FileName}  ({r.FormatBadge})", ToolTip = r.FilePath, Tag = r.FilePath };
                item.Click += async (s, _) =>
                {
                    if (s is MenuItem mi && mi.Tag is string path)
                        await OpenFileAsync(path);
                };
                FileRecentSubMenu.Items.Add(item);
            }
        }
    }

    private void ShowContentSection(string section)
    {
        HomeView.Visibility = section == "Home" ? Visibility.Visible : Visibility.Collapsed;
        RecentView.Visibility = section == "Recent" ? Visibility.Visible : Visibility.Collapsed;
        FavoritesView.Visibility = section == "Favorites" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        ViewerHost.Visibility = section == "Viewer" ? Visibility.Visible : Visibility.Collapsed;

        if (section != "Viewer")
        {
            DocumentActionBar.Visibility = Visibility.Collapsed;
        }
        else if (_activeViewer != null)
        {
            DocumentActionBar.Visibility = Visibility.Visible;
        }
    }

    private void SelectNavTag(string tag)
    {
        foreach (ListBoxItem item in NavListBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                NavListBox.SelectedItem = item;
                break;
            }
        }
    }

    private void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavListBox.SelectedItem is ListBoxItem item && item.Tag is string tag)
        {
            ShowContentSection(tag);
        }
    }

    private void FileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileMenuButton.ContextMenu != null)
        {
            FileMenuButton.ContextMenu.PlacementTarget = FileMenuButton;
            FileMenuButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            FileMenuButton.ContextMenu.IsOpen = true;
        }
    }

    private async void OpenMenu_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Document",
            Filter = FileFormatDetector.OpenFileDialogFilter,
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) == true)
        {
            await OpenFileAsync(dlg.FileName);
        }
    }

    private void TopRecent_Click(object sender, RoutedEventArgs e) => SelectNavTag("Recent");
    private void TopSettings_Click(object sender, RoutedEventArgs e) => SelectNavTag("Settings");

    private void CloseDocument_Click(object sender, RoutedEventArgs e)
    {
        DisposeActiveViewer();
        ClearDocumentChrome();
        SelectNavTag("Home");
        ShowContentSection("Home");
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => _activeViewer?.ZoomIn();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => _activeViewer?.ZoomOut();
    private void FitWidth_Click(object sender, RoutedEventArgs e) => _activeViewer?.FitWidth();
    private void FitPage_Click(object sender, RoutedEventArgs e) => _activeViewer?.FitPage();
    private void FindButton_Click(object sender, RoutedEventArgs e) => _activeViewer?.ShowFind();
    private void PrintButton_Click(object sender, RoutedEventArgs e) => _activeViewer?.Print();
    private void PresentButton_Click(object sender, RoutedEventArgs e) => _activeViewer?.StartPresentation();

    private void FavoriteToggle_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_activeFilePath)) return;
        _settings.ToggleFavorite(_activeFilePath);
        UpdateFavoriteButtonState();
        RefreshRecentAndFavoritesUi();
    }

    private async void OpenRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
            await OpenFileAsync(path);
    }

    private void ToggleRecentFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            _settings.ToggleFavorite(path);
            UpdateFavoriteButtonState();
            RefreshRecentAndFavoritesUi();
        }
    }

    private async void RecentListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentListView.SelectedItem is RecentFileEntry entry)
            await OpenFileAsync(entry.FilePath);
    }

    private async void FavoritesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FavoritesListView.SelectedItem is RecentFileEntry entry)
            await OpenFileAsync(entry.FilePath);
    }

    private void ClearRecent_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClearRecentFiles();
        RefreshRecentAndFavoritesUi();
    }

    private void BackdropCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingControls) return;
        if (BackdropCombo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<BackdropMaterial>(item.Tag?.ToString(), out var mat))
        {
            _settings.Current.Backdrop = mat;
            _settings.Save();
            _initializingControls = true;
            SyncSettingsControls();
            _initializingControls = false;
            ApplyBackdropAndSurface();
        }
    }

    private void BlurIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)Math.Round(e.NewValue);
        if (BlurIntensityValueText != null)
            BlurIntensityValueText.Text = $"{val}%";
        if (_initializingControls) return;
        _settings.Current.BlurIntensityPercent = val;
        _settings.Save();
        _transparencyEngine.ChangeBlurIntensity(val);
    }

    private void TintStrengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int val = (int)Math.Round(e.NewValue);
        if (TintStrengthValueText != null)
            TintStrengthValueText.Text = $"{val}%";
        if (_initializingControls) return;
        _settings.Current.TintStrengthPercent = val;
        _settings.Save();
        _transparencyEngine.ChangeTintOpacity(val);
    }

    private void BackgroundAdaptationCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = BackgroundAdaptationCheckBox.IsChecked != false;
        BackgroundAdaptationCheckBox.Content = enabled ? "ON" : "OFF";
        if (_initializingControls) return;
        _settings.Current.BackgroundAdaptation = enabled;
        _settings.Save();
        _transparencyEngine.SetBackgroundAdaptation(enabled);
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingControls) return;
        if (ThemeCombo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<AppThemePreference>(item.Tag?.ToString(), out var pref))
        {
            _settings.Current.Theme = pref;
            _settings.Save();
            ((App)Application.Current).ApplyThemePreference(pref);
            ApplyBackdropAndSurface();
        }
    }

    private void DefaultZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingControls) return;
        if (DefaultZoomCombo.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<DefaultZoomBehavior>(item.Tag?.ToString(), out var dz))
        {
            _settings.Current.DefaultZoom = dz;
            _settings.Save();
        }
    }

    private void ShowStatusBarCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializingControls) return;
        _settings.Current.ShowStatusBar = ShowStatusBarCheckBox.IsChecked != false;
        _settings.Save();
        UpdateStatusBarVisibility();
    }

    private void UpdateStatusBarVisibility()
    {
        StatusBarBorder.Visibility = _settings.Current.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateAssociationStatusUi()
    {
        bool reg = FileAssociationService.IsRegisteredForCurrentUser();
        FileAssocStatusText.Text = reg
            ? "Status: Registered for the current Windows user (appears in File Explorer Open With)."
            : "Status: Not registered for the current user yet.";
    }

    private void RegisterAssociations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociationService.RegisterForCurrentUser();
            UpdateAssociationStatusUi();
            MessageBox.Show(
                "Registered ClearOffice Viewer in Windows 'Open With' for .pdf, .doc, .docx, .ppt, .pptx, .html, and .htm.",
                "File Associations",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to register associations:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UnregisterAssociations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociationService.UnregisterForCurrentUser();
            UpdateAssociationStatusUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to remove associations:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenWindowsDefaultApps_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociationService.OpenWindowsDefaultAppsSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open Windows Settings:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLogger.Instance.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppLogger.Instance.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open log folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            e.Effects = files is { Length: > 0 } && FileFormatDetector.IsSupportedExtension(files[0])
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            await OpenFileAsync(files[0]);
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.O:
                    OpenMenu_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.W:
                    if (_activeViewer != null)
                    {
                        CloseDocument_Click(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                    break;
                case Key.F:
                    if (_activeViewer?.SupportsFind == true)
                    {
                        _activeViewer.ShowFind();
                        e.Handled = true;
                    }
                    break;
                case Key.P:
                    if (_activeViewer?.SupportsPrint == true)
                    {
                        _activeViewer.Print();
                        e.Handled = true;
                    }
                    break;
                case Key.OemPlus:
                case Key.Add:
                    _activeViewer?.ZoomIn();
                    e.Handled = true;
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    _activeViewer?.ZoomOut();
                    e.Handled = true;
                    break;
                case Key.D0:
                case Key.NumPad0:
                    _activeViewer?.SetZoomPercent(100);
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.F5 && _activeViewer?.SupportsPresentation == true)
        {
            _activeViewer.StartPresentation();
            e.Handled = true;
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        DisposeActiveViewer();
        _transparencyEngine.Dispose();
    }
}
