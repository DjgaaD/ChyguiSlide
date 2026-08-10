using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Data.ValueObjects;
using ChyguiSlide.Services;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace ChyguiSlide.ViewModels;

public sealed partial class ProjectionDisplayViewModel : ObservableRecipient
{
    private readonly IProjectionStateService _projectionStateService;
    private readonly IDisplaySettingsService _displaySettingsService;
    private readonly IThemeBackgroundMediaService _themeBackgroundMediaService;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<ThemeWallpaperPool, Guid> _sessionWallpaperPicks = new();

    private ThemeColors _currentColors = ThemeColors.Default;
    private ThemePreset? _appliedTheme;
    private ThemeWallpaperPool? _activeWallpaperPool;
    private Func<SectionTransitionMode, Func<Task>, Task>? _playTransitionAsync;
    private string _lastVisibleLinesKey = string.Empty;
    private string? _pendingOutgoingCaption;
    private double _pendingOutgoingFontSize;

    /// <summary>Срабатывает после смены пути фонового медиа (для синхронизации видеоплеера).</summary>
    public event EventHandler? BackgroundMediaChanged;

    public ObservableCollection<ProjectionLineItem> Lines { get; } = new();
    public ObservableCollection<ProjectionLineItem> OutgoingLines { get; } = new();

    [ObservableProperty]
    private SectionTransitionMode sectionTransitionMode = SectionTransitionMode.CrossFade;

    [ObservableProperty]
    private string? outgoingReferenceCaption;

    [ObservableProperty]
    private double outgoingDisplayFontSize = 100;

    [ObservableProperty]
    private string? songTitle;

    [ObservableProperty]
    private int sectionIndex;

    [ObservableProperty]
    private DateTimeOffset updatedAt;

    [ObservableProperty]
    private bool isBlackout;

    [ObservableProperty]
    private SolidColorBrush backgroundBrush = CreateBrush(ThemeColors.Default.Background);

    [ObservableProperty]
    private SolidColorBrush primaryBrush = CreateBrush(ThemeColors.Default.Primary);

    [ObservableProperty]
    private SolidColorBrush textOutlineBrush = CreateBrush("#000000");

    [ObservableProperty]
    private double textOutlineThickness;

    [ObservableProperty]
    private double textOutlineOpacity = 1;

    [ObservableProperty]
    private string fontFamilyName = "Segoe UI";

    [ObservableProperty]
    private FontWeight fontWeight = FontWeights.Normal;

    [ObservableProperty]
    private string textAlignment = "Center";

    [ObservableProperty]
    private bool wordWrap = true;

    [ObservableProperty]
    private TextLayoutMode textLayoutMode = TextLayoutMode.AutoMaxFit;

    /// <summary>
    /// Ширина макета внутри Viewbox. При переносе = ширина экрана (минус поля),
    /// чтобы выравнивание было относительно всего 16:9, а не узкого блока текста.
    /// </summary>
    [ObservableProperty]
    private double layoutMaxWidth = 1792;

    /// <summary>Ширина холста проекции (обычно = окно / 16:9).</summary>
    [ObservableProperty]
    private double designWidth = 1920;

    /// <summary>Высота холста проекции.</summary>
    [ObservableProperty]
    private double designHeight = 1080;

    /// <summary>Размер шрифта на холсте (подбирается под текст).</summary>
    [ObservableProperty]
    private double displayFontSize = 100;

    /// <summary>Межстрочный интервал (синхронизирован с раскладкой).</summary>
    [ObservableProperty]
    private double lineSpacing = 12;

    [ObservableProperty]
    private string? referenceCaption;

    [ObservableProperty]
    private bool showBibleReference;

    [ObservableProperty]
    private BibleReferencePlacement bibleReferencePlacement = BibleReferencePlacement.Above;

    [ObservableProperty]
    private string bibleReferenceAlignment = "Center";

    public bool IsReferenceAboveVisible =>
        ShowBibleReference
        && !string.IsNullOrWhiteSpace(ReferenceCaption)
        && BibleReferencePlacement == BibleReferencePlacement.Above;

    public bool IsReferenceBelowVisible =>
        ShowBibleReference
        && !string.IsNullOrWhiteSpace(ReferenceCaption)
        && BibleReferencePlacement == BibleReferencePlacement.Below;

    /// <summary>
    /// «После текста» вшивается в последнюю строку стиха (участвует в кегле/переносе),
    /// отдельный TextBlock не нужен.
    /// </summary>
    public bool IsReferenceAfterVisible => false;

    public bool IsReferenceTopOfScreenVisible =>
        ShowBibleReference
        && !string.IsNullOrWhiteSpace(ReferenceCaption)
        && BibleReferencePlacement == BibleReferencePlacement.TopOfScreen;

    public bool IsReferenceBottomOfScreenVisible =>
        ShowBibleReference
        && !string.IsNullOrWhiteSpace(ReferenceCaption)
        && BibleReferencePlacement == BibleReferencePlacement.BottomOfScreen;

    public bool IsOutgoingReferenceAboveVisible =>
        ShowBibleReference
        && !string.IsNullOrWhiteSpace(OutgoingReferenceCaption)
        && BibleReferencePlacement == BibleReferencePlacement.Above;

    public bool IsOutgoingReferenceBelowVisible =>
        ShowBibleReference
        && !string.IsNullOrWhiteSpace(OutgoingReferenceCaption)
        && BibleReferencePlacement == BibleReferencePlacement.Below;

    public bool IsOutgoingReferenceTopOfScreenVisible =>
        ShowBibleReference
        && !string.IsNullOrWhiteSpace(OutgoingReferenceCaption)
        && BibleReferencePlacement == BibleReferencePlacement.TopOfScreen;

    public bool IsOutgoingReferenceBottomOfScreenVisible =>
        ShowBibleReference
        && !string.IsNullOrWhiteSpace(OutgoingReferenceCaption)
        && BibleReferencePlacement == BibleReferencePlacement.BottomOfScreen;

    public double OutgoingReferenceFontSize =>
        BibleReferencePlacement.IsPinnedToScreenEdge()
            ? EdgeReferenceFontSize
            : Math.Max(22, OutgoingDisplayFontSize * 0.38);

    /// <summary>
    /// Кегль подписи у краёв экрана — фиксированный от высоты экрана (не от кегля стиха).
    /// Над/под текстом — доля от кегля стиха.
    /// </summary>
    public double ReferenceFontSize =>
        BibleReferencePlacement.IsPinnedToScreenEdge()
            ? EdgeReferenceFontSize
            : Math.Max(22, DisplayFontSize * 0.38);

    private double EdgeReferenceFontSize =>
        Math.Clamp(DesignHeight > 1 ? DesignHeight * 0.032 : 28, 22, 42);

    [ObservableProperty]
    private bool isVideoMode;

    [ObservableProperty]
    private bool isNdiVideoMode;

    [ObservableProperty]
    private bool isBackgroundImageVisible;

    [ObservableProperty]
    private bool isBackgroundVideoVisible;

    [ObservableProperty]
    private ImageSource? backgroundImageSource;

    [ObservableProperty]
    private bool loopBackgroundMedia = true;

    /// <summary>Абсолютный путь к фоновому видео (если выбран видеофайл).</summary>
    public string? BackgroundVideoPath { get; private set; }

    private double _windowWidth = 1920;
    private double _windowHeight = 1080;
    private int _displayWidth = 1920;
    private int _displayHeight = 1080;
    private bool _suppressLayoutModeSideEffects;

    public double ContentOpacity => IsBlackout ? 0 : 1;
    public double BlackoutOpacity => IsBlackout ? 1 : 0;

