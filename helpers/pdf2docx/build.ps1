# Builds tools\pdf2docx\pdf2docx.exe from pdf2docx_cli.py using an isolated virtual environment.
# Requires Python 3.9-3.12 (64-bit). Run from anywhere:  powershell -ExecutionPolicy Bypass -File helpers\pdf2docx\build.ps1
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Resolve-Path (Join-Path $here '..\..')
$venv = Join-Path $here '.venv'
$out  = Join-Path $repo 'tools\pdf2docx'

# Prefer 3.12 via the py launcher (best wheel coverage for PyMuPDF/OpenCV), else whatever "python" is.
$python = $null
foreach ($v in '-3.12', '-3.11', '-3.10') {
    try { & py $v -c "import sys" 2>$null; if ($LASTEXITCODE -eq 0) { $python = @('py', $v); break } } catch {}
}
if (-not $python) { $python = @('python') }
Write-Host "Using: $($python -join ' ')"

# Always start clean so pinned versions in requirements.txt are exactly what gets bundled.
if (Test-Path $venv) { Remove-Item -Recurse -Force $venv }
& $python[0] $python[1..9] -m venv $venv
$py = Join-Path $venv 'Scripts\python.exe'

& $py -m pip install --disable-pip-version-check --quiet --upgrade pip
& $py -m pip install --disable-pip-version-check --quiet -r (Join-Path $here 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'pip install failed' }

# --onedir starts much faster than --onefile (no unpacking to %TEMP% on every run).
& $py -m PyInstaller --noconfirm --clean --log-level WARN --onedir --console `
    --name pdf2docx `
    --distpath (Join-Path $here 'dist') --workpath (Join-Path $here 'build') --specpath (Join-Path $here 'build') `
    (Join-Path $here 'pdf2docx_cli.py')
if ($LASTEXITCODE -ne 0) { throw 'PyInstaller failed' }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
Copy-Item -Recurse (Join-Path $here 'dist\pdf2docx') $out
Write-Host "Built $out\pdf2docx.exe"
