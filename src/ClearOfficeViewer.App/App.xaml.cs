using System.IO;
using System.Text;
using System.Windows;
using ClearOfficeViewer.Core.Models;
using ClearOfficeViewer.Core.Services;

namespace ClearOfficeViewer.App;

public partial class App : Application
{
    public SettingsService Settings { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Instance.Error("UnhandledUI", "Unhandled dispatcher exception.", args.Exception);
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{args.Exception.Message}",
                "ClearOffice Viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
        ApplyThemePreference(Settings.Current.Theme);

        string? initialFile = null;
        if (e.Args.Length > 0)
        {
            string candidate = e.Args[0].Trim('"');
            if (File.Exists(candidate))
            {
                initialFile = candidate;
            }
            else
            {
                AppLogger.Instance.Warn(nameof(App), $"Startup file argument not found: '{candidate}'.");
            }
        }

        var mainWindow = new MainWindow(Settings, initialFile);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    public void ApplyThemePreference(AppThemePreference preference)
    {
        ThemeMode = preference switch
        {
            AppThemePreference.Light => ThemeMode.Light,
            AppThemePreference.Dark => ThemeMode.Dark,
            _ => ThemeMode.System
        };

        bool isDark = ClearOfficeViewer.UI.Interop.WindowBackdropHelper.ResolveIsDark(preference);
        Resources["AppForegroundBrush"] = new System.Windows.Media.SolidColorBrush(
            isDark ? System.Windows.Media.Color.FromRgb(243, 243, 243) : System.Windows.Media.Color.FromRgb(26, 26, 26));
        Resources["AppMutedForegroundBrush"] = new System.Windows.Media.SolidColorBrush(
            isDark ? System.Windows.Media.Color.FromRgb(176, 176, 176) : System.Windows.Media.Color.FromRgb(92, 92, 92));
    }
}
