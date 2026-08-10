# РЎРєСЂРёРїС‚ СЃР±РѕСЂРєРё Рё Р·Р°РїСѓСЃРєР° РїСЂРѕРµРєС‚Р° ChyguiSlide СЃ РїРѕР»РЅС‹Рј Р»РѕРіРёСЂРѕРІР°РЅРёРµРј
$ErrorActionPreference = "Stop"

# РџРµСЂРµС…РѕРґРёРј РІ РїР°РїРєСѓ СЃРѕ СЃРєСЂРёРїС‚РѕРј
Set-Location -Path $PSScriptRoot

$logFile = "build-and-run.log"
$startupLogFile = "startup.log"
$startTime = Get-Date

# РћС‡РёС‰Р°РµРј СЃС‚Р°СЂС‹Рµ Р»РѕРіРё
if (Test-Path $logFile) {
    Remove-Item $logFile -Force
}
if (Test-Path $startupLogFile) {
    Remove-Item $startupLogFile -Force
}

function Log-Message {
    param(
        [string]$Message, 
        [string]$Color = "White"
    )
    $timestamp = Get-Date -Format "HH:mm:ss.fff"
    $logMessage = "[$timestamp] $Message"
    Write-Host $logMessage -ForegroundColor $Color
    Add-Content -Path $logFile -Value $logMessage
}

Log-Message "=== РЎР‘РћР РљРђ Р Р—РђРџРЈРЎРљ РџР РћР•РљРўРђ ChyguiSlide ===" "Green"
Log-Message "Р’СЂРµРјСЏ РЅР°С‡Р°Р»Р°: $startTime" "Cyan"
Log-Message "РџР°РїРєР° РїСЂРѕРµРєС‚Р°: $PSScriptRoot" "Cyan"

# РџСЂРѕРІРµСЂРєР° .NET SDK
Log-Message "РџСЂРѕРІРµСЂРєР° РЅР°Р»РёС‡РёСЏ .NET SDK..." "Yellow"
try {
    $dotnetVersion = dotnet --version
    Log-Message "РќР°Р№РґРµРЅ .NET SDK РІРµСЂСЃРёРё: $dotnetVersion" "Green"
} catch {
    Log-Message "РћРЁРР‘РљРђ: .NET SDK РЅРµ РЅР°Р№РґРµРЅ! РЈСЃС‚Р°РЅРѕРІРёС‚Рµ .NET 8.0 SDK" "Red"
    exit 1
}

# РЁР°Рі 1: РЎР±РѕСЂРєР° РїСЂРѕРµРєС‚Р°
Log-Message "`n=== РЁРђР“ 1: РЎР‘РћР РљРђ РџР РћР•РљРўРђ ===" "Green"
Log-Message "РљРѕРјР°РЅРґР°: dotnet build -p:Platform=x64 -c Debug -v detailed" "Cyan"

try {
    $buildOutput = dotnet build -p:Platform=x64 -c Debug -v detailed 2>&1
    
    foreach ($line in $buildOutput) {
        $lineStr = $line.ToString()
        if ($lineStr) {
            # Р¦РІРµС‚РѕРІРѕРµ РєРѕРґРёСЂРѕРІР°РЅРёРµ РІС‹РІРѕРґР° СЃР±РѕСЂРєРё
            if ($lineStr -match "error|РћС€РёР±РєР°|ERROR") {
                Log-Message "BUILD: $lineStr" "Red"
            } elseif ($lineStr -match "warning|РџСЂРµРґСѓРїСЂРµР¶РґРµРЅРёРµ|WARNING") {
                Log-Message "BUILD: $lineStr" "Yellow"
            } elseif ($lineStr -match "succeeded|СѓСЃРїРµС€РЅРѕ|SUCCEEDED") {
                Log-Message "BUILD: $lineStr" "Green"
            } else {
                Log-Message "BUILD: $lineStr" "Gray"
            }
        }
    }
    
    if ($LASTEXITCODE -ne 0) {
        Log-Message "РћРЁРР‘РљРђ: РЎР±РѕСЂРєР° Р·Р°РІРµСЂС€РёР»Р°СЃСЊ СЃ РѕС€РёР±РєРѕР№ (РєРѕРґ: $LASTEXITCODE)" "Red"
        exit 1
    }
    
    Log-Message "РЎР±РѕСЂРєР° Р·Р°РІРµСЂС€РµРЅР° СѓСЃРїРµС€РЅРѕ!" "Green"
    
    # РџРѕРёСЃРє СЃРѕР±СЂР°РЅРЅРѕРіРѕ exe
    $exePath = Get-ChildItem -Path "bin\x64\Debug" -Filter "ChyguiSlide.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($exePath) {
        Log-Message "РСЃРїРѕР»РЅСЏРµРјС‹Р№ С„Р°Р№Р» РЅР°Р№РґРµРЅ: $($exePath.FullName)" "Green"
        Log-Message "Р Р°Р·РјРµСЂ С„Р°Р№Р»Р°: $([math]::Round($exePath.Length / 1MB, 2)) MB" "Cyan"
    } else {
        Log-Message "РџР Р•Р”РЈРџР Р•Р–Р”Р•РќРР•: РСЃРїРѕР»РЅСЏРµРјС‹Р№ С„Р°Р№Р» РЅРµ РЅР°Р№РґРµРЅ" "Yellow"
    }
} catch {
    Log-Message "РћРЁРР‘РљРђ РїСЂРё СЃР±РѕСЂРєРµ: $_" "Red"
    Log-Message "StackTrace: $($_.ScriptStackTrace)" "Red"
    exit 1
}

