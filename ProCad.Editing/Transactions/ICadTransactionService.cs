using ProCad.Editing.Identifiers;
using ProCad.Editing.Operations;

namespace ProCad.Editing.Transactions;

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
