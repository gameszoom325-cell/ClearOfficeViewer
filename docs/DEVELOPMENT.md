# Developer & Build Guide

## Prerequisites

- **OS**: Windows 10 (Build 19041+) or Windows 11 (Build 22621+ recommended for native Mica/Acrylic)
- **.NET SDK**: .NET 9.0 SDK (`9.0.321` or newer) with Windows Desktop targeting pack
- **WebView2 Runtime**: Pre-installed on Windows 11 (or Microsoft Edge WebView2 Evergreen Runtime on Windows 10)
- **Installer Compiler (optional)**: Inno Setup 6 (`ISCC.exe`) for building `ClearOfficeViewer-Setup.exe`

## Build & Test Commands

```powershell
# Restore and build Release
dotnet build ClearOfficeViewer.sln -c Release

# Run the xUnit test suite
dotnet test ClearOfficeViewer.sln -c Release

# Run the application directly from source
dotnet run --project src/ClearOfficeViewer.App/ClearOfficeViewer.App.csproj -c Release

# Open a specific file from CLI
dotnet run --project src/ClearOfficeViewer.App/ClearOfficeViewer.App.csproj -c Release -- "C:\path\to\sample.pdf"
```

## Building Release Binaries & Installer

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\build-release.ps1
```

Outputs are placed in `installer/Output/`:
- `ClearOfficeViewer-Setup.exe` (Inno Setup 6 wizard installer)
- `ClearOfficeViewer-Portable.exe` (Self-contained single-file executable)
- `SHA256SUMS.txt` (SHA-256 verification checksums)
