using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Abstractions;

public interface IProjectionStateService
{
    ProjectionState Current { get; }
    event EventHandler<ProjectionState>? StateChanged;

    void SetSong(
        Guid songId,
        string songTitle,
        IReadOnlyList<string> sections,
        int initialSectionIndex = 0,
        IReadOnlyList<string?>? sectionCaptions = null);
    void SetPlaylistContext(Guid? playlistId);
    void AdvanceSection();
    void PreviousSection();
    void GoToSection(int index);
    void SetLinesOverride(IReadOnlyList<string>? lines);
    void ClearLinesOverride();
    void Clear();
}

