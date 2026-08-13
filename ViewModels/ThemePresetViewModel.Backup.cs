using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Services.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using ChyguiSlide.Views.Dialogs;

namespace ChyguiSlide.ViewModels;

public sealed partial class ThemePresetEditorViewModel
{
    public ObservableCollection<YandexBackupListItem> YandexBackups { get; } = new();

    [ObservableProperty]
    private bool isBackupSectionVisible;

    [ObservableProperty]
    private bool isAboutSectionVisible;

    public string AppProductLabel => AppVersionInfo.ProductLabel;

    public string AppVersionDisplay => AppVersionInfo.DisplayVersion;

    public bool IsBetaChannel => AppVersionInfo.IsBeta;

    [ObservableProperty]
    private string yandexAccessToken = string.Empty;

    [ObservableProperty]
    private string yandexClientId = string.Empty;

    [ObservableProperty]
    private string yandexFolder = "ChyguiSlide-Backups";

    [ObservableProperty]
    private int yandexMaxCopies = 10;

    [ObservableProperty]
    private bool autoBackupEnabled;

    [ObservableProperty]
    private TimeSpan? autoBackupTime = TimeSpan.FromHours(3);

    private bool _suppressAutoBackupPersist;

    public ObservableCollection<BackupScheduleDayOption> AutoBackupDayOptions { get; } = new(
        BackupScheduleDayOption.CreateWeekdays());

    [ObservableProperty]
    private YandexBackupListItem? selectedYandexBackup;

    [ObservableProperty]
    private string? backupStatusMessage;

    [ObservableProperty]
    private bool isBackupBusy;

    public IAsyncRelayCommand SaveYandexSettingsCommand { get; private set; } = null!;
    public IAsyncRelayCommand TestYandexConnectionCommand { get; private set; } = null!;
    public IAsyncRelayCommand OpenYandexTokenPageCommand { get; private set; } = null!;
    public IAsyncRelayCommand BackupCatalogCommand { get; private set; } = null!;
    public IAsyncRelayCommand BackupCatalogLocalCommand { get; private set; } = null!;
    public IAsyncRelayCommand RefreshYandexBackupsCommand { get; private set; } = null!;
    public IAsyncRelayCommand RestoreYandexBackupCommand { get; private set; } = null!;
    public IRelayCommand CopyBackupStatusCommand { get; private set; } = null!;
    public IAsyncRelayCommand CheckForUpdatesCommand { get; private set; } = null!;

    private void InitBackupCommands()
    {
        SaveYandexSettingsCommand = new AsyncRelayCommand(SaveYandexSettingsAsync, () => !IsBackupBusy);
        TestYandexConnectionCommand = new AsyncRelayCommand(TestYandexConnectionAsync, () => !IsBackupBusy);
        OpenYandexTokenPageCommand = new AsyncRelayCommand(OpenYandexTokenPageAsync);
        BackupCatalogCommand = new AsyncRelayCommand(BackupCatalogAsync, () => !IsBackupBusy);
        BackupCatalogLocalCommand = new AsyncRelayCommand(BackupCatalogLocalAsync, () => !IsBackupBusy);
        RefreshYandexBackupsCommand = new AsyncRelayCommand(RefreshYandexBackupsAsync, () => !IsBackupBusy);
        RestoreYandexBackupCommand = new AsyncRelayCommand(RestoreYandexBackupAsync, () => !IsBackupBusy && SelectedYandexBackup is not null);
        CopyBackupStatusCommand = new RelayCommand(CopyBackupStatus, () => !string.IsNullOrWhiteSpace(BackupStatusMessage));
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);

        foreach (var day in AutoBackupDayOptions)
        {
            day.SelectionChanged = () => _ = PersistScheduleIfNeededAsync();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        await AppUpdateDialog.CheckManualAsync(XamlRoot);
    }

    partial void OnBackupStatusMessageChanged(string? value) =>
        CopyBackupStatusCommand.NotifyCanExecuteChanged();

    private void CopyBackupStatus()
    {
        if (string.IsNullOrWhiteSpace(BackupStatusMessage))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(BackupStatusMessage);
        Clipboard.SetContent(package);
        BackupStatusMessage = BackupStatusMessage.StartsWith("Скопировано.", StringComparison.Ordinal)
            ? BackupStatusMessage
            : "Скопировано.\n\n" + BackupStatusMessage;
    }

    partial void OnSelectedYandexBackupChanged(YandexBackupListItem? value) =>
        RestoreYandexBackupCommand.NotifyCanExecuteChanged();

