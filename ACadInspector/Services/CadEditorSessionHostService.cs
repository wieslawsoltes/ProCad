using System;
using System.Collections.Generic;
using System.Linq;
using ACadInspector.Editing.Selection;
using ACadInspector.Editing.Sessions;
using ACadSharp;
using ACadSharp.Entities;

namespace ACadInspector.Services;

public sealed class CadEditorSessionChangedEventArgs : EventArgs
{
    public CadEditorSessionChangedEventArgs(CadDocument document, long revision)
    {
        Document = document;
        Revision = revision;
    }

    public CadDocument Document { get; }
    public long Revision { get; }
}

public sealed class CadEditorSessionHostService : IDisposable
{
    private readonly ICadEditorSessionFactory _sessionFactory;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadSelectionService _selectionService;
    private readonly Dictionary<CadDocument, ICadEditorSession> _sessions =
        new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CadDocument, long> _changeStamps =
        new(System.Collections.Generic.ReferenceEqualityComparer.Instance);

    public CadEditorSessionHostService(
        ICadEditorSessionFactory sessionFactory,
        CadDocumentContextService documentContext,
        CadSelectionService selectionService)
    {
        _sessionFactory = sessionFactory;
        _documentContext = documentContext;
        _selectionService = selectionService;
        _documentContext.DocumentUnregistered += OnDocumentUnregistered;
    }

    public event EventHandler<CadEditorSessionChangedEventArgs>? SessionChanged;
    public event EventHandler<ICadEditorSession>? SessionRemoved;

    public ICadEditorSession GetOrCreate(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_sessions.TryGetValue(document, out var session))
        {
            return session;
        }

        session = _sessionFactory.Create(document);
        _sessions[document] = session;
        _changeStamps[document] = session.Revision;
        return session;
    }

    public bool TryGet(CadDocument document, out ICadEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _sessions.TryGetValue(document, out session!);
    }

    public ICadEditorSession? GetActiveSession()
    {
        var document = _documentContext.ActiveDocument?.Document;
        if (document is null)
        {
            return null;
        }

        var session = GetOrCreate(document);
        session.SetSelection(
            NormalizeSelectionForSession(session, _selectionService.SelectedObjects.Cast<object?>()),
            CadSelectionMode.Replace);
        return session;
    }

    public void SyncSelectionToSession(ICadEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.SetSelection(
            NormalizeSelectionForSession(session, _selectionService.SelectedObjects.Cast<object?>()),
            CadSelectionMode.Replace);
    }

    public void SyncSelectionToUi(ICadEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _selectionService.ApplySelection(
            NormalizeSelectionForSession(session, session.SelectionSet.Items.Cast<object?>()),
            CadSelectionMode.Replace);
    }

    public void NotifySessionChanged(ICadEditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        RefreshSelectionForChangedSession(session);
        var revision = session.Revision;
        if (_changeStamps.TryGetValue(session.Document, out var currentStamp))
        {
            if (revision <= currentStamp)
            {
                revision = currentStamp + 1;
            }
        }

        _changeStamps[session.Document] = revision;
        SessionChanged?.Invoke(this, new CadEditorSessionChangedEventArgs(session.Document, revision));
    }

    public bool Remove(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_sessions.Remove(document, out var session))
        {
            return false;
        }

        _changeStamps.Remove(document);
        DisposeSession(session);
        SessionRemoved?.Invoke(this, session);
        return true;
    }

    public void Clear()
    {
        foreach (var session in _sessions.Values.ToArray())
        {
            DisposeSession(session);
            SessionRemoved?.Invoke(this, session);
        }

        _sessions.Clear();
        _changeStamps.Clear();
    }

    public void Dispose()
    {
        _documentContext.DocumentUnregistered -= OnDocumentUnregistered;
        Clear();
    }

    private void OnDocumentUnregistered(object? sender, CadDocumentContextChangedEventArgs args)
    {
        Remove(args.Document);
    }

    private static void DisposeSession(ICadEditorSession session)
    {
        if (session is IDisposable disposable)
        {
            disposable.Dispose();
            return;
        }

        if (session is IAsyncDisposable asyncDisposable)
        {
            _ = asyncDisposable.DisposeAsync();
        }
    }

    private static IEnumerable<object?> NormalizeSelectionForSession(
        ICadEditorSession session,
        IEnumerable<object?> selection)
    {
        var normalized = new List<object?>();
        foreach (var item in selection)
        {
            if (!TryNormalizeSelectionItem(session, item, out var resolved))
            {
                continue;
            }

            normalized.Add(resolved);
        }

        return normalized;
    }

    private static bool TryNormalizeSelectionItem(
        ICadEditorSession session,
        object? item,
        out object? resolved)
    {
        resolved = null;
        if (item is null)
        {
            return false;
        }

        if (item is not Entity entity)
        {
            resolved = item;
            return true;
        }

        if (session.EntityIndex.TryGetId(entity, out _))
        {
            resolved = entity;
            return true;
        }

        if (entity.Handle != 0 &&
            session.EntityIndex.TryGetByHandle(entity.Handle, out var canonicalEntity, out _))
        {
            resolved = canonicalEntity;
            return true;
        }

        return false;
    }

    private void RefreshSelectionForChangedSession(ICadEditorSession session)
    {
        var selectedObject = _selectionService.SelectedObject;
        var selectedObjects = _selectionService.SelectedObjects.Cast<object?>().ToArray();
        if (selectedObjects.Length == 0 && selectedObject is not null)
        {
            selectedObjects = [selectedObject];
        }

        if (!SelectionTargetsDocument(session.Document, selectedObject, selectedObjects))
        {
            return;
        }

        var normalized = NormalizeSelectionForSession(session, selectedObjects)
            .Where(static item => item is not null)
            .ToArray();
        if (normalized.Length == 0)
        {
            _selectionService.ClearSelection();
            return;
        }

        var changed = _selectionService.ApplySelection(normalized, CadSelectionMode.Replace);
        if (selectedObject is not null &&
            TryNormalizeSelectionItem(session, selectedObject, out var normalizedPrimary) &&
            normalizedPrimary is not null)
        {
            _selectionService.SetPrimarySelection(normalizedPrimary);
        }

        if (!changed)
        {
            _selectionService.RefreshSelection();
        }
    }

    private bool SelectionTargetsDocument(
        CadDocument document,
        object? selectedObject,
        IReadOnlyList<object?> selectedObjects)
    {
        if (selectedObject is not null &&
            ReferenceEquals(_documentContext.ResolveDocument(selectedObject), document))
        {
            return true;
        }

        for (var index = 0; index < selectedObjects.Count; index++)
        {
            var candidate = selectedObjects[index];
            if (candidate is null)
            {
                continue;
            }

            if (ReferenceEquals(_documentContext.ResolveDocument(candidate), document))
            {
                return true;
            }
        }

        return false;
    }
}
