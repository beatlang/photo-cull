param(
    [string]$Configuration = "Debug",
    [switch]$SkipModelDownload
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw "dotnet SDK not found. Install the .NET 10 SDK." }

$model = if ($env:PHOTOCULL_MODEL) { $env:PHOTOCULL_MODEL } else { Join-Path $env:LOCALAPPDATA "PhotoCull\models\siglip-vision.onnx" }
if (-not (Test-Path $model) -and -not $SkipModelDownload) {
    Write-Host "Model missing, downloading (one-time, ~372 MB)..."
    & (Join-Path $PSScriptRoot "download-models.ps1")
}
elseif (-not (Test-Path $model)) {
    Write-Warning "Model missing: embeddings will be skipped."
}

& $dotnet run --project (Join-Path $root "src\PhotoCull.App\PhotoCull.App.csproj") -c $Configuration -p:Platform=x64
