namespace ChyguiSlide.Services.Models;

public sealed class YandexDiskFileInfo
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public long Size { get; init; }
    public DateTimeOffset? Modified { get; init; }
    public DateTimeOffset? Created { get; init; }
}

public sealed class YandexDiskSettings
{
    public string AccessToken { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Folder { get; set; } = "ChyguiSlide-Backups";
    public int MaxCopies { get; set; } = 10;

    /// <summary>Включить автоматическое копирование по расписанию (пока приложение запущено).</summary>
    public bool AutoBackupEnabled { get; set; }

    /// <summary>
    /// Выбранные дни недели (<see cref="DayOfWeek"/>: 0=вс … 6=сб).
    /// Пустой список — ни в один день (при включённом автобэкапе копий не будет).
    /// </summary>
    public List<int> AutoBackupDaysOfWeek { get; set; } = new();

    /// <summary>Устаревшее: один день или null (= каждый день). Читается только для миграции.</summary>
    public int? AutoBackupDayOfWeek { get; set; }

    /// <summary>Час локального времени (0–23).</summary>
    public int AutoBackupHour { get; set; } = 3;

    /// <summary>Минута локального времени (0–59).</summary>
    public int AutoBackupMinute { get; set; }

    /// <summary>Нормализованный набор дней с учётом старого поля.</summary>
    public IReadOnlyList<int> GetEffectiveAutoBackupDays()
    {
        var days = AutoBackupDaysOfWeek?
            .Where(d => d is >= 0 and <= 6)
            .Distinct()
            .OrderBy(d => d)
            .ToList() ?? new List<int>();

        if (days.Count > 0)
        {
            return days;
        }

        // Миграция со старого формата
        if (AutoBackupDayOfWeek is int single && single is >= 0 and <= 6)
        {
            return new[] { single };
        }

        // Раньше null означал «каждый день»
        if (AutoBackupDayOfWeek is null && AutoBackupEnabled)
        {
            return Enumerable.Range(0, 7).ToArray();
        }

        return Array.Empty<int>();
    }
}
