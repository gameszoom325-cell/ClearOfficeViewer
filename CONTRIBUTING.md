# Contributing to ClearOffice Viewer

Thank you for your interest in contributing to ClearOffice Viewer.

## Guiding Principles

- Keep the UI faithful to native Windows 11 productivity guidelines (Settings, File Explorer, Visual Studio). Avoid decorative gradients, excessive blur cards, or unnecessary background services.
- Maintain strict local-only processing with zero network telemetry.
- Keep startup fast and memory usage bounded.

## Reporting Issues

When opening a GitHub Issue, please include:
1. Windows version and build number (`winver`).
2. ClearOffice Viewer version (`1.0.0` or commit hash).
3. File format involved (`.pdf`, `.doc`, `.docx`, `.ppt`, `.pptx`, `.html`, `.htm`).
4. Relevant log lines from `%LOCALAPPDATA%\ClearOfficeViewer\logs\`.

## Pull Request Workflow

1. Fork the repository and create a topic branch from `main`.
2. Follow `.editorconfig` formatting conventions and enable nullable reference checks.
3. Add or update unit tests in `tests/ClearOfficeViewer.Tests/` for parser or core logic changes.
4. Verify `dotnet test ClearOfficeViewer.sln -c Release` passes with zero warnings/errors.
5. Submit a concise pull request describing the problem, solution, and verification performed.
