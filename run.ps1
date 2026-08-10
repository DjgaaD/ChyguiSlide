# Быстрый запуск приложения (только запуск, без сборки)
$ErrorActionPreference = "Stop"

Set-Location -Path $PSScriptRoot

Write-Host "=== ЗАПУСК ПРИЛОЖЕНИЯ ===" -ForegroundColor Green
Write-Host "Логи запуска: startup.log" -ForegroundColor Yellow
Write-Host "`n" -NoNewline

dotnet run --project .\ChyguiSlide.csproj -p:Platform=x64






