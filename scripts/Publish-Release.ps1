#Requires -Version 5.1
<#
.SYNOPSIS
  Builds a distributable Release x64 (self-contained), packs ZIP, and builds Setup.exe.

  Version/channel default from version.json (currently 0.0.1 beta).

  Layout (win-x64):
    ChyguiSlide.exe   - tiny launcher
    app\               - real app + all DLLs
    README.txt

  Also produces:
    ChyguiSlide-$VersionLabel-win-x64.zip
    ChyguiSlide-$VersionLabel-Setup.exe   (unless -SkipInstaller)
#>
param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$Channel = "",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

. (Join-Path $Root "scripts\Version-Common.ps1")
$vi = Get-ChyguiSlideVersionInfo -Root $Root
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $vi.Version }
if ([string]::IsNullOrWhiteSpace($Channel)) { $Channel = $vi.Channel }
$VersionLabel = if ($Channel -eq "release") { $Version } else { "$Version-$Channel" }
$DisplayName = if ($Channel -eq "release") { "Чугуй Слайды" } else { "Чугуй Слайды ($Channel)" }

# Держим ChyguiSlide.Version.props в синхроне с version.json / параметрами
$propsPath = Join-Path $Root "ChyguiSlide.Version.props"
$props = @"
<Project>
  <!-- Автообновляется Publish-Release.ps1 из version.json / параметров. -->
  <PropertyGroup>
    <AppVersion>$Version</AppVersion>
    <AppChannel>$Channel</AppChannel>
    <Version>`$(AppVersion)</Version>
    <AssemblyVersion>`$(AppVersion).0</AssemblyVersion>
    <FileVersion>`$(AppVersion).0</FileVersion>
    <InformationalVersion Condition="'`$(AppChannel)' != '' and '`$(AppChannel)' != 'release'">`$(AppVersion)-`$(AppChannel)</InformationalVersion>
    <InformationalVersion Condition="'`$(InformationalVersion)' == ''">`$(AppVersion)</InformationalVersion>
    <ApplicationDisplayVersion>`$(AppVersion)</ApplicationDisplayVersion>
    <ApplicationVersion>1</ApplicationVersion>
  </PropertyGroup>

  <ItemGroup>
    <AssemblyMetadata Include="AppChannel" Value="`$(AppChannel)" />
  </ItemGroup>
</Project>
"@
[System.IO.File]::WriteAllText($propsPath, $props.TrimStart() + "`r`n", [System.Text.UTF8Encoding]::new($false))

