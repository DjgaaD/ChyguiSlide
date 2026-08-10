using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Data.ValueObjects;
using ChyguiSlide.Services;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using ChyguiSlide.Views.Dialogs;

namespace ChyguiSlide.ViewModels;

public sealed partial class ThemePresetEditorViewModel : ObservableRecipient
{
    private readonly ICatalogService _catalogService;
    private readonly IDisplaySettingsService _displaySettingsService;
    private readonly IProjectionDisplayService _projectionDisplayService;
    private readonly IHotkeyService _hotkeyService;
    private readonly ICatalogBackupService _catalogBackupService;
    private readonly IThemeBackgroundMediaService _themeBackgroundMediaService;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _autoSaveTimer;
    private Guid? _currentPresetId;
    private HotkeyBindingItem? _listeningHotkey;
    private bool _hotkeysLoaded;
    private bool _suppressBibleReferencePersist;

    public ObservableCollection<ThemePreset> Presets { get; } = new();
    public ObservableCollection<HotkeyBindingItem> Hotkeys { get; } = new();
    public ObservableCollection<SettingsNavItem> SettingsSections { get; } = new(
        new[]
        {
            new SettingsNavItem("Трансляция", "radio", SettingsSection.Projection),
            new SettingsNavItem("Камера", "cctv", SettingsSection.Camera),
            new SettingsNavItem("Стили", "palette", SettingsSection.Themes),
            new SettingsNavItem("Горячие клавиши", "keyboard", SettingsSection.Hotkeys),
            new SettingsNavItem("Резервные копии", "cloud-upload", SettingsSection.Backup),
            new SettingsNavItem("О нас", "info", SettingsSection.About),
        });

    [ObservableProperty]
    private SettingsNavItem? selectedSettingsSection;

    [ObservableProperty]
    private bool isProjectionSectionVisible = true;

    [ObservableProperty]
    private bool isCameraSectionVisible;

    [ObservableProperty]
    private bool isThemesSectionVisible;

    [ObservableProperty]
    private bool isHotkeysSectionVisible;

    [ObservableProperty]
    private ThemePreset? selectedPreset;

    [ObservableProperty]
    private string presetName = "Новый стиль";

    [ObservableProperty]
    private string? fontFamily;

    [ObservableProperty]
    private bool isBold = false;

    [ObservableProperty]
    private string textAlignment = "Center";

    [ObservableProperty]
    private SectionTransitionOptionItem? selectedSectionTransition;

    [ObservableProperty]
    private string primaryColor = ThemeColors.Default.Primary;

    [ObservableProperty]
    private string backgroundColor = ThemeColors.Default.Background;

    [ObservableProperty]
    private string backgroundMediaPath = string.Empty;

    [ObservableProperty]
    private string backgroundMediaDisplayName = string.Empty;

    [ObservableProperty]
    private bool loopBackgroundMedia = true;

    [ObservableProperty]
    private bool useSeparateBackgrounds;

    [ObservableProperty]
    private BackgroundPickModeOptionItem? selectedBackgroundPickMode;

    [ObservableProperty]
    private WallpaperPoolOptionItem? selectedWallpaperPool;

    [ObservableProperty]
    private ThemeWallpaper? selectedWallpaper;

    [ObservableProperty]
    private bool isWallpaperPoolSelectorVisible;

    [ObservableProperty]
    private bool isFixedWallpaperPickMode = true;

    [ObservableProperty]
    private string wallpaperEmptyHint = "Добавьте обои в этот набор.";

    [ObservableProperty]
    private bool isEditingPoolEmpty = true;

    [ObservableProperty]
    private bool textOutlineEnabled;

    [ObservableProperty]
    private double textOutlineThickness = 2;

    [ObservableProperty]
    private string textOutlineColor = "#000000";

    [ObservableProperty]
    private double textOutlineOpacity = 1;

    [ObservableProperty]
    private global::Windows.UI.Color primaryPickerColor = global::Windows.UI.Color.FromArgb(255, 255, 255, 255);

    [ObservableProperty]
    private global::Windows.UI.Color backgroundPickerColor = global::Windows.UI.Color.FromArgb(255, 0, 0, 0);

    [ObservableProperty]
    private global::Windows.UI.Color textOutlinePickerColor = global::Windows.UI.Color.FromArgb(255, 0, 0, 0);

    private bool _syncingColorPicker;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private ObservableCollection<DisplayInfo> availableDisplays = new();

    [ObservableProperty]
    private DisplayInfo? selectedDisplay;

    [ObservableProperty]
    private bool isLoadingDisplays;

    [ObservableProperty]
    private bool wordWrap = true;

    [ObservableProperty]
    private TextLayoutMode textLayoutMode = TextLayoutMode.AutoMaxFit;

    [ObservableProperty]
    private bool showBibleReference;

    [ObservableProperty]
    private BibleReferencePlacementItem? selectedBibleReferencePlacement;

    [ObservableProperty]
    private string bibleReferenceAlignment = "Center";

    public ObservableCollection<TextLayoutOptionItem> TextLayoutOptions { get; } = new();
    public ObservableCollection<BibleReferencePlacementItem> BibleReferencePlacementOptions { get; } = new();
    public ObservableCollection<SectionTransitionOptionItem> SectionTransitionOptions { get; } = new();
    public ObservableCollection<BackgroundPickModeOptionItem> BackgroundPickModeOptions { get; } = new();
    public ObservableCollection<WallpaperPoolOptionItem> WallpaperPoolOptions { get; } = new();
    public ObservableCollection<ThemeWallpaper> EditingPoolWallpapers { get; } = new();
    public ObservableCollection<AppUiThemeOptionItem> AppUiThemeOptions { get; } = new();

    [ObservableProperty]
    private AppUiThemeOptionItem? selectedAppUiTheme;

    private Guid? _selectedSharedWallpaperId;
    private Guid? _selectedSongWallpaperId;
    private Guid? _selectedBibleWallpaperId;
    private List<ThemeWallpaper> _allWallpapers = new();

    public bool IsBibleRefAlignLeft
    {
        get => BibleReferenceAlignment == "Left";
        set { if (value) BibleReferenceAlignment = "Left"; }
    }

    public bool IsBibleRefAlignCenter
    {
        get => BibleReferenceAlignment == "Center";
        set { if (value) BibleReferenceAlignment = "Center"; }
    }

    public bool IsBibleRefAlignRight
    {
        get => BibleReferenceAlignment == "Right";
        set { if (value) BibleReferenceAlignment = "Right"; }
    }

    [ObservableProperty]
    private string? cameraHost;

    [ObservableProperty]
    private int cameraPort = 5000;

    public XamlRoot? XamlRoot { get; set; }

