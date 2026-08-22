using System.Collections.ObjectModel;
using System.Text;
using ChyguiSlide.Services.Abstractions;

namespace ChyguiSlide.Services.Implementations;

public class LoggingService : ILoggingService
{
    private readonly ObservableCollection<string> _logs = new();
    private readonly object _lock = new();

    public ObservableCollection<string> Logs => _logs;

    public void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] {message}";

        lock (_lock)
        {
            _logs.Add(logEntry);
            
            // Ограничиваем количество логов до 1000 записей
            if (_logs.Count > 1000)
            {
                for (int i = 0; i < 100; i++)
                {
                    _logs.RemoveAt(0);
                }
            }
        }

        // Также выводим в Debug для обратной совместимости
        System.Diagnostics.Debug.WriteLine(logEntry);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _logs.Clear();
        }
    }

    public async Task SaveToFileAsync(string filePath)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                var content = new StringBuilder();
                foreach (var log in _logs)
                {
                    content.AppendLine(log);
                }
                File.WriteAllText(filePath, content.ToString(), Encoding.UTF8);
            }
        });
    }
}
