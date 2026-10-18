$ErrorActionPreference = 'Stop'

$local = $env:LOCALAPPDATA
if (-not $local) { Write-Error "LOCALAPPDATA not set"; exit 1 }
$modelsDir = Join-Path $local "PhotoCull\models"
New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null

$dest = Join-Path $modelsDir "siglip-vision.onnx"
$uri = 'https://huggingface.co/Xenova/siglip-base-patch16-224/resolve/main/onnx/vision_model.onnx'
$expectedSha = 'f89d41bac7f4d4b87e010a467d93f98689d708916ed22f5a07f96fdfa26f475f'
$expectedSize = 371819850

if (Test-Path $dest) {
    Write-Host "Found existing file: $dest"
    $hash = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item $dest).Length
    if ($hash -eq $expectedSha -and $size -eq $expectedSize) {
        Write-Host "Model present and verified. Skipping download."
        exit 0
    }
    else {
        Write-Warning "Existing file mismatch (sha or size). Deleting and re-downloading."
        Remove-Item $dest -Force
    }
}

Write-Host "Downloading model to $dest"
Invoke-WebRequest -Uri $uri -OutFile $dest -UseBasicParsing

# verify
$hash = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $dest).Length
if ($hash -ne $expectedSha -or $size -ne $expectedSize) {
    Write-Error "Downloaded file failed integrity check. Deleting $dest"
    Remove-Item $dest -Force
    exit 2
}

Write-Host "Model downloaded and verified."