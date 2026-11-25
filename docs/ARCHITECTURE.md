# Architecture Overview

ClearOffice Viewer follows a clean, layered modular architecture separating pure format parsing, Windows-native rendering, application state persistence, and Windows shell integration.

## Solution Topology

| Assembly | Responsibility | Dependencies |
| --- | --- | --- |
| `ClearOfficeViewer.Core` | Format detection, settings/recents/favorites persistence, structured file logging, HKCU registry association service | .NET 9 Base Class Library |
| `ClearOfficeViewer.PdfEngine` | PDF page rasterization (`Windows.Data.Pdf`), word bounding box extraction (`UglyToad.PdfPig`), search, text selection, print pagination | `Core`, `UglyToad.PdfPig`, Windows WinRT `Windows.Data.Pdf` |
| `ClearOfficeViewer.DocumentEngine` | `.docx` OOXML parser, `.doc` binary OLE2 MS-DOC + RTF + HTML parser, WPF `FlowDocument` layout builder | `Core`, `DocumentFormat.OpenXml`, `OpenMcdf` |
| `ClearOfficeViewer.PresentationEngine` | `.pptx` PresentationML parser, `.ppt` OLE2 MS-PPT parser, slide/shape/table/image models | `Core`, `DocumentFormat.OpenXml`, `OpenMcdf` |
| `ClearOfficeViewer.UI` | DWM Mica/Acrylic backdrop helper, `IViewerControl` contract, `PdfViewerControl`, `DocumentViewerControl`, `PresentationViewerControl`, `PresentationFullscreenWindow`, `HtmlViewerControl` | `Core`, all Engines, `Microsoft.Web.WebView2` |
| `ClearOfficeViewer.App` | `App` entry point, command-line launch routing, `MainWindow` shell, navigation, command bar, settings UI, status bar | `Core`, `UI` |
| `ClearOfficeViewer.Tests` | Automated xUnit test suite covering format detection, settings persistence, `.pdf`, `.docx`, `.doc`, `.pptx`, and `.ppt` | `Core`, all Engines, `UI`, `xunit` |

## Data & Rendering Flow

1. **Ingress (`App.OnStartup` / `MainWindow.OpenFileAsync`)**:
   - File paths arrive from command-line arguments (`ClearOfficeViewer.exe example.pdf`), Windows Explorer double-click/Open With, File Open dialog, Recent/Favorites lists, or window drag-and-drop.
   - `FileFormatDetector.DetectFromFile` validates extension and file header magic bytes (`%PDF-`).
2. **Engine Selection & Background Parsing**:
   - Parsing runs off the UI thread (`Task.Run` / `async`) so the window remains responsive.
   - Engines produce immutable or self-contained presentation/document models (`ParsedDocument`, `ParsedPresentation`, `PdfDocumentSession`).
3. **UI Materialization**:
   - `MainWindow` swaps the active `IViewerControl` inside `ViewerHost` and updates window title, contextual toolbar actions, and bottom status bar via `IViewerControl.StatusChanged`.
   - `SettingsService.RecordFileOpened` persists recent history atomically to `%LOCALAPPDATA%\ClearOfficeViewer\settings.json`.
