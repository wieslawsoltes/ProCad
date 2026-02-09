using System.Text;
using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Sessions;
using ACadSharp;
using ACadSharp.IO;
using CSMath;

namespace ACadInspector.Editing.Clipboard;

public static class CadClipboardDxfFallbackCodec
{
    public static bool TryExport(CadClipboardPayload payload, out string dxfText)
    {
        dxfText = string.Empty;
        if (payload.Entities.Count == 0)
        {
            return false;
        }

        var document = new CadDocument();
        CadClipboardDependencyResolver.EnsureDependencies(document, payload.Dependencies);

        var session = (CadDocumentSession)new CadEditorSessionFactory().Create(document);
        var operations = new List<CadOperation>(payload.Entities.Count);
        for (var index = 0; index < payload.Entities.Count; index++)
        {
            var id = CadEntityId.New();
            if (CadClipboardEntityCodec.TryDecodeCreateOperation(
                    payload.Entities[index],
                    id,
                    XYZ.Zero,
                    out var operation,
                    out _))
            {
                operations.Add(operation);
            }
        }

        if (operations.Count == 0)
        {
            return false;
        }

        var batch = CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: 1,
            operations: operations);
        session.Apply(batch);

        using var stream = new MemoryStream();
        using (var nonClosing = new NonClosingStream(stream))
        {
            DxfWriter.Write(nonClosing, document, binary: false, configuration: new DxfWriterConfiguration(), notification: null);
        }

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        dxfText = reader.ReadToEnd();
        return !string.IsNullOrWhiteSpace(dxfText);
    }

    private sealed class NonClosingStream : Stream
    {
        private readonly Stream _inner;

        public NonClosingStream(Stream inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Flush();
            }
        }
    }
}
