# RadialConvert

Free, offline, open source file converter for Windows. Hold a modifier key (Shift by default), double-click a file in File Explorer or on the Desktop, and a radial menu of conversions opens at the cursor. A normal double-click still opens the file as usual.

Built with C# / .NET 8 / WPF. Conversions run locally through free command-line tools. Nothing goes to the cloud.

## Status

| Phase | Scope | State |
|---|---|---|
| 1 | Mouse hook, radial menu, tray, settings, image conversions | Done |
| 2 | PDF and documents (Poppler, pdf2docx, LibreOffice, Pandoc, Open XML SDK) | Done |
| 3 | Inno Setup installer, FFmpeg audio/video | Done |

Conversions:
- PNG, JPG/JPEG, WebP, BMP, GIF, TIFF to each other, and any of them to PDF (ImageMagick)
- SVG to PNG, JPG, or PDF (resvg, with Inkscape and then ImageMagick as fallbacks)
- PDF to Word (pdf2docx, packaged with PyInstaller)
- PDF to PowerPoint (Poppler renders each page at 150 DPI, then the Open XML SDK builds one slide per page)
- PDF to PNG/JPG (Poppler; a multi-page PDF goes into a new `<name> pages` folder) and PDF to Text (Poppler `pdftotext -layout`)
- DOCX/DOC/ODT/RTF, PPTX/PPT/ODP, XLSX/XLS/ODS to PDF (LibreOffice headless, with a private profile)
- Markdown, HTML, TXT, DOCX to each other (Pandoc)
- Video (MP4, MKV, MOV, AVI, WebM, WMV, FLV, M4V, MPG, 3GP, TS) to MP4 (H.264), WebM (VP9), MKV, GIF, or MP3 audio (FFmpeg)
- Audio (MP3, WAV, FLAC, M4A, AAC, OGG, Opus, WMA, AIFF) to MP3, WAV, FLAC, M4A, OGG, Opus (FFmpeg)

Audio and video conversions show a real progress bar. Every conversion toast has a Cancel button.

## Requirements

- Windows 10 or 11, x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build (free)
- Free tools placed in `tools/` (see below) or on `PATH`

## Getting the tools

Download links and folder layout are in [tools/README.md](tools/README.md). The app looks for each tool in `tools/`, then on `PATH`, then in the standard Program Files install folder. Settings > Tools in the tray menu shows what was found.

| Tool | License | Used for |
|---|---|---|
| ImageMagick | ImageMagick License (Apache-style) | Images |
| resvg / Inkscape (optional) | Apache-2.0 / MIT, GPL-2.0 | SVG |
| Poppler | GPL-2.0 | PDF to images, text, PowerPoint |
| pdf2docx | AGPL-3.0 (includes PyMuPDF) | PDF to Word |
| LibreOffice | MPL-2.0 | Office to PDF |
| Pandoc | GPL-2.0 | Markdown/HTML/TXT/DOCX |
| FFmpeg (gyan.dev essentials build) | GPL-3.0 | Audio and video |
| DocumentFormat.OpenXml (NuGet) | MIT | Building PPTX |

pdf2docx is built locally into a standalone exe. It needs Python 3.10 to 3.12 at build time only:

```powershell
powershell -ExecutionPolicy Bypass -File helpers\pdf2docx\build.ps1
```

Its dependency versions are pinned in `helpers/pdf2docx/requirements.txt`. The latest PyMuPDF 2.x / OpenCV 5 builds crash inside PyInstaller.

## Build and run

```powershell
dotnet build RadialConvert.sln -c Release
dotnet test RadialConvert.sln
.\src\RadialConvert\bin\Release\net8.0-windows\RadialConvert.exe
```

