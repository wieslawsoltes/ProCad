using System;
using System.Collections.Generic;
using System.IO;

namespace ACadInspector.Diagnostics;

public static class AppLog
{
    private const int PendingLimit = 512;
    private static readonly object Sync = new();
    private static readonly List<PendingLogEntry> PendingEntries = new();
    private static IAppLogService? _service;
    private static string? _fallbackLogPath;

    public static string Path
    {
        get
        {
            lock (Sync)
            {
                if (_service is not null)
                {
                    return _service.LogPath;
                }

                _fallbackLogPath ??= ResolveDefaultPath();
                return _fallbackLogPath;
            }
        }
    }

    public static void Write(string message)
    {
        Log(AppLogLevel.Information, message);
    }

    public static void Trace(string message, string category = "App")
    {
        Log(AppLogLevel.Trace, message, category);
    }

    public static void Debug(string message, string category = "App")
    {
        Log(AppLogLevel.Debug, message, category);
    }

    public static void Info(string message, string category = "App")
    {
        Log(AppLogLevel.Information, message, category);
    }

    public static void Warn(string message, string category = "App")
    {
        Log(AppLogLevel.Warning, message, category);
    }

    public static void Error(string message, string category = "App", Exception? exception = null)
    {
        Log(AppLogLevel.Error, message, category, exception);
    }

    public static void Critical(string message, string category = "App", Exception? exception = null)
    {
        Log(AppLogLevel.Critical, message, category, exception);
    }

    public static void Configure(IAppLogService service)
    {
        if (service is null)
        {
            throw new ArgumentNullException(nameof(service));
        }

        PendingLogEntry[] pending;
        lock (Sync)
        {
            _service = service;
            pending = PendingEntries.ToArray();
            PendingEntries.Clear();
        }

        foreach (var entry in pending)
        {
            service.Log(entry.Level, entry.Category, entry.Message, entry.Exception);
        }
    }

    public static void Log(
        AppLogLevel level,
        string message,
        string category = "App",
        Exception? exception = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            IAppLogService? service;
            lock (Sync)
            {
                service = _service;
            }

            if (service is not null)
            {
                service.Log(level, category, message, exception);
                return;
            }

            WriteFallback(level, category, message, exception);
            EnqueuePending(level, category, message, exception);
        }
        catch
        {
            // Avoid throwing during startup logging.
        }
    }

    public static string ResolveDefaultPath()
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

    private static void EnqueuePending(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception)
    {
        lock (Sync)
        {
            PendingEntries.Add(new PendingLogEntry(level, category, message, exception));
            if (PendingEntries.Count > PendingLimit)
            {
                PendingEntries.RemoveAt(0);
            }
        }
    }

    private static void WriteFallback(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception)
    {
        var path = Path;
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = $"{DateTimeOffset.Now:O} [{level}] [{category}] {message}";
        if (exception is not null)
        {
            line = $"{line}{Environment.NewLine}{exception}";
        }

        File.AppendAllText(path, $"{line}{Environment.NewLine}");
    }

    private readonly record struct PendingLogEntry(
        AppLogLevel Level,
        string Category,
        string Message,
        Exception? Exception);
}
