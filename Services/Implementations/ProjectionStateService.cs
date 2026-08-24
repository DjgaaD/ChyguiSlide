using System.Collections.Immutable;
using System.Linq;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Implementations;

public class ProjectionStateService : IProjectionStateService
{
    private readonly object _stateLock = new();

    private Guid? _songId;
    private string? _songTitle;
    private Guid? _playlistId;
    private int _sectionIndex;
    private IReadOnlyList<string> _sections = Array.Empty<string>();
    private IReadOnlyList<string?> _sectionCaptions = Array.Empty<string?>();
    private ProjectionContentKind _contentKind = ProjectionContentKind.Song;
    private ProjectionState _current = ProjectionState.Empty;
    private IReadOnlyList<string>? _linesOverride;

    public ProjectionState Current
    {
        get
        {
            lock (_stateLock)
            {
                return _current;
            }
        }
    }

    public event EventHandler<ProjectionState>? StateChanged;

    public void SetSong(
        Guid songId,
        string songTitle,
        IReadOnlyList<string> sections,
        int initialSectionIndex = 0,
        IReadOnlyList<string?>? sectionCaptions = null,
        ProjectionContentKind contentKind = ProjectionContentKind.Song)
    {
        lock (_stateLock)
        {
            var nextIndex = Math.Clamp(initialSectionIndex, 0, Math.Max(sections.Count - 1, 0));
            IReadOnlyList<string> nextSections = sections.Count > 0
                ? sections.ToImmutableList()
                : Array.Empty<string>();
            IReadOnlyList<string?> nextCaptions = sectionCaptions is { Count: > 0 }
                ? sectionCaptions.ToImmutableList()
                : Array.Empty<string?>();

            // Тот же контент — не публикуем StateChanged (иначе RefreshLines рядом с MediaPlayer → мерцание)
            if (_songId == songId
                && _sectionIndex == nextIndex
                && _linesOverride is null
                && _contentKind == contentKind
                && string.Equals(_songTitle, songTitle, StringComparison.Ordinal)
                && SectionsEqual(_sections, nextSections)
                && CaptionsEqual(_sectionCaptions, nextCaptions))
            {
                return;
            }

            _songId = songId;
            _songTitle = songTitle;
            _sectionIndex = nextIndex;
            _sections = nextSections;
            _sectionCaptions = nextCaptions;
            _contentKind = contentKind;
            _linesOverride = null;

            PublishState();
        }
    }

    private static bool SectionsEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CaptionsEqual(IReadOnlyList<string?> a, IReadOnlyList<string?> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public void SetPlaylistContext(Guid? playlistId)
    {
        lock (_stateLock)
        {
            if (_playlistId == playlistId)
            {
                return;
            }

            _playlistId = playlistId;
            PublishState();
        }
    }

    public void AdvanceSection()
    {
        lock (_stateLock)
        {
            if (_sections.Count == 0)
            {
                return;
            }

            if (_sectionIndex < _sections.Count - 1)
            {
                _sectionIndex++;
                _linesOverride = null;
                PublishState();
            }
        }
    }

    public void PreviousSection()
    {
        lock (_stateLock)
        {
            if (_sections.Count == 0)
            {
                return;
            }

            if (_sectionIndex > 0)
            {
                _sectionIndex--;
                _linesOverride = null;
                PublishState();
            }
        }
    }

    public void GoToSection(int index)
    {
        System.Diagnostics.Debug.WriteLine($"[ProjectionStateService] GoToSection: index={index}, _sections.Count={_sections.Count}, _sectionIndex={_sectionIndex}");
        ChyguiSlide.Data.InteractionLogger.Log($"ProjectionStateService.GoToSection: index={index}, _sections.Count={_sections.Count}, _sectionIndex={_sectionIndex}");

        lock (_stateLock)
        {
            if (_sections.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectionStateService] GoToSection: _sections.Count is 0, returning");
                ChyguiSlide.Data.InteractionLogger.Log($"ProjectionStateService.GoToSection: _sections.Count is 0, returning");
                return;
            }

            index = Math.Clamp(index, 0, _sections.Count - 1);
            System.Diagnostics.Debug.WriteLine($"[ProjectionStateService] GoToSection: clamped index={index}, _sectionIndex={_sectionIndex}");
            ChyguiSlide.Data.InteractionLogger.Log($"ProjectionStateService.GoToSection: clamped index={index}, _sectionIndex={_sectionIndex}");

            // Убрали проверку index == _sectionIndex, потому что
            // UpdateSectionsHighlight может обновить _sectionIndex до вызова GoToSection
            // и тогда PublishState не будет вызван, хотя содержимое на проекторе нужно обновить

            System.Diagnostics.Debug.WriteLine($"[ProjectionStateService] GoToSection: setting _sectionIndex={index}");
            ChyguiSlide.Data.InteractionLogger.Log($"ProjectionStateService.GoToSection: setting _sectionIndex={index}");
            _sectionIndex = index;
            _linesOverride = null;
            System.Diagnostics.Debug.WriteLine($"[ProjectionStateService] GoToSection: calling PublishState");
            ChyguiSlide.Data.InteractionLogger.Log($"ProjectionStateService.GoToSection: calling PublishState");
            PublishState();
        }
    }

    public void Clear()
    {
        lock (_stateLock)
        {
            _songId = null;
            _songTitle = null;
            _sections = Array.Empty<string>();
            _sectionCaptions = Array.Empty<string?>();
            _sectionIndex = 0;
            _contentKind = ProjectionContentKind.Song;
            _linesOverride = null;
            PublishState();
        }
    }

    public void SetLinesOverride(IReadOnlyList<string>? lines)
    {
        lock (_stateLock)
        {
            _linesOverride = lines?.ToImmutableList();
            PublishState();
        }
    }

    public void ClearLinesOverride()
    {
        lock (_stateLock)
        {
            if (_linesOverride is null)
            {
                return;
            }

            _linesOverride = null;
            PublishState();
        }
    }

    private void PublishState()
    {
        var lines = BuildVisibleLines();
        string? caption = null;
        if (_sectionIndex >= 0 && _sectionIndex < _sectionCaptions.Count)
        {
            caption = _sectionCaptions[_sectionIndex];
        }

        System.Diagnostics.Debug.WriteLine($"[ProjectionStateService] PublishState: _songId={_songId}, _sectionIndex={_sectionIndex}, lines.Count={lines.Count}");
        ChyguiSlide.Data.InteractionLogger.Log($"ProjectionStateService.PublishState: _songId={_songId}, _sectionIndex={_sectionIndex}, lines.Count={lines.Count}, caption={caption}");

        _current = new ProjectionState(
            _songId,
            _playlistId,
            _songTitle,
            _sectionIndex,
            lines,
            DateTimeOffset.UtcNow,
            caption,
            _contentKind);

        System.Diagnostics.Debug.WriteLine($"[ProjectionStateService] PublishState: invoking StateChanged");
        ChyguiSlide.Data.InteractionLogger.Log($"ProjectionStateService.PublishState: invoking StateChanged");
        StateChanged?.Invoke(this, _current);
    }

    private IReadOnlyList<string> BuildVisibleLines()
    {
        if (_linesOverride is not null)
        {
            return _linesOverride;
        }

        if (_sections.Count == 0 || _sectionIndex < 0 || _sectionIndex >= _sections.Count)
        {
            return Array.Empty<string>();
        }

        var current = _sections[_sectionIndex];
        var lines = current
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        return lines.Length > 0 ? lines : Array.Empty<string>();
    }
}
