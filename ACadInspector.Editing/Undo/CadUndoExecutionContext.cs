using System.Threading;

namespace ACadInspector.Editing.Undo;

public static class CadUndoExecutionContext
{
    private static readonly AsyncLocal<ScopeNode?> CurrentScope = new();

    public static CadUndoRecordOptions? Current => CurrentScope.Value?.Options;

    public static IDisposable Push(CadUndoRecordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var previous = CurrentScope.Value;
        CurrentScope.Value = new ScopeNode(options, previous);
        return new ScopeHandle(previous);
    }

    private sealed record ScopeNode(CadUndoRecordOptions Options, ScopeNode? Previous);

    private sealed class ScopeHandle : IDisposable
    {
        private readonly ScopeNode? _previous;
        private bool _disposed;

        public ScopeHandle(ScopeNode? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentScope.Value = _previous;
        }
    }
}
