using System;
using ChyguiSlide.Data.Entities;

namespace ChyguiSlide.ViewModels;

public enum SongCollectionChoiceKind
{
    /// <summary>Пользователь ещё не сделал выбор.</summary>
    Unspecified,

    /// <summary>Явно без сборника.</summary>
    WithoutCollection,

    /// <summary>Конкретный сборник.</summary>
    Specific
}

public sealed class SongCollectionOption
{
    public static SongCollectionOption Unspecified { get; } =
        new("Не выбрано", null, SongCollectionChoiceKind.Unspecified);

    public static SongCollectionOption WithoutCollection { get; } =
        new("Без сборника", null, SongCollectionChoiceKind.WithoutCollection);

    /// <summary>Устаревший алиас: явное «Без сборника».</summary>
    public static SongCollectionOption None => WithoutCollection;

    public SongCollectionOption(string title, SongCollection? collection)
        : this(title, collection, SongCollectionChoiceKind.Specific)
    {
    }

    private SongCollectionOption(string title, SongCollection? collection, SongCollectionChoiceKind kind)
    {
        Title = title;
        Collection = collection;
        Kind = kind;
    }

    public string Title { get; }

    public SongCollection? Collection { get; }

    public SongCollectionChoiceKind Kind { get; }

    public Guid? CollectionId => Collection?.Id;

    public bool IsUnspecified => Kind == SongCollectionChoiceKind.Unspecified;

    public override string ToString() => Title;
}
