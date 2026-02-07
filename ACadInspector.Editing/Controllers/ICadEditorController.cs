using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Editing.Prompt;
using ACadInspector.Editing.Sessions;
using ACadSharp;

namespace ACadInspector.Editing.Controllers;

public interface ICadEditorController : IDisposable
{
    CadDocument Document { get; }
    ICadEditorSession Session { get; }
    ICadCommandRuntime CommandRuntime { get; }
    ICadEditorContextSnapshotProvider ContextSnapshots { get; }
    void BeginCommand(string commandName);
    void CancelCommand();
    ValueTask<CadPromptResolution> SubmitAsync(string input, CancellationToken cancellationToken = default);
    ValueTask<CadPromptResolution> SubmitTokenAsync(CadPromptToken token, bool commit = false, CancellationToken cancellationToken = default);
}

public interface ICadEditorControllerFactory
{
    ICadEditorController Create(CadDocument document, ICadEditorSession session);
}
