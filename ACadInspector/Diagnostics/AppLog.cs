using System;
using System.IO;

namespace ACadInspector.Diagnostics;

public static class AppLog
{
    private static readonly string LogPath = GetLogPath();

    public static string Path => LogPath;

    public static void Write(string message)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch
        {
            // Avoid throwing during startup logging.
        }
    }

    private static string GetLogPath()
    {
        var cwd = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            return System.IO.Path.Combine(cwd, "startup.log");
        }

        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return System.IO.Path.Combine(basePath, "ACadInspector", "startup.log");
    }
}
