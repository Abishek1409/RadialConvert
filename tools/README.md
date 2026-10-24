# tools/

Put the free command-line tools in this folder. Everything here is copied next to `RadialConvert.exe` when you build. If a tool isn't found here, RadialConvert also looks on `PATH` and in the standard install folders under Program Files.

| Tool | Download | What to get | Put it at |
|---|---|---|---|
| ImageMagick | https://imagemagick.org/script/download.php#windows | The installer, or the `portable-Q16-HDRI-x64` 7z archive | Installed, or `tools/imagemagick/magick.exe` |
| resvg (optional) | https://github.com/linebender/resvg/releases | `resvg-win64.zip` | `tools/resvg/resvg.exe` |
| Inkscape (optional) | https://inkscape.org/release/ | 64-bit 7z archive | `tools/inkscape/bin/inkscape.com` |
| Poppler | https://github.com/oschwartz10612/poppler-windows/releases | `Release-xx.xx.x-0.zip` | Unzip into `tools/poppler/` so you have `tools/poppler/Library/bin/pdftoppm.exe` |
| pdf2docx | Built locally | Run `helpers\pdf2docx\build.ps1` | `tools/pdf2docx/pdf2docx.exe` (the script puts it there) |
| LibreOffice | https://www.libreoffice.org/download/download-libreoffice/ | Windows x86_64 installer | Installed to `C:\Program Files\LibreOffice` (found automatically) |
| Pandoc | https://github.com/jgm/pandoc/releases | `pandoc-x.y-windows-x86_64.zip` | `tools/pandoc/pandoc.exe` |

| FFmpeg | https://www.gyan.dev/ffmpeg/builds/ | `ffmpeg-release-essentials` (zip or 7z) | Extract into `tools/ffmpeg/` so you have `tools/ffmpeg/<build>/bin/ffmpeg.exe` |

Only needed to build the installer:

| Tool | Download |
|---|---|
| Inno Setup 6 | https://jrsoftware.org/isdl.php |
| 7-Zip (to open .7z archives) | https://www.7-zip.org/ |
