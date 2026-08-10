using ChyguiSlide.Data.Entities;

namespace ChyguiSlide.Services.Abstractions;

public interface IAnnouncementService
{
    Task<IReadOnlyList<Announcement>> GetPermanentAsync(CancellationToken cancellationToken = default);
    Task<Announcement?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Announcement> SaveAsync(Announcement announcement, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
