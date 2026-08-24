namespace ChyguiSlide.Services.Abstractions;

/// <summary>
/// LAN-выход для OBS: HTTP-страница с прозрачным фоном и WebSocket с текстом слайдов.
/// </summary>
public interface IObsStreamService
{
    bool IsRunning { get; }

    int Port { get; }

    /// <summary>Последняя ошибка запуска или null.</summary>
    string? LastError { get; }

    /// <summary>Запуск или перезапуск с новыми параметрами.</summary>
    Task ApplySettingsAsync(bool enabled, int port, CancellationToken cancellationToken = default);

    /// <summary>Отправить JSON-сообщение всем подключённым OBS-клиентам.</summary>
    void BroadcastJson(string json);

    /// <summary>Подложка под текст в OBS Browser Source.</summary>
    void ApplyBackdropSettings(bool enabled, double opacity);

    (bool Enabled, double Opacity) GetBackdropSettings();

    /// <summary>IPv4-адреса, на которых слушает сервер (для подсказки URL в настройках).</summary>
    IReadOnlyList<string> GetListenerAddresses();

    /// <summary>Лучший LAN IPv4 для URL на другом ПК (может не совпадать со списком listener, если bind не удался).</summary>
    string? GetPreferredLanIPv4();

    /// <summary>Слушает ли сервер хотя бы один не-localhost адрес.</summary>
    bool IsListeningOnLan();
}
