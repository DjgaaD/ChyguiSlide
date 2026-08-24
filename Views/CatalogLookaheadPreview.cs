using ChyguiSlide.Controls;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Implementations;
using ChyguiSlide.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChyguiSlide.Views;

/// <summary>
/// Предпросмотр слайда в каталоге: выбранная тема и текст секции, без живого проектора.
/// Рендер — тот же HTML/CSS, что на экране.
/// </summary>
internal sealed class CatalogLookaheadPreview
{
    private readonly WebProjectionPreview _preview;
    private readonly TextBlock _idleHint;
    private readonly ProjectionDisplayViewModel _viewModel;
    private readonly IDisplaySettingsService _displaySettings;
    private readonly ICatalogService _catalog;
    private Guid? _appliedThemeId;

    public CatalogLookaheadPreview(WebProjectionPreview preview, TextBlock idleHint)
    {
        _preview = preview;
        _idleHint = idleHint;
        _displaySettings = App.AppHost.Services.GetRequiredService<IDisplaySettingsService>();
        _catalog = App.AppHost.Services.GetRequiredService<ICatalogService>();
        var media = App.AppHost.Services.GetRequiredService<IThemeBackgroundMediaService>();
        _viewModel = new ProjectionDisplayViewModel(
            new DetachedProjectionStateService(),
            _displaySettings,
            media,
            bindLiveState: false);
        _preview.SetInstantSlides(true);
        _preview.BindViewModel(_viewModel);
    }

    public Task ShowAsync(Song? song, CatalogSectionPreviewItem? section)
        => ShowContentAsync(song?.Title, section?.Content);

    public async Task ShowContentAsync(string? title, string? content, string? referenceCaption = null)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            _preview.Visibility = Visibility.Collapsed;
            _idleHint.Visibility = Visibility.Visible;
            return;
        }

        _preview.Visibility = Visibility.Visible;
        _idleHint.Visibility = Visibility.Collapsed;

        ThemePreset? theme = null;
        try
        {
            var themeId = await _displaySettings.GetSelectedThemePresetIdAsync();
            if (themeId.HasValue)
            {
                theme = await _catalog.GetThemePresetAsync(themeId.Value);
            }
        }
        catch
        {
            theme = null;
        }

        if (_appliedThemeId != theme?.Id)
        {
            _appliedThemeId = null;
        }

        var lines = SplitLines(content);
        _viewModel.ApplyLookahead(lines, title, theme, referenceCaption);
        _appliedThemeId = theme?.Id;
    }

    private static IReadOnlyList<string> SplitLines(string content)
    {
        return content
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }
}