If you don't want a system-wide SDK, you can install one into the repo folder only:

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
.\dotnet-install.ps1 -Channel 8.0 -InstallDir .dotnet -NoPath
.\.dotnet\dotnet.exe build RadialConvert.sln -c Release
```

## Testing Phase 1 by hand

1. Start `RadialConvert.exe`. A ring icon shows up in the notification area along with a "RadialConvert is running" balloon.
2. Open a folder that has a `.png` in it. Double-click it without holding anything: it should open normally.
3. Hold Shift and double-click the same file: it should not open, and a ring of slices (JPG, WebP, BMP, GIF, TIFF, PDF) appears at the cursor.
4. Hover over the slices to see them highlight. Click JPG. A toast in the bottom-right shows progress, then "Conversion complete" with an Open folder button. `photo.jpg` is created next to the original.
5. Repeat step 4 and you'll get `photo (1).jpg`. Existing files are never overwritten.
6. Press Esc, click the center ✕, or click outside the ring to close it without converting.
7. Try it on the Desktop, on a `.svg`, and on a folder (a folder does nothing).
8. Rename `tools/imagemagick` and convert again. You should see an error toast saying ImageMagick wasn't found, with the path and download link.
9. In the tray menu, Settings lets you switch the key to Ctrl and turn off "Image to JPG". JPG should disappear from the ring right away.
10. The tray's Start with Windows option toggles `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\RadialConvert`.

## Testing Phase 2 by hand

1. Shift + double-click a PDF. You should see Word, PowerPoint, PNG, JPG and Text.
2. Word creates `name.docx`, which opens in Word or LibreOffice with editable text. A scanned PDF has no text layer, so you'll only get images.
3. PowerPoint creates `name.pptx` with one slide per page.
4. PNG on a one-page PDF creates `name.png`. On a multi-page PDF it creates a folder `name pages\` with `name - page 1.png`, ... (zero-padded when there are 10+ pages) and Open folder selects that folder.
5. Shift + double-click a `.docx`. You should see PDF, Markdown, HTML and Text. `.xlsx` and `.pptx` only show PDF.
6. Shift + double-click a `.md`. You should see HTML, Text and Word.

## Testing Phase 3 by hand

1. Shift + double-click an `.mp4`. You should see WebM, MKV, MP3 audio and GIF. Pick WebM: the toast bar fills as it encodes.
2. Start a long conversion and click Cancel on the toast. FFmpeg stops and no partial file is left.
3. Shift + double-click an `.mp3`. You should see WAV, FLAC, M4A, OGG and Opus.

`dotnet test` also runs integration tests that do real conversions. Each one skips itself when its tool isn't installed.

## Building the installer

Needs [Inno Setup 6](https://jrsoftware.org/isdl.php) (free) and the tools set up as above. Close RadialConvert first.

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 0.3.0
```

This runs the tests and publishes the app self-contained, so target PCs don't need .NET. It copies only the tool files each converter uses, writes `THIRD-PARTY-NOTICES.txt`, and compiles `installer\output\RadialConvert-Setup-0.3.0.exe` (about 190 MB). Add `-SkipTests` to skip the test step.

The setup:
- installs per-user by default (no admin), with an option to install for all users
- offers "Start when I sign in" (the same `HKCU\...\Run` value the tray toggle uses) and a desktop shortcut
- closes a running copy before upgrading
- on uninstall, stops the app, removes the Run value and `%LOCALAPPDATA%\RadialConvert`, and keeps your settings in `%APPDATA%\RadialConvert`

LibreOffice is not bundled because it's about 1.5 GB installed. Users who want Office to PDF install it separately, and the app finds it automatically.

## How it works

- `Hooks/MouseHook.cs` installs a `WH_MOUSE_LL` hook on its own thread. On the second button-down of a double click (using the system double-click time and distance), if the modifier is held and the cursor is over a `SHELLDLL_DefView` in the foreground Explorer window or the Desktop, it swallows that down/up pair. Explorer only sees a single click, so nothing opens.
- `Hooks/ShellSelection.cs` uses `IShellWindows`, then `IShellBrowser`, then `IFolderView2` to find the focused item in that exact view (this handles Windows 11 tabs). It then uses UI Automation to confirm the cursor is actually on that item and not on empty space.
- `UI/RadialMenuWindow.cs` is a transparent, topmost WPF window that draws the slices with `PathGeometry`. It's positioned in physical pixels (per-monitor DPI aware) and clamped to the work area.
- `Conversion/` runs each conversion on a background thread. Tools are started with `ArgumentList`, so there's no shell and no quoting issues. Output files are reserved atomically with `FileMode.CreateNew`, and partial output is deleted if a conversion fails.
- Settings are stored in `%APPDATA%\RadialConvert\settings.json`.

## Known limitations

- Explorer windows running elevated (as administrator) can't be read by a non-elevated app. In that case the double-click is swallowed but no menu appears.
- With Ctrl, the first click of the double-click still toggles selection, as Explorer normally does.
- Only the item under the cursor is converted (no batch conversion of multiple files yet).
- PDF to PowerPoint turns pages into pictures, so the slides are not editable text.
- LibreOffice conversions run one at a time. The first one takes a few extra seconds while its profile is created.
- Video to MKV drops subtitle tracks.
- Uninstalling stops every running RadialConvert process, including a copy started from the source folder.
