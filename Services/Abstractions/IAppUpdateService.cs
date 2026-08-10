using System.Diagnostics;
using ChyguiSlide.Services.Models;

namespace ChyguiSlide.Services.Abstractions;

public interface IAppUpdateService
{
    /// <summary>Проверяет GitHub Releases на более новую версию для текущего канала.</summary>
    Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>Скачивает установщик во временную папку.</summary>
    Task<string> DownloadInstallerAsync(
        AppUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Запускает Inno Setup (тихая установка) и возвращает запущенный процесс.</summary>
    Process StartInstaller(string installerPath);
}
