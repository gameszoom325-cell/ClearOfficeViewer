# Changelog

All notable changes to ClearOffice Viewer are documented in this file.

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
