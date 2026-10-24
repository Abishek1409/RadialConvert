# Builds installer\output\RadialConvert-Setup-<version>.exe
#
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 [-Version 0.3.0] [-SkipTests]
#
# 1. Publishes RadialConvert self-contained (no .NET install needed on the target PC).
# 2. Stages only the tool files each converter needs (skips ffplay, headers, docs, ...).
# 3. Compiles installer\RadialConvert.iss with Inno Setup's ISCC.exe.
#
# LibreOffice (~1.5 GB installed) is NOT bundled; the app finds a normal LibreOffice install automatically.
param(
    [string]$Version = '0.3.0',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repo  = Resolve-Path (Join-Path $PSScriptRoot '..')
$tools = Join-Path $repo 'tools'
$stage = Join-Path $PSScriptRoot 'stage'
$dotnet = if (Test-Path (Join-Path $repo '.dotnet\dotnet.exe')) { Join-Path $repo '.dotnet\dotnet.exe' } else { 'dotnet' }

function Find-First([string]$pattern) {
    Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
}

function Copy-Tree([string]$from, [string]$to, [string[]]$exclude = @()) {
    New-Item -ItemType Directory -Force $to | Out-Null
    Get-ChildItem $from -Force | Where-Object { $exclude -notcontains $_.Name } |
        Copy-Item -Destination $to -Recurse -Force
}

# --- ISCC -------------------------------------------------------------------------------------------
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php' }

# --- Tests ------------------------------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Host '== Running tests'
    & $dotnet test (Join-Path $repo 'RadialConvert.sln') -c Release -nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; installer not built.' }
}

