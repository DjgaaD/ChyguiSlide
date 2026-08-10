# Скрипт сборки проекта ChyguiSlide с подробным логированием
$ErrorActionPreference = "Stop"

# Переходим в папку со скриптом
Set-Location -Path $PSScriptRoot

$logFile = "build.log"
$startTime = Get-Date

Write-Host "=== НАЧАЛО СБОРКИ ПРОЕКТА ===" -ForegroundColor Green
Write-Host "Время начала: $startTime" -ForegroundColor Cyan
Write-Host "Папка проекта: $PSScriptRoot" -ForegroundColor Cyan

# Очищаем старый лог
if (Test-Path $logFile) {
    Remove-Item $logFile -Force
    Write-Host "Старый лог удален" -ForegroundColor Yellow
}

function Log-Message {
    param([string]$Message, [string]$Color = "White")
    $timestamp = Get-Date -Format "HH:mm:ss.fff"
    $logMessage = "[$timestamp] $Message"
    Write-Host $logMessage -ForegroundColor $Color
    Add-Content -Path $logFile -Value $logMessage
}

Log-Message "=== НАЧАЛО СБОРКИ ПРОЕКТА ChyguiSlide ===" "Green"

# Проверка наличия .NET SDK
Log-Message "Проверка наличия .NET SDK..."
try {
    $dotnetVersion = dotnet --version
    Log-Message "Найден .NET SDK версии: $dotnetVersion" "Green"
} catch {
    Log-Message "ОШИБКА: .NET SDK не найден! Установите .NET 8.0 SDK" "Red"
    exit 1
}

# Проверка наличия проекта
if (-not (Test-Path "ChyguiSlide.csproj")) {
    Log-Message "ОШИБКА: Файл ChyguiSlide.csproj не найден!" "Red"
    exit 1
}
Log-Message "Файл проекта найден" "Green"

# Восстановление пакетов
Log-Message "Восстановление NuGet пакетов..."
try {
    $restoreOutput = dotnet restore -p:Platform=x64 2>&1
    foreach ($line in $restoreOutput) {
        $lineStr = $line.ToString()
        if ($lineStr) {
            if ($lineStr -match "error|Ошибка|ERROR") {
                Log-Message "RESTORE: $lineStr" "Red"
            } elseif ($lineStr -match "warning|Предупреждение|WARNING") {
                Log-Message "RESTORE: $lineStr" "Yellow"
            } else {
                Log-Message "RESTORE: $lineStr" "Gray"
            }
        }
    }
    if ($LASTEXITCODE -eq 0) {
        Log-Message "Пакеты восстановлены успешно" "Green"
    } else {
        Log-Message "ОШИБКА: Не удалось восстановить пакеты (код: $LASTEXITCODE)" "Red"
        exit 1
    }
} catch {
    Log-Message "ОШИБКА при восстановлении пакетов: $_" "Red"
    exit 1
}

# Сборка проекта
Log-Message "Начало сборки проекта (Platform=x64, Configuration=Debug)..."
try {
    $buildOutput = dotnet build -p:Platform=x64 -c Debug -v detailed 2>&1
    foreach ($line in $buildOutput) {
        $lineStr = $line.ToString()
        if ($lineStr) {
            # Цветовое кодирование вывода сборки
            if ($lineStr -match "error|Ошибка|ERROR") {
                Log-Message "BUILD: $lineStr" "Red"
            } elseif ($lineStr -match "warning|Предупреждение|WARNING") {
                Log-Message "BUILD: $lineStr" "Yellow"
            } elseif ($lineStr -match "succeeded|успешно|SUCCEEDED") {
                Log-Message "BUILD: $lineStr" "Green"
            } else {
                Log-Message "BUILD: $lineStr" "Gray"
            }
        }
    }
    
    if ($LASTEXITCODE -eq 0) {
        Log-Message "Сборка завершена успешно!" "Green"
        
        # Поиск собранного exe
        $exePath = Get-ChildItem -Path "bin\x64\Debug" -Filter "ChyguiSlide.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($exePath) {
            Log-Message "Исполняемый файл найден: $($exePath.FullName)" "Green"
            Log-Message "Размер файла: $([math]::Round($exePath.Length / 1MB, 2)) MB" "Cyan"
        } else {
            Log-Message "ПРЕДУПРЕЖДЕНИЕ: Исполняемый файл не найден в ожидаемом месте" "Yellow"
        }
    } else {
        Log-Message "ОШИБКА: Сборка завершилась с ошибкой (код: $LASTEXITCODE)" "Red"
        exit 1
    }
} catch {
    Log-Message "ОШИБКА при сборке: $_" "Red"
    Log-Message "StackTrace: $($_.ScriptStackTrace)" "Red"
    exit 1
}

$endTime = Get-Date
$duration = $endTime - $startTime
Log-Message "=== СБОРКА ЗАВЕРШЕНА ===" "Green"
Log-Message "Время окончания: $endTime" "Cyan"
Log-Message "Длительность: $($duration.TotalSeconds) секунд" "Cyan"

Write-Host "`n=== РЕЗУЛЬТАТЫ ===" -ForegroundColor Green
Write-Host "Лог сборки сохранен в: $logFile" -ForegroundColor Cyan
Write-Host "Для запуска приложения используйте: .\run.ps1" -ForegroundColor Yellow
Write-Host "Или: dotnet run --project .\ChyguiSlide.csproj -p:Platform=x64" -ForegroundColor Yellow
Write-Host "Или запустите .exe файл из папки bin\x64\Debug\..." -ForegroundColor Yellow






