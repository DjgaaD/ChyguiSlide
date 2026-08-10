using System;
using System.IO;

namespace ChyguiSlide.Data;

public static class DatabasePathProvider
{
    private const string DatabaseFileName = "catalog.db";

    public static string GetDatabasePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChyguiSlide");
        Directory.CreateDirectory(root);
        return Path.Combine(root, DatabaseFileName);
    }
}

