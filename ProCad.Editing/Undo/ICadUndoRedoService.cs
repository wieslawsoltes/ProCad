using System;
using ProCad.Editing.Operations;

namespace ProCad.Editing.Undo;

public enum CadUndoSource
{
    Unknown = 0,
    CommandLine,
    Tool,
    CollabReplay
}

public sealed record CadUndoMetadata(
    string? CommandId,
    string Label,
    Guid ActorId,
    CadUndoSource Source,
    DateTimeOffset TimestampUtc,
    string? MergeKey);

public sealed record CadUndoRecordOptions(
    string? CommandId = null,
    string? Label = null,
    Guid? ActorId = null,
    CadUndoSource Source = CadUndoSource.Unknown,
    DateTimeOffset? TimestampUtc = null,
    string? MergeKey = null,
    TimeSpan? MergeWindow = null);

public sealed record CadUndoUnit(
    CadOperationBatch Forward,
    CadOperationBatch Inverse,
    CadUndoMetadata Metadata);

public interface ICadUndoRedoService
{
    int UndoDepth { get; }
    int RedoDepth { get; }

    void Record(CadOperationBatch forward, CadOperationBatch inverse, CadUndoRecordOptions? options = null);
    bool TryPopUndo(out CadOperationBatch inverseBatch);
    bool TryPopRedo(out CadOperationBatch forwardBatch);
    bool TryPopUndoUnit(out CadUndoUnit unit);
    bool TryPopRedoUnit(out CadUndoUnit unit);
    bool TryPopUndoUnit(Func<CadUndoUnit, bool> predicate, out CadUndoUnit unit);
    bool TryPopRedoUnit(Func<CadUndoUnit, bool> predicate, out CadUndoUnit unit);
    IReadOnlyList<CadUndoUnit> GetUndoUnits();
    void Clear();
}
