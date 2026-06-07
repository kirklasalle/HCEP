# ──────────────────────────────────────────────────────────────
# HCEP — Model Downloader Script
# Copyright © 2026 Kirk LaSalle. All rights reserved.
# ──────────────────────────────────────────────────────────────

$ErrorActionPreference = "Stop"

# Target directory
$ModelDir = Join-Path $PSScriptRoot "..\src\HCEP.App\models"
if (-not (Test-Path $ModelDir)) {
    Write-Host "Creating models directory: $ModelDir"
    New-Item -ItemType Directory -Path $ModelDir -Force | Out-Null
}

# Model URLs
$Models = @(
    @{
        Name = "arcface.onnx"
        Url  = "https://github.com/onnx/models/raw/main/validated/vision/body_analysis/arcface/model/arcface-10.onnx"
    },
    @{
        Name = "ggml-tiny.bin"
        Url  = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin"
    }
)

foreach ($Model in $Models) {
    $FilePath = Join-Path $ModelDir $Model.Name
    if (Test-Path $FilePath) {
        Write-Host "Model '$($Model.Name)' already exists at: $FilePath"
    } else {
        Write-Host "Downloading $($Model.Name)..."
        Write-Host "URL: $($Model.Url)"
        Write-Host "Destination: $FilePath"
        try {
            Invoke-WebRequest -Uri $Model.Url -OutFile $FilePath -UserAgent "Mozilla/5.0"
            Write-Host "Successfully downloaded $($Model.Name)!" -ForegroundColor Green
        } catch {
            Write-Error "Failed to download $($Model.Name): $_"
        }
    }
}

Write-Host "All models ready." -ForegroundColor Green
