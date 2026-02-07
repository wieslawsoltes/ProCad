using ACadInspector.Editing.Operations;

namespace ACadInspector.Editing.Undo;

public sealed class CadUndoRedoService : ICadUndoRedoService
{
    private static readonly TimeSpan DefaultMergeWindow = TimeSpan.FromMilliseconds(250);
    private readonly Stack<CadUndoUnit> _undo = new();
    private readonly Stack<CadUndoUnit> _redo = new();

    public int UndoDepth => _undo.Count;
    public int RedoDepth => _redo.Count;

    public void Record(CadOperationBatch forward, CadOperationBatch inverse, CadUndoRecordOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(forward);
        ArgumentNullException.ThrowIfNull(inverse);

        var effectiveOptions = MergeWithAmbient(options, CadUndoExecutionContext.Current);
        var metadata = BuildMetadata(forward, effectiveOptions);
        var unit = new CadUndoUnit(forward, inverse, metadata);
        if (TryMergeWithPrevious(unit, effectiveOptions?.MergeWindow ?? DefaultMergeWindow, out var merged))
        {
            _undo.Push(merged);
        }
        else
        {
            _undo.Push(unit);
        }

        _redo.Clear();
    }

    public bool TryPopUndo(out CadOperationBatch inverseBatch)
    {
        if (!TryPopUndoUnit(out var unit))
        {
            inverseBatch = null!;
            return false;
        }

        inverseBatch = unit.Inverse;
        return true;
    }

    public bool TryPopRedo(out CadOperationBatch forwardBatch)
    {
        if (!TryPopRedoUnit(out var unit))
        {
            forwardBatch = null!;
            return false;
        }

        forwardBatch = unit.Forward;
        return true;
    }

    public bool TryPopUndoUnit(out CadUndoUnit unit)
    {
        return TryPopUndoUnit(static _ => true, out unit);
    }

    public bool TryPopRedoUnit(out CadUndoUnit unit)
    {
        return TryPopRedoUnit(static _ => true, out unit);
    }

    public bool TryPopUndoUnit(Func<CadUndoUnit, bool> predicate, out CadUndoUnit unit)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var skipped = new Stack<CadUndoUnit>();
        while (_undo.TryPop(out var popped))
        {
            if (!predicate(popped))
            {
                skipped.Push(popped);
                continue;
            }

            unit = popped;
            _redo.Push(unit);
            RestoreSkipped(_undo, skipped);
            return true;
        }

        RestoreSkipped(_undo, skipped);
        unit = default!;
        return false;
    }

    public bool TryPopRedoUnit(Func<CadUndoUnit, bool> predicate, out CadUndoUnit unit)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var skipped = new Stack<CadUndoUnit>();
        while (_redo.TryPop(out var popped))
        {
            if (!predicate(popped))
            {
                skipped.Push(popped);
                continue;
            }

            unit = popped;
            _undo.Push(unit);
            RestoreSkipped(_redo, skipped);
            return true;
        }

        RestoreSkipped(_redo, skipped);
        unit = default!;
        return false;
    }

    public IReadOnlyList<CadUndoUnit> GetUndoUnits()
    {
        return _undo.Reverse().ToArray();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private static CadUndoRecordOptions? MergeWithAmbient(CadUndoRecordOptions? options, CadUndoRecordOptions? ambient)
    {
        if (options is null)
        {
            return ambient;
        }

        if (ambient is null)
        {
            return options;
        }

        var source = options.Source == CadUndoSource.Unknown
            ? ambient.Source
            : options.Source;

        return new CadUndoRecordOptions(
            CommandId: string.IsNullOrWhiteSpace(options.CommandId) ? ambient.CommandId : options.CommandId,
            Label: string.IsNullOrWhiteSpace(options.Label) ? ambient.Label : options.Label,
            ActorId: options.ActorId ?? ambient.ActorId,
            Source: source,
            TimestampUtc: options.TimestampUtc ?? ambient.TimestampUtc,
            MergeKey: string.IsNullOrWhiteSpace(options.MergeKey) ? ambient.MergeKey : options.MergeKey,
            MergeWindow: options.MergeWindow ?? ambient.MergeWindow);
    }

    private static CadUndoMetadata BuildMetadata(CadOperationBatch forward, CadUndoRecordOptions? options)
    {
        var timestamp = options?.TimestampUtc ?? DateTimeOffset.UtcNow;
        var commandId = options?.CommandId;
        var label = options?.Label;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = string.IsNullOrWhiteSpace(commandId) ? "Edit" : commandId;
        }

        return new CadUndoMetadata(
            CommandId: commandId,
            Label: label,
            ActorId: options?.ActorId ?? forward.ActorId,
            Source: options?.Source ?? CadUndoSource.Unknown,
            TimestampUtc: timestamp,
            MergeKey: options?.MergeKey);
    }

    private bool TryMergeWithPrevious(CadUndoUnit candidate, TimeSpan mergeWindow, out CadUndoUnit merged)
    {
        merged = candidate;
        if (string.IsNullOrWhiteSpace(candidate.Metadata.MergeKey))
        {
            return false;
        }

        if (!_undo.TryPop(out var previous))
        {
            return false;
        }

        if (!CanMerge(previous, candidate, mergeWindow))
        {
            _undo.Push(previous);
            return false;
        }

        merged = Merge(previous, candidate);
        return true;
    }

    private static bool CanMerge(CadUndoUnit previous, CadUndoUnit candidate, TimeSpan mergeWindow)
    {
        if (!string.Equals(previous.Metadata.MergeKey, candidate.Metadata.MergeKey, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(previous.Metadata.CommandId, candidate.Metadata.CommandId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (previous.Metadata.ActorId != candidate.Metadata.ActorId ||
            previous.Metadata.Source != candidate.Metadata.Source)
        {
            return false;
        }

        if (mergeWindow <= TimeSpan.Zero)
        {
            return false;
        }

        var delta = candidate.Metadata.TimestampUtc - previous.Metadata.TimestampUtc;
        return delta >= TimeSpan.Zero && delta <= mergeWindow;
    }

    private static CadUndoUnit Merge(CadUndoUnit previous, CadUndoUnit candidate)
    {
        var forwardOperations = previous.Forward.Operations.Concat(candidate.Forward.Operations).ToArray();
        var inverseOperations = candidate.Inverse.Operations.Concat(previous.Inverse.Operations).ToArray();

        var forward = candidate.Forward with
        {
            BaseVersion = previous.Forward.BaseVersion,
            Operations = forwardOperations
        };
        var inverse = candidate.Inverse with
        {
            BaseVersion = candidate.Inverse.BaseVersion,
            Operations = inverseOperations
        };

        var label = string.IsNullOrWhiteSpace(candidate.Metadata.Label)
            ? previous.Metadata.Label
            : candidate.Metadata.Label;
        var metadata = candidate.Metadata with
        {
            Label = label,
            TimestampUtc = candidate.Metadata.TimestampUtc
        };

        return new CadUndoUnit(forward, inverse, metadata);
    }

    private static void RestoreSkipped(Stack<CadUndoUnit> target, Stack<CadUndoUnit> skipped)
    {
        while (skipped.TryPop(out var skippedUnit))
        {
            target.Push(skippedUnit);
        }
    }
}
