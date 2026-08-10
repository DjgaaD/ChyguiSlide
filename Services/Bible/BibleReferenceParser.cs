using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ChyguiSlide.Services.Bible;

public readonly record struct BibleReferenceQuery(string BookId, int Chapter, int? Verse);

/// <summary>
/// Разбор быстрых ссылок: «2пар 15 8», «Екк 11:10», «Ин.3:16», «2Пар15:8».
/// </summary>
public static class BibleReferenceParser
{
    // [номер?] название… глава [:.] стих?
    // название может быть из нескольких слов («От Матфея», «Песнь Песней»)
    private static readonly Regex ReferenceRegex = new(
        @"^\s*(?<book>\d?\s*[^\d\s:.,]+(?:\s+[^\d\s:.,]+)*)\s*[.,:\s]*(?<chapter>\d{1,3})(?:\s*[.,:\s]+\s*(?<verse>\d{1,3}))?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(string? query, out BibleReferenceQuery reference)
    {
        reference = default;
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var match = ReferenceRegex.Match(query.Trim());
        if (!match.Success)
        {
            return false;
        }

        var bookToken = match.Groups["book"].Value.Trim();
        if (!BibleBookCatalog.TryResolveBook(bookToken, out var bookId))
        {
            return false;
        }

        if (!int.TryParse(match.Groups["chapter"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chapter)
            || chapter <= 0)
        {
            return false;
        }

        int? verse = null;
        if (match.Groups["verse"].Success
            && int.TryParse(match.Groups["verse"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            && v > 0)
        {
            verse = v;
        }

        reference = new BibleReferenceQuery(bookId, chapter, verse);
        return true;
    }
}
