using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ClearOfficeViewer.Core.Models;
using Microsoft.Win32;

namespace ClearOfficeViewer.UI.Interop;

public static class WindowBackdropHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMSBT_AUTO = 0;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2;      // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4;    // Mica Alt

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    public static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val)
                return val == 0;
        }
        catch
        {
            // Default to light if registry read fails.
        }
        return false;
    }

    public static bool ResolveIsDark(AppThemePreference preference) => preference switch
    {
        AppThemePreference.Dark => true,
        AppThemePreference.Light => false,
        _ => IsSystemDarkTheme()
    };

    public static void ApplyBackdrop(Window window, BackdropMaterial material, AppThemePreference themePreference)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        bool isDark = ResolveIsDark(themePreference);
        int darkVal = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));

        var hwndSource = HwndSource.FromHwnd(hwnd);
        var chrome = WindowChrome.GetWindowChrome(window);
        if (material == BackdropMaterial.Solid)
        {
            int none = DWMSBT_NONE;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
            var solidBg = isDark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243);
            if (chrome != null)
                chrome.GlassFrameThickness = new Thickness(0, 38, 0, 0);
            if (hwndSource?.CompositionTarget != null)
                hwndSource.CompositionTarget.BackgroundColor = solidBg;
            window.Background = new SolidColorBrush(solidBg);
            return;
        }

        if (chrome != null)
            chrome.GlassFrameThickness = new Thickness(-1);

        // Extend DWM glass frame across entire client area so Mica/Acrylic shows natively
        if (hwndSource?.CompositionTarget != null)
            hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;
        window.Background = Brushes.Transparent;

        var margins = new MARGINS
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1
        };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int backdropType = material switch
        {
            BackdropMaterial.Mica => DWMSBT_MAINWINDOW,
            BackdropMaterial.MicaAlt => DWMSBT_TABBEDWINDOW,
            BackdropMaterial.Acrylic => DWMSBT_TRANSIENTWINDOW,
            _ => DWMSBT_AUTO
        };
        _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
    }
}
