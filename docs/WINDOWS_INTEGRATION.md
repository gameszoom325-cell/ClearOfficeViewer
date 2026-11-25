# Windows 11 Integration & Registry Specification

## Desktop Window Manager (DWM) Backdrop Composition

## Desktop Composition & Transparency Engine (`TransparencyEngine`)

`TransparencyEngine` implements a true multi-stage Windows GPU compositor pipeline:

```text
Desktop / Window behind
        ↓
DWM (Desktop Window Manager)
        ↓
Windows.UI.Composition HostBackdropBrush
        ↓
Direct2D GaussianBlurEffect (GPU, 0–100% Blur Intensity)
        ↓
Tint + Adaptive Color Veil (0–100% Tint Strength, Adaptation ON/OFF)
        ↓
Frosted Noise Grain & Specular Rim
        ↓
WPF Controls
```

- **Deep Acrylic & Custom Glass (`Windows.UI.Composition`)**: Attaches a `DesktopWindowTarget` to a borderless, click-through, `WS_EX_NOREDIRECTIONBITMAP` backdrop window kept pixel-locked directly beneath `MainWindow` via `WM_WINDOWPOSCHANGED` + `DWMWA_EXTENDED_FRAME_BOUNDS`. Uses `Compositor.CreateHostBackdropBrush()` piped through a zero-dependency COM-interoped `CLSID_D2D1GaussianBlur` effect (`NativeGaussianBlurEffect`) and live `CompositionColorBrush` tint layers.
- **Mica & Acrylic (`DWMWA_SYSTEMBACKDROP_TYPE`)**: Uses native Windows 11 `DWMSBT_MAINWINDOW (2)` (`Mica`) and `DWMSBT_TRANSIENTWINDOW (3)` (`Acrylic`).
- **Dynamic System Integration**: Listens to `SystemEvents.UserPreferenceChanged`, `WM_THEMECHANGED`, and `WM_DWMCOMPOSITIONCHANGED` via `HandleBackdropChanges()`, and exposes `EnableAcrylic()`, `UpdateBackdrop()`, `ChangeTintOpacity()`, `ChangeBlurIntensity()`, `SetBackgroundAdaptation()`, and `DetectWindowsTheme()`.
- `DwmExtendFrameIntoClientArea(-1)` + `HwndSource.CompositionTarget.BackgroundColor = Colors.Transparent` + `WindowChrome` (`GlassFrameThickness = -1`) allow DWM to composite desktop wallpaper and background applications behind the shell without fake opacity overlays.

## Shell File Associations & "Open With"

Both the Inno Setup installer (`installer/ClearOfficeViewer.iss`) and the in-app `FileAssociationService` register standard Windows shell entries under `HKCU` (or `HKLM` when elevated):

1. `Software\Classes\Applications\ClearOfficeViewer.exe\shell\open\command` -> `"<path>\ClearOfficeViewer.exe" "%1"`
2. `Software\Classes\Applications\ClearOfficeViewer.exe\SupportedTypes` -> `.pdf`, `.doc`, `.docx`, `.ppt`, `.pptx`, `.html`, `.htm`
3. Per-extension ProgIDs (`ClearOfficeViewer.Pdf`, `ClearOfficeViewer.Doc`, `ClearOfficeViewer.Docx`, `ClearOfficeViewer.Ppt`, `ClearOfficeViewer.Pptx`, `ClearOfficeViewer.Html`, `ClearOfficeViewer.Htm`) linked via `Software\Classes\.<ext>\OpenWithProgids`.
4. `Software\ClearOfficeViewer\Capabilities\FileAssociations` + `Software\RegisteredApplications` for Windows Settings -> Apps -> Default Apps.
5. `SHChangeNotify(SHCNE_ASSOCCHANGED, ...)` to refresh File Explorer immediately.