# Package.appxmanifest Identity Version = a.b.c.0
$manifestPath = Join-Path $Root "Package.appxmanifest"
if (Test-Path $manifestPath) {
    $manifest = [System.IO.File]::ReadAllText($manifestPath)
    $manifest = [regex]::Replace(
        $manifest,
        'Version="\d+\.\d+\.\d+\.\d+"',
        "Version=`"$Version.0`"",
        1)
    [System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))
}

Write-Host "=== ChyguiSlide release $VersionLabel ($DisplayName, win-x64) ===" -ForegroundColor Cyan

Get-Process ChyguiSlide -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Stopping $($_.ProcessName) (PID $($_.Id))..."
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Seconds 1

$PublishDir = Join-Path $Root "artifacts\release\win-x64"
$ZipPath = Join-Path $Root "artifacts\release\ChyguiSlide-$VersionLabel-win-x64.zip"
$SetupPath = Join-Path $Root "artifacts\release\ChyguiSlide-$VersionLabel-Setup.exe"
$ReadmePath = Join-Path $PublishDir "README.txt"
$LauncherSrc = Join-Path $Root "scripts\ChyguiSlide.Launcher.cs"

if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

Write-Host "Publishing..."
dotnet publish `
    -c $Configuration `
    -p:Platform=x64 `
    -p:PublishProfile=win-x64 `
    -p:AppVersion=$Version `
    -p:AppChannel=$Channel `
    -p:Version=$Version `
    -p:ApplicationDisplayVersion=$Version `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $PublishDir "ChyguiSlide.exe"
if (-not (Test-Path $exe)) {
    throw "ChyguiSlide.exe not found in $PublishDir"
}

# Remove leftover culture satellite folders (WinAppSDK / .NET)
$keepCultures = @("ru", "ru-RU", "en", "en-US")
$removed = 0
Get-ChildItem $PublishDir -Directory | Where-Object {
    $_.Name -match '^[a-z]{2}([_-][A-Za-z0-9]+)*$' -and ($keepCultures -notcontains $_.Name)
} | ForEach-Object {
    Remove-Item $_.FullName -Recurse -Force
    $removed++
}
if ($removed -gt 0) {
    Write-Host "Removed $removed unused culture folders."
}

# Move everything into app\ so the root stays clean
Write-Host "Organizing into app\ ..."
$appDir = Join-Path $PublishDir "app"
New-Item -ItemType Directory -Path $appDir -Force | Out-Null
Get-ChildItem $PublishDir -Force | Where-Object { $_.Name -ne "app" } | ForEach-Object {
    Move-Item -LiteralPath $_.FullName -Destination (Join-Path $appDir $_.Name) -Force
}

$appExe = Join-Path $appDir "ChyguiSlide.exe"
if (-not (Test-Path $appExe)) {
    throw "app\ChyguiSlide.exe missing after move"
}

# Tiny Framework launcher in root (no extra runtime to ship)
$cscCandidates = @(
    "${env:WINDIR}\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "${env:WINDIR}\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    throw "csc.exe not found - cannot build launcher"
}

$launcherOut = Join-Path $PublishDir "ChyguiSlide.exe"
$iconPath = Join-Path $Root "Assets\AppIcon.ico"
$cscArgs = @("/nologo", "/target:winexe", "/optimize+", "/reference:System.Windows.Forms.dll", "/out:$launcherOut")
if (Test-Path $iconPath) {
    $cscArgs += "/win32icon:$iconPath"
}
$cscArgs += $LauncherSrc
Write-Host "Building launcher..."
& $csc @cscArgs
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $launcherOut)) {
    throw "Launcher compile failed"
}

$readmeTemplate = Join-Path $Root "scripts\Release-README.txt"
if (Test-Path $readmeTemplate) {
    $text = [System.IO.File]::ReadAllText($readmeTemplate)
    $text = $text -replace "\{VERSION_LABEL\}", $VersionLabel
    $text = $text -replace "\{VERSION\}", $Version
    $text = $text -replace "\{CHANNEL\}", $Channel
    $text = $text -replace "\{DISPLAY_NAME\}", $DisplayName
    [System.IO.File]::WriteAllText($ReadmePath, $text, [System.Text.UTF8Encoding]::new($true))
} else {
    [System.IO.File]::WriteAllText(
        $ReadmePath,
        "$DisplayName $Version`r`nRun ChyguiSlide.exe`r`nApp files are in app\",
        [System.Text.UTF8Encoding]::new($true))
}

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Write-Host "Creating ZIP..."
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipPath -CompressionLevel Optimal

# Guard: never ship developer catalog / settings
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $bad = @($zip.Entries | Where-Object {
        $_.FullName -match '(?i)(catalog\.db|display-settings\.json|yandex-disk\.json)$'
    })
    if ($bad.Count -gt 0) {
        throw ("Release ZIP contains user data (forbidden): " + ($bad.FullName -join ', '))
    }
}
finally {
    $zip.Dispose()
}

$seedSps = Join-Path $appDir "Assets\Seed\PesnVozr\pv3300.sps"
$bible = Join-Path $appDir "Assets\Bible\bible.json"
if (-not (Test-Path $seedSps)) {
    throw "Release is missing Pesn Vozr seed Assets\Seed\PesnVozr\pv3300.sps"
}
if (-not (Test-Path $bible)) {
    throw "Release is missing Bible Assets\Bible\bible.json"
}

$zipItem = Get-Item $ZipPath
$sizeMb = [math]::Round($zipItem.Length / 1MB, 1)
$rootCount = @(Get-ChildItem $PublishDir -File).Count
$appCount = @(Get-ChildItem $appDir -Recurse -File).Count

$setupSizeMb = $null
if (-not $SkipInstaller) {
    Write-Host ""
    & (Join-Path $Root "scripts\Build-Installer.ps1") -Version $Version -Channel $Channel
    if (-not (Test-Path $SetupPath)) {
        throw "Installer build failed (Setup.exe not found)"
    }
    $setupSizeMb = [math]::Round((Get-Item $SetupPath).Length / 1MB, 1)
}
else {
    Write-Host "Skipping installer (-SkipInstaller)." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Channel: $Channel"
Write-Host "  Folder:  $PublishDir"
Write-Host "  ZIP:     $ZipPath"
Write-Host "  Size:    $sizeMb MB"
if ($null -ne $setupSizeMb) {
    Write-Host "  Setup:   $SetupPath ($setupSizeMb MB)"
}
Write-Host "  Root files: $rootCount  |  app\ files: $appCount"
Write-Host "  Launch: $(Join-Path $PublishDir 'ChyguiSlide.exe')"
