using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Data.Enums;
using System.Text.Unicode;

namespace ChyguiSlide.Services.Implementations;

/// <summary>
/// Импорт песен из HTML файлов формата "Песнь возрождения"
/// </summary>
public class PesnVozrImportService
{
    static PesnVozrImportService()
    {
        // Регистрируем провайдер кодировок для поддержки windows-1251
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly Regex SongNumberRegex = new(@"№\s*(\d+)|^(\d+)\.", RegexOptions.Compiled);
    private static readonly Regex VerseNumberRegex = new(@"^(\d+)\.\s*", RegexOptions.Compiled);

    /// <summary>
    /// Импортирует песни из HTML файла
    /// </summary>
    public async Task<List<Song>> ImportFromFileAsync(string filePath, Guid? collectionId = null, CancellationToken cancellationToken = default)
    {
        var html = await File.ReadAllTextAsync(filePath, Encoding.GetEncoding("windows-1251"), cancellationToken);
        
        var songs = new List<Song>();
        
        // Разделяем по <!--PART-->
        var parts = html.Split(new[] { "<!--PART-->" }, StringSplitOptions.RemoveEmptyEntries);
        
        System.Diagnostics.Debug.WriteLine($"[PesnVozrImport] Found {parts.Length} PART sections");
        
        foreach (var part in parts)
        {
            var song = ParseSongPart(part, collectionId);
            if (song != null)
            {
                songs.Add(song);
                System.Diagnostics.Debug.WriteLine($"[PesnVozrImport] Parsed song: {song.Number} - {song.Title}");
            }
        }

        return songs;
    }

    /// <summary>
    /// Импортирует песни из директории с HTML файлами
    /// </summary>
    public async Task<List<Song>> ImportFromDirectoryAsync(string directoryPath, Guid? collectionId = null, CancellationToken cancellationToken = default)
    {
        var allSongs = new List<Song>();
        var files = Directory.GetFiles(directoryPath, "*.htm", SearchOption.TopDirectoryOnly);
        
        foreach (var file in files)
        {
            var songs = await ImportFromFileAsync(file, collectionId, cancellationToken);
            allSongs.AddRange(songs);
        }

        return allSongs;
    }

    private Song? ParseSongPart(string htmlPart, Guid? collectionId)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(htmlPart);

        // Пробуем разные варианты XPath для поиска DIV
        var txDiv = doc.DocumentNode.SelectSingleNode("//DIV[@CLASS='Tx']") 
                   ?? doc.DocumentNode.SelectSingleNode("//div[@class='Tx']")
                   ?? doc.DocumentNode.SelectSingleNode("//DIV[@class='Tx']")
                   ?? doc.DocumentNode.SelectSingleNode("//DIV[@CLASS='tx']")
                   ?? doc.DocumentNode.SelectSingleNode("//div[@class='tx']");

        if (txDiv == null) 
        {
            System.Diagnostics.Debug.WriteLine("[PesnVozrImport] No DIV with CLASS='Tx' found in part");
            return null;
        }

        // Получаем все <p> теги внутри DIV
        var pTags = txDiv.SelectNodes(".//p");
        if (pTags == null || pTags.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[PesnVozrImport] No <p> tags found in DIV");
            return null;
        }

        var lines = new List<string>();
        foreach (var p in pTags)
        {
            var pText = p.InnerText.Trim();
            if (!string.IsNullOrWhiteSpace(pText))
            {
                lines.Add(pText);
            }
        }

        System.Diagnostics.Debug.WriteLine($"[PesnVozrImport] Found {lines.Count} <p> tags in DIV");
        
        if (lines.Count == 0) return null;

        // Парсим заголовок: ищем строку с №
        int? songNumber = null;
        string? author = null;
        int startIndex = 0;

        for (int i = 0; i < Math.Min(3, lines.Count); i++)
        {
            var line = lines[i];
            var numberMatch = SongNumberRegex.Match(line);
            
            if (numberMatch.Success && line.Contains("№"))
            {
                int.TryParse(numberMatch.Groups[1].Value, out var num);
                songNumber = num;
                
                // Автор - всё что после номера
                var authorPart = line.Substring(numberMatch.Index + numberMatch.Length).Trim();
                if (!string.IsNullOrWhiteSpace(authorPart))
                {
                    author = authorPart;
                }
                
                startIndex = i + 1;
                break;
            }
        }

        // Собираем куплеты
        var sections = new List<SongSection>();
        var currentSectionLines = new List<string>();
        int? currentVerseNumber = null;
        int sectionOrder = 0;

        foreach (var line in lines.Skip(startIndex))
        {
            var verseMatch = VerseNumberRegex.Match(line);
            
            if (verseMatch.Success)
            {
                // Сохраняем предыдущий куплет
                if (currentSectionLines.Count > 0)
                {
                    sections.Add(CreateSection(currentSectionLines, currentVerseNumber, sectionOrder++));
                    currentSectionLines.Clear();
                }

                int.TryParse(verseMatch.Groups[1].Value, out var verseNum);
                currentVerseNumber = verseNum;
                
                // Добавляем строку без номера
                var content = Regex.Replace(line, verseMatch.Value, "");
                if (!string.IsNullOrWhiteSpace(content))
                {
                    currentSectionLines.Add(content);
                }
            }
            else if (line.StartsWith("<br>"))
            {
                // Продолжение текущего куплета
                var content = line.Substring(4).Trim();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    currentSectionLines.Add(content);
                }
            }
            else if (line.Contains("Припев"))
            {
                // Припев - сохраняем как отдельную секцию
                if (currentSectionLines.Count > 0)
                {
                    sections.Add(CreateSection(currentSectionLines, currentVerseNumber, sectionOrder++));
                    currentSectionLines.Clear();
                }
                currentVerseNumber = null;
                // Пропускаем строку с "Припев:"
            }
            else
            {
                // Обычная строка текста
                currentSectionLines.Add(line);
            }
        }

        // Добавляем последний куплет
        if (currentSectionLines.Count > 0)
        {
            sections.Add(CreateSection(currentSectionLines, currentVerseNumber, sectionOrder));
        }

        if (sections.Count == 0) return null;

        // Формируем заголовок из первого куплета
        var title = BuildTitleFromFirstVerse(sections);

        return new Song
        {
            Id = Guid.NewGuid(),
            Title = title,
            Subtitle = null,
            Number = songNumber,
            CollectionId = collectionId,
            Language = "ru",
            IsPublished = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Sections = sections
        };
    }

    private SongSection CreateSection(List<string> lines, int? verseNumber, int order)
    {
        var content = string.Join("\n", lines);
        var heading = verseNumber.HasValue ? $"Куплет {verseNumber.Value}" : $"Куплет {order + 1}";

        return new SongSection
        {
            Id = Guid.NewGuid(),
            SectionType = SectionType.Verse,
            Order = order,
            Heading = heading,
            Content = content,
            Timing = new Data.ValueObjects.SectionTiming(null, null, null)
        };
    }

    private string BuildTitleFromFirstVerse(List<SongSection> sections)
    {
        if (sections.Count == 0) return "Без названия";

        var firstSection = sections[0];
        var firstLine = firstSection.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstLine)) return "Без названия";

        // Берём первые 6-8 слов
        var words = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var take = Math.Min(words.Length, 6);
        var title = string.Join(' ', words.Take(take));

        // Убираем пунктуацию в конце
        title = title.TrimEnd(',', '.', ';', ':', '—', '-');

        if (title.Length > 80)
        {
            title = title.Substring(0, 80).TrimEnd();
        }

        return title;
    }
}
