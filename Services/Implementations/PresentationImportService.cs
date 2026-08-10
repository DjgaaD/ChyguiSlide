using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Models;
using A = DocumentFormat.OpenXml.Drawing;

namespace ChyguiSlide.Services.Implementations;

public class PresentationImportService : IPresentationImportService
{
    private static readonly XNamespace P =
        "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace ANs =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Pr =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public Task<PresentationImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ImportInternal(filePath), cancellationToken);
    }

    private static PresentationImportResult ImportInternal(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Файл презентации не найден.", filePath);
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        return ext switch
        {
            ".pptx" => ImportPptx(filePath),
            ".odp" => ImportOdp(filePath),
            ".ppt" => throw new NotSupportedException(
                "Формат .ppt не поддерживается. Сохраните файл как .pptx или .odp и попробуйте снова."),
            _ => throw new NotSupportedException($"Формат «{ext}» не поддерживается.")
        };
    }

    private static PresentationImportResult ImportPptx(string filePath)
    {
        // Сначала SDK; при битых Content_Types (частая ошибка RFC 2616) — разбор ZIP/XML.
        try
        {
            return ImportPptxViaOpenXml(filePath);
        }
        catch (Exception ex) when (
            ex is OpenXmlPackageException
            or ArgumentException
            or InvalidDataException
            or InvalidOperationException
            or FileFormatException)
        {
            try
            {
                return ImportPptxViaZip(filePath);
            }
            catch (Exception zipEx)
            {
                throw new InvalidOperationException(
                    "Не удалось прочитать PPTX. Файл повреждён или имеет нестандартную разметку.\n\n" +
                    ErrorDialogSafeMessage(ex) + "\n\n" + ErrorDialogSafeMessage(zipEx),
                    zipEx);
            }
        }
    }

    private static string ErrorDialogSafeMessage(Exception ex) =>
        ex.InnerException is null ? ex.Message : $"{ex.Message} ({ex.InnerException.Message})";

    private static PresentationImportResult ImportPptxViaOpenXml(string filePath)
    {
        var slides = new List<PresentationSlide>();

        using var presentation = PresentationDocument.Open(filePath, false);
        var deck = presentation.PresentationPart?.Presentation;
        if (deck?.SlideIdList is null)
        {
            return new PresentationImportResult
            {
                Title = Path.GetFileNameWithoutExtension(filePath),
                Slides = slides
            };
        }

        var slideIds = deck.SlideIdList.Elements<SlideId>().ToList();
        for (var i = 0; i < slideIds.Count; i++)
        {
            var relId = slideIds[i].RelationshipId?.Value;
            if (string.IsNullOrEmpty(relId))
            {
                continue;
            }

            if (presentation.PresentationPart?.GetPartById(relId) is not SlidePart slidePart
                || slidePart.Slide is null)
            {
                continue;
            }

            var content = ExtractTextOpenXml(slidePart);

            slides.Add(new PresentationSlide
            {
                Heading = $"Куплет {slides.Count + 1}",
                Content = string.IsNullOrWhiteSpace(content) ? "(пусто)" : content
            });
        }

        return new PresentationImportResult
        {
            Title = Path.GetFileNameWithoutExtension(filePath),
            Slides = slides
        };
    }

    private static PresentationImportResult ImportPptxViaZip(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);

        var slideTargets = GetPptxSlideTargets(archive);
        var slides = new List<PresentationSlide>();

        for (var i = 0; i < slideTargets.Count; i++)
        {
            var target = slideTargets[i];
            var entryName = NormalizeZipPath("ppt/" + target.TrimStart('/'));
            var entry = FindEntry(archive, entryName)
                        ?? FindEntry(archive, target.TrimStart('/'));
            if (entry is null)
            {
                continue;
            }

            using var stream = entry.Open();
            var doc = XDocument.Load(stream);
            var content = string.Join(Environment.NewLine, ExtractPptxParagraphs(doc));

            slides.Add(new PresentationSlide
            {
                Heading = $"Куплет {slides.Count + 1}",
                Content = string.IsNullOrWhiteSpace(content) ? "(пусто)" : content
            });
        }

        if (slides.Count == 0)
        {
            // Fallback: все slide*.xml по имени
            var slideEntries = archive.Entries
                .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                            && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                            && !e.FullName.Contains("_rels", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < slideEntries.Count; i++)
            {
                using var stream = slideEntries[i].Open();
                var doc = XDocument.Load(stream);
                var content = string.Join(Environment.NewLine, ExtractPptxParagraphs(doc));

                slides.Add(new PresentationSlide
                {
                    Heading = $"Куплет {slides.Count + 1}",
                    Content = string.IsNullOrWhiteSpace(content) ? "(пусто)" : content
                });
            }
        }

        return new PresentationImportResult
        {
            Title = Path.GetFileNameWithoutExtension(filePath),
            Slides = slides
        };
    }

    private static List<string> GetPptxSlideTargets(ZipArchive archive)
    {
        var result = new List<string>();
        var presentationEntry = FindEntry(archive, "ppt/presentation.xml");
        var relsEntry = FindEntry(archive, "ppt/_rels/presentation.xml.rels");
        if (presentationEntry is null || relsEntry is null)
        {
            return result;
        }

        Dictionary<string, string> relMap;
        using (var relStream = relsEntry.Open())
        {
            var relDoc = XDocument.Load(relStream);
            relMap = relDoc.Root?
                .Elements(Pr + "Relationship")
                .Select(e => (
                    Id: (string?)e.Attribute("Id"),
                    Target: (string?)e.Attribute("Target"),
                    Type: (string?)e.Attribute("Type")))
                .Where(x => !string.IsNullOrEmpty(x.Id) && !string.IsNullOrEmpty(x.Target))
                .Where(x => x.Type is null
                            || x.Type.EndsWith("/slide", StringComparison.OrdinalIgnoreCase)
                            || x.Target!.Contains("slides/slide", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(x => x.Id!, x => x.Target!, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using (var presStream = presentationEntry.Open())
        {
            var presDoc = XDocument.Load(presStream);
            foreach (var sldId in presDoc.Descendants(P + "sldId"))
            {
                var rid = (string?)sldId.Attribute(R + "id");
                if (rid is not null && relMap.TryGetValue(rid, out var target))
                {
                    result.Add(target.Replace('\\', '/'));
                }
            }
        }

        return result;
    }

    private static List<string> ExtractPptxParagraphs(XDocument doc)
    {
        var paragraphs = new List<string>();
        foreach (var paragraph in doc.Descendants(ANs + "p"))
        {
            var text = string.Concat(
                paragraph.Descendants(ANs + "t").Select(t => t.Value));
            if (!string.IsNullOrWhiteSpace(text))
            {
                paragraphs.Add(text.Trim());
            }
        }

        return paragraphs;
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        var normalized = NormalizeZipPath(path);
        return archive.Entries.FirstOrDefault(e =>
            string.Equals(NormalizeZipPath(e.FullName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeZipPath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static PresentationImportResult ImportOdp(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var contentEntry = archive.Entries.FirstOrDefault(e =>
            e.FullName.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
        if (contentEntry is null)
        {
            throw new InvalidOperationException("Файл ODP не содержит content.xml");
        }

        using var stream = contentEntry.Open();
        var doc = XDocument.Load(stream);
        XNamespace draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
        XNamespace text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

        var slides = new List<PresentationSlide>();
        var pages = doc.Root?
            .Descendants(draw + "page")
            .ToList() ?? new List<XElement>();

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var paragraphs = page.Descendants(text + "p")
                .Select(p => string.Concat(p.DescendantNodes().OfType<XText>().Select(t => t.Value)))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(p => p.Trim())
                .ToList();

            var content = paragraphs.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, paragraphs);

            slides.Add(new PresentationSlide
            {
                Heading = $"Куплет {slides.Count + 1}",
                Content = string.IsNullOrWhiteSpace(content) ? "(пусто)" : content
            });
        }

        return new PresentationImportResult
        {
            Title = Path.GetFileNameWithoutExtension(filePath),
            Slides = slides
        };
    }

    private static string ExtractTextOpenXml(SlidePart slidePart)
    {
        var texts = slidePart.Slide.Descendants<Shape>()
            .Select(GetShapeText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList();

        return string.Join(Environment.NewLine, texts);
    }

    private static string GetShapeText(Shape shape)
    {
        if (shape.TextBody is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var paragraph in shape.TextBody.Descendants<A.Paragraph>())
        {
            var text = string.Concat(paragraph.Descendants<A.Text>().Select(t => t.Text));
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.AppendLine(text.Trim());
            }
        }

        return sb.ToString().TrimEnd();
    }
}
