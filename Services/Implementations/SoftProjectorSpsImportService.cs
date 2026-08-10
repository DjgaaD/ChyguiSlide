using System.Text;
using System.Text.RegularExpressions;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using ChyguiSlide.Data.ValueObjects;
using ChyguiSlide.Services;

namespace ChyguiSlide.Services.Implementations;

public sealed class SoftProjectorSpsImportResult
{
    public string SongbookName { get; init; } = "Импортированный сборник";
    public string? Description { get; init; }
    public IReadOnlyList<Song> Songs { get; init; } = Array.Empty<Song>();
    public string? Warning { get; init; }
}

/// <summary>
/// Импорт сборника SoftProjector (.sps) — текстовый экспорт (UTF-8 / windows-1251).
/// Формат строки: номер#$#название#$#категория#$#тональность#$#слова#$#музыка#$#текст
/// В тексте: @% = перевод строки, @$ = пустая строка (разделитель куплетов).
/// </summary>
public sealed class SoftProjectorSpsImportService
{
    private static readonly Regex SectionHeaderRegex = new(
        @"^\s*(?<kind>куплет|припев|chorus|verse|bridge|купл\.?)\s*(?<num>\d+)?\s*:?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static SoftProjectorSpsImportService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<SoftProjectorSpsImportResult> ImportFromFileAsync(
        string filePath,
        Guid? collectionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Файл SPS не найден.", filePath);
        }

