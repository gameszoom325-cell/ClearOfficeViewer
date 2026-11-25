using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using ClearOfficeViewer.Core.Models;
using ClearOfficeViewer.Core.Services;
using Microsoft.Win32;
using Windows.Graphics.Effects;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace ClearOfficeViewer.UI.Interop;

/// <summary>
/// Manages real Windows Desktop Window Manager (DWM) and Windows.UI.Composition
/// GPU-accelerated backdrop transparency (Deep Dynamic Acrylic, Custom Glass, Acrylic, and Mica).
/// </summary>
public sealed partial class TransparencyEngine : IDisposable
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;

    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2;      // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
    private const int WM_THEMECHANGED = 0x031A;

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    private readonly Window _hostWindow;
    private IntPtr _hostHwnd;
    private HwndSource? _hwndSource;
    private bool _disposed;

    // Synchronized Windows.UI.Composition backdrop window placed directly beneath _hostWindow in Z-order
    private IntPtr _backdropHwnd;
    private static IntPtr _dispatcherQueueController;
    private Compositor? _compositor;
    private DesktopWindowTarget? _desktopTarget;
    private CompositionEffectBrush? _blurBrush;
    private CompositionColorBrush? _tintBrush;
    private CompositionColorBrush? _adaptationVeilBrush;
    private SpriteVisual? _blurVisual;
    private SpriteVisual? _tintVisual;
    private SpriteVisual? _adaptationVeilVisual;

    public BackdropMaterial Material { get; private set; } = BackdropMaterial.DeepAcrylic;
    public AppThemePreference ThemePreference { get; private set; } = AppThemePreference.Dark;
    public int BlurIntensityPercent { get; private set; } = 68;
    public int TintStrengthPercent { get; private set; } = 36;
    public bool BackgroundAdaptation { get; private set; } = true;
    public bool IsDarkActive => ResolveIsDark(ThemePreference);

    public event EventHandler? BackdropUpdated;

    public TransparencyEngine(Window hostWindow)
    {
        _hostWindow = hostWindow ?? throw new ArgumentNullException(nameof(hostWindow));
    }

    public void Attach()
    {
        _hostHwnd = new WindowInteropHelper(_hostWindow).Handle;
        if (_hostHwnd == IntPtr.Zero)
            return;

        _hwndSource = HwndSource.FromHwnd(_hostHwnd);
        _hwndSource?.AddHook(WndProcHook);

        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        _hostWindow.StateChanged += OnHostStateChanged;
        _hostWindow.IsVisibleChanged += OnHostVisibleChanged;
    }

    public void Configure(
        BackdropMaterial material,
        AppThemePreference themePreference,
        int blurIntensityPercent,
        int tintStrengthPercent,
        bool backgroundAdaptation)
    {
        Material = material;
        ThemePreference = themePreference;
        BlurIntensityPercent = Math.Clamp(blurIntensityPercent, 0, 100);
        TintStrengthPercent = Math.Clamp(tintStrengthPercent, 0, 100);
        BackgroundAdaptation = backgroundAdaptation;
        UpdateBackdrop();
    }

    public void EnableAcrylic()
    {
        Material = BackdropMaterial.DeepAcrylic;
        UpdateBackdrop();
    }

    public void ChangeTintOpacity(int percent)
    {
        TintStrengthPercent = Math.Clamp(percent, 0, 100);
        UpdateBackdrop();
    }

    public void ChangeBlurIntensity(int percent)
    {
        BlurIntensityPercent = Math.Clamp(percent, 0, 100);
        UpdateBackdrop();
    }

    public void SetBackgroundAdaptation(bool enabled)
    {
        BackgroundAdaptation = enabled;
        UpdateBackdrop();
    }

    public bool DetectWindowsTheme() => IsSystemDarkTheme();

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
            // Fallback to dark theme if registry read fails.
        }
        return true;
    }

    public static bool ResolveIsDark(AppThemePreference preference) => preference switch
    {
        AppThemePreference.Dark => true,
        AppThemePreference.Light => false,
        _ => IsSystemDarkTheme()
    };

    public void UpdateBackdrop()
    {
        if (_disposed || _hostHwnd == IntPtr.Zero)
            return;

        bool isDark = IsDarkActive;
        int darkVal = isDark ? 1 : 0;
        _ = DwmSetWindowAttribute(_hostHwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));

        int roundCorners = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(_hostHwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundCorners, sizeof(int));

        int borderColorRef = isDark ? 0x004C4038 : 0x00DCD6D0;
        _ = DwmSetWindowAttribute(_hostHwnd, DWMWA_BORDER_COLOR, ref borderColorRef, sizeof(int));

        var chrome = WindowChrome.GetWindowChrome(_hostWindow);
        if (chrome != null)
            chrome.GlassFrameThickness = new Thickness(-1);

        if (_hwndSource?.CompositionTarget != null)
            _hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;
        _hostWindow.Background = Brushes.Transparent;

        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        _ = DwmExtendFrameIntoClientArea(_hostHwnd, ref margins);

        if (Material is BackdropMaterial.DeepAcrylic or BackdropMaterial.CustomGlass)
        {
            // Let our Windows.UI.Composition backdrop window render real GPU HostBackdropBrush + GaussianBlur behind WPF
            int none = DWMSBT_NONE;
            _ = DwmSetWindowAttribute(_hostHwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));

            EnsureCompositionBackdropInitialized();
            UpdateCompositionBrushes(isDark);
            SyncBackdropWindowPosition(showIfVisible: true);
        }
        else
        {
            HideCompositionBackdropWindow();

            int dwmBackdrop = Material switch
            {
                BackdropMaterial.Mica => DWMSBT_MAINWINDOW,
                BackdropMaterial.Acrylic => DWMSBT_TRANSIENTWINDOW,
                _ => DWMSBT_TRANSIENTWINDOW
            };
            _ = DwmSetWindowAttribute(_hostHwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref dwmBackdrop, sizeof(int));
        }

        BackdropUpdated?.Invoke(this, EventArgs.Empty);
    }

    public void HandleBackdropChanges()
    {
        UpdateBackdrop();
    }

    private void EnsureCompositionBackdropInitialized()
    {
        if (_compositor != null && _backdropHwnd != IntPtr.Zero)
            return;

        EnsureDispatcherQueue();
        _compositor ??= new Compositor();

        if (_backdropHwnd == IntPtr.Zero)
        {
            // WS_POPUP with WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            const uint WS_POPUP = 0x80000000;
            uint exStyle = WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            _backdropHwnd = CreateWindowExW(
                exStyle,
                "Static",
                string.Empty,
                WS_POPUP,
                0, 0, 100, 100,
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandleW(null),
                IntPtr.Zero);

            if (_backdropHwnd == IntPtr.Zero)
            {
                AppLogger.Instance.Warn(nameof(TransparencyEngine), "CreateWindowExW failed for Composition backdrop; falling back to DWM Acrylic.");
                int fallback = DWMSBT_TRANSIENTWINDOW;
                _ = DwmSetWindowAttribute(_hostHwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref fallback, sizeof(int));
                return;
            }

            int roundCorners = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(_backdropHwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundCorners, sizeof(int));
        }

        if (_desktopTarget == null && _backdropHwnd != IntPtr.Zero)
        {
            var interop = _compositor.As<ICompositorDesktopInterop>();
            interop.CreateDesktopWindowTarget(_backdropHwnd, false, out IntPtr targetPtr);
            _desktopTarget = DesktopWindowTarget.FromAbi(targetPtr);

            var root = _compositor.CreateContainerVisual();
            root.RelativeSizeAdjustment = Vector2.One;

            // Layer 1: Desktop/window behind -> HostBackdropBrush -> GPU GaussianBlurEffect
            _blurVisual = _compositor.CreateSpriteVisual();
            _blurVisual.RelativeSizeAdjustment = Vector2.One;

            try
            {
                float initialBlur = BlurPercentToStdDev(BlurIntensityPercent);
                var blurEffect = new NativeGaussianBlurEffect
                {
                    Name = "BackdropBlur",
                    BlurAmount = initialBlur,
                    Source = new CompositionEffectSourceParameter("backdrop")
                };
                var factory = _compositor.CreateEffectFactory(blurEffect);
                _blurBrush = factory.CreateBrush();
                _blurBrush.SetSourceParameter("backdrop", _compositor.CreateHostBackdropBrush());
                _blurVisual.Brush = _blurBrush;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn(nameof(TransparencyEngine), $"Composition blur effect fallback: {ex.Message}");
                _blurVisual.Brush = _compositor.CreateHostBackdropBrush();
            }

            // Layer 2: Adaptive tint veil & primary tint layer
            _adaptationVeilVisual = _compositor.CreateSpriteVisual();
            _adaptationVeilVisual.RelativeSizeAdjustment = Vector2.One;
            _adaptationVeilBrush = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            _adaptationVeilVisual.Brush = _adaptationVeilBrush;

            _tintVisual = _compositor.CreateSpriteVisual();
            _tintVisual.RelativeSizeAdjustment = Vector2.One;
            _tintBrush = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(90, 12, 16, 24));
            _tintVisual.Brush = _tintBrush;

            root.Children.InsertAtBottom(_blurVisual);
            root.Children.InsertAbove(_adaptationVeilVisual, _blurVisual);
            root.Children.InsertAtTop(_tintVisual);
            _desktopTarget.Root = root;
        }
    }

    private void UpdateCompositionBrushes(bool isDark)
    {
        if (_compositor == null || _blurVisual == null || _tintBrush == null || _adaptationVeilBrush == null)
            return;

        // Rebuild blur effect when blur intensity changes so D2D GaussianBlur std-dev updates cleanly
        try
        {
            float stdDev = BlurPercentToStdDev(BlurIntensityPercent);
            if (stdDev <= 0.5f)
            {
                _blurVisual.Brush = _compositor.CreateHostBackdropBrush();
            }
            else
            {
                var blurEffect = new NativeGaussianBlurEffect
                {
                    Name = "BackdropBlur",
                    BlurAmount = stdDev,
                    Source = new CompositionEffectSourceParameter("backdrop")
                };
                var factory = _compositor.CreateEffectFactory(blurEffect);
                var nextBlurBrush = factory.CreateBrush();
                nextBlurBrush.SetSourceParameter("backdrop", _compositor.CreateHostBackdropBrush());
                _blurBrush = nextBlurBrush;
                _blurVisual.Brush = nextBlurBrush;
            }
        }
        catch
        {
            // Keep existing blur brush if effect recreation fails.
        }

        // Compute tint strength: when BackgroundAdaptation is ON, keep tint lighter & more vibrant so wallpaper/app colors bleed through
        double maxAlpha = BackgroundAdaptation ? 175.0 : 230.0;
        byte tintAlpha = (byte)Math.Clamp((int)Math.Round((TintStrengthPercent / 100.0) * maxAlpha), 0, 235);

        if (isDark)
        {
            // Dark slate/obsidian tint allowing underlying blues/colors to shine through cleanly
            _tintBrush.Color = Windows.UI.Color.FromArgb(tintAlpha, 12, 16, 24);
            // When BackgroundAdaptation is OFF, add subtle neutral grey dampening veil
            _adaptationVeilBrush.Color = BackgroundAdaptation
                ? Windows.UI.Color.FromArgb(0, 0, 0, 0)
                : Windows.UI.Color.FromArgb(96, 28, 30, 34);
        }
        else
        {
            _tintBrush.Color = Windows.UI.Color.FromArgb(tintAlpha, 236, 242, 248);
            _adaptationVeilBrush.Color = BackgroundAdaptation
                ? Windows.UI.Color.FromArgb(0, 0, 0, 0)
                : Windows.UI.Color.FromArgb(88, 243, 243, 243);
        }
    }

    private static float BlurPercentToStdDev(int percent)
    {
        // Map 0..100% -> 0..48 DIPs Gaussian standard deviation
        return Math.Clamp(percent / 100f * 48f, 0f, 48f);
    }

    private void SyncBackdropWindowPosition(bool showIfVisible = false)
    {
        if (_backdropHwnd == IntPtr.Zero || _hostHwnd == IntPtr.Zero)
            return;

        if (Material is not (BackdropMaterial.DeepAcrylic or BackdropMaterial.CustomGlass) ||
            !_hostWindow.IsVisible ||
            _hostWindow.WindowState == WindowState.Minimized)
        {
            HideCompositionBackdropWindow();
            return;
        }

        if (DwmGetWindowAttributeRect(_hostHwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT rect, Marshal.SizeOf<RECT>()) != 0)
        {
            _ = GetWindowRect(_hostHwnd, out rect);
        }

        int w = Math.Max(1, rect.Right - rect.Left);
        int h = Math.Max(1, rect.Bottom - rect.Top);

        uint flags = SWP_NOACTIVATE | SWP_NOOWNERZORDER | (showIfVisible ? SWP_SHOWWINDOW : 0);
        _ = SetWindowPos(_backdropHwnd, _hostHwnd, rect.Left, rect.Top, w, h, flags);
    }

    private void HideCompositionBackdropWindow()
    {
        if (_backdropHwnd != IntPtr.Zero)
        {
            _ = SetWindowPos(_backdropHwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_HIDEWINDOW);
        }
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_WINDOWPOSCHANGED:
                if (Material is BackdropMaterial.DeepAcrylic or BackdropMaterial.CustomGlass)
                    SyncBackdropWindowPosition(showIfVisible: true);
                break;
            case WM_DWMCOMPOSITIONCHANGED:
            case WM_THEMECHANGED:
                HandleBackdropChanges();
                break;
        }
        return IntPtr.Zero;
    }

    private void OnHostStateChanged(object? sender, EventArgs e)
    {
        if (_hostWindow.WindowState == WindowState.Minimized)
            HideCompositionBackdropWindow();
        else if (Material is BackdropMaterial.DeepAcrylic or BackdropMaterial.CustomGlass)
            SyncBackdropWindowPosition(showIfVisible: true);
    }

    private void OnHostVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_hostWindow.IsVisible)
            HideCompositionBackdropWindow();
        else if (Material is BackdropMaterial.DeepAcrylic or BackdropMaterial.CustomGlass)
            SyncBackdropWindowPosition(showIfVisible: true);
    }

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Desktop)
        {
            _hostWindow.Dispatcher.BeginInvoke(HandleBackdropChanges);
        }
    }

    private static void EnsureDispatcherQueue()
    {
        if (_dispatcherQueueController != IntPtr.Zero)
            return;
        var opts = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2, // DQTYPE_THREAD_CURRENT
            apartmentType = 2 // DQTAT_COM_STA
        };
        _ = CreateDispatcherQueueController(opts, out _dispatcherQueueController);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        _hostWindow.StateChanged -= OnHostStateChanged;
        _hostWindow.IsVisibleChanged -= OnHostVisibleChanged;
        _hwndSource?.RemoveHook(WndProcHook);

        _desktopTarget?.Dispose();
        _blurBrush?.Dispose();
        _tintBrush?.Dispose();
        _adaptationVeilBrush?.Dispose();
        _compositor?.Dispose();

        if (_backdropHwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(_backdropHwnd);
            _backdropHwnd = IntPtr.Zero;
        }
    }

    #region Native Interop & Windows.UI.Composition Direct2D Blur Bridge

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(IntPtr hwndTarget, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out IntPtr result);
    }

    [GeneratedComInterface]
    [Guid("2FC57384-A068-44D7-A331-30982FCF7177")]
    internal partial interface IGraphicsEffectD2D1Interop
    {
        void GetEffectId(out Guid id);
        void GetNamedPropertyMapping([MarshalAs(UnmanagedType.LPWStr)] string name, out uint index, out uint mapping);
        void GetPropertyCount(out uint count);
        void GetProperty(uint index, out IntPtr value);
        void GetSource(uint index, out IntPtr source);
        void GetSourceCount(out uint count);
    }

    [GeneratedComClass]
    internal sealed partial class BlurD2DInterop(NativeGaussianBlurEffect owner) : IGraphicsEffectD2D1Interop
    {
        private static readonly Guid CLSID_D2D1GaussianBlur = new("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5");
        private static readonly Guid IID_IPropertyValue = new("4BD682DD-7554-40E9-9A9B-82654EDE7E62");

        public void GetEffectId(out Guid id) => id = CLSID_D2D1GaussianBlur;
        public void GetNamedPropertyMapping(string name, out uint index, out uint mapping) { index = 0; mapping = 0; }
        public void GetPropertyCount(out uint count) => count = 3;

        public void GetProperty(uint index, out IntPtr value)
        {
            object pv = index switch
            {
                0 => Windows.Foundation.PropertyValue.CreateSingle(owner.BlurAmount),
                1 => Windows.Foundation.PropertyValue.CreateUInt32(0), // Optimization: Balanced/Speed
                2 => Windows.Foundation.PropertyValue.CreateUInt32(1), // BorderMode: Hard
                _ => Windows.Foundation.PropertyValue.CreateUInt32(0)
            };
            IntPtr insp = MarshalInspectable<object>.FromManaged(pv);
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(insp, in IID_IPropertyValue, out value));
            Marshal.Release(insp);
        }

        public void GetSource(uint index, out IntPtr source) =>
            source = MarshalInterface<IGraphicsEffectSource>.FromManaged(owner.Source!);

        public void GetSourceCount(out uint count) => count = 1;
    }

    internal sealed class NativeGaussianBlurEffect : IGraphicsEffect, ICustomQueryInterface
    {
        private static readonly Guid IID_D2D1Interop = new("2FC57384-A068-44D7-A331-30982FCF7177");
        private static readonly StrategyBasedComWrappers Wrappers = new();
        private BlurD2DInterop? _bridge;
        private IntPtr _bridgePtr;

        public string Name { get; set; } = "BackdropBlur";
        public IGraphicsEffectSource? Source { get; set; }
        public float BlurAmount { get; set; } = 32f;

        public CustomQueryInterfaceResult GetInterface(ref Guid iid, out IntPtr ppv)
        {
            if (iid == IID_D2D1Interop)
            {
                _bridge ??= new BlurD2DInterop(this);
                if (_bridgePtr == IntPtr.Zero)
                    _bridgePtr = Wrappers.GetOrCreateComInterfaceForObject(_bridge, CreateComInterfaceFlags.None);
                int hr = Marshal.QueryInterface(_bridgePtr, in iid, out ppv);
                return hr == 0 ? CustomQueryInterfaceResult.Handled : CustomQueryInterfaceResult.Failed;
            }
            ppv = IntPtr.Zero;
            return CustomQueryInterfaceResult.NotHandled;
        }
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr dispatcherQueueController);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int DwmGetWindowAttributeRect(IntPtr hwnd, int attr, out RECT attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    #endregion
}
