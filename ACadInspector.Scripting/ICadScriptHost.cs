using System.Threading;
using System.Threading.Tasks;

namespace ACadInspector.Scripting;

public interface ICadScriptHost
{
    Task<CadScriptExecutionResult> ExecuteAsync(string code, CadScriptGlobals globals, CancellationToken cancellationToken);
}
