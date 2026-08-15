using System;
using System.IO;

namespace ChyguiSlide.Data;

internal static class InteractionLogger
{
    private static readonly object _lock = new object();

    public static void Log(string message)
    {
        try
        {
            var path = AppPaths.GetLogPath("interaction.log");
            var line = $"[{DateTimeOffset.Now:O}] {message}" + Environment.NewLine;
            lock (_lock)
            {
                File.AppendAllText(path, line);
                try
                {
                    // Дублируем лог в папку проекта `./logs` для удобства локальной отладки
                    var projectLogsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                    Directory.CreateDirectory(projectLogsDir);
                    var projectPath = Path.Combine(projectLogsDir, "interaction.log");
                    File.AppendAllText(projectPath, line);
                }
                catch
                {
                    // Игнорируем ошибки записи в папку проекта
                }
            }
        }
        catch
        {
            // ignore logging errors
        }
    }
}