# РЁР°Рі 2: Р—Р°РїСѓСЃРє РїСЂРёР»РѕР¶РµРЅРёСЏ
Log-Message "`n=== РЁРђР“ 2: Р—РђРџРЈРЎРљ РџР РР›РћР–Р•РќРРЇ ===" "Green"
Log-Message "Р›РѕРіРё Р·Р°РїСѓСЃРєР° Р±СѓРґСѓС‚ РѕС‚РѕР±СЂР°Р¶Р°С‚СЊСЃСЏ РІ СЂРµР°Р»СЊРЅРѕРј РІСЂРµРјРµРЅРё" "Yellow"
Log-Message "Р›РѕРіРё С‚Р°РєР¶Рµ СЃРѕС…СЂР°РЅСЏСЋС‚СЃСЏ РІ: $startupLogFile" "Yellow"
Log-Message "РљРѕРјР°РЅРґР°: dotnet run --project .\ChyguiSlide.csproj -p:Platform=x64" "Cyan"
Log-Message "`n" "White"

# Р—Р°РїСѓСЃРєР°РµРј РїСЂРёР»РѕР¶РµРЅРёРµ СЃ РІС‹РІРѕРґРѕРј РІ РєРѕРЅСЃРѕР»СЊ
try {
    # Р—Р°РїСѓСЃРєР°РµРј dotnet run, РєРѕС‚РѕСЂС‹Р№ РїРѕРєР°Р¶РµС‚ РІСЃРµ Р»РѕРіРё РІ СЂРµР°Р»СЊРЅРѕРј РІСЂРµРјРµРЅРё
    dotnet run --project .\ChyguiSlide.csproj -p:Platform=x64
    
    # РџРѕСЃР»Рµ Р·Р°РІРµСЂС€РµРЅРёСЏ РїРѕРєР°Р·С‹РІР°РµРј С„РёРЅР°Р»СЊРЅС‹Рµ Р»РѕРіРё
    if (Test-Path $startupLogFile) {
        Log-Message "`n=== Р¤РРќРђР›Р¬РќР«Р• Р›РћР“Р Р—РђРџРЈРЎРљРђ ===" "Green"
        $startupLogs = Get-Content $startupLogFile -ErrorAction SilentlyContinue
        if ($startupLogs) {
            foreach ($line in $startupLogs) {
                if ($line.Trim()) {
                    Write-Host "[STARTUP] $line" -ForegroundColor Cyan
                    Add-Content -Path $logFile -Value "[STARTUP] $line"
                }
            }
        }
    }
} catch {
    Log-Message "РћРЁРР‘РљРђ РїСЂРё Р·Р°РїСѓСЃРєРµ: $_" "Red"
    Log-Message "StackTrace: $($_.ScriptStackTrace)" "Red"
    
    # РџРѕРєР°Р·С‹РІР°РµРј Р»РѕРіРё РѕС€РёР±РѕРє, РµСЃР»Рё РµСЃС‚СЊ
    if (Test-Path $startupLogFile) {
        Write-Host "`n=== Р›РћР“Р РћРЁРР‘РљР ===" -ForegroundColor Red
        Get-Content $startupLogFile | ForEach-Object {
            Write-Host "[ERROR] $_" -ForegroundColor Red
        }
    }
}

$endTime = Get-Date
$duration = $endTime - $startTime
Log-Message "`n=== Р—РђР’Р•Р РЁР•РќРћ ===" "Green"
Log-Message "Р’СЂРµРјСЏ РѕРєРѕРЅС‡Р°РЅРёСЏ: $endTime" "Cyan"
Log-Message "Р”Р»РёС‚РµР»СЊРЅРѕСЃС‚СЊ: $($duration.TotalSeconds) СЃРµРєСѓРЅРґ" "Cyan"
Log-Message "РџРѕР»РЅС‹Р№ Р»РѕРі СЃРѕС…СЂР°РЅРµРЅ РІ: $logFile" "Cyan"
Log-Message "Р›РѕРіРё Р·Р°РїСѓСЃРєР° СЃРѕС…СЂР°РЅРµРЅС‹ РІ: $startupLogFile" "Cyan"






