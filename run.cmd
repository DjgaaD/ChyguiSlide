@echo off
REM Быстрый запуск ChyguiSlide (двойной клик)
cd /d "%~dp0"
dotnet run --project ".\ChyguiSlide.csproj" -p:Platform=x64
