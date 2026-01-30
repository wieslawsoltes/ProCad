using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace ACadInspector.Scripting;

public sealed class CadScriptHost : ICadScriptHost
{
    private readonly ScriptOptions _scriptOptions;

    public CadScriptHost() : this(CadScriptHostOptions.CreateDefault())
    {
    }

    public CadScriptHost(CadScriptHostOptions options)
    {
        _scriptOptions = BuildOptions(options);
    }

    public async Task<CadScriptExecutionResult> ExecuteAsync(
        string code,
        CadScriptGlobals globals,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return CadScriptExecutionResult.FromDiagnostics(new[] { "Script is empty." });
        }

        var outputBuilder = new StringBuilder();
        IReadOnlyList<string> diagnostics = Array.Empty<string>();
        var stopwatch = Stopwatch.StartNew();

        var scopedGlobals = new CadScriptGlobals
        {
            Document = globals.Document,
            Documents = globals.Documents,
            Selection = globals.Selection,
            Format = globals.Format,
            DocumentName = globals.DocumentName,
            DocumentPath = globals.DocumentPath,
            CancellationToken = cancellationToken,
            Log = message =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    outputBuilder.AppendLine(message);
                    globals.Log?.Invoke(message);
                }
            }
        };

        try
        {
            var script = CSharpScript.Create(code, _scriptOptions, typeof(CadScriptGlobals));
            diagnostics = GetDiagnostics(script);
            if (diagnostics.Count > 0)
            {
                return CadScriptExecutionResult.FromDiagnostics(diagnostics);
            }

            var state = await script.RunAsync(scopedGlobals, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return CadScriptExecutionResult.FromSuccess(state.ReturnValue, outputBuilder.ToString(), diagnostics, stopwatch.Elapsed);
        }
        catch (CompilationErrorException ex)
        {
            stopwatch.Stop();
            var errors = ex.Diagnostics.Select(diag => diag.ToString()).ToArray();
            return CadScriptExecutionResult.FromDiagnostics(errors);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return CadScriptExecutionResult.FromException(new TaskCanceledException("Script cancelled."), outputBuilder.ToString(), diagnostics, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return CadScriptExecutionResult.FromException(ex, outputBuilder.ToString(), diagnostics, stopwatch.Elapsed);
        }
    }

    private static ScriptOptions BuildOptions(CadScriptHostOptions options)
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in options.Assemblies)
        {
            if (assembly is null || string.IsNullOrWhiteSpace(assembly.Location))
            {
                continue;
            }

            if (!seen.Add(assembly.Location))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        AddTrustedPlatformAssemblies(references, seen);

        return ScriptOptions.Default
            .WithReferences(references)
            .WithImports(options.Imports)
            .WithMetadataResolver(new DisabledMetadataReferenceResolver())
            .WithSourceResolver(new DisabledSourceReferenceResolver());
    }

    private static void AddTrustedPlatformAssemblies(List<MetadataReference> references, HashSet<string> seen)
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
        {
            return;
        }

        foreach (var path in tpa.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (!seen.Add(path))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(path));
        }
    }

    private static IReadOnlyList<string> GetDiagnostics(Script script)
    {
        var diagnostics = script.Compile();
        if (diagnostics.IsDefaultOrEmpty)
        {
            return Array.Empty<string>();
        }

        return diagnostics
            .Where(diag => diag.Severity == DiagnosticSeverity.Error)
            .Select(diag => diag.ToString())
            .ToArray();
    }
}
