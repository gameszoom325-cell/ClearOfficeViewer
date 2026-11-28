# Changelog

All notable changes to ClearOffice Viewer are documented in this file.

## [1.1.1] - 2026-11-28

### Fixed
- **Bottom-Left Active Document Card (`ActiveDocSidebarBox`)**: Replaced the cramped/truncated layout and misaligned `Close Document` button with a responsive 2-column header row (format badge + dynamically trimming filename that returns to the active document on click) and a full-width, properly spaced `Close Document` action button with explicit hover/pressed states (`SidebarHeaderButtonStyle`, `SidebarActionButtonStyle`).
- **Mica / Acrylic Translucency & Surface Legibility**: Fixed `{DynamicResource BrandAccentBrush}` theme responsiveness on the active document badge/indicator, synchronized `GlassSpecularRim` corner radius on maximize/restore, and ensured sidebar cards/command bar/text remain crisp and readable over dynamic desktop wallpaper & background window blur.
- **PDF Rendering, Resize, Listener & Resource Leak Fixes**:
  - Replaced aggressive per-scroll `CancellationTokenSource` churn in `PdfViewerControl` with a coalesced render loop (`RunRenderLoopAsync`) that eliminates scroll flicker and avoids `OperationCanceledException`/disposal leaks.
  - Guarded `VisualTreeHelper.GetDpi` when unloaded, wrapped clipboard copy errors, unsubscribed `SizeChanged` / WebView2 handlers (`ZoomFactorChanged`, `DocumentTitleChanged`) on `Dispose`, and cancelled in-flight `OpenFileAsync` loads cleanly when clicking `Close Document` or closing the window (`_openFileCts`).
  - Disposed native `Windows.UI.Composition` brushes, `GaussianBlurEffect` factories, `HostBackdropBrush`, and Direct2D COM bridge pointers cleanly inside `TransparencyEngine`.
  - Added `ResilientBackdropConverter` in `SettingsService` so legacy or unknown `Backdrop` enum strings fall back safely without discarding recent files or favorites.

## [1.1.0] - 2026-11-25

### Added
- **True Dynamic Compositor Transparency (`TransparencyEngine`)**: Replaced static backdrop overrides with a real-time `Windows.UI.Composition` pipeline (`Compositor.CreateHostBackdropBrush()` -> Direct2D `CLSID_D2D1GaussianBlur` via `IGraphicsEffectD2D1Interop` -> adaptive tint & noise layer -> WPF controls) hosted on a Z-order-synchronized `WS_EX_NOREDIRECTIONBITMAP` composition target.
- **Adaptive Background Color Bleeding**: Desktop wallpaper tones and underlying open application colors dynamically tint and illuminate the frosted glass in real time, even when moving the window or when unfocused.
- **Appearance Controls in Settings**:
  - **Material**: `Deep Acrylic` (Default), `Custom Glass`, `Acrylic`, and `Mica`.
  - **Blur intensity (`0–100%`)**: Live GPU Gaussian blur standard deviation control.
  - **Tint strength (`0–100%`)**: Live glass tint opacity control.
  - **Background adaptation (`ON/OFF`)**: Toggle between vivid adaptive wallpaper/window bleed and uniform neutral frosting.

## [1.0.1] - 2026-11-25

### Changed
- Upgraded default window backdrop to custom **Deep Acrylic Glass** powered by Win32 DWM `SetWindowCompositionAttribute` (`ACCENT_ENABLE_ACRYLICBLURBEHIND`), `DWMWA_WINDOW_CORNER_PREFERENCE`, `DWMWA_BORDER_COLOR`, and a GPU-cached tiled monochrome Acrylic noise grain layer (`GlassNoiseBrush`) with specular glass edge highlighting.
- Added **Ultra Translucent Glass** mode and a live **Glass tint density** (`20%`–`75%`) slider in Settings for real-time adjustment of the dark-tinted blur layer over desktop wallpapers and background windows.
- Tuned translucent content surface contrast and Dark glass theme defaults for optimal legibility over bright or colorful desktop backgrounds.

## [1.0.0] - 2026-11-25

### Added
- Native Windows 11 WPF (.NET 9) application shell with DWM Mica, Mica Alt, Acrylic, and Solid backdrop support and Light/Dark/System theme integration.
- Multi-page PDF viewer (`Windows.Data.Pdf` + `UglyToad.PdfPig`) with lazy DPI-scaled rendering, text selection, clipboard copy, inline search (`Ctrl+F`), Fit Width/Fit Page/custom zoom, and Windows print support (`Ctrl+P`).
- Word document viewer supporting `.docx` (WordprocessingML paragraphs, headings, lists, tables, images, hyperlinks, headers/footers) and `.doc` (OLE2 MS-DOC FIB + Clx piece table, RTF, and HTML fallback).
- PowerPoint presentation viewer supporting `.pptx` (PresentationML slides, order, theme colors, shapes, tables, images, speaker notes) and `.ppt` (OLE2 MS-PPT record parser), plus full-screen Presentation Mode (`F5`).
- Local HTML/HTM viewer powered by Microsoft Edge WebView2 with HTML5, CSS3, JavaScript execution, zoom, find, and print support.
- Home, Recent files, Favorites, and Settings navigation views with atomic JSON persistence.
- Windows Shell "Open With" and Default Apps registry integration for `.pdf`, `.doc`, `.docx`, `.ppt`, `.pptx`, `.html`, and `.htm`.
- Inno Setup 6 installer (`ClearOfficeViewer-Setup.exe`) and self-contained single-file portable binary (`ClearOfficeViewer-Portable.exe`).
