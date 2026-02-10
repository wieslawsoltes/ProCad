using System;
using System.Globalization;

namespace ACadInspector.Diagnostics;

public sealed class AppLogEntry
{
    public AppLogEntry(
        long sequence,
        DateTimeOffset timestampUtc,
        AppLogLevel level,
        string category,
        string message,
        string exceptionText,
        int threadId)
    {
        Sequence = sequence;
        TimestampUtc = timestampUtc;
        Level = level;
        Category = category;
        Message = message;
        ExceptionText = exceptionText;
        ThreadId = threadId;
    }

    public long Sequence { get; }
    public DateTimeOffset TimestampUtc { get; }
    public AppLogLevel Level { get; }
    public string Category { get; }
    public string Message { get; }
    public string ExceptionText { get; }
    public int ThreadId { get; }

    public string TimestampLocal =>
        TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public string LevelText => Level.ToString().ToUpperInvariant();

    public string ThreadText => ThreadId.ToString(CultureInfo.InvariantCulture);
}
