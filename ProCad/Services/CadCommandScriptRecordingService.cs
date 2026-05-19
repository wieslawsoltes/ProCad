using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Editing.Prompt;
using ProCad.Editing.Undo;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.Services;

public sealed record CadScriptRecordingEntry(
    int Sequence,
    DateTimeOffset TimestampUtc,
    string Input,
    string CommandName,
    CadUndoSource Source,
    bool Success,
    string Message);

public sealed record CadScriptRecordingSnapshot(
    bool IsRecording,
    bool IsPaused,
    bool IncludeFailedCommands,
    bool IncludeMetadataComments,
    bool IncludeTimestampComments,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastRecordedAtUtc,
    int EntryCount,
    string StatusMessage,
    IReadOnlyList<CadScriptRecordingEntry> Entries);

public sealed record CadScriptRecordingSaveResult(
    string Path,
    int EntryCount,
    int LineCount);

public interface ICadCommandScriptRecordingService
{
    bool IsRecording { get; }
    bool IsPaused { get; }
    bool IncludeFailedCommands { get; set; }
    bool IncludeMetadataComments { get; set; }
    bool IncludeTimestampComments { get; set; }
    DateTimeOffset? StartedAtUtc { get; }
    DateTimeOffset? LastRecordedAtUtc { get; }
    int EntryCount { get; }
    IReadOnlyList<CadScriptRecordingEntry> Entries { get; }
    CadScriptRecordingSnapshot Snapshot { get; }
    event EventHandler<CadScriptRecordingSnapshot>? SnapshotChanged;

    void Start(bool clearExisting = false);
    void Pause();
    void Resume();
    void Stop();
    void Clear();
    void Record(CadCommandExecutedEventArgs args);
    string BuildScript(bool includeHeader = true);
    ValueTask<CadScriptRecordingSaveResult> SaveAsync(
        string path,
        bool includeHeader = true,
        CancellationToken cancellationToken = default);
}

