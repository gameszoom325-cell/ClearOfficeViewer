# Supported File Formats & Parsing Internals

## 1. PDF (`.pdf`)
- **Rasterization**: Uses `Windows.Data.Pdf.PdfDocument` (Direct2D-backed Windows Runtime PDF engine) to render visible pages at zoom-aware DPI (`240px` to `2800px` width).
- **Text Layer & Search**: Uses `UglyToad.PdfPig` to extract word tokens and convert bottom-left PDF user-space bounding boxes into normalized top-down `[0..1]` coordinates (`PdfWordBox`).
- **Selection & Print**: Drag selection intersects normalized rectangles with `PdfWordBox` coordinates to reconstruct reading-order text; `PdfPrintPaginator` renders 200-DPI print pages through `System.Windows.Controls.PrintDialog`.

## 2. Word Documents (`.docx`, `.doc`)
- **`.docx` (WordprocessingML)**: `DocxParser` reads `MainDocumentPart`, `StyleDefinitionsPart` (including `basedOn` inheritance), `NumberingDefinitionsPart` (bullets and decimal numbering), paragraphs, alignment, spacing, borders, shading, hyperlinks, tables (`gridSpan` and `vMerge`), headers/footers, and `ImagePart` drawings.
- **`.doc` (Legacy Word)**: `LegacyDocParser` inspects header signatures to route:
  - OLE2 Compound Files (`D0 CF 11 E0 ...`) via `OpenMcdf.RootStorage`, reading the MS-DOC File Information Block (`WordDocument`), `0Table`/`1Table` `Clx` `PlcPcd` piece table (both UTF-16LE and compressed CP1252 pieces), table markers (`\x07`), and embedded PNG images in the `Data` stream.
  - RTF-wrapped `.doc` files (`{\rtf...`) loaded natively into WPF `FlowDocument`.
  - HTML-disguised `.doc` exports decoded into paragraphs.

## 3. Presentations (`.ppt`, `.pptx`)
- **`.pptx` (PresentationML)**: `PptxParser` preserves `Presentation.SlideIdList` slide ordering, slide dimensions (`SlideSize`), theme colors (`ColorScheme` + luminance transforms), Slide/Layout/Master backgrounds, shapes (`rect`, `roundRect`, `ellipse`, `line`), group shapes, tables, embedded images, and speaker notes.
- **`.ppt` (Legacy PowerPoint)**: `LegacyPptParser` reads the OLE2 `PowerPoint Document` and `Pictures` streams, walking MS-PPT records (`DocumentContainer`, `DocumentAtom`, `SlideListWithText`, `SlideContainer`, `SpContainer`, `ClientAnchor`, `TextCharsAtom`, `TextBytesAtom`, OfficeArt BLIPs).

## 4. Web Files (`.html`, `.htm`)
- **WebView2**: `HtmlViewerControl` embeds `Microsoft.Web.WebView2.Wpf.WebView2` with isolated profile data under `%LOCALAPPDATA%\ClearOfficeViewer\WebView2`, rendering local HTML5, CSS3, JavaScript, and relative local assets.
