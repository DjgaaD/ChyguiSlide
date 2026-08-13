#Requires -Version 5.1
<#
.SYNOPSIS
  Reads version.json from the repo root.
#>
function Get-ChyguiSlideVersionInfo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $path = Join-Path $Root "version.json"
    if (-not (Test-Path $path)) {
        throw "version.json not found: $path"
    }

    $json = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    $version = [string]$json.version
    $channel = [string]$json.channel
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "version.json: 'version' is empty"
    }
    if ([string]::IsNullOrWhiteSpace($channel)) {
        $channel = "release"
    }

    $label = if ($channel -eq "release") { $version } else { "$version-$channel" }
    $displayName = if ($channel -eq "release") {
        "Чугуй Слайды"
    } else {
        "Чугуй Слайды ($channel)"
    }

    [pscustomobject]@{
        Version     = $version
        Channel     = $channel
        Label       = $label
        DisplayName = $displayName
        IsBeta      = ($channel -eq "beta")
    }
}
