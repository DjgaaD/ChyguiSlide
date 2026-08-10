# Обновления ChyguiSlide через GitHub Releases

Приложение при запуске (и по кнопке в **Настройки → О нас**) проверяет:

`https://api.github.com/repos/DjgaaD/ChyguiSlide/releases`

## Как выложить обновление

1. Соберите релиз:
   ```powershell
   .\scripts\Publish-Release.ps1
   ```
2. На GitHub: **Releases → Draft a new release**
3. Тег, например:
   - бета: `v0.0.2-beta` (отметьте **Set as a pre-release**)
   - стабильная: `v1.0.0` (обычный release)
4. В описание (Release notes) напишите список изменений — его увидит пользователь.
5. Прикрепите файл:
   `artifacts\release\ChyguiSlide-0.0.2-beta-Setup.exe`
   (имя должно содержать `Setup` и желательно `ChyguiSlide`)

## Каналы

| Сборка приложения | Какие релизы видит |
|-------------------|--------------------|
| `channel: beta` в `version.json` | pre-release или тег с `beta` |
| `channel: release` | только обычные релизы |

Текущая версия сравнивается по числам (`0.0.1` &lt; `0.0.2`).
