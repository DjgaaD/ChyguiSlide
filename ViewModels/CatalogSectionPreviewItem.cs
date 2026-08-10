using CommunityToolkit.Mvvm.ComponentModel;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;

namespace ChyguiSlide.ViewModels;

/// <summary>Секция в панели деталей каталога с выделением как на Трансляции.</summary>
public sealed partial class CatalogSectionPreviewItem : ObservableObject
{
    public CatalogSectionPreviewItem(int listIndex, SongSection section)
    {
        ListIndex = listIndex;
        Section = section;
        Title = BuildTitle(section);
        Content = string.IsNullOrWhiteSpace(section.Content) ? string.Empty : section.Content.Trim();
    }

    public int ListIndex { get; }
    public SongSection Section { get; }
    public string Title { get; }
    public string Content { get; }

    [ObservableProperty]
    private bool isSelected;

    private static string BuildTitle(SongSection section)
    {
        if (!string.IsNullOrWhiteSpace(section.Heading))
        {
            return section.Heading.Trim();
        }

        return section.SectionType switch
        {
            SectionType.Chorus => "Припев",
            SectionType.Verse => "Куплет",
            _ => "Секция"
        };
    }
}
