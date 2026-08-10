namespace ChyguiSlide.Data.Enums;

/// <summary>Пул обоев стиля.</summary>
public enum ThemeWallpaperPool
{
    Shared = 0,
    Songs = 1,
    Bible = 2
}

/// <summary>Как выбирать обои из пула.</summary>
public enum ThemeBackgroundPickMode
{
    /// <summary>Конкретный выбранный файл.</summary>
    Fixed = 0,
    /// <summary>Случайный при каждом запуске трансляции (и при смене пула песни/Библия).</summary>
    RandomOnStart = 1
}
