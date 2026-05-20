using System;
using System.Reactive.Linq;
using ProCad.Editing.Prompt;
using ReactiveUI;

namespace ProCad.Services;

public sealed class CadCommandScriptRecordingTracker : IDisposable
{
    private readonly CadEditorControllerHostService _controllerHost;
    private readonly CadDocumentContextService _documentContext;
    private readonly ICadCommandScriptRecordingService _recording;
    private readonly IDisposable _activeDocumentSubscription;
    private ICadCommandRuntime? _runtime;
    private bool _disposed;

    public CadCommandScriptRecordingTracker(
        CadEditorControllerHostService controllerHost,
        CadDocumentContextService documentContext,
        ICadCommandScriptRecordingService recording)
    {
        _controllerHost = controllerHost ?? throw new ArgumentNullException(nameof(controllerHost));
        _documentContext = documentContext ?? throw new ArgumentNullException(nameof(documentContext));
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
        _activeDocumentSubscription = _documentContext.WhenAnyValue(x => x.ActiveDocument)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => BindActiveRuntime());
        BindActiveRuntime();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnbindRuntime();
        _activeDocumentSubscription.Dispose();
    }

    private void BindActiveRuntime()
    {
        UnbindRuntime();
        var controller = _controllerHost.GetActiveController();
        _runtime = controller?.CommandRuntime;
        if (_runtime is null)
        {
            return;
        }

        _runtime.CommandExecuted += OnCommandExecuted;
    }

    private void UnbindRuntime()
    {
        if (_runtime is null)
        {
            return;
        }

        _runtime.CommandExecuted -= OnCommandExecuted;
        _runtime = null;
    }

    private void OnCommandExecuted(object? sender, CadCommandExecutedEventArgs args)
    {
        _recording.Record(args);
    }
}
