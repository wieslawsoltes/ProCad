using System;
using System.Collections.ObjectModel;

namespace ACadInspector.Diagnostics;

public interface IAppLogService
{
    ReadOnlyObservableCollection<AppLogEntry> Entries { get; }

    string LogPath { get; }

    void Log(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception = null);

    void Clear();
}