        var text = await ReadSpsTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length < 4)
        {
            throw new InvalidDataException("Файл SPS слишком короткий или повреждён.");
        }

        var songbookName = StripHeaderPrefix(lines[1]);
        if (string.IsNullOrWhiteSpace(songbookName))
        {
            songbookName = Path.GetFileNameWithoutExtension(filePath);
        }

        var description = StripHeaderPrefix(lines[2]);
        // Описание иногда содержит @%@%... служебный хвост
        var descEnd = description.IndexOf("@%", StringComparison.Ordinal);
        if (descEnd >= 0)
        {
            description = description[..descEnd].Trim();
        }

        var songs = new List<Song>();
        var skipped = 0;

        for (var i = 3; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            var song = ParseSongLine(line, collectionId);
            if (song is null)
            {
                skipped++;
                continue;
            }

            songs.Add(song);
        }

        string? warning = null;
        if (songs.Count == 0)
        {
            warning = "В файле не найдено ни одной песни.";
        }
        else if (skipped > 0)
        {
            warning = $"Пропущено строк без текста: {skipped}.";
        }

        return new SoftProjectorSpsImportResult
        {
            SongbookName = songbookName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            Songs = songs,
            Warning = warning
        };
    }

    private static async Task<string> ReadSpsTextAsync(string filePath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);

        // SoftProjector v2 export — обычно UTF-8 (как pv3300.sps)
        if (LooksLikeUtf8(bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return Encoding.GetEncoding(1251).GetString(bytes);
    }

    private static bool LooksLikeUtf8(byte[] bytes)
    {
        // Наличие кириллицы в UTF-8: D0/D1 …
        var sampleLen = Math.Min(bytes.Length, 4096);
        var utf8Cyr = 0;
        for (var i = 0; i < sampleLen - 1; i++)
        {
            if (bytes[i] is 0xD0 or 0xD1 && bytes[i + 1] >= 0x80)
            {
                utf8Cyr++;
            }
        }

        return utf8Cyr >= 3;
    }

    private static string StripTrailingSoftProjectorMarkers(string body)
    {
        // Хвост SoftProjector: пустые поля #$# и иногда выравнивание (left/center/right)
        return Regex.Replace(
                body,
                @"(?:#\$#)+(?:left|center|right|centre)?\s*$",
                string.Empty,
                RegexOptions.IgnoreCase)
            .TrimEnd();
    }

    private static string StripHeaderPrefix(string line)
    {
        var value = line.Trim();
        if (value.StartsWith("##", StringComparison.Ordinal))
        {
            value = value[2..].Trim();
        }

        return value;
    }

    private static Song? ParseSongLine(string line, Guid? collectionId)
    {
        var parts = line.Split(new[] { "#$#" }, StringSplitOptions.None);
        if (parts.Length < 7)
        {
            return null;
        }

        var numberText = parts[0].Trim();
        var title = parts[1].Trim();
        var key = parts.ElementAtOrDefault(3)?.Trim();
        _ = parts.ElementAtOrDefault(4)?.Trim(); // wordsBy — не показываем в UI
        _ = parts.ElementAtOrDefault(5)?.Trim(); // musicBy

        // Текст — только 7-е поле. Хвост #$##$##$# — пустые служебные поля SoftProjector.
        var compressed = parts[6];

        if (string.IsNullOrWhiteSpace(compressed))
        {
            return null;
        }

        var body = compressed
            .Replace("@%", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("@$", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Trim();
        body = StripTrailingSoftProjectorMarkers(body);

        var sections = ParseSections(body);
        if (sections.Count == 0)
        {
            sections.Add(new SongSection
            {
                Id = Guid.NewGuid(),
                SectionType = SectionType.Verse,
                Heading = "Куплет 1",
                Content = body,
                Order = 0,
                Timing = new SectionTiming(null, null, null)
            });
        }

        SongChorusLayout.ExpandChorusAfterEachVerse(sections);

        if (string.IsNullOrWhiteSpace(title))
        {
            title = BuildTitleFromFirstSection(sections);
        }

        int? number = null;
        if (int.TryParse(numberText, out var n) && n > 0)
        {
            number = n;
        }

        return new Song
        {
            Id = Guid.NewGuid(),
            Title = Truncate(title, 256),
            Number = number,
            Subtitle = null,
            DefaultKey = string.IsNullOrWhiteSpace(key) ? null : Truncate(key, 64),
            CollectionId = collectionId,
            Language = "ru",
            IsPublished = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Sections = sections
        };
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static List<SongSection> ParseSections(string body)
    {
        var result = new List<SongSection>();
        var blocks = Regex.Split(body.Replace("\r\n", "\n"), @"\n\s*\n+")
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();

        string? pendingHeading = null;
        var pendingIsChorus = false;
        var verseIndex = 0;
        var order = 0;

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                continue;
            }

            var first = lines[0];
            var headerMatch = SectionHeaderRegex.Match(first);
            string heading;
            string content;
            bool isChorus;

            if (headerMatch.Success)
            {
                var kind = headerMatch.Groups["kind"].Value;
                isChorus = Regex.IsMatch(kind, @"^(припев|chorus)$", RegexOptions.IgnoreCase);
                var num = headerMatch.Groups["num"].Success ? headerMatch.Groups["num"].Value : null;

                if (isChorus)
                {
                    heading = string.IsNullOrEmpty(num) ? "Припев" : $"Припев {num}";
                }
                else
                {
                    if (string.IsNullOrEmpty(num))
                    {
                        verseIndex++;
                        num = verseIndex.ToString();
                    }
                    else if (int.TryParse(num, out var n))
                    {
                        verseIndex = Math.Max(verseIndex, n);
                    }

                    heading = $"Куплет {num}";
                }

                content = string.Join("\n", lines.Skip(1)).Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    // Заголовок отдельным блоком — контент в следующем
                    pendingHeading = heading;
                    pendingIsChorus = isChorus;
                    continue;
                }
            }
            else if (pendingHeading is not null)
            {
                heading = pendingHeading;
                isChorus = pendingIsChorus;
                content = block;
                pendingHeading = null;
            }
            else
            {
                verseIndex++;
                heading = $"Куплет {verseIndex}";
                content = block;
                isChorus = false;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            result.Add(new SongSection
            {
                Id = Guid.NewGuid(),
                SectionType = isChorus ? SectionType.Chorus : SectionType.Verse,
                Heading = heading,
                Content = content,
                Order = order++,
                Timing = new SectionTiming(null, null, null)
            });
        }

        return result;
    }

    private static string BuildTitleFromFirstSection(IReadOnlyList<SongSection> sections)
    {
        var first = sections.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Content))?.Content;
        if (string.IsNullOrWhiteSpace(first))
        {
            return "Без названия";
        }

        var line = first.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "Без названия";
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var title = string.Join(' ', words.Take(Math.Min(6, words.Length))).TrimEnd(',', '.', ';', ':', '—', '-');
        return string.IsNullOrWhiteSpace(title) ? "Без названия" : title;
    }
}
