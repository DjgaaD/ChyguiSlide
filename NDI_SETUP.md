# Настройка NDI для ChyguiSlide

## Требования

Для работы с NDI необходимо установить **NDI Tools** (NDI Runtime).

## Установка NDI Runtime

1. Скачайте NDI Tools с официального сайта: https://ndi.tv/tools/
2. Установите NDI Tools (это установит NDI Runtime в стандартную директорию)

## Расположение DLL

После установки NDI Tools, DLL `Processing.NDI.Lib.x64.dll` обычно находится в:
- `C:\Program Files\NDI\NDI 6 Runtime\Processing.NDI.Lib.x64.dll`
- `C:\Program Files (x86)\NDI\NDI 6 Runtime\Processing.NDI.Lib.x64.dll`

## Автоматическое обнаружение

Приложение автоматически ищет DLL в стандартных местах установки. Если DLL найдена, она будет загружена автоматически.

## Ручное копирование DLL (опционально)

Если автоматическое обнаружение не работает, вы можете скопировать DLL вручную:

1. Найдите `Processing.NDI.Lib.x64.dll` в установке NDI Tools
2. Скопируйте её в папку с вашим приложением (рядом с `.exe` файлом):
   - Для Debug: `bin\x64\Debug\net8.0-windows10.0.22621.0\win-x64\`
   - Для Release: `bin\x64\Release\net8.0-windows10.0.22621.0\win-x64\`

## Проверка установки

После установки NDI Tools, убедитесь что:
1. NDI Tools установлены корректно
2. В логах приложения видно сообщение `[NdiNative] Successfully loaded NDI DLL from: ...`
3. Если видите ошибку `DllNotFoundException`, проверьте пути установки выше

## Использование

1. Настройте OBS на ПК1 для отдачи NDI (Tools → NDI Output Settings)
2. Включите NDI Output в OBS
3. В вашем приложении вызовите `ToggleNdiVideoModeAsync()` для переключения на NDI режим


