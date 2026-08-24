using System.Text.Json;
using ChyguiSlide.Data;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using ChyguiSlide.ViewModels;
using Microsoft.UI.Dispatching;

namespace ChyguiSlide.Services.Implementations;

/// <summary>
/// Дублирует payload проекции в LAN-выход OBS (без фона).
/// </summary>
public sealed class ObsProjectionBridge : IDisposable
{
    private readonly IObsStreamService _obsStream;
    private readonly IProjectionStateService _projectionState;
    private readonly ProjectionDisplayViewModel _viewModel;
    private readonly DispatcherQueue _dispatcher;

    private bool _disposed;
    private bool _slideDebounceQueued;

    public ObsProjectionBridge(
        IObsStreamService obsStream,
        IProjectionStateService projectionState,
        ProjectionDisplayViewModel viewModel)
    {
        _obsStream = obsStream;
        _projectionState = projectionState;
        _viewModel = viewModel;
        _dispatcher = App.MainDispatcherQueue
            ?? DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("DispatcherQueue недоступен для ObsProjectionBridge.");

        _viewModel.PropertyChanged += OnPropertyChanged;
        _viewModel.Lines.CollectionChanged += OnLinesChanged;
        _projectionState.StateChanged += OnProjectionStateChanged;
        InteractionLogger.Log("[ObsStream] Projection bridge attached to ViewModel");
    }

    private void OnProjectionStateChanged(object? sender, ProjectionState state) => QueueSlideUpdate();

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ProjectionDisplayViewModel.ReferenceCaption):
            case nameof(ProjectionDisplayViewModel.ShowBibleReference):
            case nameof(ProjectionDisplayViewModel.BibleReferencePlacement):
            case nameof(ProjectionDisplayViewModel.BibleReferenceAlignment):
                QueueSlideUpdate();
                break;
            case nameof(ProjectionDisplayViewModel.TransitionStyle):
                SendTransitionStyle();
                break;
            case nameof(ProjectionDisplayViewModel.SectionTransitionDurationMs):
                SendTransitionDuration();
                break;
            case nameof(ProjectionDisplayViewModel.PrimaryBrush):
            case nameof(ProjectionDisplayViewModel.FontFamilyName):
            case nameof(ProjectionDisplayViewModel.TextOutlineBrush):
            case nameof(ProjectionDisplayViewModel.TextOutlineThickness):
            case nameof(ProjectionDisplayViewModel.TextOutlineOpacity):
                SendThemeUpdate();
                break;
        }
    }

    private void OnLinesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => QueueSlideUpdate();

    private void QueueSlideUpdate()
    {
        if (_disposed || _slideDebounceQueued)
        {
            return;
        }

        _slideDebounceQueued = true;
        _ = _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _slideDebounceQueued = false;
            if (!_disposed)
            {
                SendSlideUpdate();
            }
        });
    }

    private void SendSlideUpdate()
    {
        var state = _projectionState.Current;
        if (ShouldClearObs(state))
        {
            SendClearSlide();
            return;
        }

        var text = BuildObsText(state);
        var (backdropEnabled, backdropOpacity) = _obsStream.GetBackdropSettings();
        SendMessage(new
        {
            type = "updateSlide",
            text,
            showBibleReference = false,
            backdropEnabled,
            backdropOpacity,
            primaryColor = GetCssColorFromBrush(_viewModel.PrimaryBrush),
            maxFontSize = Math.Max(_viewModel.DisplayFontSize, 200),
            fontFamily = _viewModel.FontFamilyName,
            fontWeight = (int)_viewModel.FontWeight.Weight,
            textAlignment = _viewModel.TextAlignment,
            textOutlineColor = GetCssColorFromBrush(_viewModel.TextOutlineBrush),
            textOutlineThickness = _viewModel.TextOutlineThickness,
            textOutlineOpacity = _viewModel.TextOutlineOpacity
        });
    }

    private static bool ShouldClearObs(ProjectionState state) =>
        state.ContentKind == ProjectionContentKind.Announcement
        || state.SongId is null
        || state.VisibleLines.Count == 0;

    private void SendClearSlide()
    {
        SendMessage(new
        {
            type = "updateSlide",
            text = string.Empty,
            showBibleReference = false,
            backdropEnabled = false
        });
    }

    private string BuildObsText(ProjectionState state)
    {
        var text = JoinSourceLines(state.VisibleLines);

        if (string.IsNullOrWhiteSpace(state.ReferenceCaption))
        {
            return text;
        }

        var caption = state.ReferenceCaption.Trim();

        if (state.ContentKind == ProjectionContentKind.Bible)
        {
            return string.IsNullOrEmpty(text) ? caption : $"{text} {caption}";
        }

        if (!_viewModel.ShowBibleReference)
        {
            return text;
        }

        return _viewModel.BibleReferencePlacement is BibleReferencePlacement.Below
            or BibleReferencePlacement.BottomOfScreen
            or BibleReferencePlacement.After
            ? string.IsNullOrEmpty(text) ? caption : $"{text} {caption}"
            : string.IsNullOrEmpty(text) ? caption : $"{caption} {text}";
    }

    private static string JoinSourceLines(IReadOnlyList<string> lines)
    {
        return string.Join(
            " ",
            lines
                .Select(line => line?.Trim())
                .Where(line => !string.IsNullOrEmpty(line)));
    }

    private void SendTransitionStyle()
    {
        SendMessage(new
        {
            type = "setTransitionStyle",
            style = _viewModel.TransitionStyle.ToString()
        });
    }

    private void SendTransitionDuration()
    {
        var ms = _viewModel.SectionTransitionDurationMs;
        if (ms <= 0)
        {
            ms = 750;
        }

        SendMessage(new
        {
            type = "setTransitionDuration",
            durationMs = ms
        });
    }

    private void SendThemeUpdate()
    {
        SendMessage(new
        {
            type = "updateTheme",
            primaryColor = GetCssColorFromBrush(_viewModel.PrimaryBrush),
            fontSize = _viewModel.DisplayFontSize,
            fontFamily = _viewModel.FontFamilyName,
            fontWeight = (int)_viewModel.FontWeight.Weight,
            textAlignment = _viewModel.TextAlignment,
            lineSpacing = _viewModel.LineSpacing > 0 ? _viewModel.LineSpacing : 12,
            textOutlineColor = GetCssColorFromBrush(_viewModel.TextOutlineBrush),
            textOutlineThickness = _viewModel.TextOutlineThickness,
            textOutlineOpacity = _viewModel.TextOutlineOpacity
        });
    }

    private void SendMessage(object message)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(message);
            _obsStream.BroadcastJson(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ObsProjectionBridge] Serialize failed: {ex.Message}");
        }
    }

    private static string? GetCssColorFromBrush(Microsoft.UI.Xaml.Media.Brush? brush)
    {
        if (brush is Microsoft.UI.Xaml.Media.SolidColorBrush solid)
        {
            return $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}";
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnPropertyChanged;
        _viewModel.Lines.CollectionChanged -= OnLinesChanged;
        _projectionState.StateChanged -= OnProjectionStateChanged;
    }
}
