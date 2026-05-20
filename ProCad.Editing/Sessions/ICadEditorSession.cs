using ProCad.Editing.EntityIndex;
using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;
using ProCad.Editing.Constraints;
using ProCad.Editing.Selection;
using ProCad.Editing.Undo;
using ACadSharp;

namespace ProCad.Editing.Sessions;

public interface ICadEditorSession
{
    CadDocumentSessionId SessionId { get; }
    CadDocument Document { get; }
    CadSelectionSet SelectionSet { get; }
    ICadEntityIndex EntityIndex { get; }
    ICadUndoRedoService UndoRedo { get; }
    ICadConstraintService Constraints { get; }
    long Revision { get; }
    bool IsDirty { get; }

    CadOperationBatch Apply(CadOperationBatch batch);
    bool TryUndo(Guid actorId, out CadOperationBatch undoBatch);
    bool TryRedo(Guid actorId, out CadOperationBatch redoBatch);
    bool SetSelection(IEnumerable<object?> selection, CadSelectionMode mode);
}

public interface ICadEditorSessionFactory
{
    ICadEditorSession Create(CadDocument document);
}
