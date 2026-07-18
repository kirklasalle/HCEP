# ──────────────────────────────────────────────────────────────
# HCEP — Release Packaging Script
# Copyright © 2026 Kirk LaSalle. All rights reserved.
# ──────────────────────────────────────────────────────────────

$ErrorActionPreference = "Stop"

# Define Paths
$ProjectRoot = Resolve-Path "$PSScriptRoot\.."
$AppCsproj = Join-Path $ProjectRoot "src\HCEP.App\HCEP.App.csproj"
$PublishDir = Join-Path $ProjectRoot "publish"
$BuildPropsPath = Join-Path $ProjectRoot "Directory.Build.props"
$BuildProps = [xml](Get-Content -Path $BuildPropsPath -Raw)
$Version = [string]$BuildProps.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($Version)) {
  throw "Unable to read Version from $BuildPropsPath"
}
$PackageVersion = if ($Version.Split('.').Length -eq 3) { "$Version.0" } else { $Version }

Write-Host "Starting HCEP release packaging process..." -ForegroundColor Cyan
Write-Host "Packaging version: $Version" -ForegroundColor Cyan

# Clean previous publish folder
if (Test-Path $PublishDir) {
    Write-Host "Cleaning existing publish directory: $PublishDir"
    Remove-Item -Path $PublishDir -Recurse -Force | Out-Null
}
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

# Step 1: Ensure models are downloaded
Write-Host "Checking model files..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "download_models.ps1")

# Step 2: Publish App (Self-contained, trimmed, single folder for maximum portability)
Write-Host "Publishing HCEP application via dotnet publish..." -ForegroundColor Cyan
dotnet publish $AppCsproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishReadyToRun=true `
  -p:PublishSingleFile=false `
  --output (Join-Path $PublishDir "app")

# Step 3: Copy AppxManifest.xml for MSIX packaging/sideloading
$ManifestPath = Join-Path $PublishDir "app\AppxManifest.xml"
Write-Host "Creating AppxManifest.xml for MSIX package..." -ForegroundColor Cyan
$ManifestContent = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities">
  <Identity Name="KirkLaSalle.HCEP"
            Publisher="CN=KirkLaSalle, O=KirkLaSalle, C=US"
            Version="$PackageVersion"
            ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>Human Communication Eye Protocol (HCEP)</DisplayName>
    <PublisherDisplayName>Kirk LaSalle</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22000.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-US"/>
  </Resources>
  <Applications>
    <Application Id="App" Executable="HCEP.App.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="HCEP Perception Engine"
                          Description="Real-time multi-modal human communication eye protocol analysis."
                          BackgroundColor="#2D2D30"
                          Square150x150Logo="Assets\Logo.png"
                          Square44x44Logo="Assets\SmallLogo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\WideLogo.png" />
      </uap:VisualElements>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust"/>
    <DeviceCapability Name="webcam"/>
    <DeviceCapability Name="microphone"/>
  </Capabilities>
</Package>
"@
Set-Content -Path $ManifestPath -Value $ManifestContent

# Create Assets folder for placeholders
$AssetsDir = Join-Path $PublishDir "app\Assets"
New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null
Set-Content -Path (Join-Path $AssetsDir "StoreLogo.png") -Value "placeholder"
Set-Content -Path (Join-Path $AssetsDir "Logo.png") -Value "placeholder"
Set-Content -Path (Join-Path $AssetsDir "SmallLogo.png") -Value "placeholder"
Set-Content -Path (Join-Path $AssetsDir "WideLogo.png") -Value "placeholder"

# Step 4: Zip release package
Write-Host "Creating distribution zip archive..." -ForegroundColor Cyan
$ZipPath = Join-Path $PublishDir "HCEP-win-x64-v$Version.zip"
if (Test-Path $ZipPath) {
  Remove-Item -Path $ZipPath -Force | Out-Null
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
  (Join-Path $PublishDir "app"),
  $ZipPath,
  [System.IO.Compression.CompressionLevel]::Optimal,
  $false)

$ZipItem = Get-Item -Path $ZipPath -ErrorAction Stop
Write-Host ("Archive size: {0:N2} MB" -f ($ZipItem.Length / 1MB)) -ForegroundColor Cyan

Write-Host "--------------------------------------------------" -ForegroundColor Green
Write-Host "HCEP packaging completed successfully!" -ForegroundColor Green
Write-Host "Publish Folder: $PublishDir" -ForegroundColor Green
Write-Host "Distribution Archive: $ZipPath" -ForegroundColor Green
Write-Host "App Folder: $(Join-Path $PublishDir "app")" -ForegroundColor Green
Write-Host "--------------------------------------------------" -ForegroundColor Green