    public void UpdateWindowSize(double width, double height)
    {
        if (Math.Abs(_windowWidth - width) > 1 || Math.Abs(_windowHeight - height) > 1)
        {
            _windowWidth = width;
            _windowHeight = height;
            SyncDesignSurface();

            // Пересчитываем строки при изменении размера окна, если включён перенос
            if (TextLayoutMode != TextLayoutMode.ShrinkToFit)
            {
                _ = RefreshLinesWithDisplayResolutionAsync(_projectionStateService.Current.VisibleLines);
            }
        }
    }

    private void SyncDesignSurface()
    {
        // Холст = реальный экран 16:9 (или текущее окно) — одинаковый для коротких и длинных стихов
        var w = _windowWidth > 1 ? _windowWidth : (_displayWidth > 0 ? _displayWidth : 1920);
        var h = _windowHeight > 1 ? _windowHeight : (_displayHeight > 0 ? _displayHeight : 1080);
        DesignWidth = w;
        DesignHeight = h;

        // Поля как Margin="64" → 128 суммарно
        const double margin = 128;
        LayoutMaxWidth = TextLayoutMode == TextLayoutMode.ShrinkToFit
            ? Math.Max(200, w)
            : Math.Max(200, w - margin);
    }

    private async Task LoadDisplayResolutionAsync()
    {
        try
        {
            var display = await _displaySettingsService.GetSelectedDisplayAsync();
            if (display is not null)
            {
                _displayWidth = display.Width;
                _displayHeight = display.Height;
                System.Diagnostics.Debug.WriteLine($"LoadDisplayResolutionAsync: Разрешение экрана: {_displayWidth}x{_displayHeight}");
            }
            else
            {
                _displayWidth = (int)_windowWidth;
                _displayHeight = (int)_windowHeight;
                System.Diagnostics.Debug.WriteLine($"LoadDisplayResolutionAsync: Экран не выбран, используем размер окна: {_displayWidth}x{_displayHeight}");
            }

            SyncDesignSurface();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadDisplayResolutionAsync: Ошибка: {ex.Message}");
            _displayWidth = (int)_windowWidth;
            _displayHeight = (int)_windowHeight;
            SyncDesignSurface();
        }
    }

    public ProjectionDisplayViewModel(
        IProjectionStateService projectionStateService,
        IDisplaySettingsService displaySettingsService,
        IThemeBackgroundMediaService themeBackgroundMediaService)
    {
        _projectionStateService = projectionStateService;
        _displaySettingsService = displaySettingsService;
        _themeBackgroundMediaService = themeBackgroundMediaService;
        _dispatcher = DispatcherQueue.GetForCurrentThread() ?? App.MainDispatcherQueue;

        _projectionStateService.StateChanged += OnProjectionStateChanged;
        UpdateFromState(_projectionStateService.Current);
        
        // Загружаем настройку раскладки текста и разрешение экрана
        _ = LoadTextLayoutModeAsync();
        _ = LoadDisplayResolutionAsync();
    }

    private async Task LoadTextLayoutModeAsync()
    {
        try
        {
            var mode = await _displaySettingsService.GetTextLayoutModeAsync();
            _suppressLayoutModeSideEffects = true;
            TextLayoutMode = mode;
            WordWrap = mode != TextLayoutMode.ShrinkToFit;
            SyncDesignSurface();
            _suppressLayoutModeSideEffects = false;
            RefreshLines(_projectionStateService.Current.VisibleLines);
        }
        catch (Exception ex)
        {
            _suppressLayoutModeSideEffects = false;
            System.Diagnostics.Debug.WriteLine($"LoadTextLayoutModeAsync: Ошибка: {ex.Message}");
            TextLayoutMode = TextLayoutMode.AutoMaxFit;
            WordWrap = true;
        }
    }

    private async Task LoadWordWrapAsync()
    {
        await LoadTextLayoutModeAsync();
    }

    public void BeginBackgroundSession()
    {
        _sessionWallpaperPicks.Clear();
        _activeWallpaperPool = null;
    }

    public void ApplyTheme(ThemePreset? theme, bool startNewBackgroundSession = false)
    {
        if (startNewBackgroundSession)
        {
            BeginBackgroundSession();
        }

        var colors = theme?.Colors ?? ThemeColors.Default;
        _currentColors = colors;
        _appliedTheme = theme;

        System.Diagnostics.Debug.WriteLine($"ApplyTheme: theme={theme?.Name ?? "null"}, Primary={colors.Primary}, Background={colors.Background}");

        FontFamilyName = string.IsNullOrWhiteSpace(theme?.FontFamily)
            ? "Segoe UI"
            : theme.FontFamily;

        var newFontWeight = theme?.IsBold == true ? FontWeights.Bold : FontWeights.Normal;
        System.Diagnostics.Debug.WriteLine($"ApplyTheme: Устанавливаем FontWeight: IsBold={theme?.IsBold}, newFontWeight.Weight={newFontWeight.Weight}, текущий FontWeight.Weight={FontWeight.Weight}");
        
        FontWeight = newFontWeight;
        
        System.Diagnostics.Debug.WriteLine($"ApplyTheme: FontWeight установлен: FontWeight.Weight={FontWeight.Weight}");

        TextAlignment = theme?.TextAlignment ?? "Center";
        System.Diagnostics.Debug.WriteLine($"ApplyTheme: TextAlignment установлен: {TextAlignment}");

        SectionTransitionMode = NormalizeTransition(theme?.SectionTransitionMode);

        BackgroundBrush = CreateBrush(colors.Background);
        PrimaryBrush = CreateBrush(colors.Primary);

        var outlineEnabled = theme?.TextOutlineEnabled == true;
        TextOutlineThickness = outlineEnabled ? Math.Max(0, theme!.TextOutlineThickness) : 0;
        TextOutlineOpacity = theme?.TextOutlineOpacity ?? 1;
        TextOutlineBrush = CreateBrush(string.IsNullOrWhiteSpace(theme?.TextOutlineColor)
            ? "#000000"
            : theme!.TextOutlineColor);

        System.Diagnostics.Debug.WriteLine($"ApplyTheme: BackgroundBrush={BackgroundBrush.Color}, PrimaryBrush={PrimaryBrush.Color}, FontWeight={FontWeight.Weight}, IsBold={theme?.IsBold}");

        ApplyResolvedBackground(forceNewRandom: startNewBackgroundSession);

        // Тема применяется без анимации смены слайда
        RefreshLines(_projectionStateService.Current.VisibleLines);
    }

    /// <summary>Пересчитать фон под текущий контент (песня / Библия).</summary>
    public void ApplyResolvedBackground(bool forceNewRandom = false)
    {
        var isBible = !string.IsNullOrWhiteSpace(ReferenceCaption)
            || !string.IsNullOrWhiteSpace(_projectionStateService.Current.ReferenceCaption);

        if (_appliedTheme is not null)
        {
            var pool = ThemeBackgroundResolver.ResolvePool(_appliedTheme, isBible);
            if (_activeWallpaperPool is not null && _activeWallpaperPool != pool)
            {
                // Смена пула (песни ↔ Библия) — новый случайный выбор для пула, если его ещё нет в сессии
                forceNewRandom = forceNewRandom
                    || _appliedTheme.BackgroundPickMode == ThemeBackgroundPickMode.RandomOnStart
                       && !_sessionWallpaperPicks.ContainsKey(pool);
            }

            _activeWallpaperPool = pool;
        }

        LoopBackgroundMedia = _appliedTheme?.LoopBackgroundMedia ?? true;
        var path = ThemeBackgroundResolver.ResolvePath(
            _appliedTheme,
            isBible,
            _sessionWallpaperPicks,
            forceNewRandom);

        ApplyBackgroundPath(path);
    }

