using System.Collections.ObjectModel;

namespace ChyguiSlide.Services.Abstractions;

public interface ILoggingService
{
    ObservableCollection<string> Logs { get; }
    void Log(string message);
    void Clear();
    Task SaveToFileAsync(string filePath);
}
