#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $Version)
    {
        $manifest = Get-Content "manifest.json" | ConvertFrom-Json
        $Version = $manifest.version
    }
    Write-Host "Building rororo-ur-ocr v$Version ($Configuration)..." -ForegroundColor Cyan

    $artifacts = Join-Path $root "artifacts"
    Remove-Item -Recurse -Force $artifacts -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

    dotnet publish "rororo-ur-ocr.csproj" -c $Configuration -r win-x64 --self-contained false `
        -p:PublishSingleFile=true -p:Version=$Version `
        -o "$artifacts/publish"

    Copy-Item "manifest.json" "$artifacts/publish/manifest.json" -Force
    Copy-Item "icon.png" "$artifacts/publish/icon.png" -Force

    $zip = Join-Path $artifacts "plugin.zip"
    Compress-Archive -Path "$artifacts/publish/*" -DestinationPath $zip -Force

    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path (Join-Path $artifacts "manifest.sha256") -Value $hash -NoNewline

    Copy-Item "manifest.json" (Join-Path $artifacts "manifest.json") -Force

    Write-Host "Done. Artifacts:" -ForegroundColor Green
    Get-ChildItem $artifacts
}
finally { Pop-Location }
