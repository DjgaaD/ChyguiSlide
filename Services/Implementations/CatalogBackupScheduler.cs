using ChyguiSlide.Services.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChyguiSlide.Services.Implementations;

/// <summary>
/// Фоновый запуск бэкапа по локальному дню/времени.
/// Срабатывает только в назначенную минуту, пока приложение запущено —
/// пропущенные слоты не нагоняются.
/// </summary>
public sealed class CatalogBackupScheduler : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly ICatalogBackupService _backupService;
    private readonly ILogger<CatalogBackupScheduler> _logger;
    private string? _firedSlotKey;

    public CatalogBackupScheduler(
        ICatalogBackupService backupService,
        ILogger<CatalogBackupScheduler> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await TryRunScheduledBackupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка планировщика резервного копирования");
            }
        }
    }

    private async Task TryRunScheduledBackupAsync(CancellationToken cancellationToken)
    {
        var settings = await _backupService.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!settings.AutoBackupEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            return;
        }

        var now = DateTime.Now;
        var days = settings.GetEffectiveAutoBackupDays();
        if (days.Count == 0 || !days.Contains((int)now.DayOfWeek))
        {
            return;
        }

        if (now.Hour != settings.AutoBackupHour || now.Minute != settings.AutoBackupMinute)
        {
            return;
        }

        // Ключ слота = календарная минута. Если приложение стартовало позже — слот уже другой.
        var slotKey = $"{now:yyyy-MM-dd}T{now.Hour:D2}:{now.Minute:D2}";
        if (string.Equals(_firedSlotKey, slotKey, StringComparison.Ordinal))
        {
            return;
        }

        // Фиксируем слот до запуска: повтор в ту же минуту и «догон» после сбоя не делаем.
        _firedSlotKey = slotKey;
        _logger.LogInformation("Автобэкап по расписанию: слот {Slot}", slotKey);

        try
        {
            var name = await _backupService
                .BackupToYandexDiskAsync(progress: null, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation("Автобэкап завершён: {FileName}", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Автобэкап не выполнен для слота {Slot}", slotKey);
        }
    }
}
