using ACadInspector.Editing.EntityIndex;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Constraints;
using ACadInspector.Editing.Selection;
using ACadInspector.Editing.Undo;
using ACadSharp;

namespace ACadInspector.Editing.Sessions;

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
