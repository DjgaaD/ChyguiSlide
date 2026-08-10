#Requires -Version 5.1
<#
.SYNOPSIS
  Compiles the Inno Setup installer from artifacts\release\win-x64.

.PARAMETER Version
  Semantic version (e.g. 0.0.1). Defaults from version.json.

.PARAMETER Channel
  Release channel (beta / release). Defaults from version.json.
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$Version = "",
    [Parameter(Mandatory = $false)]
    [string]$Channel = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

. (Join-Path $Root "scripts\Version-Common.ps1")
$vi = Get-ChyguiSlideVersionInfo -Root $Root
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $vi.Version }
if ([string]::IsNullOrWhiteSpace($Channel)) { $Channel = $vi.Channel }
$label = if ($Channel -eq "release") { $Version } else { "$Version-$Channel" }
$displayName = if ($Channel -eq "release") { "ChyguiSlide" } else { "ChyguiSlide ($Channel)" }

$IssPath = Join-Path $Root "installer\ChyguiSlide.iss"
$PublishDir = Join-Path $Root "artifacts\release\win-x64"
$Launcher = Join-Path $PublishDir "ChyguiSlide.exe"
$OutSetup = Join-Path $Root "artifacts\release\ChyguiSlide-$label-Setup.exe"

if (-not (Test-Path $IssPath)) {
    throw "Inno script not found: $IssPath"
}

if (-not (Test-Path $Launcher)) {
    throw "Publish folder missing or incomplete: $PublishDir (run Publish-Release.ps1 first)"
}

function Find-ISCC {
    $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }

    $candidates = @(
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) {
            return $path
        }
    }

    return $null
}

$iscc = Find-ISCC
if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup 6 (ISCC.exe) не найден." -ForegroundColor Red
    Write-Host "Установите Inno Setup 6 с русским языком:" -ForegroundColor Yellow
    Write-Host "  https://jrsoftware.org/isinfo.php"
    Write-Host "Затем повторите сборку."
    throw "ISCC.exe not found"
}

Write-Host "=== Building installer $label ($displayName) ===" -ForegroundColor Cyan
Write-Host "ISCC: $iscc"
Write-Host "Script: $IssPath"

if (Test-Path $OutSetup) {
    Remove-Item $OutSetup -Force
}

& $iscc `
    "/DMyAppVersion=$Version" `
    "/DMyAppVersionLabel=$label" `
    "/DMyAppDisplayName=`"$displayName`"" `
    $IssPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $OutSetup)) {
    throw "Expected installer not found: $OutSetup"
}

$sizeMb = [math]::Round((Get-Item $OutSetup).Length / 1MB, 1)
Write-Host ""
Write-Host "Installer ready." -ForegroundColor Green
Write-Host "  Setup: $OutSetup"
Write-Host "  Size:  $sizeMb MB"