    private void ApplyBackgroundPath(string? path)
    {
        var previousVideo = BackgroundVideoPath;
        var previousImage = (BackgroundImageSource as BitmapImage)?.UriSource?.LocalPath;

        ClearBackgroundMedia();

        var resolved = _themeBackgroundMediaService.ResolveExistingPath(path);
        if (resolved is null)
        {
            if (!string.Equals(previousVideo, BackgroundVideoPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(previousImage, null, StringComparison.OrdinalIgnoreCase))
            {
                BackgroundMediaChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        var ext = Path.GetExtension(resolved);
        if (IsImageExtension(ext))
        {
            BackgroundImageSource = new BitmapImage(new Uri(resolved, UriKind.Absolute));
            IsBackgroundImageVisible = true;
        }
        else if (IsVideoExtension(ext))
        {
            BackgroundVideoPath = resolved;
            IsBackgroundVideoVisible = true;
        }

        BackgroundMediaChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearBackgroundMedia()
    {
        IsBackgroundImageVisible = false;
        IsBackgroundVideoVisible = false;
        BackgroundImageSource = null;
        BackgroundVideoPath = null;
    }

    private static bool IsImageExtension(string? extension)
        => extension is not null
           && extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp";

    private static bool IsVideoExtension(string? extension)
        => extension is not null
           && extension.ToLowerInvariant() is ".mp4" or ".mov" or ".wmv" or ".mkv" or ".avi" or ".webm" or ".m4v";

    partial void OnFontWeightChanged(FontWeight value)
    {
        System.Diagnostics.Debug.WriteLine($"OnFontWeightChanged: FontWeight изменён на {value.Weight}");
        // При изменении FontWeight обновляем все строки
        RefreshLines(_projectionStateService.Current.VisibleLines);
    }

    partial void OnTextAlignmentChanged(string value)
    {
        System.Diagnostics.Debug.WriteLine($"OnTextAlignmentChanged: TextAlignment изменён на {value}");
        // При изменении TextAlignment обновляем все строки
        RefreshLines(_projectionStateService.Current.VisibleLines);
    }

    partial void OnWordWrapChanged(bool value)
    {
        if (_suppressLayoutModeSideEffects)
        {
            return;
        }

        // Совместимость: старый флаг отражает «не ShrinkToFit»
        if (!value && TextLayoutMode != TextLayoutMode.ShrinkToFit)
        {
            TextLayoutMode = TextLayoutMode.ShrinkToFit;
        }
        else if (value && TextLayoutMode == TextLayoutMode.ShrinkToFit)
        {
            TextLayoutMode = TextLayoutMode.MaximizeFont;
        }
    }

    partial void OnTextLayoutModeChanged(TextLayoutMode value)
    {
        if (_suppressLayoutModeSideEffects)
        {
            return;
        }

        System.Diagnostics.Debug.WriteLine($"OnTextLayoutModeChanged: {value}");
        _suppressLayoutModeSideEffects = true;
        WordWrap = value != TextLayoutMode.ShrinkToFit;
        SyncDesignSurface();
        _suppressLayoutModeSideEffects = false;
        _ = SaveTextLayoutModeAsync(value);
        _ = RefreshLinesWithDisplayResolutionAsync(_projectionStateService.Current.VisibleLines);
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

    public void SetBlackout(bool value)
    {
        if (IsBlackout == value)
        {
            return;
        }

        IsBlackout = value;
    }

    private void OnProjectionStateChanged(object? sender, ProjectionState state)
    {
        if (_dispatcher.HasThreadAccess)
        {
            UpdateFromState(state);
        }
        else
        {
            _dispatcher.TryEnqueue(() => UpdateFromState(state));
        }
    }

    public void SetTransitionPlayer(Func<SectionTransitionMode, Func<Task>, Task>? playTransitionAsync)
    {
        _playTransitionAsync = playTransitionAsync;
    }

    private static SectionTransitionMode NormalizeTransition(SectionTransitionMode? mode)
    {
        if (mode is SectionTransitionMode.None or SectionTransitionMode.CrossFade)
        {
            return mode.Value;
        }

        return SectionTransitionMode.CrossFade;
    }

    private void UpdateFromState(ProjectionState state)
    {
        // Запоминаем подпись/кегль ДО смены — для слоя «уходящего» слайда
        _pendingOutgoingCaption = ReferenceCaption;
        _pendingOutgoingFontSize = DisplayFontSize;

        var previousWasBible = !string.IsNullOrWhiteSpace(ReferenceCaption);
        var nextIsBible = !string.IsNullOrWhiteSpace(state.ReferenceCaption);

        SongTitle = state.SongTitle;
        SectionIndex = state.SectionIndex;
        UpdatedAt = state.UpdatedAt.ToLocalTime();
        ReferenceCaption = state.ReferenceCaption;
        _ = RefreshBibleReferenceSettingsAsync();

        if (previousWasBible != nextIsBible || _activeWallpaperPool is null)
        {
            ApplyResolvedBackground(forceNewRandom: false);
        }

        var linesKey = BuildLinesKey(state.VisibleLines);
        var shouldAnimate = !string.Equals(linesKey, _lastVisibleLinesKey, StringComparison.Ordinal)
            && !IsBlackout
            && SectionTransitionMode != SectionTransitionMode.None
            && _playTransitionAsync is not null
            && !string.IsNullOrEmpty(_lastVisibleLinesKey)
            && Lines.Count > 0; // первый слайд / пустой экран — без кроссфейда (иначе белая вспышка)

        _ = ApplyVisibleLinesAsync(state.VisibleLines, shouldAnimate);
    }

    private static string BuildLinesKey(IReadOnlyList<string>? lines)
    {
        if (lines is null || lines.Count == 0)
        {
            return string.Empty;
        }

        return string.Join('\u001e', lines);
    }

    private async Task ApplyVisibleLinesAsync(IReadOnlyList<string> lines, bool animate)
    {
        async Task ApplyCoreAsync()
        {
            // Не дёргаем разрешение экрана на каждый слайд — это даёт рывки
            if (TextLayoutMode != TextLayoutMode.ShrinkToFit && DesignWidth <= 1)
            {
                await RefreshLinesWithDisplayResolutionAsync(lines).ConfigureAwait(true);
            }
            else
            {
                RefreshLines(lines);
            }
        }

        if (!animate || _playTransitionAsync is null || SectionTransitionMode == SectionTransitionMode.None)
        {
            ClearOutgoingSnapshot();
            await ApplyCoreAsync().ConfigureAwait(true);
            return;
        }

        CaptureOutgoingSnapshot();
        await _playTransitionAsync(SectionTransitionMode, ApplyCoreAsync).ConfigureAwait(true);
        ClearOutgoingSnapshot();
    }

    private void CaptureOutgoingSnapshot()
    {
        OutgoingLines.Clear();
        foreach (var line in Lines)
        {
            OutgoingLines.Add(new ProjectionLineItem(
                line.Text,
                line.Foreground,
                line.FontWeight,
                line.TextAlignment,
                line.Opacity));
        }

        OutgoingReferenceCaption = _pendingOutgoingCaption;
        OutgoingDisplayFontSize = _pendingOutgoingFontSize > 1 ? _pendingOutgoingFontSize : DisplayFontSize;
        NotifyOutgoingReferenceVisibility();
    }

    private void ClearOutgoingSnapshot()
    {
        OutgoingLines.Clear();
        OutgoingReferenceCaption = null;
        NotifyOutgoingReferenceVisibility();
    }

    private void NotifyOutgoingReferenceVisibility()
    {
        OnPropertyChanged(nameof(IsOutgoingReferenceAboveVisible));
        OnPropertyChanged(nameof(IsOutgoingReferenceBelowVisible));
        OnPropertyChanged(nameof(IsOutgoingReferenceTopOfScreenVisible));
        OnPropertyChanged(nameof(IsOutgoingReferenceBottomOfScreenVisible));
        OnPropertyChanged(nameof(OutgoingReferenceFontSize));
    }

    partial void OnOutgoingDisplayFontSizeChanged(double value) =>
        OnPropertyChanged(nameof(OutgoingReferenceFontSize));

    partial void OnOutgoingReferenceCaptionChanged(string? value) =>
        NotifyOutgoingReferenceVisibility();

    private async Task RefreshBibleReferenceSettingsAsync()
    {
        try
        {
            ShowBibleReference = await _displaySettingsService.GetShowBibleReferenceAsync();
            BibleReferencePlacement = await _displaySettingsService.GetBibleReferencePlacementAsync();
            BibleReferenceAlignment = await _displaySettingsService.GetBibleReferenceAlignmentAsync();
            NotifyReferenceVisibility();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RefreshBibleReferenceSettingsAsync: {ex.Message}");
        }
    }

    public async Task ApplyBibleReferenceSettingsAsync(
        bool show,
        BibleReferencePlacement placement,
        string alignment)
    {
        ShowBibleReference = show;
        BibleReferencePlacement = placement;
        BibleReferenceAlignment = alignment;
        NotifyReferenceVisibility();
        await _displaySettingsService.SetShowBibleReferenceAsync(show);
        await _displaySettingsService.SetBibleReferencePlacementAsync(placement);
        await _displaySettingsService.SetBibleReferenceAlignmentAsync(alignment);
        RelayoutCurrentSlideForReference();
    }

    private void NotifyReferenceVisibility()
    {
        OnPropertyChanged(nameof(IsReferenceAboveVisible));
        OnPropertyChanged(nameof(IsReferenceBelowVisible));
        OnPropertyChanged(nameof(IsReferenceAfterVisible));
        OnPropertyChanged(nameof(IsReferenceTopOfScreenVisible));
        OnPropertyChanged(nameof(IsReferenceBottomOfScreenVisible));
        OnPropertyChanged(nameof(ReferenceFontSize));
    }

    private void RelayoutCurrentSlideForReference()
    {
        var source = _projectionStateService.Current.VisibleLines;
        if (source is null || source.Count == 0)
        {
            return;
        }

        RefreshLines(source);
    }

    partial void OnDisplayFontSizeChanged(double value) => OnPropertyChanged(nameof(ReferenceFontSize));
    partial void OnReferenceCaptionChanged(string? value)
    {
        NotifyReferenceVisibility();
        RelayoutCurrentSlideForReference();
    }

    partial void OnShowBibleReferenceChanged(bool value)
    {
        NotifyReferenceVisibility();
        RelayoutCurrentSlideForReference();
    }

    partial void OnBibleReferencePlacementChanged(BibleReferencePlacement value)
    {
        NotifyReferenceVisibility();
        RelayoutCurrentSlideForReference();
    }

    private bool ShouldInlineReferenceAfterText() =>
        ShowBibleReference
        && !string.IsNullOrWhiteSpace(ReferenceCaption)
        && BibleReferencePlacement == BibleReferencePlacement.After;

    /// <summary>
    /// Для «После текста» дописываем подпись к последней исходной строке —
    /// она участвует в переносе и подборе кегля как обычный текст.
    /// </summary>
    private IReadOnlyList<string> PrepareSourceLinesForLayout(IReadOnlyList<string> lines)
    {
        if (!ShouldInlineReferenceAfterText() || lines.Count == 0)
        {
            return lines;
        }

        var prepared = lines.ToList();
        var caption = ReferenceCaption!.Trim();
        var last = prepared[^1]?.TrimEnd() ?? string.Empty;
        prepared[^1] = string.IsNullOrWhiteSpace(last)
            ? caption
            : $"{last} {caption}";
        return prepared;
    }

    private async Task RefreshLinesWithDisplayResolutionAsync(IReadOnlyList<string> lines)
    {
        // Обновляем разрешение экрана перед разбиением
        await LoadDisplayResolutionAsync();
        RefreshLines(lines);
    }

    private void RefreshLines(IReadOnlyList<string> lines)
    {
        Lines.Clear();
        SyncDesignSurface();
        OnPropertyChanged(nameof(ReferenceFontSize));
        _lastVisibleLinesKey = BuildLinesKey(lines);

        System.Diagnostics.Debug.WriteLine(
            $"RefreshLines: FontWeight={FontWeight.Weight}, Lines={lines.Count}, Mode={TextLayoutMode}, Design={DesignWidth:F0}x{DesignHeight:F0}");

        if (lines.Count == 0)
        {
            DisplayFontSize = 100;
            Lines.Add(new ProjectionLineItem("— пауза —", PrimaryBrush, FontWeight, TextAlignment, 0.7));
            return;
        }

        var source = PrepareSourceLinesForLayout(lines);

        // Все режимы с переносом: раскладка на полном холсте 16:9 + подбор кегля.
        // Иначе Viewbox масштабирует «высокий» блок текста и по бокам остаются поля
        // (короткий стих тянется на всю ширину, длинный — нет).
        if (TextLayoutMode != TextLayoutMode.ShrinkToFit)
        {
            var (fitted, fontSize) = LayoutSlideForMaxFontWithSize(source);
            DisplayFontSize = fontSize;
            LineSpacing = Math.Max(8, fontSize * 0.12);
            for (var i = 0; i < fitted.Count; i++)
            {
                var opacity = i == 0 ? 1.0 : 0.85;
                Lines.Add(new ProjectionLineItem(fitted[i], PrimaryBrush, FontWeight, TextAlignment, opacity));
            }
            return;
        }

        // Без переноса: одна исходная строка = одна визуальная, кегль под ширину
        DisplayFontSize = ComputeShrinkToFitFontSize(source);
        LineSpacing = Math.Max(8, DisplayFontSize * 0.12);
        for (var i = 0; i < source.Count; i++)
        {
            var opacity = i == 0 ? 1.0 : 0.85;
            Lines.Add(new ProjectionLineItem(source[i], PrimaryBrush, FontWeight, TextAlignment, opacity));
        }
    }

    private double ComputeShrinkToFitFontSize(IReadOnlyList<string> lines)
    {
        var maxWidth = LayoutMaxWidth;
        var captionReserve = EstimateReferenceCaptionReserve();
        var maxHeight = Math.Max(120, DesignHeight - 128 - captionReserve);
        double font = Math.Min(maxWidth, maxHeight);
        foreach (var line in lines)
        {
            var w = MeasureTextWidthWithFontSize(line, 100);
            if (w > 0.5)
            {
                font = Math.Min(font, maxWidth * 100.0 / w);
            }
        }

        var lineCount = Math.Max(1, lines.Count);
        var heightBudget = maxHeight / (lineCount * 1.32);
        return Math.Clamp(font, 12, heightBudget);
    }

    /// <summary>
    /// Высота, которую нужно оставить под подпись книги/главы/стиха, чтобы она не обрезалась.
    /// «После текста» — 0 (подпись уже в строках). Сверху/снизу экрана — фиксированный кегль.
    /// </summary>
    private double EstimateReferenceCaptionReserve()
    {
        if (!ShowBibleReference || string.IsNullOrWhiteSpace(ReferenceCaption))
        {
            return 0;
        }

        // Встроена в текст — учтена в MeasureLayoutHeight
        if (BibleReferencePlacement == BibleReferencePlacement.After)
        {
            return 0;
        }

        double font;
        double gap;
        if (BibleReferencePlacement.IsPinnedToScreenEdge())
        {
            // Фиксированная подпись у края — не зависит от кегля стиха
            font = EdgeReferenceFontSize;
            gap = 36;
        }
        else
        {
            // Над/под текстом — рядом с блоком, кегль от стиха
            font = Math.Max(22, Math.Min(DisplayFontSize, 160) * 0.38);
            gap = 28;
        }

        var captionHeight = MeasureTextHeightWithFontSize(ReferenceCaption, font, LayoutMaxWidth);
        return Math.Max(captionHeight + gap, font * 1.6 + gap);
    }

    /// <summary>
    /// Подбирает максимальный размер шрифта бинарным поиском и переносит слова
    /// только внутри исходных строк так, чтобы весь слайд поместился на экран.
    /// </summary>
    private List<string> LayoutSlideForMaxFont(IReadOnlyList<string> sourceLines)
    {
        var (layout, _) = LayoutSlideForMaxFontWithSize(sourceLines);
        return layout;
    }

    private (List<string> Layout, double FontSize) LayoutSlideForMaxFontWithSize(IReadOnlyList<string> sourceLines)
    {
        SyncDesignSurface();
        var availableWidth = LayoutMaxWidth;
        // Margin=64*2 + запас под подпись стиха, чтобы она не обрезалась
        var captionReserve = EstimateReferenceCaptionReserve();
        var availableHeight = Math.Max(120, (DesignHeight - 128 - captionReserve) * 0.94);

        var prepared = sourceLines
            .Select(l => (l ?? string.Empty).Trim())
            .Where(l => l.Length > 0)
            .Select(l => SplitIntoWords(l))
            .Where(w => w.Count > 0)
            .ToList();

        if (prepared.Count == 0)
        {
            return (sourceLines.ToList(), 100);
        }

        var longestWord = prepared.SelectMany(w => w).OrderByDescending(w => w.Length).First();
        double lo = 8;
        double hi = Math.Min(availableWidth, availableHeight);

        var wordWidthAt100 = MeasureTextWidthWithFontSize(longestWord, 100);
        if (wordWidthAt100 > 0)
        {
            hi = Math.Min(hi, availableWidth * 100.0 / wordWidthAt100);
        }

        List<string> bestLayout = WrapSourceLinesAtFontSize(prepared, availableWidth, lo);
        var bestSize = lo;

        for (var iter = 0; iter < 28; iter++)
        {
            var mid = (lo + hi) / 2.0;
            if (!TryLayoutAtFontSize(prepared, availableWidth, availableHeight, mid, out var layout))
            {
                hi = mid;
                continue;
            }

            bestSize = mid;
            bestLayout = layout;
            lo = mid;
        }

        // Финальная проверка реальными измерениями — если всё же не влезает, чуть уменьшаем кегль
        bestLayout = WrapSourceLinesAtFontSize(prepared, availableWidth, bestSize);
        var measured = MeasureLayoutHeight(bestLayout, bestSize, availableWidth);
        if (measured > availableHeight && measured > 0.5)
        {
            bestSize = Math.Max(8, bestSize * (availableHeight / measured) * 0.98);
            bestLayout = WrapSourceLinesAtFontSize(prepared, availableWidth, bestSize);
        }

        System.Diagnostics.Debug.WriteLine(
            $"LayoutSlideForMaxFont: bestFont={bestSize:F1}, lines={bestLayout.Count}, area={availableWidth:F0}x{availableHeight:F0}, measuredH={measured:F0}");

        return (bestLayout.Count > 0 ? bestLayout : sourceLines.ToList(), bestSize);
    }

    private bool TryLayoutAtFontSize(
        List<List<string>> preparedLines,
        double availableWidth,
        double availableHeight,
        double fontSize,
        out List<string> layout)
    {
        layout = WrapSourceLinesAtFontSize(preparedLines, availableWidth, fontSize);
        if (layout.Count == 0)
        {
            return false;
        }

        foreach (var line in layout)
        {
            if (MeasureTextWidthWithFontSize(line, fontSize) > availableWidth + 0.75)
            {
                return false;
            }
        }

        var totalHeight = MeasureLayoutHeight(layout, fontSize, availableWidth);
        return totalHeight <= availableHeight + 0.5;
    }

    /// <summary>
    /// Реальная высота блока строк (как в XAML), а не оценка 1.2*FontSize.
    /// </summary>
    private double MeasureLayoutHeight(IReadOnlyList<string> layout, double fontSize, double maxWidth)
    {
        if (layout.Count == 0)
        {
            return 0;
        }

        // В XAML Spacing="12" — в оценке берём не меньше, иначе при мелком кегле высота занижается и низ обрезается
        var spacing = Math.Max(12, fontSize * 0.12);
        double total = 0;
        for (var i = 0; i < layout.Count; i++)
        {
            total += MeasureTextHeightWithFontSize(layout[i], fontSize, maxWidth);
            if (i < layout.Count - 1)
            {
                total += spacing;
            }
        }

        return total;
    }

    private double MeasureTextHeightWithFontSize(string text, double fontSize, double maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return fontSize * 1.35;
        }

        var textBlock = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(FontFamilyName),
            FontSize = fontSize,
            FontWeight = FontWeight,
            TextWrapping = TextWrapping.NoWrap
        };

        textBlock.Measure(new Size(Math.Max(1, maxWidth), double.PositiveInfinity));
        var height = textBlock.DesiredSize.Height;
        // Запас: bold/кириллица часто выше «идеальной» метрики
        if (height < 0.5)
        {
            return fontSize * 1.4;
        }

        return height * 1.04;
    }

    private List<string> WrapSourceLinesAtFontSize(List<List<string>> preparedLines, double maxWidth, double fontSize)
    {
        var result = new List<string>();
        foreach (var words in preparedLines)
        {
            // Жадно заполняем всю доступную ширину — без «балансировки»,
            // которая дробила длинные стихи на узкую колонку коротких строк.
            var wrapped = GroupWordsIntoLinesWithStaticSize(words, maxWidth * 0.98, fontSize);
            if (wrapped.Count == 0)
            {
                result.Add(string.Join(" ", words));
            }
            else
            {
                result.AddRange(wrapped);
            }
        }

        return result;
    }

    /// <summary>
    /// Перераспределяет слова так, чтобы визуальные строки одной фразы были ближе по длине.
    /// </summary>
    private List<string> BalanceWrappedLines(List<string> words, List<string> greedyLines, double maxWidth, double fontSize)
    {
        if (words.Count <= 1 || greedyLines.Count <= 1)
        {
            return greedyLines;
        }

        var targetCount = greedyLines.Count;
        // Пробуем разбить на targetCount примерно равных по ширине строк
        var totalWidth = words.Sum(w => MeasureTextWidthWithFontSize(w, fontSize))
                         + Math.Max(0, words.Count - 1) * MeasureTextWidthWithFontSize(" ", fontSize);
        var targetWidth = Math.Min(maxWidth, totalWidth / targetCount);

        var balanced = new List<string>();
        var index = 0;
        for (var lineIndex = 0; lineIndex < targetCount; lineIndex++)
        {
            var remainingLines = targetCount - lineIndex;
            var remainingWords = words.Count - index;
            if (remainingWords <= 0)
            {
                break;
            }

            // Последняя строка забирает всё оставшееся
            if (remainingLines == 1)
            {
                balanced.Add(string.Join(" ", words.Skip(index)));
                break;
            }

            var line = new System.Text.StringBuilder();
            double width = 0;
            var spaceWidth = MeasureTextWidthWithFontSize(" ", fontSize);
            var wordsInLine = 0;

            while (index < words.Count)
            {
                // Оставляем хотя бы по одному слову на оставшиеся строки
                var wordsLeftAfter = words.Count - index - 1;
                var linesLeftAfter = remainingLines - 1;
                if (wordsInLine > 0 && wordsLeftAfter < linesLeftAfter)
                {
                    break;
                }

                var word = words[index];
                var wordWidth = MeasureTextWidthWithFontSize(word, fontSize);
                var needed = wordsInLine > 0 ? spaceWidth + wordWidth : wordWidth;
                var newWidth = width + needed;

                if (wordsInLine > 0 && newWidth > maxWidth)
                {
                    break;
                }

                // Для не-последних строк стараемся не сильно превышать целевую ширину
                if (wordsInLine > 0 && newWidth > targetWidth * 1.08 && newWidth > maxWidth * 0.55)
                {
                    break;
                }

                if (wordsInLine > 0)
                {
                    line.Append(' ');
                }

                line.Append(word);
                width = newWidth;
                wordsInLine++;
                index++;
            }

            if (wordsInLine == 0 && index < words.Count)
            {
                // Слово шире maxWidth — всё равно ставим его отдельно
                balanced.Add(words[index]);
                index++;
            }
            else if (wordsInLine > 0)
            {
                balanced.Add(line.ToString());
            }
        }

        return balanced.Count > 0 ? balanced : greedyLines;
    }

    /// <summary>
    /// Перенос по ширине экрана: длинная строка разбивается по словам,
    /// чтобы уместиться в доступную ширину при крупном шрифте (режим Holyrics слева).
    /// </summary>
    private List<string> WrapTextToWidth(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string> { text };
        }

        var trimmed = text.Trim();
        var words = SplitIntoWords(trimmed);
        if (words.Count <= 1)
        {
            return new List<string> { trimmed };
        }

        var windowWidth = _windowWidth > 0 ? _windowWidth : 1920;
        var windowHeight = _windowHeight > 0 ? _windowHeight : 1080;
        var aspectRatio = windowWidth / Math.Max(windowHeight, 1);

        double staticWidth;
        double staticFontSize;
        if (aspectRatio >= 1.7)
        {
            staticWidth = 960;
            staticFontSize = 50;
        }
        else if (aspectRatio >= 1.5)
        {
            staticWidth = 800;
            staticFontSize = 40;
        }
        else if (aspectRatio >= 1.2)
        {
            staticWidth = 600;
            staticFontSize = 30;
        }
        else
        {
            staticWidth = 500;
            staticFontSize = 25;
        }

        // Переносим, когда строка шире ~70% статической ширины — как на превью Holyrics
        var targetLineWidth = staticWidth * 0.70;
        return GroupWordsIntoLinesWithStaticSize(words, targetLineWidth, staticFontSize);
    }

    /// <summary>
    /// Разбивает текст на строки для максимального размера шрифта.
    /// Использует итеративный подход для решения проблемы циклической зависимости:
    /// Viewbox масштабирует контент, но мы не знаем масштаб, пока не знаем размер контента.
    /// </summary>
    private List<string> WrapTextForMaximumSize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string> { text };
        }

        var trimmed = text.Trim();
        
        // Разбиваем текст на слова (игнорируем знаки препинания)
        var words = SplitIntoWords(trimmed);
        
        if (words.Count == 0)
        {
            return new List<string> { trimmed };
        }
        
        // Используем реальный размер окна
        var windowWidth = _windowWidth > 0 ? _windowWidth : 1920;
        var windowHeight = _windowHeight > 0 ? _windowHeight : 1080;
        
        // Вычисляем доступную область с учетом отступов
        // Margin="64" в StackPanel, Spacing="12" между строками, Spacing="32" между элементами
        var margin = 128; // 64*2 для margin
        var availableWidth = windowWidth - margin;
        var availableHeight = windowHeight - margin;
        
        // Вычисляем aspect ratio окна
        var aspectRatio = (double)availableWidth / availableHeight;
        
        // СТАТИЧЕСКИЙ ПОДХОД: Работаем с фиксированными размерами для решения проблемы циклической зависимости
        // 1. Определяем aspect ratio по разрешению экрана
        // 2. Задаем статические размеры для работы алгоритма
        // 3. Разбиваем текст в статических размерах
        // 4. Viewbox автоматически масштабирует результат до реальных размеров
        
        // Определяем статические размеры в зависимости от aspect ratio
        double staticWidth, staticHeight, staticFontSize;
        
        if (aspectRatio >= 1.7) // 16:9 и шире (например, 1920x1080)
        {
            staticWidth = 960;
            staticHeight = 540;
            staticFontSize = 50; // Пропорция примерно 1:19 (960/50 ≈ 19)
        }
        else if (aspectRatio >= 1.5) // 16:10 (например, 1920x1200)
        {
            staticWidth = 800;
            staticHeight = 500;
            staticFontSize = 40; // Пропорция примерно 1:20
        }
        else if (aspectRatio >= 1.2) // 4:3 (например, 1024x768)
        {
            staticWidth = 600;
            staticHeight = 450;
            staticFontSize = 30; // Пропорция примерно 1:20
        }
        else // Квадратные и вертикальные (1:1 и меньше)
        {
            staticWidth = 500;
            staticHeight = 500;
            staticFontSize = 25; // Пропорция примерно 1:20
        }
        
        // Вычисляем доступную область в статических размерах (с учетом отступов)
        var staticMargin = staticWidth * 0.1; // 10% отступы
        var staticAvailableWidth = staticWidth - staticMargin * 2;
        var staticAvailableHeight = staticHeight - staticMargin * 2;
        
        // Измеряем текст со статическим размером шрифта
        var totalTextWidth = MeasureTextWidthWithFontSize(trimmed, staticFontSize);
        
        // Вычисляем оптимальное количество строк для заполнения статического экрана
        // Цель: минимизировать отступы по вертикали и горизонтали
        var estimatedLines = Math.Ceiling(totalTextWidth / staticAvailableWidth);
        var estimatedHeight = estimatedLines * staticFontSize + (estimatedLines - 1) * (staticFontSize * 0.12); // Spacing = 12% от размера шрифта
        
        // Корректируем количество строк, чтобы минимизировать отступы
        int optimalLines = (int)estimatedLines;
        var bestBalance = double.MaxValue;
        
        // Пробуем разное количество строк и выбираем то, которое лучше заполняет экран
        for (int testLines = Math.Max(1, (int)estimatedLines - 2); testLines <= Math.Min(words.Count, (int)estimatedLines + 3); testLines++)
        {
            var testLineWidth = totalTextWidth / testLines;
            var testHeight = testLines * staticFontSize + (testLines - 1) * (staticFontSize * 0.12);
            
            // Вычисляем отступы
            var horizontalPadding = Math.Abs(staticAvailableWidth - testLineWidth);
            var verticalPadding = Math.Abs(staticAvailableHeight - testHeight);
            
            // Оценка: меньше отступы = лучше
            var balance = horizontalPadding + verticalPadding;
            
            if (balance < bestBalance && testLineWidth <= staticAvailableWidth && testHeight <= staticAvailableHeight)
            {
                bestBalance = balance;
                optimalLines = testLines;
            }
        }
        
        // Вычисляем целевую ширину строки в статических размерах
        var targetLineWidth = staticAvailableWidth * 0.95; // 95% для запаса
        
        System.Diagnostics.Debug.WriteLine($"WrapTextForMaximumSize: aspectRatio={aspectRatio:F2}, staticSize={staticWidth}x{staticHeight}, staticFontSize={staticFontSize}, optimalLines={optimalLines}, targetLineWidth={targetLineWidth:F2}");
        
        // Группируем слова в строки, используя статические размеры
        var lines = GroupWordsIntoLinesWithStaticSize(words, targetLineWidth, staticFontSize);
        
        System.Diagnostics.Debug.WriteLine($"WrapTextForMaximumSize: итоговое количество строк={lines.Count}");
        
        return lines;
    }

