using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChyguiSlide.Services.Bible;

/// <summary>
/// Русские названия, сокращения и алиасы книг для book_id из RST.
/// </summary>
public static class BibleBookCatalog
{
    private sealed record BookInfo(string Ru, string Abbr, bool Nt, int Order, string[] ExtraAliases);

    private static readonly Dictionary<string, BookInfo> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Gen"] = new("Бытие", "Быт", false, 1, ["Бытие", "Gen"]),
            ["Exod"] = new("Исход", "Исх", false, 2, ["Исход", "Exod", "Ex"]),
            ["Lev"] = new("Левит", "Лев", false, 3, ["Левит", "Lev"]),
            ["Num"] = new("Числа", "Чис", false, 4, ["Числа", "Числ", "Num"]),
            ["Deut"] = new("Второзаконие", "Втор", false, 5, ["Второзаконие", "Вторз", "Deut", "Dt"]),
            ["Josh"] = new("Иисус Навин", "Нав", false, 6, ["Навин", "ИисНав", "Josh"]),
            ["Judg"] = new("Судьи", "Суд", false, 7, ["Судьи", "Судей", "Judg"]),
            ["Ruth"] = new("Руфь", "Руф", false, 8, ["Руфь", "Ruth"]),
            ["1Sam"] = new("1-я Царств", "1Цар", false, 9, ["1Царств", "1Sam", "1Samuel", "1 Царств"]),
            ["2Sam"] = new("2-я Царств", "2Цар", false, 10, ["2Царств", "2Sam", "2Samuel", "2 Царств"]),
            ["1Kgs"] = new("3-я Царств", "3Цар", false, 11, ["3Царств", "1Kgs", "3Цар", "3 Царств"]),
            ["2Kgs"] = new("4-я Царств", "4Цар", false, 12, ["4Царств", "2Kgs", "4Цар", "4 Царств"]),
            ["1Chr"] = new("1-я Паралипоменон", "1Пар", false, 13, ["1Паралипоменон", "1Пар", "1Хр", "1Chr", "1Par", "1 Паралипоменон"]),
            ["2Chr"] = new("2-я Паралипоменон", "2Пар", false, 14, ["2Паралипоменон", "2Пар", "2Хр", "2Chr", "2Par", "2 Паралипоменон"]),
            ["Ezra"] = new("Ездра", "Езд", false, 15, ["Ездра", "Ezra"]),
            ["Neh"] = new("Неемия", "Неем", false, 16, ["Неемия", "Неем", "Neh"]),
            ["Esth"] = new("Есфирь", "Есф", false, 17, ["Есфирь", "Esth", "Est"]),
            ["Job"] = new("Иов", "Иов", false, 18, ["Job"]),
            ["Ps"] = new("Псалом", "Пс", false, 19, ["Псалмы", "Псалом", "Псалтирь", "Ps", "Psalms"]),
            ["Prov"] = new("Притчи", "Прит", false, 20, ["Притчи", "Притч", "Prov"]),
            ["Eccl"] = new("Екклесиаст", "Еккл", false, 21, ["Екклесиаст", "Екклезиаст", "Екк", "Eccl", "Ecc"]),
            ["Song"] = new("Песня Песней", "Песн", false, 22, ["Песнь", "Песни", "Песнь Песней", "Песня Песней", "Song", "Cant"]),
            ["Isa"] = new("Исаия", "Ис", false, 23, ["Исаия", "Исайя", "Isa"]),
            ["Jer"] = new("Иеремия", "Иер", false, 24, ["Иеремия", "Jer"]),
            ["Lam"] = new("Плач Иеремии", "Плач", false, 25, ["Плач", "Lam"]),
            ["Ezek"] = new("Иезекииль", "Иез", false, 26, ["Иезекииль", "Ezek"]),
            ["Dan"] = new("Даниил", "Дан", false, 27, ["Даниил", "Dan"]),
            ["Hos"] = new("Осия", "Ос", false, 28, ["Осия", "Hos"]),
            ["Joel"] = new("Иоиль", "Иоил", false, 29, ["Иоиль", "Joel"]),
            ["Amos"] = new("Амос", "Ам", false, 30, ["Амос", "Amos"]),
            ["Obad"] = new("Авдий", "Авд", false, 31, ["Авдий", "Obad"]),
            ["Jona"] = new("Иона", "Ион", false, 32, ["Иона", "Jona", "Jonah"]),
            ["Mic"] = new("Михей", "Мих", false, 33, ["Михей", "Mic"]),
            ["Nah"] = new("Наум", "Наум", false, 34, ["Nah"]),
            ["Hab"] = new("Аввакум", "Авв", false, 35, ["Аввакум", "Hab"]),
            ["Zeph"] = new("Софония", "Соф", false, 36, ["Софония", "Zeph"]),
            ["Hag"] = new("Аггей", "Агг", false, 37, ["Аггей", "Hag"]),
            ["Zech"] = new("Захария", "Зах", false, 38, ["Захария", "Zech"]),
            ["Mal"] = new("Малахия", "Мал", false, 39, ["Малахия", "Mal"]),
            ["Matt"] = new("Матфея", "Мф", true, 40, ["Матфея", "Матфей", "Мат", "От Матфея", "Matt", "Mt"]),
            ["Mark"] = new("Марка", "Мк", true, 41, ["Марка", "Марк", "От Марка", "Mark", "Mk"]),
            ["Luke"] = new("Луки", "Лк", true, 42, ["Луки", "Лука", "От Луки", "Luke", "Lk"]),
            ["John"] = new("Иоанна", "Ин", true, 43, ["Иоанна", "Иоанн", "От Иоанна", "John", "Jn"]),
            ["Acts"] = new("Деяния", "Деян", true, 44, ["Деяния", "Деян", "Acts"]),
            ["Rom"] = new("К Римлянам", "Рим", true, 45, ["Римлянам", "Рим", "Rom"]),
            ["1Cor"] = new("1-е Коринфянам", "1Кор", true, 46, ["1Коринфянам", "1 Кор", "1Cor", "1 Коринфянам"]),
            ["2Cor"] = new("2-е Коринфянам", "2Кор", true, 47, ["2Коринфянам", "2 Кор", "2Cor", "2 Коринфянам"]),
            ["Gal"] = new("К Галатам", "Гал", true, 48, ["Галатам", "Gal"]),
            ["Eph"] = new("К Ефесянам", "Еф", true, 49, ["Ефесянам", "Eph"]),
            ["Phil"] = new("К Филиппийцам", "Флп", true, 50, ["Филиппийцам", "Фил", "Phil"]),
            ["Col"] = new("К Колоссянам", "Кол", true, 51, ["Колоссянам", "Col"]),
            ["1Thess"] = new("1-е Фессалоникийцам", "1Фес", true, 52, ["1Фессалоникийцам", "1Thess", "1Фесс", "1 Фессалоникийцам"]),
            ["2Thess"] = new("2-е Фессалоникийцам", "2Фес", true, 53, ["2Фессалоникийцам", "2Thess", "2Фесс", "2 Фессалоникийцам"]),
            ["1Tim"] = new("1-е Тимофею", "1Тим", true, 54, ["1Тимофею", "1Tim", "1 Тимофею"]),
            ["2Tim"] = new("2-е Тимофею", "2Тим", true, 55, ["2Тимофею", "2Tim", "2 Тимофею"]),
            ["Titus"] = new("К Титу", "Тит", true, 56, ["Титу", "Тит", "Titus"]),
            ["Phlm"] = new("К Филимону", "Флм", true, 57, ["Филимону", "Phlm"]),
            ["Heb"] = new("К Евреям", "Евр", true, 58, ["Евреям", "Heb"]),
            ["Jas"] = new("Иакова", "Иак", true, 59, ["Иакова", "Jas", "James"]),
            ["1Pet"] = new("1-е Петра", "1Пет", true, 60, ["1Петра", "1Pet", "1 Петра"]),
            ["2Pet"] = new("2-е Петра", "2Пет", true, 61, ["2Петра", "2Pet", "2 Петра"]),
            ["1John"] = new("1-е Иоанна", "1Ин", true, 62, ["1Иоанна", "1John", "1Ин", "1 Иоанна"]),
            ["2John"] = new("2-е Иоанна", "2Ин", true, 63, ["2Иоанна", "2John", "2Ин", "2 Иоанна"]),
            ["3John"] = new("3 Иоанна", "3Ин", true, 64, ["3Иоанна", "3John", "3Ин", "3-е Иоанна"]),
            ["Jude"] = new("Иуды", "Иуд", true, 65, ["Иуды", "Jude"]),
            ["Rev"] = new("Откровение", "Откр", true, 66, ["Откровение", "Апок", "Rev"]),
        };

    private static readonly Dictionary<string, string> TitleToBookId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Бытие"] = "Gen",
            ["Исход"] = "Exod",
            ["Левит"] = "Lev",
            ["Числа"] = "Num",
            ["Второзаконие"] = "Deut",
            ["Иисус Навин"] = "Josh",
            ["Судьи"] = "Judg",
            ["Руфь"] = "Ruth",
            ["1-я Царств"] = "1Sam",
            ["2-я Царств"] = "2Sam",
            ["3-я Царств"] = "1Kgs",
            ["4-я Царств"] = "2Kgs",
            ["1-я Паралипоменон"] = "1Chr",
            ["2-я Паралипоменон"] = "2Chr",
            ["Ездра"] = "Ezra",
            ["Неемия"] = "Neh",
            ["Есфирь"] = "Esth",
            ["Иов"] = "Job",
            ["Псалом"] = "Ps",
            ["Притчи"] = "Prov",
            ["Екклесиаст"] = "Eccl",
            ["Песня Песней"] = "Song",
            ["Исаия"] = "Isa",
            ["Иеремия"] = "Jer",
            ["Плач Иеремии"] = "Lam",
            ["Иезекииль"] = "Ezek",
            ["Даниил"] = "Dan",
            ["Осия"] = "Hos",
            ["Иоиль"] = "Joel",
            ["Амос"] = "Amos",
            ["Авдий"] = "Obad",
            ["Иона"] = "Jona",
            ["Михей"] = "Mic",
            ["Наум"] = "Nah",
            ["Аввакум"] = "Hab",
            ["Софония"] = "Zeph",
            ["Аггей"] = "Hag",
            ["Захария"] = "Zech",
            ["Малахия"] = "Mal",
            ["Матфея"] = "Matt",
            ["Марка"] = "Mark",
            ["Луки"] = "Luke",
            ["Иоанна"] = "John",
            ["Деяния"] = "Acts",
            ["Иакова"] = "Jas",
            ["1-е Петра"] = "1Pet",
            ["2-е Петра"] = "2Pet",
            ["1-е Иоанна"] = "1John",
            ["2-е Иоанна"] = "2John",
            ["3 Иоанна"] = "3John",
            ["Иуды"] = "Jude",
            ["К Римлянам"] = "Rom",
            ["1-е Коринфянам"] = "1Cor",
            ["2-е Коринфянам"] = "2Cor",
            ["К Галатам"] = "Gal",
            ["К Ефесянам"] = "Eph",
            ["К Филиппийцам"] = "Phil",
            ["К Колоссянам"] = "Col",
            ["1-е Фессалоникийцам"] = "1Thess",
            ["2-е Фессалоникийцам"] = "2Thess",
            ["1-е Тимофею"] = "1Tim",
            ["2-е Тимофею"] = "2Tim",
            ["К Титу"] = "Titus",
            ["К Филимону"] = "Phlm",
            ["К Евреям"] = "Heb",
            ["Откровение"] = "Rev",
        };

    private static readonly Lazy<List<(string BookId, string Alias)>> AliasIndex = new(BuildAliasIndex);

    public static (string RussianName, string Abbreviation, bool IsNewTestament, int Order) Resolve(
        string bookId,
        string? englishFallback = null)
    {
        if (Map.TryGetValue(bookId, out var info))
        {
            return (info.Ru, info.Abbr, info.Nt, info.Order);
        }

        var name = string.IsNullOrWhiteSpace(englishFallback) ? bookId : englishFallback;
        return (name, bookId, false, 1000);
    }

    /// <summary>Точное сопоставление русского названия из bible.json → book_id.</summary>
    public static bool TryResolveByRussianTitle(string title, out string bookId)
    {
        bookId = string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (TitleToBookId.TryGetValue(title.Trim(), out var id))
        {
            bookId = id;
            return true;
        }

        return TryResolveBook(title, out bookId);
    }

    /// <summary>
    /// Находит книгу по сокращению / части названия («2пар», «екк», «матф»).
    /// </summary>
    public static bool TryResolveBook(string token, out string bookId)
    {
        bookId = string.Empty;
        var normalized = Normalize(token);
        if (normalized.Length == 0)
        {
            return false;
        }

        // 1) Точное совпадение алиаса
        var exact = AliasIndex.Value
            .Where(a => a.Alias == normalized)
            .Select(a => a.BookId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (exact.Count == 1)
        {
            bookId = exact[0];
            return true;
        }

        // 2) Алиас начинается с токена (екк → еккл) или токен — префикс русского имени
        var prefix = AliasIndex.Value
            .Where(a => a.Alias.StartsWith(normalized, StringComparison.Ordinal)
                        || normalized.StartsWith(a.Alias, StringComparison.Ordinal) && a.Alias.Length >= 2)
            .GroupBy(a => a.BookId, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                BookId = g.Key,
                BestLen = g.Max(x => x.Alias.Length),
                Exactness = g.Any(x => x.Alias == normalized) ? 2
                    : g.Any(x => x.Alias.StartsWith(normalized, StringComparison.Ordinal)) ? 1 : 0
            })
            .OrderByDescending(x => x.Exactness)
            .ThenByDescending(x => Math.Min(x.BestLen, normalized.Length))
            .ThenByDescending(x => x.BestLen)
            .ToList();

        if (prefix.Count == 0)
        {
            return false;
        }

        // Если несколько и токен короткий — только при явном лидере по Exactness/длине
        if (prefix.Count == 1
            || prefix[0].Exactness > prefix[1].Exactness
            || (prefix[0].Exactness == prefix[1].Exactness
                && prefix[0].BestLen > prefix[1].BestLen
                && normalized.Length >= 3))
        {
            bookId = prefix[0].BookId;
            return true;
        }

        // Для нумерованных книг: «2пар» однозначно, «пар» — нет
        if (char.IsDigit(normalized[0]))
        {
            var numbered = prefix.Where(p =>
            {
                if (!Map.TryGetValue(p.BookId, out var info))
                {
                    return false;
                }

                var abbr = Normalize(info.Abbr);
                return abbr.StartsWith(normalized, StringComparison.Ordinal)
                       || Normalize(info.Ru).StartsWith(normalized, StringComparison.Ordinal);
            }).ToList();
            if (numbered.Count == 1)
            {
                bookId = numbered[0].BookId;
                return true;
            }
        }

        return false;
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (ch == 'ё')
            {
                sb.Append('е');
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static List<(string BookId, string Alias)> BuildAliasIndex()
    {
        var list = new List<(string, string)>();
        foreach (var (bookId, info) in Map)
        {
            void Add(string? raw)
            {
                var n = Normalize(raw ?? string.Empty);
                if (n.Length > 0)
                {
                    list.Add((bookId, n));
                }
            }

            Add(bookId);
            Add(info.Abbr);
            Add(info.Ru);
            Add(info.Ru.Replace(" ", string.Empty, StringComparison.Ordinal));
            // Без ведущего номера для частичного ввода «пар» → неоднозначно, но «паралип» ок
            var ruNoNum = Normalize(info.Ru);
            if (ruNoNum.Length > 0 && char.IsDigit(ruNoNum[0]))
            {
                var i = 0;
                while (i < ruNoNum.Length && char.IsDigit(ruNoNum[i]))
                {
                    i++;
                }

                if (i < ruNoNum.Length)
                {
                    Add(ruNoNum[i..]);
                }
            }

            foreach (var extra in info.ExtraAliases)
            {
                Add(extra);
            }
        }

        return list;
    }
}
