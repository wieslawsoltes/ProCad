using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;

namespace ACadInspector.Diagnostics;

public sealed class FastPathDiagnosticsService : ReactiveObject
{
    private const int MaxEntries = 200;
    private readonly ObservableCollection<FastPathDiagnosticEntry> _entries = new();
    private readonly ReadOnlyObservableCollection<FastPathDiagnosticEntry> _readOnlyEntries;
    private bool _isEnabled;
    private int _warningCount;
    private string _lastMessage = "No fast-path warnings recorded.";
    private string _statusText = "No fast-path warnings recorded.";

    public FastPathDiagnosticsService()
    {
        _readOnlyEntries = new ReadOnlyObservableCollection<FastPathDiagnosticEntry>(_entries);
        _entries.CollectionChanged += OnEntriesChanged;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    public ReadOnlyObservableCollection<FastPathDiagnosticEntry> Entries => _readOnlyEntries;

    public int WarningCount
    {
        get => _warningCount;
        private set => this.RaiseAndSetIfChanged(ref _warningCount, value);
    }

    public string LastMessage
    {
        get => _lastMessage;
        private set => this.RaiseAndSetIfChanged(ref _lastMessage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public void Clear()
    {
        if (_entries.Count == 0)
        {
            UpdateStatus();
            return;
        }

        _entries.Clear();
        LastMessage = "No fast-path warnings recorded.";
        UpdateStatus();
    }

    public void ReportMissingAccessor(DataGridFastPathMissingAccessorEventArgs args, DataGrid grid)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (args is null || grid is null)
        {
            return;
        }

        var entry = FastPathDiagnosticEntry.Create(args, grid);
        void AddEntry()
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
            }

            LastMessage = entry.Summary;
            UpdateStatus();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            AddEntry();
        }
        else
        {
            Dispatcher.UIThread.Post(AddEntry);
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        WarningCount = _entries.Count;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText = WarningCount == 0
            ? "No fast-path warnings recorded."
            : $"{WarningCount} warning(s). Last: {LastMessage}";
    }
}
