using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Abstractions;

public interface IBibleService
{
    string TranslationName { get; }
    bool IsLoaded { get; }

    Task EnsureLoadedAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<BibleBook> GetBooks();

    IReadOnlyList<int> GetChapters(string bookId);

    IReadOnlyList<BibleVerse> GetVerses(string bookId, int chapter);

    IReadOnlyList<BibleVerse> GetPassage(string bookId, int chapter, int fromVerse, int? toVerse = null);

    IReadOnlyList<BibleVerse> Search(string query, int maxResults = 80);
}
