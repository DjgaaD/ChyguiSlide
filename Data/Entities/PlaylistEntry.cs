using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace ChyguiSlide.Data.Entities;

public class PlaylistEntry : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PlaylistId { get; set; }
    public Playlist Playlist { get; set; } = null!;

    public Guid SongId { get; set; }
    public Song Song { get; set; } = null!;

    public Guid? AttachmentId { get; set; }
    public Attachment? Attachment { get; set; }

    [Range(0, int.MaxValue)]
    public int Order { get; set; }

    public int? TransposeSteps { get; set; }
    public int? TempoOverride { get; set; }

    [MaxLength(512)]
    public string? Cues { get; set; }

    private bool _wasPlayed;

    /// <summary>Уже запускали в текущей сессии быстрого плейлиста (только UI).</summary>
    [NotMapped]
    public bool WasPlayed
    {
        get => _wasPlayed;
        set
        {
            if (_wasPlayed == value)
            {
                return;
            }

            _wasPlayed = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Путь к медиафайлу быстрого плейлиста (фото/mp4). Не сохраняется в БД.</summary>
    [NotMapped]
    public string? MediaPath { get; set; }

    [NotMapped]
    public bool IsMediaItem => !string.IsNullOrWhiteSpace(MediaPath);

    /// <summary>Заголовок в UI быстрого плейлиста (для медиа — переименовывается в программе).</summary>
    [NotMapped]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Song?.Title))
            {
                return Song.Title;
            }

            if (!string.IsNullOrWhiteSpace(MediaPath))
            {
                return System.IO.Path.GetFileName(MediaPath);
            }

            return "Без названия";
        }
        set
        {
            if (Song is null)
            {
                return;
            }

            var next = string.IsNullOrWhiteSpace(value) ? Song.Title : value.Trim();
            if (string.Equals(Song.Title, next, StringComparison.Ordinal))
            {
                return;
            }

            Song.Title = next;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
