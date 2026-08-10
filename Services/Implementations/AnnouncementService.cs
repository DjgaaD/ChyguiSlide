using ChyguiSlide.Data;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ChyguiSlide.Services.Implementations;

public sealed class AnnouncementService : IAnnouncementService
{
    private readonly AppDbContext _db;

    public AnnouncementService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<Announcement>> GetPermanentAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Announcements
            .AsNoTracking()
            .Where(a => a.IsPermanent)
            .OrderByDescending(a => a.IsPinned)
            .ThenBy(a => a.SortOrder)
            .ThenByDescending(a => a.UpdatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Announcement?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _db.Announcements
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Announcement> SaveAsync(Announcement announcement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        announcement.Title = (announcement.Title ?? string.Empty).Trim();
        announcement.Content = (announcement.Content ?? string.Empty).Trim();
        announcement.UpdatedAt = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(announcement.Content))
        {
            throw new InvalidOperationException("Текст объявления не может быть пустым.");
        }

        if (string.IsNullOrWhiteSpace(announcement.Title))
        {
            announcement.Title = MakeTitleFromContent(announcement.Content);
        }

        var existing = await _db.Announcements
            .FirstOrDefaultAsync(a => a.Id == announcement.Id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            announcement.CreatedAt = DateTime.UtcNow;
            announcement.IsPermanent = true;
            if (announcement.SortOrder == 0)
            {
                var maxOrder = await _db.Announcements
                    .Select(a => (int?)a.SortOrder)
                    .MaxAsync(cancellationToken)
                    .ConfigureAwait(false) ?? 0;
                announcement.SortOrder = maxOrder + 1;
            }

            _db.Announcements.Add(announcement);
        }
        else
        {
            existing.Title = announcement.Title;
            existing.Content = announcement.Content;
            existing.IsPinned = announcement.IsPinned;
            existing.IsPermanent = true;
            existing.SortOrder = announcement.SortOrder;
            existing.UpdatedAt = announcement.UpdatedAt;
            announcement = existing;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return announcement;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Announcements
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return;
        }

        _db.Announcements.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string MakeTitleFromContent(string content)
    {
        var firstLine = content
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim() ?? "Объявление";

        return firstLine.Length <= 80 ? firstLine : firstLine[..77] + "…";
    }
}