    public IRelayCommand CreateCommand { get; }
    public IRelayCommand EditCommand { get; }
    public IRelayCommand SetTextAlignmentCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand ApplyThemeCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand RefreshDisplaysCommand { get; }
    public IAsyncRelayCommand SaveDisplaySelectionCommand { get; }
    public IAsyncRelayCommand ResetHotkeysCommand { get; }
    public IRelayCommand<TextLayoutMode> SelectTextLayoutModeCommand { get; }
    public IAsyncRelayCommand BrowseBackgroundMediaCommand { get; }
    public IRelayCommand ClearBackgroundMediaCommand { get; }
    public IAsyncRelayCommand AddWallpaperCommand { get; }
    public IAsyncRelayCommand RemoveWallpaperCommand { get; }
    public IRelayCommand SelectWallpaperAsFixedCommand { get; }

    public ThemePresetEditorViewModel(
        ICatalogService catalogService,
        IDisplaySettingsService displaySettingsService,
        IProjectionDisplayService projectionDisplayService,
        IHotkeyService hotkeyService,
        ICatalogBackupService catalogBackupService,
        IThemeBackgroundMediaService themeBackgroundMediaService)
    {
        _catalogService = catalogService;
        _displaySettingsService = displaySettingsService;
        _projectionDisplayService = projectionDisplayService;
        _hotkeyService = hotkeyService;
        _catalogBackupService = catalogBackupService;
        _themeBackgroundMediaService = themeBackgroundMediaService;

        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() ?? App.MainDispatcherQueue;
        _autoSaveTimer = dispatcher.CreateTimer();
        _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(500); // Задержка 500ms перед автосохранением
        _autoSaveTimer.IsRepeating = false;
        _autoSaveTimer.Tick += async (s, e) => await AutoSaveAsync();

        CreateCommand = new RelayCommand(CreateNew);
        EditCommand = new RelayCommand(EditSelected, () => SelectedPreset is not null);
        SetTextAlignmentCommand = new RelayCommand<string?>(SetTextAlignment);
        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        ApplyThemeCommand = new AsyncRelayCommand(ApplyThemeAsync, () => SelectedPreset is not null && !IsBusy);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedPreset is not null && !IsBusy);
        RefreshDisplaysCommand = new AsyncRelayCommand(LoadDisplaysAsync);
        SaveDisplaySelectionCommand = new AsyncRelayCommand(SaveDisplaySelectionAsync);
        ResetHotkeysCommand = new AsyncRelayCommand(ResetHotkeysAsync);
        SelectTextLayoutModeCommand = new RelayCommand<TextLayoutMode>(SelectTextLayoutMode);
        BrowseBackgroundMediaCommand = new AsyncRelayCommand(AddWallpaperAsync, () => !IsBusy);
        ClearBackgroundMediaCommand = new RelayCommand(() => { }, () => false);
        AddWallpaperCommand = new AsyncRelayCommand(AddWallpaperAsync, () => !IsBusy);
        RemoveWallpaperCommand = new AsyncRelayCommand(RemoveSelectedWallpaperAsync, () => !IsBusy && SelectedWallpaper is not null);
        SelectWallpaperAsFixedCommand = new RelayCommand(SelectWallpaperAsFixed, () => IsFixedWallpaperPickMode && SelectedWallpaper is not null);
        InitBackupCommands();

