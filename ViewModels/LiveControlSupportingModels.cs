using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;

namespace ChyguiSlide.ViewModels;

public sealed record LiveQueueEntry(Guid PlaylistId, string Title, DateTime? ScheduledAt, string Subtitle, IReadOnlyList<PlaylistEntry> Entries, ThemePreset? ThemePreset)
{
    public int SongsCount => Entries.Count;
    public string ScheduleLabel => ScheduledAt.HasValue
        ? ScheduledAt.Value.ToString("dddd, dd MMMM HH:mm", CultureInfo.CurrentCulture)
        : Subtitle;
}

public sealed partial class LiveSectionItem : ObservableObject
{
    public LiveSectionItem() : this(0, string.Empty, string.Empty, null)
    {
    }

    public LiveSectionItem(int index, string title, string content, string? notes)
    {
        Index = index;
        Title = title;
        Content = content;
        Notes = notes;
    }

    public int Index { get; }

    [ObservableProperty]
    private string title;

    /// <summary>Полный текст секции (как в каталоге песен).</summary>
    [ObservableProperty]
    private string content;

    [ObservableProperty]
    private string? notes;

    [ObservableProperty]
    private bool isCurrent;

    public string DisplayOrder => (Index + 1).ToString("00", CultureInfo.CurrentCulture);

    /// <summary>Короткий превью для совместимости (первая строка).</summary>
    public string Preview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Content))
            {
                return "(пустая секция)";
            }

            var firstLine = Content
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return "(пустая секция)";
            }

            return firstLine.Length <= 80 ? firstLine : $"{firstLine[..77]}...";
        }
    }
}

internal sealed record SectionSnapshot
{
    private static readonly Dictionary<SectionType, string> SectionNames = new()
    {
        { SectionType.Verse, "Куплет" },
        { SectionType.Chorus, "Припев" }
    };

    public SectionSnapshot(SongSection section)
    {
        Index = section.Order;
        Title = BuildTitle(section);
        Content = string.IsNullOrWhiteSpace(section.Content) ? string.Empty : section.Content.Trim();
        Notes = section.Notes;
    }

    public int Index { get; }
    public string Title { get; }
    public string Content { get; }
    public string? Notes { get; }

    private static string BuildTitle(SongSection section)
    {
        // Как в разделе «Песни»: показываем Heading секции
        if (!string.IsNullOrWhiteSpace(section.Heading))
        {
            return section.Heading.Trim();
        }

        return SectionNames.TryGetValue(section.SectionType, out var name)
            ? name
            : "Секция";
    }
}
