# Windows 11 Integration & Registry Specification

## Desktop Window Manager (DWM) Backdrop Composition

`WindowBackdropHelper` configures native Windows 11 translucency via `dwmapi.dll`:

- `DWMWA_USE_IMMERSIVE_DARK_MODE (20)`: Synced to `AppsUseLightTheme` (or user override) so window borders, shadows, and system menus match Light/Dark mode.
- `DWMWA_SYSTEMBACKDROP_TYPE (38)`:
  - `DWMSBT_MAINWINDOW (2)` — **Mica**
  - `DWMSBT_TABBEDWINDOW (4)` — **Mica Alt**
  - `DWMSBT_TRANSIENTWINDOW (3)` — **Acrylic**
  - `DWMSBT_NONE (1)` — **Solid**
- `DwmExtendFrameIntoClientArea(-1)` + `HwndSource.CompositionTarget.BackgroundColor = Colors.Transparent` + `WindowChrome` (`GlassFrameThickness = -1`) allow DWM to composite desktop wallpaper and background applications behind the shell without fake opacity overlays.

## Shell File Associations & "Open With"

Both the Inno Setup installer (`installer/ClearOfficeViewer.iss`) and the in-app `FileAssociationService` register standard Windows shell entries under `HKCU` (or `HKLM` when elevated):

1. `Software\Classes\Applications\ClearOfficeViewer.exe\shell\open\command` -> `"<path>\ClearOfficeViewer.exe" "%1"`
2. `Software\Classes\Applications\ClearOfficeViewer.exe\SupportedTypes` -> `.pdf`, `.doc`, `.docx`, `.ppt`, `.pptx`, `.html`, `.htm`
3. Per-extension ProgIDs (`ClearOfficeViewer.Pdf`, `ClearOfficeViewer.Doc`, `ClearOfficeViewer.Docx`, `ClearOfficeViewer.Ppt`, `ClearOfficeViewer.Pptx`, `ClearOfficeViewer.Html`, `ClearOfficeViewer.Htm`) linked via `Software\Classes\.<ext>\OpenWithProgids`.
4. `Software\ClearOfficeViewer\Capabilities\FileAssociations` + `Software\RegisteredApplications` for Windows Settings -> Apps -> Default Apps.
5. `SHChangeNotify(SHCNE_ASSOCCHANGED, ...)` to refresh File Explorer immediately.