        BuildTextLayoutOptions();
        BuildBibleReferencePlacementOptions();
        BuildSectionTransitionOptions();
        BuildBackgroundPickModeOptions();
        BuildWallpaperPoolOptions();
        BuildAppUiThemeOptions();
        SelectedSettingsSection = SettingsSections[0];

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SelectedPreset))
            {
                EditCommand.NotifyCanExecuteChanged();
                ApplyThemeCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
            }

            if (args.PropertyName is nameof(PresetName)
                or nameof(PrimaryColor)
                or nameof(BackgroundColor))
            {
                SaveCommand.NotifyCanExecuteChanged();
            }

            if (args.PropertyName == nameof(IsBusy))
            {
                ApplyThemeCommand.NotifyCanExecuteChanged();
                DeleteCommand.NotifyCanExecuteChanged();
                BrowseBackgroundMediaCommand.NotifyCanExecuteChanged();
                AddWallpaperCommand.NotifyCanExecuteChanged();
                RemoveWallpaperCommand.NotifyCanExecuteChanged();
            }

            if (args.PropertyName == nameof(SelectedWallpaper))
            {
                RemoveWallpaperCommand.NotifyCanExecuteChanged();
                SelectWallpaperAsFixedCommand.NotifyCanExecuteChanged();
            }

            // Автосохранение при изменении свойств редактируемого стиля
            if (_currentPresetId.HasValue && args.PropertyName is nameof(PresetName)
                or nameof(FontFamily)
                or nameof(IsBold)
                or nameof(TextAlignment)
                or nameof(SelectedSectionTransition)
                or nameof(PrimaryColor)
                or nameof(BackgroundColor)
                or nameof(LoopBackgroundMedia)
                or nameof(UseSeparateBackgrounds)
                or nameof(SelectedBackgroundPickMode)
                or nameof(TextOutlineEnabled)
                or nameof(TextOutlineThickness)
                or nameof(TextOutlineColor)
                or nameof(TextOutlineOpacity))
            {
                if (CanSave())
                {
                    _autoSaveTimer.Stop();
                    _autoSaveTimer.Start(); // Перезапускаем таймер при каждом изменении
                }
            }
        };

        ResetToDefaults();
    }

    public double TextOutlineOpacityPercent
    {
        get => TextOutlineOpacity * 100;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            TextOutlineOpacity = clamped / 100.0;
            OnPropertyChanged();
        }
    }

    public double EffectiveTextOutlineThickness =>
        TextOutlineEnabled ? TextOutlineThickness : 0;

    partial void OnTextOutlineOpacityChanged(double value) =>
        OnPropertyChanged(nameof(TextOutlineOpacityPercent));

    partial void OnTextOutlineEnabledChanged(bool value) =>
        OnPropertyChanged(nameof(EffectiveTextOutlineThickness));

    partial void OnTextOutlineThicknessChanged(double value) =>
        OnPropertyChanged(nameof(EffectiveTextOutlineThickness));

    partial void OnPrimaryColorChanged(string value) =>
        SyncPickerFromHex(value, c => PrimaryPickerColor = c);

    partial void OnBackgroundColorChanged(string value) =>
        SyncPickerFromHex(value, c => BackgroundPickerColor = c);

    partial void OnTextOutlineColorChanged(string value) =>
        SyncPickerFromHex(value, c => TextOutlinePickerColor = c);

    partial void OnPrimaryPickerColorChanged(global::Windows.UI.Color value) =>
        SyncHexFromPicker(value, hex => PrimaryColor = hex);

    partial void OnBackgroundPickerColorChanged(global::Windows.UI.Color value) =>
        SyncHexFromPicker(value, hex => BackgroundColor = hex);

    partial void OnTextOutlinePickerColorChanged(global::Windows.UI.Color value) =>
        SyncHexFromPicker(value, hex => TextOutlineColor = hex);

    private void SyncPickerFromHex(string hex, Action<global::Windows.UI.Color> apply)
    {
        if (_syncingColorPicker || !TryParseColorToWindows(hex, out var color))
        {
            return;
        }

        _syncingColorPicker = true;
        apply(color);
        _syncingColorPicker = false;
    }

    private void SyncHexFromPicker(global::Windows.UI.Color color, Action<string> apply)
    {
        if (_syncingColorPicker)
        {
            return;
        }

        _syncingColorPicker = true;
        apply($"#{color.R:X2}{color.G:X2}{color.B:X2}");
        _syncingColorPicker = false;
    }

    private static bool TryParseColorToWindows(string hex, out global::Windows.UI.Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var cleaned = hex.Trim().TrimStart('#');
        if (cleaned.Length is not (6 or 8))
        {
            return false;
        }

        if (!uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (cleaned.Length == 6)
        {
            value |= 0xFF000000;
        }

        color = global::Windows.UI.Color.FromArgb(
            (byte)((value >> 24) & 0xFF),
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
        return true;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_hotkeysLoaded)
        {
            await LoadHotkeysAsync();
        }

        await LoadAppUiThemeAsync(cancellationToken);

        if (Presets.Count > 0)
        {
            return;
        }

        await LoadPresetsAsync(cancellationToken);
        await LoadDisplaysAsync(cancellationToken);
        await LoadTextLayoutModeAsync(cancellationToken);
        await LoadBibleReferenceSettingsAsync();
        await LoadCameraSettingsAsync(cancellationToken);
    }

    partial void OnSelectedSettingsSectionChanged(SettingsNavItem? value)
    {
        CancelHotkeyListening();
        var section = value?.Section ?? SettingsSection.Projection;
        IsProjectionSectionVisible = section == SettingsSection.Projection;
        IsCameraSectionVisible = section == SettingsSection.Camera;
        IsThemesSectionVisible = section == SettingsSection.Themes;
        IsHotkeysSectionVisible = section == SettingsSection.Hotkeys;
        IsBackupSectionVisible = section == SettingsSection.Backup;
        IsAboutSectionVisible = section == SettingsSection.About;
        if (section == SettingsSection.Backup)
        {
            _ = LoadBackupSettingsAsync();
        }
    }

    public bool IsCapturingHotkey => _listeningHotkey is not null;

    public bool TryCaptureHotkey(VirtualKey key, bool ctrl, bool alt, bool shift)
    {
        if (_listeningHotkey is null)
        {
            return false;
        }

        if (key == VirtualKey.Escape && !ctrl && !alt && !shift)
        {
            CancelHotkeyListening();
            return true;
        }

        if (HotkeyBinding.IsModifierKey(key))
        {
            return true;
        }

        var item = _listeningHotkey;
        _listeningHotkey = null;
        _ = item.ApplyAsync(HotkeyBinding.Create(key, ctrl, alt, shift));
        return true;
    }

    public void CancelHotkeyListening()
    {
        if (_listeningHotkey is null)
        {
            return;
        }

        _listeningHotkey.CancelListening();
        _listeningHotkey = null;
    }

    private async Task LoadHotkeysAsync()
    {
        try
        {
            // Прогреваем кэш сервиса горячих клавиш до показа UI
            await _hotkeyService.GetAllAsync();
            Hotkeys.Clear();

            foreach (AppHotkeyAction action in Enum.GetValues<AppHotkeyAction>())
            {
                var binding = await _hotkeyService.GetAsync(action);
                Hotkeys.Add(new HotkeyBindingItem(action, binding, OnHotkeyChangedAsync, OnHotkeyStartListening));
            }

            _hotkeysLoaded = true;
        }
        catch
        {
            // ignore
        }
    }

    private void OnHotkeyStartListening(HotkeyBindingItem item)
    {
        if (_listeningHotkey is not null && !ReferenceEquals(_listeningHotkey, item))
        {
            _listeningHotkey.CancelListening();
        }

        _listeningHotkey = item;
    }

    private async Task OnHotkeyChangedAsync(HotkeyBindingItem item, HotkeyBinding binding)
    {
        try
        {
            await _hotkeyService.SetAsync(item.Action, binding);

            // Обновляем отображение, если клавиша была освобождена у другого действия
            var all = await _hotkeyService.GetAllAsync();
            foreach (var hotkey in Hotkeys)
            {
                if (all.TryGetValue(hotkey.Action, out var current) && !hotkey.Binding.Equals(current))
                {
                    hotkey.Binding = current;
                    hotkey.KeyDisplay = current.ToDisplayString();
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private async Task ResetHotkeysAsync()
    {
        try
        {
            CancelHotkeyListening();
            await _hotkeyService.ResetDefaultsAsync();
            Hotkeys.Clear();
            _hotkeysLoaded = false;
            await LoadHotkeysAsync();
        }
        catch
        {
            // ignore
        }
    }

    private void BuildTextLayoutOptions()
    {
        TextLayoutOptions.Clear();

        TextLayoutOptions.Add(new TextLayoutOptionItem(
            TextLayoutMode.AutoMaxFit,
            "Строка 1 Строка 1 Строка 1",
            "Строка 1 Строка 1",
            "Строка 2 Строка 2 Строка 2",
            "Строка 2 Строка 2",
            "Строка 3 Строка 3 Строка 3",
            "Строка 3 Строка 3",
            previewFontSize: 16,
            showExtraLines: true));

        TextLayoutOptions.Add(new TextLayoutOptionItem(
            TextLayoutMode.MaximizeFont,
            "Строка 1 Строка 1",
            "Строка 1 Строка 1 Строка 1",
            "Строка 2 Строка 2",
            "Строка 2 Строка 2 Строка 2",
            "Строка 3 Строка 3",
            "Строка 3 Строка 3 Строка 3",
            previewFontSize: 15,
            showExtraLines: true));

        TextLayoutOptions.Add(new TextLayoutOptionItem(
            TextLayoutMode.WrapToWidth,
            "Строка 1 Строка 1 Строка 1",
            "Строка 1 Строка 1",
            "Строка 2 Строка 2 Строка 2",
            "Строка 2 Строка 2",
            "Строка 3 Строка 3 Строка 3",
            "Строка 3 Строка 3",
            previewFontSize: 14,
            showExtraLines: true));

        TextLayoutOptions.Add(new TextLayoutOptionItem(
            TextLayoutMode.ShrinkToFit,
            "Строка 1 Строка 1 Строка 1 Строка 1 Строка 1",
            "Строка 2 Строка 2 Строка 2 Строка 2 Строка 2",
            "Строка 3 Строка 3 Строка 3 Строка 3 Строка 3",
            string.Empty,
            string.Empty,
            string.Empty,
            previewFontSize: 9,
            showExtraLines: false));

        SyncTextLayoutSelection(TextLayoutMode);
    }

    private void BuildBibleReferencePlacementOptions()
    {
        BibleReferencePlacementOptions.Clear();
        foreach (BibleReferencePlacement placement in Enum.GetValues<BibleReferencePlacement>())
        {
            BibleReferencePlacementOptions.Add(new BibleReferencePlacementItem(placement, placement.GetTitle()));
        }
    }

    private void BuildSectionTransitionOptions()
    {
        SectionTransitionOptions.Clear();
        foreach (SectionTransitionMode mode in Enum.GetValues<SectionTransitionMode>())
        {
            SectionTransitionOptions.Add(new SectionTransitionOptionItem(mode));
        }

        SelectedSectionTransition = SectionTransitionOptions.FirstOrDefault(o => o.Mode == SectionTransitionMode.CrossFade)
            ?? SectionTransitionOptions.FirstOrDefault();
    }

    private void BuildBackgroundPickModeOptions()
    {
        BackgroundPickModeOptions.Clear();
        BackgroundPickModeOptions.Add(new BackgroundPickModeOptionItem(
            ThemeBackgroundPickMode.Fixed,
            "Конкретные обои",
            "Всегда показывать выбранный файл из набора."));
        BackgroundPickModeOptions.Add(new BackgroundPickModeOptionItem(
            ThemeBackgroundPickMode.RandomOnStart,
            "Случайно при запуске",
            "При каждом запуске трансляции выбирается случайный файл из набора."));
        SelectedBackgroundPickMode = BackgroundPickModeOptions.FirstOrDefault();
    }

    private void BuildAppUiThemeOptions()
    {
        AppUiThemeOptions.Clear();
        AppUiThemeOptions.Add(new AppUiThemeOptionItem(
            AppUiThemeMode.System,
            "Как в системе",
            "Следовать светлой или тёмной теме Windows."));
        AppUiThemeOptions.Add(new AppUiThemeOptionItem(
            AppUiThemeMode.Light,
            "Светлая",
            "Всегда светлый интерфейс."));
        AppUiThemeOptions.Add(new AppUiThemeOptionItem(
            AppUiThemeMode.Dark,
            "Тёмная",
            "Всегда тёмный интерфейс."));
        SelectedAppUiTheme = AppUiThemeOptions.FirstOrDefault();
    }

    private bool _suppressAppUiThemePersist;

    private async Task LoadAppUiThemeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _suppressAppUiThemePersist = true;
            var mode = await _displaySettingsService.GetAppUiThemeAsync();
            SelectedAppUiTheme = AppUiThemeOptions.FirstOrDefault(o => o.Mode == mode)
                ?? AppUiThemeOptions.FirstOrDefault();
            AppUiThemeApplier.Apply(SelectedAppUiTheme?.Mode ?? AppUiThemeMode.System);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadAppUiThemeAsync: {ex.Message}");
        }
        finally
        {
            _suppressAppUiThemePersist = false;
        }
    }

    partial void OnSelectedAppUiThemeChanged(AppUiThemeOptionItem? value)
    {
        if (_suppressAppUiThemePersist || value is null)
        {
            return;
        }

        AppUiThemeApplier.Apply(value.Mode);
        _ = SaveAppUiThemeAsync(value.Mode);
    }

    private async Task SaveAppUiThemeAsync(AppUiThemeMode mode)
    {
        try
        {
            await _displaySettingsService.SetAppUiThemeAsync(mode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveAppUiThemeAsync: {ex.Message}");
        }
    }

    private void BuildWallpaperPoolOptions()
    {
        WallpaperPoolOptions.Clear();
        WallpaperPoolOptions.Add(new WallpaperPoolOptionItem(ThemeWallpaperPool.Shared, "Общие"));
        WallpaperPoolOptions.Add(new WallpaperPoolOptionItem(ThemeWallpaperPool.Songs, "Песни"));
        WallpaperPoolOptions.Add(new WallpaperPoolOptionItem(ThemeWallpaperPool.Bible, "Библия"));
        SelectedWallpaperPool = WallpaperPoolOptions.FirstOrDefault();
    }

    private ThemeWallpaperPool GetEditingPool()
    {
        if (!UseSeparateBackgrounds)
        {
            return ThemeWallpaperPool.Shared;
        }

        return SelectedWallpaperPool?.Pool ?? ThemeWallpaperPool.Songs;
    }

    private void SetSelectedWallpaperId(ThemeWallpaperPool pool, Guid? id)
    {
        switch (pool)
        {
            case ThemeWallpaperPool.Songs:
                _selectedSongWallpaperId = id;
                break;
            case ThemeWallpaperPool.Bible:
                _selectedBibleWallpaperId = id;
                break;
            default:
                _selectedSharedWallpaperId = id;
                break;
        }
    }

    private Guid? GetSelectedWallpaperId(ThemeWallpaperPool pool) =>
        pool switch
        {
            ThemeWallpaperPool.Songs => _selectedSongWallpaperId,
            ThemeWallpaperPool.Bible => _selectedBibleWallpaperId,
            _ => _selectedSharedWallpaperId
        };

    private void SyncLegacyBackgroundPath()
    {
        var sharedSelected = _allWallpapers.FirstOrDefault(w => w.Id == _selectedSharedWallpaperId)
            ?? _allWallpapers.FirstOrDefault(w => w.Pool == ThemeWallpaperPool.Shared);
        BackgroundMediaPath = sharedSelected?.FilePath ?? string.Empty;
        BackgroundMediaDisplayName = _themeBackgroundMediaService.GetDisplayName(BackgroundMediaPath);
    }

    private void RefreshEditingPoolWallpapers()
    {
        IsWallpaperPoolSelectorVisible = UseSeparateBackgrounds;
        IsFixedWallpaperPickMode = SelectedBackgroundPickMode?.Mode != ThemeBackgroundPickMode.RandomOnStart;

        var pool = GetEditingPool();
        var selectedId = GetSelectedWallpaperId(pool);
        var items = _allWallpapers
            .Where(w => w.Pool == pool)
            .OrderBy(w => w.SortOrder)
            .ThenBy(w => w.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        EditingPoolWallpapers.Clear();
        foreach (var item in items)
        {
            EditingPoolWallpapers.Add(item);
        }

        WallpaperEmptyHint = pool switch
        {
            ThemeWallpaperPool.Songs => "Нет обоев для песен. Добавьте файлы или используйте общие.",
            ThemeWallpaperPool.Bible => "Нет обоев для Библии. Добавьте файлы или используйте общие.",
            _ => "Добавьте обои в этот набор."
        };
        IsEditingPoolEmpty = EditingPoolWallpapers.Count == 0;

        SelectedWallpaper = EditingPoolWallpapers.FirstOrDefault(w => w.Id == selectedId)
            ?? EditingPoolWallpapers.FirstOrDefault();

        SelectWallpaperAsFixedCommand.NotifyCanExecuteChanged();
        RemoveWallpaperCommand.NotifyCanExecuteChanged();
    }

    partial void OnUseSeparateBackgroundsChanged(bool value)
    {
        if (value)
        {
            SelectedWallpaperPool = WallpaperPoolOptions.FirstOrDefault(o => o.Pool == ThemeWallpaperPool.Songs)
                ?? WallpaperPoolOptions.FirstOrDefault();
        }
        else
        {
            SelectedWallpaperPool = WallpaperPoolOptions.FirstOrDefault(o => o.Pool == ThemeWallpaperPool.Shared)
                ?? WallpaperPoolOptions.FirstOrDefault();
        }

        RefreshEditingPoolWallpapers();
    }

    partial void OnSelectedWallpaperPoolChanged(WallpaperPoolOptionItem? value) =>
        RefreshEditingPoolWallpapers();

    partial void OnSelectedBackgroundPickModeChanged(BackgroundPickModeOptionItem? value)
    {
        IsFixedWallpaperPickMode = value?.Mode != ThemeBackgroundPickMode.RandomOnStart;
        SelectWallpaperAsFixedCommand.NotifyCanExecuteChanged();
    }

    private static SectionTransitionMode NormalizeTransition(SectionTransitionMode mode) =>
        mode is SectionTransitionMode.None or SectionTransitionMode.CrossFade
            ? mode
            : SectionTransitionMode.CrossFade;

    private async Task LoadBibleReferenceSettingsAsync()
    {
        try
        {
            _suppressBibleReferencePersist = true;
            ShowBibleReference = await _displaySettingsService.GetShowBibleReferenceAsync();
            var placement = await _displaySettingsService.GetBibleReferencePlacementAsync();
            SelectedBibleReferencePlacement = BibleReferencePlacementOptions
                .FirstOrDefault(o => o.Placement == placement)
                ?? BibleReferencePlacementOptions.FirstOrDefault();
            BibleReferenceAlignment = await _displaySettingsService.GetBibleReferenceAlignmentAsync();
            OnPropertyChanged(nameof(IsBibleRefAlignLeft));
            OnPropertyChanged(nameof(IsBibleRefAlignCenter));
            OnPropertyChanged(nameof(IsBibleRefAlignRight));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadBibleReferenceSettingsAsync: {ex.Message}");
        }
        finally
        {
            _suppressBibleReferencePersist = false;
        }
    }

    partial void OnShowBibleReferenceChanged(bool value)
    {
        if (!_suppressBibleReferencePersist)
        {
            _ = PersistBibleReferenceSettingsAsync();
        }
    }

    partial void OnSelectedBibleReferencePlacementChanged(BibleReferencePlacementItem? value)
    {
        if (!_suppressBibleReferencePersist)
        {
            _ = PersistBibleReferenceSettingsAsync();
        }
    }

    partial void OnBibleReferenceAlignmentChanged(string value)
    {
        OnPropertyChanged(nameof(IsBibleRefAlignLeft));
        OnPropertyChanged(nameof(IsBibleRefAlignCenter));
        OnPropertyChanged(nameof(IsBibleRefAlignRight));
        if (!_suppressBibleReferencePersist)
        {
            _ = PersistBibleReferenceSettingsAsync();
        }
    }

    private async Task PersistBibleReferenceSettingsAsync()
    {
        try
        {
            var placement = SelectedBibleReferencePlacement?.Placement ?? BibleReferencePlacement.Above;
            await _displaySettingsService.SetShowBibleReferenceAsync(ShowBibleReference);
            await _displaySettingsService.SetBibleReferencePlacementAsync(placement);
            await _displaySettingsService.SetBibleReferenceAlignmentAsync(BibleReferenceAlignment);

            if (App.AppHost.Services.GetService(typeof(ProjectionDisplayViewModel)) is ProjectionDisplayViewModel projectionVm)
            {
                await projectionVm.ApplyBibleReferenceSettingsAsync(ShowBibleReference, placement, BibleReferenceAlignment);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PersistBibleReferenceSettingsAsync: {ex.Message}");
        }
    }

    private void SelectTextLayoutMode(TextLayoutMode mode)
    {
        TextLayoutMode = mode;
    }

    private void SyncTextLayoutSelection(TextLayoutMode mode)
    {
        foreach (var option in TextLayoutOptions)
        {
            option.IsSelected = option.Mode == mode;
        }
    }

    partial void OnTextLayoutModeChanged(TextLayoutMode value)
    {
        SyncTextLayoutSelection(value);

        WordWrap = value != TextLayoutMode.ShrinkToFit;
        _ = SaveTextLayoutModeAsync(value);

        try
        {
            var projectionVm = App.AppHost.Services.GetService(typeof(ProjectionDisplayViewModel)) as ProjectionDisplayViewModel;
            if (projectionVm is not null)
            {
                projectionVm.TextLayoutMode = value;
            }
        }
        catch
        {
            // ignore
        }
    }

    private async Task LoadTextLayoutModeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var mode = await _displaySettingsService.GetTextLayoutModeAsync();
            if (TextLayoutMode != mode)
            {
                TextLayoutMode = mode;
            }
            else
            {
                // Значение уже совпадает с полем по умолчанию — OnTextLayoutModeChanged не вызовется.
                SyncTextLayoutSelection(mode);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadTextLayoutModeAsync: Ошибка: {ex.Message}");
            TextLayoutMode = TextLayoutMode.AutoMaxFit;
            SyncTextLayoutSelection(TextLayoutMode.AutoMaxFit);
        }
    }

    private async Task SaveTextLayoutModeAsync(TextLayoutMode mode)
    {
        try
        {
            await _displaySettingsService.SetTextLayoutModeAsync(mode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveTextLayoutModeAsync: Ошибка: {ex.Message}");
        }
    }

    private async Task LoadWordWrapAsync(CancellationToken cancellationToken = default)
    {
        await LoadTextLayoutModeAsync(cancellationToken);
    }

    partial void OnWordWrapChanged(bool value)
    {
        // Основной источник — TextLayoutMode
    }

    private async Task SaveWordWrapAsync(bool wordWrap)
    {
        try
        {
            await _displaySettingsService.SetWordWrapAsync(wordWrap);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveWordWrapAsync: Ошибка: {ex.Message}");
        }
    }

    private async Task LoadCameraSettingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            CameraHost = await _displaySettingsService.GetCameraHostAsync();
            CameraPort = await _displaySettingsService.GetCameraPortAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadCameraSettingsAsync: Ошибка: {ex.Message}");
            CameraPort = 5000; // По умолчанию порт 5000
        }
    }

    partial void OnCameraHostChanged(string? value)
    {
        _ = SaveCameraSettingsAsync();
    }

    partial void OnCameraPortChanged(int value)
    {
        _ = SaveCameraSettingsAsync();
    }

    private async Task SaveCameraSettingsAsync()
    {
        try
        {
            await _displaySettingsService.SetCameraHostAsync(CameraHost);
            await _displaySettingsService.SetCameraPortAsync(CameraPort);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveCameraSettingsAsync: Ошибка: {ex.Message}");
        }
    }

    private async Task LoadPresetsAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Загружаем стили оформления...";

            var previousId = _currentPresetId ?? SelectedPreset?.Id;

            await EnsureDefaultPresetsAsync(cancellationToken);

            Presets.Clear();
            var presets = await _catalogService.GetThemePresetsAsync(cancellationToken);

            foreach (var preset in presets.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Presets.Add(preset);
            }

            // Пытаемся загрузить сохранённый стиль из настроек
            var savedThemePresetId = await _displaySettingsService.GetSelectedThemePresetIdAsync();
            if (savedThemePresetId.HasValue)
            {
                var savedPreset = Presets.FirstOrDefault(p => p.Id == savedThemePresetId.Value);
                if (savedPreset is not null)
                {
                    SelectedPreset = savedPreset;
                    _currentPresetId = savedPreset.Id;
                }
                else
                {
                    // Если сохранённый стиль не найден, используем предыдущий выбор или первый
                    SelectedPreset = previousId is not null
                        ? Presets.FirstOrDefault(p => p.Id == previousId)
                        : Presets.FirstOrDefault();
                }
            }
            else
            {
                // Если стиль не сохранён в настройках, используем предыдущий выбор или первый
                SelectedPreset = previousId is not null
                    ? Presets.FirstOrDefault(p => p.Id == previousId)
                    : Presets.FirstOrDefault();
            }

            StatusMessage = Presets.Count == 0
                ? "Пока нет сохранённых стилей. Настройте оформление и нажмите «Сохранить»."
                : $"Доступно {Presets.Count} стилей. Выберите стиль или создайте новый.";
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось загрузить стили", ex);
        }
        finally
        {
            IsBusy = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private void CreateNew()
    {
        _currentPresetId = null;
        SelectedPreset = null;
        _autoSaveTimer.Stop(); // Останавливаем автосохранение для нового стиля
        ResetToDefaults();
        StatusMessage = "Новый стиль. Заполните параметры и нажмите «Сохранить», чтобы добавить в список.";
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void EditSelected()
    {
        if (SelectedPreset is null)
        {
            return;
        }

        _currentPresetId = SelectedPreset.Id;
        PresetName = SelectedPreset.Name;
        FontFamily = SelectedPreset.FontFamily;
        IsBold = SelectedPreset.IsBold;
        TextAlignment = SelectedPreset.TextAlignment ?? "Center";
        SelectedSectionTransition = SectionTransitionOptions.FirstOrDefault(o => o.Mode == NormalizeTransition(SelectedPreset.SectionTransitionMode))
            ?? SectionTransitionOptions.FirstOrDefault(o => o.Mode == SectionTransitionMode.CrossFade)
            ?? SectionTransitionOptions.FirstOrDefault();
        PrimaryColor = SelectedPreset.Colors.Primary;
        BackgroundColor = SelectedPreset.Colors.Background;
        LoopBackgroundMedia = SelectedPreset.LoopBackgroundMedia;
        UseSeparateBackgrounds = SelectedPreset.UseSeparateBackgrounds;
        SelectedBackgroundPickMode = BackgroundPickModeOptions.FirstOrDefault(o => o.Mode == SelectedPreset.BackgroundPickMode)
            ?? BackgroundPickModeOptions.FirstOrDefault();
        _selectedSharedWallpaperId = SelectedPreset.SelectedSharedWallpaperId;
        _selectedSongWallpaperId = SelectedPreset.SelectedSongWallpaperId;
        _selectedBibleWallpaperId = SelectedPreset.SelectedBibleWallpaperId;
        _allWallpapers = SelectedPreset.Wallpapers?.ToList() ?? new List<ThemeWallpaper>();
        SyncLegacyBackgroundPath();
        RefreshEditingPoolWallpapers();
        TextOutlineEnabled = SelectedPreset.TextOutlineEnabled;
        TextOutlineThickness = SelectedPreset.TextOutlineThickness;
        TextOutlineColor = SelectedPreset.TextOutlineColor ?? "#000000";
        TextOutlineOpacity = SelectedPreset.TextOutlineOpacity;

        StatusMessage = $"Редактирование стиля «{SelectedPreset.Name}». Изменения сохраняются автоматически.";
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void SetTextAlignment(string? alignment)
    {
        if (alignment is "Left" or "Center" or "Right")
        {
            TextAlignment = alignment;
        }
    }

    private async Task AddWallpaperAsync()
    {
        if (!_currentPresetId.HasValue)
        {
            if (!CanSave())
            {
                StatusMessage = "Сначала заполните название стиля, затем добавьте обои.";
                return;
            }

            await SaveAsync();
            if (!_currentPresetId.HasValue)
            {
                return;
            }
        }

        var picker = new global::Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.VideosLibrary,
            ViewMode = global::Windows.Storage.Pickers.PickerViewMode.Thumbnail
        };
        foreach (var ext in new[]
                 {
                     ".mp4", ".m4v", ".mov", ".wmv", ".mkv", ".avi", ".webm",
                     ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
                 })
        {
            picker.FileTypeFilter.Add(ext);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            string importedPath;
            if (!string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path))
            {
                importedPath = await _themeBackgroundMediaService.ImportAsync(file.Path);
            }
            else
            {
                importedPath = await ImportBackgroundFromStorageFileAsync(file);
            }

            var pool = GetEditingPool();
            var wallpaper = await _catalogService.AddThemeWallpaperAsync(
                _currentPresetId.Value,
                importedPath,
                _themeBackgroundMediaService.GetDisplayName(importedPath),
                pool);

            _allWallpapers.Add(wallpaper);
            if (GetSelectedWallpaperId(pool) is null)
            {
                SetSelectedWallpaperId(pool, wallpaper.Id);
            }

            SyncLegacyBackgroundPath();
            RefreshEditingPoolWallpapers();
            SelectedWallpaper = EditingPoolWallpapers.FirstOrDefault(w => w.Id == wallpaper.Id);

            var savedPreset = await _catalogService.UpsertThemePresetAsync(BuildPresetModel());
            // Перечитываем обои и выбор с диска после сохранения
            if (savedPreset.Wallpapers is not null)
            {
                _allWallpapers = savedPreset.Wallpapers.ToList();
            }

            _selectedSharedWallpaperId = savedPreset.SelectedSharedWallpaperId;
            _selectedSongWallpaperId = savedPreset.SelectedSongWallpaperId;
            _selectedBibleWallpaperId = savedPreset.SelectedBibleWallpaperId;
            SyncLegacyBackgroundPath();
            RefreshEditingPoolWallpapers();

            UpsertPreset(savedPreset);
            SelectedPreset = Presets.FirstOrDefault(p => p.Id == savedPreset.Id) ?? SelectedPreset;
            _projectionDisplayService.ApplyTheme(savedPreset);
            await SaveSelectedThemeToSettingsAsync(savedPreset.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось добавить обои", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveSelectedWallpaperAsync()
    {
        if (SelectedWallpaper is null || !_currentPresetId.HasValue)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var wallpaper = SelectedWallpaper;
            await _catalogService.RemoveThemeWallpaperAsync(wallpaper.Id);
            _themeBackgroundMediaService.TryDeleteManaged(wallpaper.FilePath);

            _allWallpapers.RemoveAll(w => w.Id == wallpaper.Id);
            if (_selectedSharedWallpaperId == wallpaper.Id)
            {
                _selectedSharedWallpaperId = null;
            }

            if (_selectedSongWallpaperId == wallpaper.Id)
            {
                _selectedSongWallpaperId = null;
            }

            if (_selectedBibleWallpaperId == wallpaper.Id)
            {
                _selectedBibleWallpaperId = null;
            }

            SyncLegacyBackgroundPath();
            RefreshEditingPoolWallpapers();

            var savedPreset = await _catalogService.UpsertThemePresetAsync(BuildPresetModel());
            UpsertPreset(savedPreset);
            SelectedPreset = Presets.FirstOrDefault(p => p.Id == savedPreset.Id) ?? SelectedPreset;
            _projectionDisplayService.ApplyTheme(savedPreset);
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось удалить обои", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SelectWallpaperAsFixed()
    {
        if (SelectedWallpaper is null || !IsFixedWallpaperPickMode)
        {
            return;
        }

        SetSelectedWallpaperId(GetEditingPool(), SelectedWallpaper.Id);
        SyncLegacyBackgroundPath();
        if (_currentPresetId.HasValue && CanSave())
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        _projectionDisplayService.ApplyTheme(BuildPresetModel());
        RefreshEditingPoolWallpapers();
    }

    private async Task<string> ImportBackgroundFromStorageFileAsync(global::Windows.Storage.StorageFile file)
    {
        var extension = Path.GetExtension(file.Name);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".mp4";
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"gs-bg-{Guid.NewGuid():N}{extension}");
        await using (var src = await file.OpenStreamForReadAsync())
        await using (var dst = File.Create(tempPath))
        {
            await src.CopyToAsync(dst);
        }

        try
        {
            return await _themeBackgroundMediaService.ImportAsync(tempPath);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* ignore */ }
        }
    }

    private void ResetToDefaults()
    {
        PresetName = "Новый стиль";
        FontFamily = "Segoe UI";
        IsBold = false;
        TextAlignment = "Center";
        SelectedSectionTransition = SectionTransitionOptions.FirstOrDefault(o => o.Mode == SectionTransitionMode.CrossFade)
            ?? SectionTransitionOptions.FirstOrDefault();
        PrimaryColor = ThemeColors.Default.Primary;
        BackgroundColor = ThemeColors.Default.Background;
        BackgroundMediaPath = string.Empty;
        BackgroundMediaDisplayName = string.Empty;
        LoopBackgroundMedia = true;
        UseSeparateBackgrounds = false;
        SelectedBackgroundPickMode = BackgroundPickModeOptions.FirstOrDefault(o => o.Mode == ThemeBackgroundPickMode.Fixed)
            ?? BackgroundPickModeOptions.FirstOrDefault();
        SelectedWallpaperPool = WallpaperPoolOptions.FirstOrDefault(o => o.Pool == ThemeWallpaperPool.Shared)
            ?? WallpaperPoolOptions.FirstOrDefault();
        _selectedSharedWallpaperId = null;
        _selectedSongWallpaperId = null;
        _selectedBibleWallpaperId = null;
        _allWallpapers = new List<ThemeWallpaper>();
        RefreshEditingPoolWallpapers();
        TextOutlineEnabled = false;
        TextOutlineThickness = 2;
        TextOutlineColor = "#000000";
        TextOutlineOpacity = 1;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool CanSave()
    {
        return !IsBusy &&
               !string.IsNullOrWhiteSpace(PresetName) &&
               TryParseColor(PrimaryColor) &&
               TryParseColor(BackgroundColor) &&
               TryParseColor(TextOutlineColor);
    }

    partial void OnSelectedPresetChanged(ThemePreset? value)
    {
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        
        // При выборе стиля автоматически открываем его для редактирования
        if (value is not null)
        {
            EditSelected();
        }
        
        // Сохраняем выбранный стиль в настройки и применяем его
        _ = SaveSelectedThemeToSettingsAsync(value?.Id);
    }

    private async Task SaveSelectedThemeToSettingsAsync(Guid? themePresetId)
    {
        try
        {
            await _displaySettingsService.SetSelectedThemePresetIdAsync(themePresetId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка при сохранении выбранного стиля в настройки: {ex.Message}");
        }
    }

    private static bool TryParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var cleaned = hex.TrimStart('#');
        if (cleaned.Length is 6 or 8)
        {
            return uint.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
        }

        return false;
    }

    private async Task SaveAsync()
    {
        if (!CanSave())
        {
            return;
        }

        try
        {
            IsBusy = true;
            SaveCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();

            StatusMessage = "Сохраняем стиль...";

            var presetToSave = BuildPresetModel();
            var savedPreset = await _catalogService.UpsertThemePresetAsync(presetToSave);

            UpsertPreset(savedPreset);
            SelectedPreset = Presets.FirstOrDefault(p => p.Id == savedPreset.Id);
            
            // Если это было редактирование существующего стиля, сохраняем ID
            if (_currentPresetId.HasValue && _currentPresetId.Value == savedPreset.Id)
            {
                _currentPresetId = savedPreset.Id;
            }
            else
            {
                // Это новый стиль, сохраняем ID для дальнейшего автосохранения
                _currentPresetId = savedPreset.Id;
            }
            
            System.Diagnostics.Debug.WriteLine($"SaveAsync: Сохранён стиль '{savedPreset.Name}', Primary={savedPreset.Colors.Primary}, Background={savedPreset.Colors.Background}");

            StatusMessage = $"Стиль «{savedPreset.Name}» сохранён и добавлен в список.";

            // Новый или обновлённый стиль сразу применяем к проекции при сохранении.
            _projectionDisplayService.ApplyTheme(savedPreset);
            await SaveSelectedThemeToSettingsAsync(savedPreset.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось сохранить стиль", ex);
        }
        finally
        {
            IsBusy = false;
            SaveCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
            ApplyThemeCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task AutoSaveAsync()
    {
        if (!CanSave() || !_currentPresetId.HasValue || IsBusy)
        {
            return;
        }

        try
        {
            var presetToSave = BuildPresetModel();
            var savedPreset = await _catalogService.UpsertThemePresetAsync(presetToSave);

            UpsertPreset(savedPreset);
            SelectedPreset = Presets.FirstOrDefault(p => p.Id == savedPreset.Id);
            
            System.Diagnostics.Debug.WriteLine($"AutoSaveAsync: Автосохранён стиль '{savedPreset.Name}'");
            _projectionDisplayService.ApplyTheme(savedPreset);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoSaveAsync: Ошибка при автосохранении: {ex.Message}");
        }
    }

    private ThemePreset BuildPresetModel()
    {
        // Если редактируем существующий стиль, используем его ID, иначе создаем новый
        var presetId = _currentPresetId ?? Guid.NewGuid();

        return new ThemePreset
        {
            Id = presetId,
            Name = PresetName,
            FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? null : FontFamily,
            IsBold = IsBold,
            TextAlignment = string.IsNullOrWhiteSpace(TextAlignment) ? "Center" : TextAlignment,
            SectionTransitionMode = SelectedSectionTransition?.Mode ?? SectionTransitionMode.CrossFade,
            Colors = new ThemeColors(
                PrimaryColor,
                BackgroundColor),
            BackgroundMediaPath = string.IsNullOrWhiteSpace(BackgroundMediaPath) ? null : BackgroundMediaPath,
            LoopBackgroundMedia = LoopBackgroundMedia,
            UseSeparateBackgrounds = UseSeparateBackgrounds,
            BackgroundPickMode = SelectedBackgroundPickMode?.Mode ?? ThemeBackgroundPickMode.Fixed,
            SelectedSharedWallpaperId = _selectedSharedWallpaperId,
            SelectedSongWallpaperId = _selectedSongWallpaperId,
            SelectedBibleWallpaperId = _selectedBibleWallpaperId,
            Wallpapers = _allWallpapers.ToList(),
            TextOutlineEnabled = TextOutlineEnabled,
            TextOutlineThickness = TextOutlineThickness,
            TextOutlineColor = TextOutlineColor,
            TextOutlineOpacity = TextOutlineOpacity
        };
    }

    private async Task DeleteAsync()
    {
        if (SelectedPreset is null || XamlRoot is null)
        {
            return;
        }

        // Показываем диалог подтверждения
        var dialog = new ContentDialog
        {
            Title = "Удаление стиля",
            Content = $"Вы уверены, что хотите удалить стиль «{SelectedPreset.Name}»?",
            PrimaryButtonText = "Удалить",
            SecondaryButtonText = "Отмена",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Secondary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            IsBusy = true;
            SaveCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();

            StatusMessage = $"Удаляем стиль «{SelectedPreset.Name}»...";

            var removedId = SelectedPreset.Id;
            var mediaPaths = (SelectedPreset.Wallpapers ?? Array.Empty<ThemeWallpaper>())
                .Select(w => w.FilePath)
                .Concat(string.IsNullOrWhiteSpace(SelectedPreset.BackgroundMediaPath)
                    ? Array.Empty<string>()
                    : new[] { SelectedPreset.BackgroundMediaPath })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            await _catalogService.RemoveThemePresetAsync(removedId);
            foreach (var mediaPath in mediaPaths)
            {
                _themeBackgroundMediaService.TryDeleteManaged(mediaPath);
            }

            var index = Presets.IndexOf(SelectedPreset);
            Presets.Remove(SelectedPreset);

            SelectedPreset = index < Presets.Count
                ? Presets[index]
                : Presets.LastOrDefault();

            if (SelectedPreset is null)
            {
                CreateNew();
            }
            else
            {
                _currentPresetId = null;
            }

            StatusMessage = "Стиль удалён.";
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось удалить стиль", ex);
        }
        finally
        {
            IsBusy = false;
            SaveCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
        }
    }

    private void UpsertPreset(ThemePreset preset)
    {
        var existing = Presets.FirstOrDefault(p => p.Id == preset.Id);
        if (existing is not null)
        {
            var index = Presets.IndexOf(existing);
            Presets[index] = preset;
        }
        else
        {
            Presets.Add(preset);
        }

        SortPresets();
    }

    private void SortPresets()
    {
        if (Presets.Count <= 1)
        {
            return;
        }

        var ordered = Presets
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (ordered.SequenceEqual(Presets))
        {
            return;
        }

        Presets.Clear();
        foreach (var preset in ordered)
        {
            Presets.Add(preset);
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        ApplyThemeCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyThemeAsync()
    {
        // Если это новый стиль (еще не сохранен), сначала сохраняем его
        if (!_currentPresetId.HasValue && CanSave())
        {
            await SaveAsync();
            // После сохранения SelectedPreset будет установлен
            if (SelectedPreset is null)
            {
                return;
            }
        }

        if (SelectedPreset is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ApplyThemeCommand.NotifyCanExecuteChanged();

            StatusMessage = $"Применяем стиль «{SelectedPreset.Name}»...";

            // Применяем актуальные значения редактора, не устаревший SelectedPreset
            var presetToApply = _currentPresetId.HasValue ? BuildPresetModel() : SelectedPreset;
            _projectionDisplayService.ApplyTheme(presetToApply);

            // Сохраняем выбранный стиль в настройки
            await SaveSelectedThemeToSettingsAsync(SelectedPreset.Id);

            StatusMessage = $"Стиль «{SelectedPreset.Name}» применён.";
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось применить стиль", ex);
        }
        finally
        {
            IsBusy = false;
            ApplyThemeCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task LoadDisplaysAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoadingDisplays)
        {
            return;
        }

        try
        {
            IsLoadingDisplays = true;
            StatusMessage = "Загружаем список экранов...";
            var displays = await _displaySettingsService.GetAvailableDisplaysAsync();
            
            AvailableDisplays.Clear();
            foreach (var display in displays)
            {
                AvailableDisplays.Add(display);
            }

            // Load selected display
            var selectedDisplayId = await _displaySettingsService.GetSelectedDisplayIdAsync();
            SelectedDisplay = selectedDisplayId is not null
                ? AvailableDisplays.FirstOrDefault(d => d.Id == selectedDisplayId)
                : AvailableDisplays.FirstOrDefault(d => d.IsPrimary) ?? AvailableDisplays.FirstOrDefault();
            
            // Показываем информацию о найденных экранах в сообщении
            if (AvailableDisplays.Count > 0)
            {
                StatusMessage = $"Найдено экранов: {AvailableDisplays.Count}. " + 
                               string.Join(", ", AvailableDisplays.Select(d => d.Name));
            }
            else
            {
                StatusMessage = "Экраны не найдены. Проверьте подключение мониторов.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось загрузить список экранов", ex);
        }
        finally
        {
            IsLoadingDisplays = false;
        }
    }

    private async Task EnsureDefaultPresetsAsync(CancellationToken cancellationToken)
    {
        var presets = await _catalogService.GetThemePresetsAsync(cancellationToken);
        if (presets.Count > 0)
        {
            return;
        }

        var defaultPresets = new[]
        {
            new ThemePreset
            {
                Name = "Чёрный фон",
                FontFamily = "Segoe UI",
                IsBold = false,
                TextAlignment = "Center",
                SectionTransitionMode = SectionTransitionMode.CrossFade,
                Colors = new ThemeColors("#FFFFFF", "#000000"),
                BackgroundMediaPath = null,
                LoopBackgroundMedia = true
            },
            new ThemePreset
            {
                Name = "Белый фон",
                FontFamily = "Segoe UI",
                IsBold = false,
                TextAlignment = "Center",
                SectionTransitionMode = SectionTransitionMode.CrossFade,
                Colors = new ThemeColors("#000000", "#FFFFFF"),
                BackgroundMediaPath = null,
                LoopBackgroundMedia = true
            }
        };

        foreach (var preset in defaultPresets)
        {
            await _catalogService.UpsertThemePresetAsync(preset, cancellationToken);
        }
    }

    private async Task SaveDisplaySelectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _displaySettingsService.SetSelectedDisplayIdAsync(SelectedDisplay?.Id);
            StatusMessage = SelectedDisplay is not null
                ? $"Выбран экран: {SelectedDisplay.Name}"
                : "Выбор экрана сброшен. Будет использован основной экран.";
        }
        catch (Exception ex)
        {
            StatusMessage = null;
            await ErrorDialog.ShowAsync("Не удалось сохранить выбор экрана", ex);
        }
    }

    partial void OnSelectedDisplayChanged(DisplayInfo? value)
    {
        if (value is not null)
        {
            SaveDisplaySelectionCommand.ExecuteAsync(null);
        }
    }
}

