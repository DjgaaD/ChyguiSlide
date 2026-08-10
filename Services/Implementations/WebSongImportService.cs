using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace ChyguiSlide.Services.Implementations;

public sealed class WebSongImportResult
{
    public string? Title { get; init; }
    public IReadOnlyList<(string Heading, string Content, bool IsChorus)> Sections { get; init; }
        = Array.Empty<(string, string, bool)>();
    public string? SourceUrl { get; init; }
    public string? Warning { get; init; }
}

public interface IWebSongImportService
{
    Task<WebSongImportResult> ImportFromUrlAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Импорт текста песни по URL. Для holychords.pro — отдельный разбор #music_text без аккордов.
/// </summary>
public sealed class WebSongImportService : IWebSongImportService
{
    private static readonly HttpClient Http = CreateClient();

    private static readonly Regex ChordLineRegex = new(
        @"^\s*(?:[A-H](?:#|b|♭|♯)?(?:m|maj|min|dim|aug|sus|add|°)?\d*(?:/[A-H](?:#|b|♭|♯)?)?\s*)+\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SectionHeaderRegex = new(
        @"^\s*(?:(?<num>\d+)\s*)?(?<kind>куплет|verse|купл\.?|припев|chorus|refrain|refrein)\s*(?<num2>\d+)?\s*:?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        return client;
    }

    public async Task<WebSongImportResult> ImportFromUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Укажите корректную ссылку http(s).");
        }

