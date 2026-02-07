using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;

namespace ACadInspector.Editing.Transactions;

public sealed class CadTransactionService : ICadTransactionService
{
    public ICadTransaction Begin(CadDocumentSessionId sessionId, string name)
    {
        return new CadTransaction(sessionId, name);
    }

    private sealed class CadTransaction : ICadTransaction
    {
        private readonly List<CadOperation> _operations = new();

        public CadDocumentSessionId SessionId { get; }
        public string Name { get; }
        public bool IsCompleted { get; private set; }

        public CadTransaction(CadDocumentSessionId sessionId, string name)
        {
            SessionId = sessionId;
            Name = string.IsNullOrWhiteSpace(name) ? "Transaction" : name;
        }

        public void AddOperation(CadOperation operation)
        {
            ObjectDisposedException.ThrowIf(IsCompleted, this);
            ArgumentNullException.ThrowIfNull(operation);
            _operations.Add(operation);
        }

        public CadOperationBatch Commit(Guid actorId, long baseVersion, long sequence)
        {
            ObjectDisposedException.ThrowIf(IsCompleted, this);
            if (_operations.Count == 0)
            {
                throw new InvalidOperationException("Cannot commit an empty transaction.");
            }

            IsCompleted = true;
            return CadOperationBatch.Create(actorId, baseVersion, sequence, _operations.ToArray());
        }

        public void Rollback()
        {
            if (IsCompleted)
            {
                return;
            }

            _operations.Clear();
            IsCompleted = true;
        }

        public void Dispose()
        {
            Rollback();
        }
    }
}