    /// <summary>
    /// Разбивает текст на слова, сохраняя знаки препинания вместе со словами.
    /// </summary>
    private List<string> SplitIntoWords(string text)
    {
        var words = new List<string>();
        var currentWord = new System.Text.StringBuilder();
        
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (currentWord.Length > 0)
                {
                    words.Add(currentWord.ToString());
                    currentWord.Clear();
                }
            }
            else
            {
                currentWord.Append(ch);
            }
        }
        
        if (currentWord.Length > 0)
        {
            words.Add(currentWord.ToString());
        }
        
        return words;
    }

    /// <summary>
    /// Группирует слова в строки, используя статические размеры экрана.
    /// Работает с фиксированными размерами, что решает проблему циклической зависимости.
    /// </summary>
    private List<string> GroupWordsIntoLinesWithStaticSize(List<string> words, double maxLineWidth, double fontSize)
    {
        if (words.Count == 0)
        {
            return new List<string>();
        }
        
        var result = new List<string>();
        var currentLine = new System.Text.StringBuilder();
        double currentLineWidth = 0;
        var spaceWidth = MeasureTextWidthWithFontSize(" ", fontSize);
        
        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var wordWidth = MeasureTextWidthWithFontSize(word, fontSize);
            var spaceNeeded = currentLine.Length > 0 ? spaceWidth : 0;
            var newLineWidth = currentLineWidth + spaceNeeded + wordWidth;
            
            // Проверяем, поместится ли слово в текущую строку
            if (newLineWidth > maxLineWidth && currentLine.Length > 0)
            {
                // Слово не помещается - сохраняем текущую строку
                var lineText = currentLine.ToString();
                result.Add(lineText);
                System.Diagnostics.Debug.WriteLine($"GroupWordsIntoLinesWithStaticSize: Добавлена строка длиной {currentLineWidth:F2}px (макс {maxLineWidth:F2}px): '{lineText}'");
                
                // Начинаем новую строку с текущего слова
                currentLine.Clear();
                currentLine.Append(word);
                currentLineWidth = wordWidth;
            }
            else
            {
                // Слово помещается - добавляем к текущей строке
                if (currentLine.Length > 0)
                {
                    currentLine.Append(" ");
                }
                currentLine.Append(word);
                currentLineWidth = newLineWidth;
            }
        }
        
        // Добавляем оставшуюся строку
        if (currentLine.Length > 0)
        {
            var lineText = currentLine.ToString();
            result.Add(lineText);
            System.Diagnostics.Debug.WriteLine($"GroupWordsIntoLinesWithStaticSize: Добавлена последняя строка длиной {currentLineWidth:F2}px: '{lineText}'");
        }
        
        return result;
    }

    /// <summary>
    /// Группирует слова в строки с учетом реальной ширины текста.
    /// Использует измерение TextBlock для точного определения ширины.
    /// ВАЖНО: Если слово не помещается, оно переносится на новую строку, а не обрезается!
    /// </summary>
    private List<string> GroupWordsIntoLinesSimple(List<string> words, double maxWidth)
    {
        if (words.Count == 0)
        {
            return new List<string>();
        }
        
        var result = new List<string>();
        var currentLine = new System.Text.StringBuilder();
        double currentLineWidth = 0;
        var spaceWidth = MeasureTextWidth(" "); // Измеряем пробел один раз
        
        for (int i = 0; i < words.Count; i++)
        {
            var word = words[i];
            
            // Измеряем ширину слова
            var wordWidth = MeasureTextWidth(word);
            
            // Вычисляем ширину строки с добавлением нового слова (включая пробел)
            var spaceNeeded = currentLine.Length > 0 ? spaceWidth : 0;
            var newLineWidth = currentLineWidth + spaceNeeded + wordWidth;
            
            // Проверяем, поместится ли слово в текущую строку
            if (newLineWidth > maxWidth && currentLine.Length > 0)
            {
                // Слово не помещается в текущую строку
                // Сохраняем текущую строку и начинаем новую со слова
                var lineText = currentLine.ToString();
                result.Add(lineText);
                System.Diagnostics.Debug.WriteLine($"GroupWordsIntoLinesSimple: Добавлена строка длиной {currentLineWidth:F2}px (макс {maxWidth:F2}px): '{lineText}'");
                
                // Начинаем новую строку с текущего слова
                currentLine.Clear();
                currentLine.Append(word);
                currentLineWidth = wordWidth;
            }
            else
            {
                // Слово помещается - добавляем его к текущей строке
                if (currentLine.Length > 0)
                {
                    currentLine.Append(" ");
                }
                currentLine.Append(word);
                currentLineWidth = newLineWidth;
            }
        }
        
        // Добавляем оставшуюся строку
        if (currentLine.Length > 0)
        {
            var lineText = currentLine.ToString();
            result.Add(lineText);
            System.Diagnostics.Debug.WriteLine($"GroupWordsIntoLinesSimple: Добавлена последняя строка длиной {currentLineWidth:F2}px: '{lineText}'");
        }
        
        return result;
    }

    /// <summary>
    /// Измеряет реальную ширину текста с учетом текущего шрифта.
    /// </summary>
    private double MeasureTextWidth(string text)
    {
        return MeasureTextWidthWithFontSize(text, 100); // Базовый размер шрифта (как в XAML)
    }

    /// <summary>
    /// Измеряет реальную ширину текста с заданным размером шрифта.
    /// </summary>
    private double MeasureTextWidthWithFontSize(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        
        // Создаем TextBlock для измерения
        var textBlock = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(FontFamilyName),
            FontSize = fontSize,
            FontWeight = FontWeight
        };
        
        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var width = textBlock.DesiredSize.Width;
        // Если Measure не дал ширину (шрифт/поток) — оценка по длине, иначе перенос «ломается»
        if (width < 0.5)
        {
            return Math.Max(text.Length, 1) * fontSize * 0.55;
        }

        return width;
    }

    /// <summary>
    /// Постобработка строк: объединяет слишком короткие строки и разбивает слишком длинные.
    /// Использует реальную ширину текста для измерений.
    /// </summary>
    private List<string> PostProcessLinesByWidth(List<string> lines, double targetLineWidth, double maxWidth)
    {
        if (lines.Count <= 1)
        {
            return lines;
        }
        
        var result = new List<string>();
        var i = 0;
        
        while (i < lines.Count)
        {
            var currentLine = lines[i];
            var currentWidth = MeasureTextWidth(currentLine);
            
            // Если строка слишком короткая (< 50% целевой ширины), пытаемся объединить со следующей
            if (currentWidth < targetLineWidth * 0.5 && i < lines.Count - 1)
            {
                var nextLine = lines[i + 1];
                var mergedText = currentLine + " " + nextLine;
                var mergedWidth = MeasureTextWidth(mergedText);
                
                // Если объединенная строка не превысит максимум, объединяем
                if (mergedWidth <= maxWidth)
                {
                    result.Add(mergedText);
                    i += 2; // Пропускаем обе строки
                    continue;
                }
            }
            
            // Если строка слишком длинная (> 105% максимальной ширины), пытаемся разбить
            if (currentWidth > maxWidth * 1.05)
            {
                // Разбиваем строку на слова и перераспределяем
                var lineWords = currentLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (lineWords.Length > 1)
                {
                    // Разбиваем на две части примерно пополам
                    var midPoint = lineWords.Length / 2;
                    var firstPart = string.Join(" ", lineWords.Take(midPoint));
                    var secondPart = string.Join(" ", lineWords.Skip(midPoint));
                    
                    if (!string.IsNullOrEmpty(firstPart) && !string.IsNullOrEmpty(secondPart))
                    {
                        result.Add(firstPart);
                        // Вставляем вторую часть для следующей итерации
                        lines.Insert(i + 1, secondPart);
                        i++;
                        continue;
                    }
                }
                else
                {
                    // Если это одно длинное слово, которое не помещается, оставляем как есть
                    // (оно уже должно было быть обработано в GroupWordsIntoLinesSimple)
                    System.Diagnostics.Debug.WriteLine($"PostProcessLinesByWidth: Предупреждение - очень длинное слово '{currentLine}' ({currentWidth:F2}px) не помещается в {maxWidth:F2}px");
                }
            }
            
            // Оставляем строку как есть
            result.Add(currentLine);
            i++;
        }
        
        return result;
    }

    /// <summary>
    /// Перераспределяет строки, разбивая длинные на более короткие для достижения целевого количества.
    /// </summary>
    private List<string> RedistributeLines(List<string> lines, int targetLines, int targetLineLength)
    {
        if (lines.Count >= targetLines)
        {
            return lines;
        }
        
        var result = new List<string>(lines);
        
        // Пока не достигнем целевого количества строк
        while (result.Count < targetLines && result.Count > 0)
        {
            // Находим самую длинную строку
            var longestIndex = 0;
            var longestLength = 0;
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Length > longestLength)
                {
                    longestLength = result[i].Length;
                    longestIndex = i;
                }
            }
            
            // Если самая длинная строка недостаточно длинная для разбиения, прекращаем
            if (longestLength < targetLineLength * 0.6)
            {
                break;
            }
            
            var longestLine = result[longestIndex];
            
            // Пытаемся разбить её пополам по знакам препинания или по середине
            // Используем реальную ширину для поиска точки разрыва
            var longestLineWidth = MeasureTextWidth(longestLine);
            var splitIndex = FindBestSplitPointByWidth(longestLine, longestLineWidth / 2);
            
            if (splitIndex > 0 && splitIndex < longestLine.Length)
            {
                var firstPart = longestLine.Substring(0, splitIndex).Trim();
                var secondPart = longestLine.Substring(splitIndex).Trim();
                
                if (!string.IsNullOrEmpty(firstPart) && !string.IsNullOrEmpty(secondPart))
                {
                    result[longestIndex] = firstPart;
                    result.Insert(longestIndex + 1, secondPart);
                }
                else
                {
                    // Если не удалось разбить, прекращаем попытки
                    break;
                }
            }
            else
            {
                // Если не удалось найти точку разрыва, прекращаем попытки
                break;
            }
        }
        
        return result;
    }

    /// <summary>
    /// Объединяет короткие строки для уменьшения общего количества строк.
    /// </summary>
    private List<string> MergeShortLines(List<string> lines, int targetLineLength)
    {
        if (lines.Count <= 1)
        {
            return lines;
        }
        
        var result = new List<string>();
        var i = 0;
        
        while (i < lines.Count)
        {
            var currentLine = lines[i];
            var currentLength = currentLine.Length;
            
            // Если текущая строка достаточно длинная, оставляем как есть
            if (currentLength >= targetLineLength * 0.7)
            {
                result.Add(currentLine);
                i++;
            }
            else
            {
                // Пытаемся объединить с следующей строкой
                var merged = currentLine;
                var j = i + 1;
                
                while (j < lines.Count && merged.Length + 1 + lines[j].Length <= targetLineLength * 1.2)
                {
                    merged += " " + lines[j];
                    j++;
                }
                
                result.Add(merged);
                i = j;
            }
        }
        
        return result;
    }

    /// <summary>
    /// Находит лучшую точку для разрыва строки на основе реальной ширины текста.
    /// </summary>
    private int FindBestSplitPointByWidth(string line, double targetWidth)
    {
        // Ищем пробел около середины строки по ширине
        var bestIndex = line.Length / 2;
        var bestScore = double.MaxValue;
        
        // Измеряем ширину всей строки
        var totalWidth = MeasureTextWidth(line);
        var targetPosition = (int)(line.Length * (targetWidth / totalWidth));
        
        // Ищем пробел около целевой позиции
        var searchRange = Math.Max(5, line.Length / 4);
        for (int i = Math.Max(0, targetPosition - searchRange); i < Math.Min(line.Length, targetPosition + searchRange); i++)
        {
            if (i < line.Length && char.IsWhiteSpace(line[i]))
            {
                // Измеряем ширину части строки до этого пробела
                var partWidth = MeasureTextWidth(line.Substring(0, i));
                var distance = Math.Abs(partWidth - targetWidth);
                if (distance < bestScore)
                {
                    bestScore = distance;
                    bestIndex = i + 1; // Разрываем после пробела
                }
            }
        }
        
        return bestIndex;
    }

    /// <summary>
    /// Разбивает длинную строку на более короткие по словам.
    /// Использует эвристику: примерно 8-12 слов на строку для оптимального размера шрифта.
    /// </summary>
    private List<string> SplitLongLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return new List<string> { line };
        }

        var trimmed = line.Trim();
        var words = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        // Если слов мало, возвращаем как есть
        if (words.Length <= 6)
        {
            return new List<string> { trimmed };
        }

        // Разбиваем на строки по 6-8 слов
        var result = new List<string>();
        var wordsPerLine = Math.Max(6, words.Length / 2); // Примерно половина слов на строку

        for (int i = 0; i < words.Length; i += wordsPerLine)
        {
            var lineWords = words.Skip(i).Take(wordsPerLine);
            var lineText = string.Join(" ", lineWords).Trim();
            if (!string.IsNullOrEmpty(lineText))
            {
                result.Add(lineText);
            }
        }

        return result;
    }

    partial void OnIsBlackoutChanged(bool value)
    {
        OnPropertyChanged(nameof(ContentOpacity));
        OnPropertyChanged(nameof(BlackoutOpacity));
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return CreateBrush("#000000");
        }

        var cleaned = hex.Trim();
        if (!cleaned.StartsWith('#'))
        {
            cleaned = $"#{cleaned}";
        }

        if (cleaned.Length is not (7 or 9))
        {
            cleaned = "#000000";
        }

        var hasAlpha = cleaned.Length == 9;
        var alpha = hasAlpha
            ? byte.Parse(cleaned.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : (byte)255;
        var startIndex = hasAlpha ? 3 : 1;
        var red = byte.Parse(cleaned.Substring(startIndex, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var green = byte.Parse(cleaned.Substring(startIndex + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var blue = byte.Parse(cleaned.Substring(startIndex + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        var color = Color.FromArgb(alpha, red, green, blue);
        return new SolidColorBrush(color);
    }
}

public sealed partial class ProjectionLineItem : ObservableObject
{
    public ProjectionLineItem(string text, SolidColorBrush foreground, FontWeight fontWeight, string textAlignment, double opacity)
    {
        this.text = text;
        this.foreground = foreground;
        this.fontWeight = fontWeight;
        this.textAlignment = textAlignment;
        this.opacity = opacity;
    }

    [ObservableProperty]
    private string text;

    [ObservableProperty]
    private SolidColorBrush foreground;

    [ObservableProperty]
    private FontWeight fontWeight;

    [ObservableProperty]
    private string textAlignment;

    [ObservableProperty]
    private double opacity;
}


