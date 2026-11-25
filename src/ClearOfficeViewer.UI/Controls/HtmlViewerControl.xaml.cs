using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClearOfficeViewer.Core.Services;
using Microsoft.Web.WebView2.Core;

namespace ClearOfficeViewer.UI.Controls;

public partial class HtmlViewerControl : UserControl, IViewerControl
{
    private bool _initialized;
    private string _loadedPath = string.Empty;
    private string _pageTitle = "Local HTML";
    private bool _disposed;

    public event EventHandler? StatusChanged;

    public string StatusPrimaryText => string.IsNullOrEmpty(_loadedPath)
        ? "No HTML file loaded"
        : _pageTitle;

    public double ZoomPercent => Math.Round((WebView.ZoomFactor > 0 ? WebView.ZoomFactor : 1.0) * 100.0);
    public bool SupportsFind => true;
    public bool SupportsPrint => true;
    public bool SupportsPresentation => false;

    public HtmlViewerControl()
    {
        InitializeComponent();
        WebView.ZoomFactorChanged += (_, _) => RaiseStatusChanged();
    }

    public async Task LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("HTML file not found.", filePath);

        _loadedPath = Path.GetFullPath(filePath);
        _pageTitle = Path.GetFileName(_loadedPath);

        if (!_initialized)
        {
            string userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearOfficeViewer",
                "WebView2");
            Directory.CreateDirectory(userDataDir);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataDir);
            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebView.CoreWebView2.Settings.IsScriptEnabled = true;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            WebView.CoreWebView2.DocumentTitleChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(WebView.CoreWebView2.DocumentTitle))
                {
                    _pageTitle = WebView.CoreWebView2.DocumentTitle;
                    RaiseStatusChanged();
                }
            };
            _initialized = true;
        }

        WebView.Source = new Uri(_loadedPath);
        RaiseStatusChanged();
    }

    public void ZoomIn() => SetZoomPercent(ZoomPercent + 15);
    public void ZoomOut() => SetZoomPercent(ZoomPercent - 15);
    public void FitWidth() => SetZoomPercent(100);
    public void FitPage() => SetZoomPercent(100);

    public void SetZoomPercent(double percent)
    {
        double clamped = Math.Clamp(percent, 25, 300) / 100.0;
        WebView.ZoomFactor = clamped;
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
        if (_initialized && WebView.CoreWebView2 != null)
        {
            WebView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        }
    }

    public void StartPresentation() { }

    private async void ExecuteBrowserFind(bool backward)
    {
        if (!_initialized || WebView.CoreWebView2 == null) return;
        string term = FindTextBox.Text;
        if (string.IsNullOrEmpty(term)) return;

        string jsonTerm = JsonSerializer.Serialize(term);
        string backStr = backward ? "true" : "false";
        await WebView.CoreWebView2.ExecuteScriptAsync($"window.find({jsonTerm}, false, {backStr}, true, false, true, false);");
    }

    private void FindTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteBrowserFind((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FindBar.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => ExecuteBrowserFind(false);
    private void FindClose_Click(object sender, RoutedEventArgs e) => FindBar.Visibility = Visibility.Collapsed;

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            WebView.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn(nameof(HtmlViewerControl), $"WebView dispose warning: {ex.Message}");
        }
    }
}
