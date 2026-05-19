using System;
using System.IO;
using System.Reactive.Linq;
using System.Text;
using ProCad.Services;
using ACadSharp;
using ACadSharp.Classes;
using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.IO;
using AvaloniaEdit.Document;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadDxfRawViewModel : CadToolViewModelBase
{
    private readonly CadSelectionService _selectionService;
    private readonly CadDocumentContextService _documentContext;
    private CadDocument? _rawDxfCacheDocument;
    private long _rawDxfCacheSelectionStamp = -1;
    private string? _rawDxfCacheText;

    public TextDocument RawDxfDocument { get; } = new();

    [Reactive]
    public partial string SelectedTitle { get; set; } = "No selection";

    [Reactive]
    public partial bool IsActive { get; set; }

    public CadDxfRawViewModel(
        CadSelectionService selectionService,
        CadDocumentContextService documentContext)
    {
        _selectionService = selectionService;
        _documentContext = documentContext;

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdatePreview);

        this.WhenAnyValue(x => x.IsActive)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnIsActiveChanged);
    }

    private void UpdatePreview(object? selected)
    {
        if (!IsActive)
        {
            return;
        }

        RawDxfDocument.Text = string.Empty;

        if (selected is null)
        {
            SelectedTitle = "No selection";
            return;
        }

        SelectedTitle = CadSelectionTitleFormatter.BuildTitle(selected);

        var document = _documentContext.ResolveDocument(selected);
        if (document is null)
        {
            return;
        }

        _documentContext.TrySetActiveFromSelection(selected);
        RawDxfDocument.Text = BuildRawDxfPreview(selected, document, _selectionService.SelectionStamp);
    }

    private void OnIsActiveChanged(bool isActive)
    {
        if (!isActive)
        {
            RawDxfDocument.Text = string.Empty;
            SelectedTitle = "No selection";
            return;
        }

        UpdatePreview(_selectionService.SelectedObject);
    }

    private string BuildRawDxfPreview(object selected, CadDocument document, long selectionStamp)
    {
        var text = GetOrCreateRawDxfText(document, selectionStamp);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (selected is CadObject cadObject && cadObject.Handle != 0)
        {
            var handle = cadObject.Handle.ToString("X");
            return TryExtractObjectSegment(text, handle) ?? text;
        }

        if (selected is CadHeader)
        {
            return TryExtractSection(text, "HEADER") ?? text;
        }

        if (selected is DxfClass dxfClass)
        {
            return TryExtractClassSegment(text, dxfClass) ?? TryExtractSection(text, "CLASSES") ?? text;
        }

        if (selected is DxfClassCollection)
        {
            return TryExtractSection(text, "CLASSES") ?? text;
        }

        return text;
    }

    private string GetOrCreateRawDxfText(CadDocument document, long selectionStamp)
    {
        if (ReferenceEquals(_rawDxfCacheDocument, document) &&
            _rawDxfCacheSelectionStamp == selectionStamp &&
            _rawDxfCacheText is not null)
        {
            return _rawDxfCacheText;
        }

        using var stream = new MemoryStream();
        using var nonClosing = new NonClosingStream(stream);
        var configuration = new DxfWriterConfiguration();
        DxfWriter.Write(nonClosing, document, binary: false, configuration: configuration, notification: null);
        stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var text = reader.ReadToEnd();
        _rawDxfCacheDocument = document;
        _rawDxfCacheSelectionStamp = selectionStamp;
        _rawDxfCacheText = text;
        return text;
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

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Flush();
            }
        }
    }

    private static string? TryExtractSection(string text, string sectionName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (!string.Equals(lines[i].Trim(), "0", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(lines[i + 1].Trim(), "SECTION", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 3 >= lines.Length || !string.Equals(lines[i + 2].Trim(), "2", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(lines[i + 3].Trim(), sectionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var start = i;
            for (var j = i + 4; j + 1 < lines.Length; j++)
            {
                if (string.Equals(lines[j].Trim(), "0", StringComparison.Ordinal) &&
                    string.Equals(lines[j + 1].Trim(), "ENDSEC", StringComparison.OrdinalIgnoreCase))
                {
                    var end = j + 2;
                    return string.Join(Environment.NewLine, lines[start..end]);
                }
            }

            return string.Join(Environment.NewLine, lines[start..]);
        }

        return null;
    }

    private static string? TryExtractClassSegment(string text, DxfClass dxfClass)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var section = TryExtractSection(text, "CLASSES");
        if (string.IsNullOrWhiteSpace(section))
        {
            return null;
        }

        var lines = section.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var matchTokens = new[]
        {
            dxfClass.DxfName,
            dxfClass.CppClassName,
            dxfClass.ApplicationName
        };

        for (var i = 0; i + 1 < lines.Length; i++)
        {
            if (!string.Equals(lines[i].Trim(), "0", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(lines[i + 1].Trim(), "CLASS", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var start = i;
            var end = lines.Length;
            for (var j = i + 2; j + 1 < lines.Length; j++)
            {
                if (string.Equals(lines[j].Trim(), "0", StringComparison.Ordinal) &&
                    string.Equals(lines[j + 1].Trim(), "CLASS", StringComparison.OrdinalIgnoreCase))
                {
                    end = j;
                    break;
                }
            }

            var segment = string.Join(Environment.NewLine, lines[start..end]);
            foreach (var token in matchTokens)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (segment.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return segment;
                }
            }

            i = end - 1;
        }

        return section;
    }

    private static string? TryExtractObjectSegment(string text, string handle)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var pairCount = lines.Length / 2;
        var codePairs = new (int Code, string Value, int LineIndex)[pairCount];

        var pairIndex = 0;
        for (var i = 0; i + 1 < lines.Length; i += 2)
        {
            if (!int.TryParse(lines[i].Trim(), out var code))
            {
                continue;
            }

            codePairs[pairIndex++] = (code, lines[i + 1], i);
        }

        for (var index = 0; index < pairIndex; index++)
        {
            var pair = codePairs[index];
            if (pair.Code != 5)
            {
                continue;
            }

            if (!string.Equals(pair.Value.Trim(), handle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var start = FindStartIndex(codePairs, index);
            var end = FindEndIndex(codePairs, index, pairIndex);
            var startLine = codePairs[start].LineIndex;
            var endLine = end < pairIndex ? codePairs[end].LineIndex : lines.Length;
            return string.Join(Environment.NewLine, lines[startLine..endLine]);
        }

        return null;
    }

    private static int FindStartIndex((int Code, string Value, int LineIndex)[] pairs, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (pairs[i].Code == 0)
            {
                return i;
            }
        }

        return 0;
    }

    private static int FindEndIndex((int Code, string Value, int LineIndex)[] pairs, int index, int count)
    {
        for (var i = index + 1; i < count; i++)
        {
            if (pairs[i].Code == 0)
            {
                return i;
            }
        }

        return count;
    }
}
