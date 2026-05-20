using System.Threading;
using System.Threading.Tasks;

namespace ProCad.Scripting;

public interface ICadScriptHost
{
    Task<CadScriptExecutionResult> ExecuteAsync(string code, CadScriptGlobals globals, CancellationToken cancellationToken);
}
