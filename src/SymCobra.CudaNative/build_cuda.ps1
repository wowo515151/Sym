param(
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'

if (-not $OutputDir) {
    throw "OutputDir is required."
}

$nvcc = 'C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.2\bin\nvcc.exe'
if (-not (Test-Path $nvcc)) {
    Write-Host "CUDA nvcc not found. Skipping native CUDA build."
    exit 0
}

$cl = Get-ChildItem 'C:\Program Files\Microsoft Visual Studio' -Recurse -Filter cl.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like '*\VC\Tools\MSVC\*\bin\Hostx64\x64\cl.exe' } |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $cl) {
    throw "Unable to locate cl.exe for nvcc host compilation."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $scriptDir 'cobra_cuda.cu'

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$dllPath = Join-Path $OutputDir 'cobra_cuda.dll'

if ((Test-Path $dllPath) -and ((Get-Item $dllPath).LastWriteTimeUtc -ge (Get-Item $source).LastWriteTimeUtc)) {
    Write-Host "CUDA native runtime is up to date. Skipping rebuild."
    exit 0
}

& $nvcc `
    -ccbin (Split-Path -Parent $cl) `
    -shared `
    -Xcompiler "/EHsc /MD" `
    -o $dllPath `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "nvcc failed with exit code $LASTEXITCODE."
}
