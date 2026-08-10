using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Abstractions;

public interface IPresentationImportService
{
    Task<PresentationImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default);
}












