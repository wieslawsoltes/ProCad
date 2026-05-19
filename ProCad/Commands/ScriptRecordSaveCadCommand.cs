using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Globalization;
using ProCad.Editing.Commands;
using ProCad.Services;

namespace ProCad.Commands;

public sealed class ScriptRecordSaveCadCommand : ICadDescribedCommandHandler
{
    private readonly ICadCommandScriptRecordingService _recording;

    public ScriptRecordSaveCadCommand(ICadCommandScriptRecordingService recording)
    {
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
    }

    public string Name => "SCRIPTRECSAVE";
    public IReadOnlyList<string> Aliases => ["SCRSAVE"];
    public CadCommandDescriptor Descriptor => new(
        Name,
        Aliases,
        "Saves current script recording to a .scr file.",
        new[]
        {
            new CadCommandSyntax(
                Usage: "SCRIPTRECSAVE <path> [NOHEADER]",
                Description: "Writes captured commands into script file.",
                Parameters: new[]
                {
                    new CadCommandParameterDescriptor(
                        Name: "path",
                        Kind: CadCommandParameterKind.Text,
                        IsOptional: false,
                        Description: "Destination .scr file path."),
                    new CadCommandParameterDescriptor(
                        Name: "mode",
                        Kind: CadCommandParameterKind.Keyword,
                        IsOptional: true,
                        Description: "Optional save mode.")
                },
                Keywords: new[]
                {
                    new CadCommandKeywordDescriptor("NOHEADER", "Save without metadata header comments.")
                })
        });

    public bool CanExecute(CadCommandContext context)
    {
        return true;
    }

    public async ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        if (context.Arguments.Count == 0)
        {
            return CadCommandResult.Fail("Usage: SCRIPTRECSAVE <path> [NOHEADER]");
        }

        var rawPath = context.Arguments[0].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return CadCommandResult.Fail("Usage: SCRIPTRECSAVE <path> [NOHEADER]");
        }

        var includeHeader = true;
        if (context.Arguments.Count > 1 &&
            context.Arguments[1].Equals("NOHEADER", StringComparison.OrdinalIgnoreCase))
        {
            includeHeader = false;
        }

        try
        {
            var result = await _recording
                .SaveAsync(rawPath, includeHeader, context.CancellationToken)
                .ConfigureAwait(false);
            return CadCommandResult.Ok(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Saved {result.EntryCount} command(s) to '{result.Path}'."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return CadCommandResult.Fail($"Unable to save script recording: {ex.Message}");
        }
    }
}
