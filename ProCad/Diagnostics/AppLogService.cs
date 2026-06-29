using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Threading;

namespace ProCad.Diagnostics;

public sealed class AppLogService : IAppLogService
{
    private const int DefaultMaxEntries = 5000;
    private readonly ObservableCollection<AppLogEntry> _entries = new();
    private readonly ReadOnlyObservableCollection<AppLogEntry> _entriesReadOnly;
    private readonly object _fileGate = new();
    private readonly Action<Action> _invokeOnUiThread;
    private readonly int _maxEntries;
    private long _sequence;

    public AppLogService()
        : this(logPath: null, maxEntries: DefaultMaxEntries, InvokeOnUiThread)
    {
    }

    public AppLogService(string? logPath, int maxEntries)
        : this(logPath, maxEntries, InvokeOnUiThread)
    {
    }

    internal AppLogService(
        string? logPath,
        int maxEntries,
        Action<Action> invokeOnUiThread)
    {
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        _invokeOnUiThread = invokeOnUiThread ?? throw new ArgumentNullException(nameof(invokeOnUiThread));
        _maxEntries = maxEntries;
        LogPath = string.IsNullOrWhiteSpace(logPath)
            ? AppLog.ResolveDefaultPath()
            : logPath;
        _entriesReadOnly = new ReadOnlyObservableCollection<AppLogEntry>(_entries);
    }

    public ReadOnlyObservableCollection<AppLogEntry> Entries => _entriesReadOnly;

    public string LogPath { get; }

    public void Log(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? "App" : category.Trim();
        var entry = new AppLogEntry(
            sequence: Interlocked.Increment(ref _sequence),
            timestampUtc: DateTimeOffset.UtcNow,
            level,
            category: normalizedCategory,
            message: message.Trim(),
            exceptionText: exception?.ToString() ?? string.Empty,
            threadId: Environment.CurrentManagedThreadId);

        WriteToFile(entry);
        _invokeOnUiThread(() => AppendEntry(entry));
    }

    public void Clear()
    {
        _invokeOnUiThread(_entries.Clear);
    }

    private void AppendEntry(AppLogEntry entry)
    {
        _entries.Add(entry);
        while (_entries.Count > _maxEntries)
        {
            _entries.RemoveAt(0);
        }
    }

    private void WriteToFile(AppLogEntry entry)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{entry.TimestampUtc:O} [{entry.LevelText}] [{entry.Category}] [T{entry.ThreadText}] {entry.Message}";
            if (!string.IsNullOrWhiteSpace(entry.ExceptionText))
            {
                line = $"{line}{Environment.NewLine}{entry.ExceptionText}";
            }

            lock (_fileGate)
            {
                File.AppendAllText(LogPath, $"{line}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging should never throw.
        }
    }

    private static void InvokeOnUiThread(Action action)
    {
        try
        {
            if (Application.Current is null)
            {
                action();
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
            }
        }
        catch
        {
            action();
        }
    }
}