        using var response = await Http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style|//noscript")
                     ?? Enumerable.Empty<HtmlNode>())
        {
            node.Remove();
        }

        List<(string Heading, string Content, bool IsChorus)> sections;
        string? warning = null;

        if (IsHolyChords(uri))
        {
            sections = ParseHolyChords(doc, out warning);
        }
        else
        {
            var main = doc.DocumentNode.SelectSingleNode("//*[@id='music_text']")
                       ?? doc.DocumentNode.SelectSingleNode("//pre[contains(@class,'music')]")
                       ?? doc.DocumentNode.SelectSingleNode("//article")
                       ?? doc.DocumentNode.SelectSingleNode("//*[contains(@class,'lyric') or contains(@id,'lyric')]")
                       ?? doc.DocumentNode.SelectSingleNode("//main")
                       ?? doc.DocumentNode.SelectSingleNode("//body");

            var text = ExtractVisibleText(main);
            text = StripChordLines(text);
            sections = ParseSections(text);

            if (sections.Count == 0)
            {
                warning = "Не удалось уверенно разобрать куплеты. Проверьте текст в редакторе.";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sections.Add(("Текст", text.Trim(), false));
                }
            }
            else if (sections.Count == 1)
            {
                warning = "Найден один блок текста — разметку куплетов/припевов стоит проверить вручную.";
            }
        }

        var title = BuildTitleFromLyrics(sections);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = ExtractFallbackTitle(doc) ?? "Импортированная песня";
        }

        return new WebSongImportResult
        {
            Title = title,
            Sections = sections,
            SourceUrl = uri.ToString(),
            Warning = warning
        };
    }

    private static bool IsHolyChords(Uri uri) =>
        uri.Host.Contains("holychords", StringComparison.OrdinalIgnoreCase);

    private static List<(string Heading, string Content, bool IsChorus)> ParseHolyChords(
        HtmlDocument doc,
        out string? warning)
    {
        warning = null;
        var music = doc.DocumentNode.SelectSingleNode("//*[@id='music_text']")
                    ?? doc.DocumentNode.SelectSingleNode("//pre[contains(@class,'F#') or contains(@class,'music_text')]");

        if (music is null)
        {
            warning = "На странице holychords не найден блок текста песни (#music_text).";
            return new List<(string, string, bool)>();
        }

        var raw = HtmlEntity.DeEntitize(music.InnerText ?? string.Empty);
        var cleaned = StripChordLines(raw);
        var sections = ParseSectionsFromMarkedLines(cleaned);

        if (sections.Count == 0 && !string.IsNullOrWhiteSpace(cleaned))
        {
            warning = "Не удалось разбить на куплеты — проверьте текст в редакторе.";
            sections.Add(("Текст", cleaned.Trim(), false));
        }

        return sections;
    }

    private static string StripChordLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                sb.AppendLine();
                continue;
            }

            // Строки только с аккордами
            if (ChordLineRegex.IsMatch(line))
            {
                continue;
            }

            // Строка из одних букв аккордов с большими пробелами (часто на holychords)
            var compact = Regex.Replace(line, @"\s+", " ").Trim();
            if (ChordLineRegex.IsMatch(compact))
            {
                continue;
            }

            sb.AppendLine(line);
        }

        var result = sb.ToString();
        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        return result.Trim();
    }

    private static List<(string Heading, string Content, bool IsChorus)> ParseSectionsFromMarkedLines(string text)
    {
        var result = new List<(string, string, bool)>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        string? currentHeading = null;
        var isChorus = false;
        var contentLines = new List<string>();
        var verseIndex = 0;

        void Flush()
        {
            if (contentLines.Count == 0)
            {
                return;
            }

            var content = string.Join("\n", contentLines).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                contentLines.Clear();
                return;
            }

            var heading = currentHeading;
            if (string.IsNullOrWhiteSpace(heading))
            {
                verseIndex++;
                heading = $"Куплет {verseIndex}";
                isChorus = false;
            }

            result.Add((heading!, content, isChorus));
            contentLines.Clear();
        }

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var match = SectionHeaderRegex.Match(line);
            if (match.Success)
            {
                Flush();
                var kind = match.Groups["kind"].Value;
                isChorus = Regex.IsMatch(kind, @"^(припев|chorus|refrain|refrein)$", RegexOptions.IgnoreCase);
                if (isChorus)
                {
                    currentHeading = "Припев";
                }
                else
                {
                    var num = match.Groups["num"].Success
                        ? match.Groups["num"].Value
                        : match.Groups["num2"].Success
                            ? match.Groups["num2"].Value
                            : null;
                    if (string.IsNullOrEmpty(num))
                    {
                        verseIndex++;
                        num = verseIndex.ToString();
                    }
                    else if (int.TryParse(num, out var n))
                    {
                        verseIndex = Math.Max(verseIndex, n);
                    }

                    currentHeading = $"Куплет {num}";
                }

                continue;
            }

            contentLines.Add(line);
        }

        Flush();
        return result;
    }

    private static List<(string Heading, string Content, bool IsChorus)> ParseSections(string text)
    {
        // Общий случай: сначала пробуем явные заголовки, иначе — блоки по пустым строкам
        var marked = ParseSectionsFromMarkedLines(text);
        if (marked.Count > 1 || (marked.Count == 1 && SectionHeaderRegex.IsMatch(marked[0].Heading) == false && text.Contains("куплет", StringComparison.OrdinalIgnoreCase)))
        {
            if (marked.Count >= 1 && HasExplicitHeaders(text))
            {
                return marked;
            }
        }

        if (HasExplicitHeaders(text))
        {
            return marked;
        }

        var result = new List<(string, string, bool)>();
        var blocks = Regex.Split(text.Replace("\r\n", "\n"), @"\n\s*\n+")
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();

        var verseIndex = 0;
        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                continue;
            }

            var first = lines[0];
            var headerMatch = SectionHeaderRegex.Match(first);
            if (headerMatch.Success)
            {
                var kind = headerMatch.Groups["kind"].Value;
                var isChorus = Regex.IsMatch(kind, @"^(припев|chorus|refrain|refrein)$", RegexOptions.IgnoreCase);
                var content = string.Join("\n", lines.Skip(1)).Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                var heading = isChorus ? "Припев" : first;
                if (!isChorus && int.TryParse(headerMatch.Groups["num"].Value, out var n))
                {
                    heading = $"Куплет {n}";
                }

                result.Add((heading, content, isChorus));
            }
            else
            {
                verseIndex++;
                result.Add(($"Куплет {verseIndex}", block, false));
            }
        }

        return result.Count > 0 ? result : marked;
    }

    private static bool HasExplicitHeaders(string text) =>
        text.Split('\n').Any(l => SectionHeaderRegex.IsMatch(l.Trim()));

    private static string? BuildTitleFromLyrics(
        IReadOnlyList<(string Heading, string Content, bool IsChorus)> sections)
    {
        var firstContent = sections.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Content)).Content;
        if (string.IsNullOrWhiteSpace(firstContent))
        {
            return null;
        }

        var firstLine = firstContent
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        // Убрать номер стиха в начале, если есть
        firstLine = Regex.Replace(firstLine, @"^\d+[.)]\s*", "");
        var words = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return null;
        }

        var take = Math.Min(words.Length, 6);
        var title = string.Join(' ', words.Take(take));
        if (title.Length > 80)
        {
            title = title[..80].TrimEnd();
        }

        // Убрать хвостовую пунктуацию
        title = title.TrimEnd(',', '.', ';', ':', '—', '-');
        return title;
    }

    private static string? ExtractFallbackTitle(HtmlDocument doc)
    {
        var h2 = doc.DocumentNode.SelectSingleNode("//h2");
        if (h2 is not null)
        {
            var t = NormalizeTitle(h2.InnerText);
            if (!IsSiteBrandTitle(t))
            {
                return t;
            }
        }

        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode is not null)
        {
            var raw = HtmlEntity.DeEntitize(titleNode.InnerText);
            // "… | Слова | … HOLYCHORDS.pro" → взять кусок до |
            var part = raw.Split('|')[0].Trim();
            part = Regex.Replace(part, @"^\s*Песнь\s+Возрождения\s+\d+\s*", "", RegexOptions.IgnoreCase).Trim();
            part = NormalizeTitle(part);
            if (!IsSiteBrandTitle(part))
            {
                return part;
            }
        }

        return null;
    }

    private static bool IsSiteBrandTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        return title.Contains("HOLYCHORDS", StringComparison.OrdinalIgnoreCase)
               || title.Equals("песня", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var t = HtmlEntity.DeEntitize(value).Trim();
        t = Regex.Replace(t, @"\s+", " ");
        return t;
    }

    private static string ExtractVisibleText(HtmlNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var child in node.DescendantsAndSelf())
        {
            if (child.NodeType != HtmlNodeType.Text)
            {
                continue;
            }

            var value = HtmlEntity.DeEntitize(child.InnerText);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            sb.AppendLine(value.Trim());
        }

        var raw = sb.ToString();
        raw = Regex.Replace(raw, @"[ \t]+\r?\n", "\n");
        raw = Regex.Replace(raw, @"\n{3,}", "\n\n");
        return raw.Trim();
    }
}
