$ErrorActionPreference = "Stop"

$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;" + $env:PATH
$repoRoot = Split-Path -Parent $PSScriptRoot
$appProj = Join-Path $repoRoot "src\ClearOfficeViewer.App\ClearOfficeViewer.App.csproj"
$publishSetupDir = Join-Path $repoRoot "publish\setup-payload"
$publishPortableDir = Join-Path $repoRoot "publish\portable"
$outDir = Join-Path $PSScriptRoot "Output"

Write-Host "1. Running tests..."
dotnet test (Join-Path $repoRoot "ClearOfficeViewer.sln") -c Release

Write-Host "2. Publishing unpacked self-contained payload for Setup..."
Remove-Item $publishSetupDir -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $appProj -c Release -r win-x64 --self-contained true -o $publishSetupDir `
  -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false

Write-Host "3. Publishing single-file self-contained Portable executable..."
Remove-Item $publishPortableDir -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $appProj -c Release -r win-x64 --self-contained true -o $publishPortableDir `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:DebugSymbols=false

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$portableSrc = Join-Path $publishPortableDir "ClearOfficeViewer.exe"
$portableDst = Join-Path $outDir "ClearOfficeViewer-Portable.exe"
Copy-Item $portableSrc $portableDst -Force

Write-Host "4. Compiling Inno Setup installer..."
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    $iscc = (Get-Command iscc.exe -ErrorAction Stop).Source
}
& $iscc (Join-Path $PSScriptRoot "ClearOfficeViewer.iss")

Write-Host "5. Generating SHA-256 checksums..."
$setupExe = Join-Path $outDir "ClearOfficeViewer-Setup.exe"
$hashLines = @(
    (Get-FileHash $setupExe -Algorithm SHA256),
    (Get-FileHash $portableDst -Algorithm SHA256)
) | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path -Leaf $_.Path)" }
$hashLines | Set-Content (Join-Path $outDir "SHA256SUMS.txt") -Encoding UTF8

Get-ChildItem $outDir | Format-Table Name, Length, LastWriteTime