# --- Publish ----------------------------------------------------------------------------------------
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$app = Join-Path $stage 'app'
Write-Host '== Publishing app (self-contained win-x64)'
& $dotnet publish (Join-Path $repo 'src\RadialConvert\RadialConvert.csproj') -c Release -r win-x64 --self-contained true `
    -p:CopyTools=false -p:Version=$Version -p:DebugType=none -o $app -nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

# --- Tools ------------------------------------------------------------------------------------------
$out = Join-Path $stage 'tools'
$notices = [System.Collections.Generic.List[string]]::new()
$missing = [System.Collections.Generic.List[string]]::new()

# ImageMagick: bundled tools\imagemagick, else the installed copy (minus uninstaller and HTML docs).
$im = $null
if (Test-Path "$tools\imagemagick\magick.exe") { $im = "$tools\imagemagick" }
else { $f = Find-First "$env:ProgramFiles\ImageMagick-*\magick.exe"; if ($f) { $im = $f.DirectoryName } }
if ($im) {
    Write-Host "== ImageMagick: $im"
    Copy-Tree $im "$out\imagemagick" @('unins000.exe', 'unins000.dat', 'www', 'index.html', 'ChangeLog.md', 'uninstall', 'images')
    $notices.Add("ImageMagick - ImageMagick License (https://imagemagick.org/script/license.php). See tools\imagemagick\License.txt")
} else { $missing.Add('ImageMagick') }

# Poppler: only Library\bin (exes + DLLs) and share\poppler (encoding data).
$pdftoppm = Find-First "$tools\poppler\*\Library\bin\pdftoppm.exe"
if (-not $pdftoppm) { $pdftoppm = Find-First "$tools\poppler\Library\bin\pdftoppm.exe" }
if ($pdftoppm) {
    $popRoot = $pdftoppm.Directory.Parent.Parent.FullName
    Write-Host "== Poppler: $popRoot"
    Copy-Tree $pdftoppm.DirectoryName "$out\poppler\Library\bin"
    if (Test-Path "$popRoot\share\poppler") { Copy-Tree "$popRoot\share\poppler" "$out\poppler\share\poppler" }
    $notices.Add('Poppler - GPL-2.0 (https://poppler.freedesktop.org). Windows build: https://github.com/oschwartz10612/poppler-windows')
} else { $missing.Add('Poppler') }

# FFmpeg: ffmpeg.exe only (ffplay/ffprobe are not used) plus its license.
$ffmpeg = Find-First "$tools\ffmpeg\*\bin\ffmpeg.exe"
if (-not $ffmpeg) { $ffmpeg = Find-First "$tools\ffmpeg\bin\ffmpeg.exe" }
if ($ffmpeg) {
    Write-Host "== FFmpeg: $($ffmpeg.FullName)"
    New-Item -ItemType Directory -Force "$out\ffmpeg\bin" | Out-Null
    Copy-Item $ffmpeg.FullName "$out\ffmpeg\bin\"
    $lic = Join-Path $ffmpeg.Directory.Parent.FullName 'LICENSE'
    if (Test-Path $lic) { Copy-Item $lic "$out\ffmpeg\LICENSE.txt" }
    $notices.Add('FFmpeg - GPL-3.0 build by gyan.dev (https://www.gyan.dev/ffmpeg/builds/). Source: https://ffmpeg.org')
} else { $missing.Add('FFmpeg') }

# Pandoc: exe + copyright files.
$pandoc = Find-First "$tools\pandoc\*\pandoc.exe"
if (-not $pandoc) { $pandoc = Find-First "$tools\pandoc\pandoc.exe" }
if ($pandoc) {
    Write-Host "== Pandoc: $($pandoc.FullName)"
    New-Item -ItemType Directory -Force "$out\pandoc" | Out-Null
    Copy-Item $pandoc.FullName "$out\pandoc\"
    Get-ChildItem $pandoc.DirectoryName -Filter 'COPY*' | Copy-Item -Destination "$out\pandoc\"
    $notices.Add('Pandoc - GPL-2.0-or-later (https://pandoc.org)')
} else { $missing.Add('Pandoc') }

# pdf2docx (PyInstaller onedir build).
if (Test-Path "$tools\pdf2docx\pdf2docx.exe") {
    Write-Host '== pdf2docx'
    Copy-Tree "$tools\pdf2docx" "$out\pdf2docx"
    $notices.Add('pdf2docx - MIT, bundles PyMuPDF (AGPL-3.0, https://pymupdf.readthedocs.io), OpenCV (Apache-2.0), NumPy (BSD), python-docx (MIT)')
} else { $missing.Add('pdf2docx (run helpers\pdf2docx\build.ps1)') }

# Optional SVG renderers.
foreach ($opt in @(@{ Name = 'resvg'; Exe = 'resvg\resvg.exe'; Notice = 'resvg - Apache-2.0 / MIT (https://github.com/linebender/resvg)' },
                   @{ Name = 'inkscape'; Exe = 'inkscape\bin\inkscape.com'; Notice = 'Inkscape - GPL-2.0 (https://inkscape.org)' })) {
    if (Test-Path (Join-Path $tools $opt.Exe)) {
        Write-Host "== $($opt.Name)"
        Copy-Tree (Join-Path $tools $opt.Name) (Join-Path $out $opt.Name)
        $notices.Add($opt.Notice)
    }
}

if ($missing.Count -gt 0) {
    Write-Warning "Not bundled (those conversions will show a 'tool missing' message): $($missing -join ', ')"
}

$notices.Add('DocumentFormat.OpenXml - MIT (https://github.com/dotnet/Open-XML-SDK)')
$notices.Add('.NET runtime - MIT (https://github.com/dotnet/runtime)')
@(
    'RadialConvert bundles the following free, open source tools. Each is distributed under its own license.'
    'Their license files are included in the matching folder under "tools".'
    ''
    $notices | ForEach-Object { " - $_" }
    ''
    'Office to PDF conversion uses LibreOffice (MPL-2.0), which is not bundled.'
    'Install it from https://www.libreoffice.org/download/download-libreoffice/ and RadialConvert finds it automatically.'
) | Set-Content -Encoding UTF8 (Join-Path $stage 'THIRD-PARTY-NOTICES.txt')

"{0:N0} MB staged" -f ((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB) | Write-Host

# --- Compile ----------------------------------------------------------------------------------------
Write-Host "== Compiling installer with $iscc"
& $iscc /Q "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'RadialConvert.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }

$setup = Join-Path $PSScriptRoot "output\RadialConvert-Setup-$Version.exe"
"Built $setup ({0:N0} MB)" -f ((Get-Item $setup).Length / 1MB) | Write-Host
