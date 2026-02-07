using ACadInspector.Editing.Dependencies;
using ACadInspector.Editing.EntityIndex;
using ACadInspector.Editing.Constraints;
using ACadInspector.Editing.Undo;
using ACadInspector.Editing.Transactions;
using ACadSharp;

namespace ACadInspector.Editing.Sessions;

public sealed class CadEditorSessionFactory : ICadEditorSessionFactory
{
    public ICadEditorSession Create(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new CadDocumentSession(
            document,
            new CadEntityIndex(),
            new CadUndoRedoService(),
            new CadTransactionService(),
            new CadDependencyResolver(),
            new CadConstraintService(
                new CadConstraintStore(),
                new CadConstraintJsonSnapshotCodec()),
            new CadGeometricConstraintSolver());
    }
}
