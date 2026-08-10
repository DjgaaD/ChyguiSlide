using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Bible;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Implementations;

public sealed class BibleService : IBibleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private List<BibleBook> _books = new();
    private Dictionary<string, List<BibleVerse>> _versesByBook = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public string TranslationName => "Синодальный перевод";

    public bool IsLoaded => _loaded;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            var path = ResolveDataPath();
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Файл Библии не найден: {path}. Положите bible.json в Assets/Bible/.",
                    path);
            }

            await using var stream = File.OpenRead(path);
            var root = await JsonSerializer.DeserializeAsync<BibleRoot>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (root?.Books is null || root.Books.Count == 0)
            {
                throw new InvalidDataException($"Пустой или неверный формат Библии: {path}");
            }

            var versesByBook = new Dictionary<string, List<BibleVerse>>(StringComparer.OrdinalIgnoreCase);
            var bookMeta = new Dictionary<string, (string Title, int ChapterCount)>(StringComparer.OrdinalIgnoreCase);

            foreach (var book in root.Books)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(book.Title) || book.Chapters is null)
                {
                    continue;
                }

                if (!BibleBookCatalog.TryResolveByRussianTitle(book.Title, out var bookId))
                {
                    System.Diagnostics.Debug.WriteLine($"Bible: неизвестная книга «{book.Title}» — пропуск.");
                    continue;
                }

                var list = new List<BibleVerse>();
                for (var chapterIndex = 0; chapterIndex < book.Chapters.Count; chapterIndex++)
                {
                    var chapterNum = chapterIndex + 1;
                    var chapter = book.Chapters[chapterIndex];
                    if (chapter is null)
                    {
                        continue;
                    }

                    foreach (var verse in chapter)
                    {
                        if (verse is null || string.IsNullOrWhiteSpace(verse.Text))
                        {
                            continue;
                        }

                        list.Add(new BibleVerse
                        {
                            BookId = bookId,
                            BookName = book.Title,
                            Chapter = chapterNum,
                            Verse = verse.Verse,
                            Text = verse.Text.Trim()
                        });
                    }
                }

                if (list.Count == 0)
                {
                    continue;
                }

                list.Sort((a, b) =>
                {
                    var c = a.Chapter.CompareTo(b.Chapter);
                    return c != 0 ? c : a.Verse.CompareTo(b.Verse);
                });

                versesByBook[bookId] = list;
                bookMeta[bookId] = (book.Title, book.Chapters.Count);
            }

            _versesByBook = versesByBook;
            _books = bookMeta
                .Select(pair =>
                {
                    var (ru, abbr, nt, order) = BibleBookCatalog.Resolve(pair.Key, pair.Value.Title);
                    return new BibleBook
                    {
                        BookId = pair.Key,
                        EnglishName = pair.Value.Title,
                        RussianName = string.IsNullOrWhiteSpace(ru) ? pair.Value.Title : ru,
                        Abbreviation = abbr,
                        IsNewTestament = nt,
                        Order = order,
                        ChapterCount = pair.Value.ChapterCount
                    };
                })
                .OrderBy(b => b.Order)
                .ThenBy(b => b.RussianName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public IReadOnlyList<BibleBook> GetBooks() => _books;

    public IReadOnlyList<int> GetChapters(string bookId)
    {
        if (!_versesByBook.TryGetValue(bookId, out var verses))
        {
            return Array.Empty<int>();
        }

        return verses.Select(v => v.Chapter).Distinct().OrderBy(c => c).ToList();
    }

    public IReadOnlyList<BibleVerse> GetVerses(string bookId, int chapter)
    {
        if (!_versesByBook.TryGetValue(bookId, out var verses))
        {
            return Array.Empty<BibleVerse>();
        }

        return verses.Where(v => v.Chapter == chapter).ToList();
    }

    public IReadOnlyList<BibleVerse> GetPassage(string bookId, int chapter, int fromVerse, int? toVerse = null)
    {
        var verses = GetVerses(bookId, chapter);
        if (verses.Count == 0)
        {
            return verses;
        }

        var end = toVerse ?? int.MaxValue;
        return verses.Where(v => v.Verse >= fromVerse && v.Verse <= end).ToList();
    }

    public IReadOnlyList<BibleVerse> Search(string query, int maxResults = 80)
    {
        if (string.IsNullOrWhiteSpace(query) || !_loaded)
        {
            return Array.Empty<BibleVerse>();
        }

        var tokens = NormalizeForSearch(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Array.Empty<BibleVerse>();
        }

        return _versesByBook.Values
            .SelectMany(v => v)
            .Where(v =>
            {
                var haystack = NormalizeForSearch($"{v.Text} {v.BookName} {v.Chapter} {v.Verse}");
                return tokens.All(token => haystack.Contains(token, StringComparison.Ordinal));
            })
            .Take(maxResults)
            .ToList();
    }

    private static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch))
            {
                sb.Append(' ');
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ResolveDataPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "Bible", "bible.json"),
            Path.Combine(AppContext.BaseDirectory, "Bible", "bible.json"),
            Path.Combine(AppContext.BaseDirectory, "bible.json"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return candidates[0];
    }

    private sealed class BibleRoot
    {
        [JsonPropertyName("books")]
        public List<BibleBookJson>? Books { get; set; }
    }

    private sealed class BibleBookJson
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("chapters")]
        public List<List<BibleVerseJson>?>? Chapters { get; set; }
    }

    private sealed class BibleVerseJson
    {
        [JsonPropertyName("verse")]
        public int Verse { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