public sealed partial class CadCommandScriptRecordingService : ReactiveObject, ICadCommandScriptRecordingService, IDisposable
{
    private static readonly HashSet<string> SuppressedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "SCRIPTREC",
        "SCRIPTRECSAVE",
        "SCRIPT",
        "SCRREC",
        "SCRSAVE",
        "SCR"
    };

    private readonly ObservableCollection<CadScriptRecordingEntry> _entries = new();
    private bool _disposed;

    [Reactive]
    public partial bool IsRecording { get; private set; }

    [Reactive]
    public partial bool IsPaused { get; private set; }

    [Reactive]
    public partial bool IncludeFailedCommands { get; set; }

    [Reactive]
    public partial bool IncludeMetadataComments { get; set; } = true;

    [Reactive]
    public partial bool IncludeTimestampComments { get; set; } = true;

    [Reactive]
    public partial DateTimeOffset? StartedAtUtc { get; private set; }

    [Reactive]
    public partial DateTimeOffset? LastRecordedAtUtc { get; private set; }

    [Reactive]
    public partial string StatusMessage { get; private set; } = "Script recorder idle.";

    public CadCommandScriptRecordingService()
    {
    }

    public IReadOnlyList<CadScriptRecordingEntry> Entries => _entries;
    public int EntryCount => _entries.Count;
    public event EventHandler<CadScriptRecordingSnapshot>? SnapshotChanged;

    public CadScriptRecordingSnapshot Snapshot => new(
        IsRecording: IsRecording,
        IsPaused: IsPaused,
        IncludeFailedCommands: IncludeFailedCommands,
        IncludeMetadataComments: IncludeMetadataComments,
        IncludeTimestampComments: IncludeTimestampComments,
        StartedAtUtc: StartedAtUtc,
        LastRecordedAtUtc: LastRecordedAtUtc,
        EntryCount: _entries.Count,
        StatusMessage: StatusMessage,
        Entries: _entries);

    public void Start(bool clearExisting = false)
    {
        if (clearExisting)
        {
            ClearInternal(updateStatus: false);
        }

        if (!IsRecording)
        {
            StartedAtUtc = DateTimeOffset.UtcNow;
            IsRecording = true;
        }

        IsPaused = false;
        StatusMessage = "Script recording started.";
        RaiseEntryStateChanged();
    }

    public void Pause()
    {
        if (!IsRecording)
        {
            StatusMessage = "Script recorder is not running.";
            RaiseEntryStateChanged();
            return;
        }

        if (!IsPaused)
        {
            IsPaused = true;
            StatusMessage = "Script recording paused.";
            RaiseEntryStateChanged();
        }
    }

    public void Resume()
    {
        if (!IsRecording)
        {
            StatusMessage = "Script recorder is not running.";
            RaiseEntryStateChanged();
            return;
        }

        if (IsPaused)
        {
            IsPaused = false;
            StatusMessage = "Script recording resumed.";
            RaiseEntryStateChanged();
        }
    }

    public void Stop()
    {
        if (!IsRecording)
        {
            StatusMessage = "Script recorder is not running.";
            RaiseEntryStateChanged();
            return;
        }

        IsRecording = false;
        IsPaused = false;
        StatusMessage = string.Create(
            CultureInfo.InvariantCulture,
            $"Script recording stopped. Captured {_entries.Count} command(s).");
        RaiseEntryStateChanged();
    }

    public void Clear()
    {
        ClearInternal(updateStatus: true);
    }

    public void Record(CadCommandExecutedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        OnRuntimeCommandExecuted(this, args);
    }

    public string BuildScript(bool includeHeader = true)
    {
        var builder = new StringBuilder(Math.Max(64, _entries.Count * 32));
        if (includeHeader)
        {
            builder.AppendLine("; ProCad command script recording");
            if (StartedAtUtc is { } startedAt)
            {
                builder.Append("; Started UTC: ");
                builder.AppendLine(startedAt.ToString("O", CultureInfo.InvariantCulture));
            }

            builder.Append("; Entries: ");
            builder.AppendLine(_entries.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
        }

        foreach (var entry in _entries)
        {
            if (!entry.Success && IncludeMetadataComments)
            {
                builder.Append("; FAILED ");
                builder.Append(entry.CommandName);
                if (!string.IsNullOrWhiteSpace(entry.Message))
                {
                    builder.Append(": ");
                    builder.Append(entry.Message.Trim());
                }

                builder.AppendLine();
            }

            if (IncludeMetadataComments)
            {
                builder.Append("; SOURCE ");
                builder.Append(entry.Source.ToString().ToUpperInvariant());
                builder.AppendLine();
                if (IncludeTimestampComments)
                {
                    builder.Append("; UTC ");
                    builder.Append(entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
                    builder.AppendLine();
                }
            }

            builder.AppendLine(entry.Input);
        }

        return builder.ToString();
    }

    public async ValueTask<CadScriptRecordingSaveResult> SaveAsync(
        string path,
        bool includeHeader = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var script = BuildScript(includeHeader);
        await File.WriteAllTextAsync(fullPath, script, cancellationToken).ConfigureAwait(false);

        var lineCount = 0;
        using (var reader = new StringReader(script))
        {
            while (reader.ReadLine() is not null)
            {
                lineCount++;
            }
        }

        StatusMessage = string.Create(
            CultureInfo.InvariantCulture,
            $"Saved script recording to '{fullPath}'.");
        RaiseEntryStateChanged();

        return new CadScriptRecordingSaveResult(
            Path: fullPath,
            EntryCount: _entries.Count,
            LineCount: lineCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    private void OnRuntimeCommandExecuted(object? sender, CadCommandExecutedEventArgs args)
    {
        if (!IsRecording || IsPaused)
        {
            return;
        }

        var input = args.Input.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var commandName = ResolveCommandName(args, input);
        if (SuppressedCommands.Contains(commandName))
        {
            return;
        }

        if (!args.Result.Success && !IncludeFailedCommands)
        {
            return;
        }

        var entry = new CadScriptRecordingEntry(
            Sequence: _entries.Count + 1,
            TimestampUtc: args.TimestampUtc,
            Input: input,
            CommandName: commandName,
            Source: args.Source,
            Success: args.Result.Success,
            Message: args.Result.Message);
        _entries.Add(entry);
        LastRecordedAtUtc = entry.TimestampUtc;
        StatusMessage = string.Create(
            CultureInfo.InvariantCulture,
            $"Recorded {entry.CommandName} ({_entries.Count}).");
        RaiseEntryStateChanged();
    }

    private void ClearInternal(bool updateStatus)
    {
        _entries.Clear();
        LastRecordedAtUtc = null;
        StartedAtUtc = IsRecording ? DateTimeOffset.UtcNow : null;
        if (updateStatus)
        {
            StatusMessage = "Script recording cleared.";
        }

        RaiseEntryStateChanged();
    }

    private void RaiseEntryStateChanged()
    {
        this.RaisePropertyChanged(nameof(Entries));
        this.RaisePropertyChanged(nameof(EntryCount));
        SnapshotChanged?.Invoke(this, Snapshot);
    }

    private static string ResolveCommandName(CadCommandExecutedEventArgs args, string input)
    {
        if (!string.IsNullOrWhiteSpace(args.CommandName))
        {
            return args.CommandName.Trim().ToUpperInvariant();
        }

        var separator = input.IndexOfAny([' ', '\t']);
        var token = separator >= 0 ? input[..separator] : input;
        return token.Trim().ToUpperInvariant();
    }
}
