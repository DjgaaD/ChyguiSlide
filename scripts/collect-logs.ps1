# Copies logs from %LocalAppData%\ChyguiSlide\logs to ./logs and prints last 300 lines
$src = Join-Path $env:LOCALAPPDATA 'ChyguiSlide\logs'
$dst = Join-Path (Get-Location) 'logs'
if (Test-Path $src) {
    Write-Output "Found local app logs at: $src"
    Get-ChildItem -Path $src -File -ErrorAction SilentlyContinue | ForEach-Object {
        $target = Join-Path $dst $_.Name
        Copy-Item -Path $_.FullName -Destination $target -Force
        Write-Output "Copied: $($_.Name) -> $target"
    }
    Write-Output "--- Tail of ./logs/interaction.log ---"
    if (Test-Path (Join-Path $dst 'interaction.log')) {
        Get-Content (Join-Path $dst 'interaction.log') -Tail 300
    } else {
        Write-Output "interaction.log not found in ./logs"
    }
} else {
    Write-Output "No LocalAppData ChyguiSlide logs folder found at $src"
    Write-Output "Showing ./logs contents:";
    Get-ChildItem -Path $dst -File -ErrorAction SilentlyContinue | ForEach-Object { Write-Output $_.FullName }
}
