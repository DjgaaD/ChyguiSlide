using System;
using ChyguiSlide.Data.Entities;

namespace ChyguiSlide.ViewModels;

public enum CollectionFilterKind
{
    All,
    Specific
}

public sealed class CollectionFilterOption
{
    public static CollectionFilterOption All { get; } = new("Все песни", CollectionFilterKind.All, null);

    public CollectionFilterOption(string title, CollectionFilterKind kind, SongCollection? collection)
    {
        Title = title;
        Kind = kind;
        Collection = collection;
    }

    public string Title { get; }

    public CollectionFilterKind Kind { get; }

    public SongCollection? Collection { get; }

    public Guid? CollectionId => Collection?.Id;

    public override string ToString() => Title;
}
