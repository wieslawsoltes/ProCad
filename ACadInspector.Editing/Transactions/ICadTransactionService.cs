using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;

namespace ACadInspector.Editing.Transactions;

public interface ICadTransaction : IDisposable
{
    CadDocumentSessionId SessionId { get; }
    string Name { get; }
    bool IsCompleted { get; }
    void AddOperation(CadOperation operation);
    CadOperationBatch Commit(Guid actorId, long baseVersion, long sequence);
    void Rollback();
}

public interface ICadTransactionService
{
    ICadTransaction Begin(CadDocumentSessionId sessionId, string name);
}
