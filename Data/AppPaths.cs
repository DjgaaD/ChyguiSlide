using System;
using System.IO;

namespace ChyguiSlide.Data;

public static class AppPaths
{
    public static string GetLocalAppDataRoot()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChyguiSlide");
        Directory.CreateDirectory(root);
        return root;
    }

    public static string EnsureLogsDir()
    {
        var dir = Path.Combine(GetLocalAppDataRoot(), "logs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetLogPath(string fileName)
    {
        return Path.Combine(EnsureLogsDir(), fileName);
    }

    public static string GetLockPath(string name)
    {
        return Path.Combine(GetLocalAppDataRoot(), name + ".lock");
    }
}
