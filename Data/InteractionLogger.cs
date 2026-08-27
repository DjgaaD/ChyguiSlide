using System;
using System.Diagnostics;
using System.IO;

namespace ChyguiSlide.Data;

internal enum InteractionLogLevel
{
    /// <summary>Горячие пути: только Debug в DEBUG-сборке, без диска и UI.</summary>
    Verbose = 0,

    /// <summary>Обычные события: один файл в AppData, без UI.</summary>
    Info = 1,

    /// <summary>Ошибки: AppData + UI-логгер.</summary>
    Error = 2,
}

/// <summary>Лёгкий лог взаимодействия (проекция / медиа). Не спамить Verbose на диск.</summary>
internal static class InteractionLogger
{
    private static readonly object Gate = new();
    private static Action<string>? _uiLoggerCallback;

    public static void SetUiLoggerCallback(Action<string>? callback)
    {
        _uiLoggerCallback = callback;
    }

    public static void Log(string message) => Write(InteractionLogLevel.Info, message);

    public static void LogVerbose(string message) => Write(InteractionLogLevel.Verbose, message);

    public static void LogError(string message) => Write(InteractionLogLevel.Error, message);

    private static void Write(InteractionLogLevel level, string message)
    {
        try
        {
#if DEBUG
            Debug.WriteLine($"[Interaction] {message}");
#endif
            if (level == InteractionLogLevel.Verbose)
            {
                return;
            }

            var line = $"[{DateTimeOffset.Now:O}] {message}" + Environment.NewLine;
            var path = AppPaths.GetLogPath("interaction.log");
            lock (Gate)
            {
                File.AppendAllText(path, line);
#if DEBUG
                try
                {
                    var projectLogsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                    Directory.CreateDirectory(projectLogsDir);
                    File.AppendAllText(Path.Combine(projectLogsDir, "interaction.log"), line);
                }
                catch
                {
                    // ignore project-folder write errors
                }
#endif
            }

            if (level >= InteractionLogLevel.Error)
            {
                _uiLoggerCallback?.Invoke(message);
            }
        }
        catch
        {
            // ignore logging errors
        }
    }
}
