using ProCad.Editing.Dependencies;
using ProCad.Editing.EntityIndex;
using ProCad.Editing.Constraints;
using ProCad.Editing.Undo;
using ProCad.Editing.Transactions;
using ACadSharp;

namespace ProCad.Editing.Sessions;

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
