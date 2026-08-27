using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Implementations;

/// <summary>Состояние проекции-заглушка для отдельного предпросмотра каталога.</summary>
internal sealed class DetachedProjectionStateService : IProjectionStateService
{
    public ProjectionState Current => ProjectionState.Empty;

    public event EventHandler<ProjectionState>? StateChanged
    {
        add { }
        remove { }
    }

    public void SetSong(
        Guid songId,
        string songTitle,
        IReadOnlyList<string> sections,
        int initialSectionIndex = 0,
        IReadOnlyList<string?>? sectionCaptions = null,
        ProjectionContentKind contentKind = ProjectionContentKind.Song)
    {
    }

    public void SetMedia(string mediaPath, string title, Guid? songId = null)
    {
    }

    public void SetPlaylistContext(Guid? playlistId)
    {
    }

    public void AdvanceSection()
    {
    }

    public void PreviousSection()
    {
    }

    public void GoToSection(int index)
    {
    }

    public void SetLinesOverride(IReadOnlyList<string>? lines)
    {
    }

    public void ClearLinesOverride()
    {
    }

    public void Clear()
    {
    }
}
