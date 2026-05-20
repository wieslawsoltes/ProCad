using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ProCad.Editing.Commands;

namespace ProCad.Commands;

public sealed class ScriptFileCadCommand : ICadDescribedCommandHandler
{
    private readonly ICadScriptCommandHost _scriptCommandHost;

    public ScriptFileCadCommand(ICadScriptCommandHost scriptCommandHost)
    {
        _scriptCommandHost = scriptCommandHost;
    }

    public string Name => "SCRIPT";
    public IReadOnlyList<string> Aliases => ["SCR"];
    public CadCommandDescriptor Descriptor => new(
        Name,
        Aliases,
        "Executes AutoCAD-style script file commands.",
        new[]
        {
            new CadCommandSyntax(
                Usage: "SCRIPT <path> [CONTINUE]",
                Description: "Runs commands from a .scr file.",
                Parameters: new[]
                {
                    new CadCommandParameterDescriptor(
                        Name: "path",
                        Kind: CadCommandParameterKind.Text,
                        IsOptional: false,
                        Description: "Path to script file."),
                    new CadCommandParameterDescriptor(
                        Name: "mode",
                        Kind: CadCommandParameterKind.Keyword,
                        IsOptional: true,
                        Description: "Execution mode.",
                        DefaultValue: "STOP",
                        Example: "CONTINUE")
                },
                Keywords: new[]
                {
                    new CadCommandKeywordDescriptor("CONTINUE", "Continue execution after errors.")
                })
        });

    public bool CanExecute(CadCommandContext context)
    {
        return context.Session is not null;
    }

    public async ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        if (context.Session is null)
        {
            return CadCommandResult.Fail("SCRIPT requires an active editing session.");
        }

        if (context.Arguments.Count == 0)
        {
            return CadCommandResult.Fail("Usage: SCRIPT <path-to-scr> [CONTINUE]");
        }

        var rawPath = context.Arguments[0].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return CadCommandResult.Fail("Usage: SCRIPT <path-to-scr> [CONTINUE]");
        }

        var fullPath = Path.GetFullPath(rawPath);
        if (!File.Exists(fullPath))
        {
            return CadCommandResult.Fail($"Script file '{fullPath}' was not found.");
        }

        string scriptText;
        try
        {
            scriptText = await File.ReadAllTextAsync(fullPath, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CadCommandResult.Fail($"Unable to read script file '{fullPath}': {ex.Message}");
        }

        var continueOnError = context.Arguments.Skip(1).Any(IsContinueOnErrorToken);
        var options = new CadScriptCommandPlaybackOptions(StopOnError: !continueOnError);
        var playback = await _scriptCommandHost.ExecuteAsync(
            scriptText,
            context.Session,
            options,
            context.CancellationToken).ConfigureAwait(false);

        if (playback.Success)
        {
            return CadCommandResult.Ok(
                $"Script '{Path.GetFileName(fullPath)}' executed {playback.ExecutedCount} command(s) successfully.");
        }

        var failed = playback.Entries.FirstOrDefault(static entry => !entry.Result.Success);
        if (failed is not null)
        {
            return CadCommandResult.Fail(
                $"Script '{Path.GetFileName(fullPath)}' failed at line {failed.LineNumber}: {failed.Result.Message}");
        }

        return CadCommandResult.Fail(
            $"Script '{Path.GetFileName(fullPath)}' failed after {playback.ExecutedCount} command(s).");
    }

    private static bool IsContinueOnErrorToken(string token)
    {
        return token.Equals("CONTINUE", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("-k", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("/k", StringComparison.OrdinalIgnoreCase);
    }
}