    partial void OnIsBackupBusyChanged(bool value)
    {
        SaveYandexSettingsCommand.NotifyCanExecuteChanged();
        TestYandexConnectionCommand.NotifyCanExecuteChanged();
        BackupCatalogCommand.NotifyCanExecuteChanged();
        BackupCatalogLocalCommand.NotifyCanExecuteChanged();
        RefreshYandexBackupsCommand.NotifyCanExecuteChanged();
        RestoreYandexBackupCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadBackupSettingsAsync()
    {
        try
        {
            var settings = await _catalogBackupService.GetSettingsAsync();
            _suppressAutoBackupPersist = true;
            try
            {
                YandexAccessToken = settings.AccessToken;
                YandexClientId = settings.ClientId;
                YandexFolder = settings.Folder;
                YandexMaxCopies = settings.MaxCopies;
                AutoBackupEnabled = settings.AutoBackupEnabled;
                AutoBackupTime = new TimeSpan(
                    Math.Clamp(settings.AutoBackupHour, 0, 23),
                    Math.Clamp(settings.AutoBackupMinute, 0, 59),
                    0);
                var selectedDays = new HashSet<int>(settings.GetEffectiveAutoBackupDays());
                foreach (var day in AutoBackupDayOptions)
                {
                    day.SetSelected(selectedDays.Contains(day.DayOfWeekValue));
                }
            }
            finally
            {
                _suppressAutoBackupPersist = false;
            }
        }
        catch (Exception ex)
        {
            BackupStatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось загрузить настройки бэкапа", ex);
        }
    }

    private async Task SaveYandexSettingsAsync()
    {
        IsBackupBusy = true;
        try
        {
            await SaveYandexSettingsInternalAsync();
            BackupStatusMessage = "Настройки Яндекс.Диска сохранены.";
        }
        catch (Exception ex)
        {
            BackupStatusMessage = null;
            await ErrorDialog.ShowAsync("Ошибка сохранения настроек бэкапа", ex);
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    partial void OnAutoBackupEnabledChanged(bool value) => _ = PersistScheduleIfNeededAsync();

    partial void OnAutoBackupTimeChanged(TimeSpan? value) => _ = PersistScheduleIfNeededAsync();

    private async Task PersistScheduleIfNeededAsync()
    {
        if (_suppressAutoBackupPersist)
        {
            return;
        }

        try
        {
            await SaveYandexSettingsInternalAsync();
        }
        catch (Exception ex)
        {
            BackupStatusMessage = null;
            await ErrorDialog.ShowAsync("Ошибка сохранения расписания бэкапа", ex);
        }
    }

    private async Task TestYandexConnectionAsync()
    {
        IsBackupBusy = true;
        try
        {
            await SaveYandexSettingsInternalAsync();
            var ok = await _catalogBackupService.TestConnectionAsync();
            BackupStatusMessage = ok
                ? "Токен действителен — связь с Яндекс.Диском есть."
                : "Токен не принят. Проверьте OAuth-токен и права disk.read / disk.write.";
        }
        catch (Exception ex)
        {
            BackupStatusMessage = null;
            await ErrorDialog.ShowAsync("Ошибка проверки Яндекс.Диска", ex);
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    private async Task OpenYandexTokenPageAsync()
    {
        if (string.IsNullOrWhiteSpace(YandexClientId))
        {
            BackupStatusMessage =
                "Укажите Client ID приложения с oauth.yandex.ru (права: disk.info, disk.read, disk.write).";
            return;
        }

        var url =
            "https://oauth.yandex.ru/authorize?response_type=token"
            + "&client_id=" + Uri.EscapeDataString(YandexClientId.Trim())
            + "&redirect_uri=" + Uri.EscapeDataString("https://oauth.yandex.ru/verification_code");

        await Launcher.LaunchUriAsync(new Uri(url));
        BackupStatusMessage =
            "В браузере разрешите доступ и скопируйте access_token со страницы в поле «OAuth-токен».";
    }

    private async Task BackupCatalogAsync()
    {
        IsBackupBusy = true;
        BackupStatusMessage = "Резервное копирование…";
        try
        {
            await SaveYandexSettingsInternalAsync();
            var progress = new Progress<string>(msg => BackupStatusMessage = msg);
            var name = await _catalogBackupService.BackupToYandexDiskAsync(progress);
            BackupStatusMessage = $"Загружено: {name}";
            await RefreshYandexBackupsAsync();
        }
        catch (Exception ex)
        {
            BackupStatusMessage = null;
            await ErrorDialog.ShowAsync("Ошибка бэкапа", FormatBackupError("Ошибка бэкапа", ex));
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    private async Task BackupCatalogLocalAsync()
    {
        IsBackupBusy = true;
        BackupStatusMessage = "Выбор папки…";
        try
        {
            var picker = new global::Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                BackupStatusMessage = "Локальная копия отменена.";
                return;
            }

            BackupStatusMessage = "Резервное копирование…";
            var progress = new Progress<string>(msg => BackupStatusMessage = msg);
            var path = await _catalogBackupService.BackupToLocalFolderAsync(folder.Path, progress);
            BackupStatusMessage = $"Сохранено: {path}";
        }
        catch (Exception ex)
        {
            BackupStatusMessage = null;
            await ErrorDialog.ShowAsync("Ошибка локальной копии", FormatBackupError("Ошибка локальной копии", ex));
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    private async Task RefreshYandexBackupsAsync()
    {
        IsBackupBusy = true;
        try
        {
            await SaveYandexSettingsInternalAsync();
            var files = await _catalogBackupService.ListBackupsAsync();
            YandexBackups.Clear();
            foreach (var file in files)
            {
                YandexBackups.Add(new YandexBackupListItem(file));
            }

            BackupStatusMessage = files.Count == 0
                ? "На Диске пока нет копий в этой папке."
                : $"Найдено копий: {files.Count}";
        }
        catch (Exception ex)
        {
            BackupStatusMessage = null;
            await ErrorDialog.ShowAsync("Ошибка списка копий", FormatBackupError("Ошибка списка", ex));
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    private async Task RestoreYandexBackupAsync()
    {
        if (SelectedYandexBackup is null)
        {
            return;
        }

        IsBackupBusy = true;
        BackupStatusMessage = "Восстановление…";
        try
        {
            await SaveYandexSettingsInternalAsync();
            var progress = new Progress<string>(msg => BackupStatusMessage = msg);
            await _catalogBackupService.RestoreFromYandexDiskAsync(SelectedYandexBackup.Path, progress);
            BackupStatusMessage =
                "База восстановлена. Закройте и снова откройте Чугуй Слайды, чтобы изменения применились.";
        }
        catch (Exception ex)
        {
            BackupStatusMessage = null;
            await ErrorDialog.ShowAsync("Ошибка восстановления", FormatBackupError("Ошибка восстановления", ex));
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    private async Task SaveYandexSettingsInternalAsync()
    {
        var time = AutoBackupTime ?? TimeSpan.FromHours(3);
        await _catalogBackupService.SaveSettingsAsync(new YandexDiskSettings
        {
            AccessToken = YandexAccessToken,
            ClientId = YandexClientId,
            Folder = YandexFolder,
            MaxCopies = YandexMaxCopies,
            AutoBackupEnabled = AutoBackupEnabled,
            AutoBackupDaysOfWeek = AutoBackupDayOptions
                .Where(d => d.IsSelected)
                .Select(d => d.DayOfWeekValue)
                .ToList(),
            AutoBackupHour = time.Hours,
            AutoBackupMinute = time.Minutes
        });
    }

    private static string FormatBackupError(string title, Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(title).Append(':');
        var current = ex;
        var depth = 0;
        while (current is not null)
        {
            sb.AppendLine();
            if (depth > 0)
            {
                sb.Append("→ ");
            }

            sb.Append(current.GetType().Name).Append(": ").Append(current.Message);
            current = current.InnerException;
            depth++;
        }

        return sb.ToString().TrimEnd();
    }
}

public sealed partial class BackupScheduleDayOption : ObservableObject
{
    public BackupScheduleDayOption(string title, int dayOfWeek)
    {
        Title = title;
        DayOfWeekValue = dayOfWeek;
    }

    public string Title { get; }
    public int DayOfWeekValue { get; }

    public Action? SelectionChanged { get; set; }

    [ObservableProperty]
    private bool isSelected;

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke();

    /// <summary>Установка без вызова автосохранения (при загрузке настроек).</summary>
    public void SetSelected(bool selected)
    {
        var callback = SelectionChanged;
        SelectionChanged = null;
        IsSelected = selected;
        SelectionChanged = callback;
    }

    public static IEnumerable<BackupScheduleDayOption> CreateWeekdays()
    {
        yield return new BackupScheduleDayOption("Пн", (int)System.DayOfWeek.Monday);
        yield return new BackupScheduleDayOption("Вт", (int)System.DayOfWeek.Tuesday);
        yield return new BackupScheduleDayOption("Ср", (int)System.DayOfWeek.Wednesday);
        yield return new BackupScheduleDayOption("Чт", (int)System.DayOfWeek.Thursday);
        yield return new BackupScheduleDayOption("Пт", (int)System.DayOfWeek.Friday);
        yield return new BackupScheduleDayOption("Сб", (int)System.DayOfWeek.Saturday);
        yield return new BackupScheduleDayOption("Вс", (int)System.DayOfWeek.Sunday);
    }
}

public sealed class YandexBackupListItem
{
    public YandexBackupListItem(YandexDiskFileInfo file)
    {
        Name = file.Name;
        Path = file.Path;
        SizeBytes = file.Size;
        Modified = file.Modified ?? file.Created;
    }

    public string Name { get; }
    public string Path { get; }
    public long SizeBytes { get; }
    public DateTimeOffset? Modified { get; }

    public string SizeDisplay =>
        SizeBytes < 1024 * 1024
            ? $"{SizeBytes / 1024.0:0.#} КБ"
            : $"{SizeBytes / (1024.0 * 1024.0):0.##} МБ";

    public string ModifiedDisplay =>
        Modified?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";

    public string Title => $"{Name}  ·  {ModifiedDisplay}  ·  {SizeDisplay}";
}
