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
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;

    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2;      // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // System Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4;    // Mica Alt

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int ACCENT_FLAG_ENABLE_GRADIENT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

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
            // Default to dark if registry read fails.
        }
        return false;
    }

    public static bool ResolveIsDark(AppThemePreference preference) => preference switch
    {
        AppThemePreference.Dark => true,
        AppThemePreference.Light => false,
        _ => IsSystemDarkTheme()
    };

    public static void ApplyBackdrop(
        Window window,
        BackdropMaterial material,
        AppThemePreference themePreference,
        int tintOpacityPercent = 46)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        bool isDark = ResolveIsDark(themePreference);
        int darkVal = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));

        int roundCorners = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundCorners, sizeof(int));

        // Subtle glass rim border (COLORREF = 0x00BBGGRR)
        int borderColorRef = isDark ? 0x00463B32 : 0x00DCD6D0;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColorRef, sizeof(int));

        var hwndSource = HwndSource.FromHwnd(hwnd);
        var chrome = WindowChrome.GetWindowChrome(window);
        if (chrome != null)
            chrome.GlassFrameThickness = new Thickness(-1);

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

        if (material is BackdropMaterial.DeepAcrylicGlass or BackdropMaterial.UltraAcrylicGlass)
        {
            // Clear Windows 11 DWMWA_SYSTEMBACKDROP_TYPE so DWM honors custom ACCENT_ENABLE_ACRYLICBLURBEHIND
            int none = DWMSBT_NONE;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));

            int effectivePercent = material == BackdropMaterial.UltraAcrylicGlass
                ? Math.Clamp(Math.Min(tintOpacityPercent, 32), 18, 45)
                : Math.Clamp(tintOpacityPercent, 20, 75);

            byte alpha = (byte)Math.Clamp((int)Math.Round(effectivePercent / 100.0 * 255.0), 38, 210);
            // ABGR format: Dark obsidian-slate (#0D1118 -> R=13, G=17, B=24) or Frosted mist (#E6ECF2)
            uint abgr = isDark
                ? ((uint)alpha << 24) | (24u << 16) | (17u << 8) | 13u
                : ((uint)alpha << 24) | (242u << 16) | (236u << 8) | 230u;

            SetAccentPolicy(hwnd, ACCENT_ENABLE_ACRYLICBLURBEHIND, ACCENT_FLAG_ENABLE_GRADIENT, abgr);
        }
        else
        {
            // Clear custom accent policy first before enabling native Windows 11 system backdrop
            SetAccentPolicy(hwnd, ACCENT_DISABLED, 0, 0);

            int backdropType = material switch
            {
                BackdropMaterial.Mica => DWMSBT_MAINWINDOW,
                BackdropMaterial.MicaAlt => DWMSBT_TABBEDWINDOW,
                BackdropMaterial.WindowsAcrylic => DWMSBT_TRANSIENTWINDOW,
                _ => DWMSBT_TRANSIENTWINDOW
            };
            _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        }
    }

    private static void SetAccentPolicy(IntPtr hwnd, int accentState, int accentFlags, uint gradientColor)
    {
        var accent = new AccentPolicy
        {
            AccentState = accentState,
            AccentFlags = accentFlags,
            GradientColor = gradientColor,
            AnimationId = 0
        };

        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, fDeleteOld: false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size
            };
            _ = SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
