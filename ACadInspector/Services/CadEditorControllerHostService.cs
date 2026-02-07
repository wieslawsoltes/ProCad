using System;
using System.Collections.Generic;
using ACadInspector.Editing.Controllers;
using ACadSharp;

namespace ACadInspector.Services;

public sealed class CadEditorControllerHostService : IDisposable
{
    private readonly CadEditorSessionHostService _sessionHost;
    private readonly ICadEditorControllerFactory _controllerFactory;
    private readonly CadDocumentContextService _documentContext;
    private readonly Dictionary<CadDocument, ICadEditorController> _controllers =
        new(System.Collections.Generic.ReferenceEqualityComparer.Instance);

    public CadEditorControllerHostService(
        CadEditorSessionHostService sessionHost,
        ICadEditorControllerFactory controllerFactory,
        CadDocumentContextService documentContext)
    {
        _sessionHost = sessionHost ?? throw new ArgumentNullException(nameof(sessionHost));
        _controllerFactory = controllerFactory ?? throw new ArgumentNullException(nameof(controllerFactory));
        _documentContext = documentContext ?? throw new ArgumentNullException(nameof(documentContext));
        _sessionHost.SessionRemoved += OnSessionRemoved;
    }

    public ICadEditorController GetOrCreate(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_controllers.TryGetValue(document, out var existing))
        {
            return existing;
        }

        var session = _sessionHost.GetOrCreate(document);
        var controller = _controllerFactory.Create(document, session);
        _controllers[document] = controller;
        return controller;
    }

    public bool TryGet(CadDocument document, out ICadEditorController controller)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _controllers.TryGetValue(document, out controller!);
    }

    public ICadEditorController? GetActiveController()
    {
        var document = _documentContext.ActiveDocument?.Document;
        if (document is null)
        {
            return null;
        }

        return GetOrCreate(document);
    }

    public bool Remove(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_controllers.Remove(document, out var controller))
        {
            return false;
        }

        controller.Dispose();
        return true;
    }

    public void Clear()
    {
        foreach (var controller in _controllers.Values)
        {
            controller.Dispose();
        }

        _controllers.Clear();
    }

    public void Dispose()
    {
        _sessionHost.SessionRemoved -= OnSessionRemoved;
        Clear();
    }

    private void OnSessionRemoved(object? sender, Editing.Sessions.ICadEditorSession session)
    {
        if (session is null)
        {
            return;
        }

        Remove(session.Document);
    }
}
