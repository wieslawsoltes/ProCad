using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ACadInspector.Editing.Commands;
using ACadInspector.Services;

namespace ACadInspector.Commands;

public sealed class ScriptRecordCadCommand : ICadDescribedCommandHandler
{
    private readonly ICadCommandScriptRecordingService _recording;

    public ScriptRecordCadCommand(ICadCommandScriptRecordingService recording)
    {
        _recording = recording ?? throw new ArgumentNullException(nameof(recording));
    }

    public string Name => "SCRIPTREC";
    public IReadOnlyList<string> Aliases => ["SCRREC"];
    public CadCommandDescriptor Descriptor => new(
        Name,
        Aliases,
        "Controls command script recording.",
        new[]
        {
            new CadCommandSyntax(
                Usage: "SCRIPTREC [START|STOP|PAUSE|RESUME|CLEAR|STATUS] [RESET]",
                Description: "Starts, pauses, resumes, clears, or reports script recording state.",
                Parameters: new[]
                {
                    new CadCommandParameterDescriptor(
                        Name: "action",
                        Kind: CadCommandParameterKind.Keyword,
                        IsOptional: true,
                        Description: "Recorder action.",
                        DefaultValue: "STATUS"),
                    new CadCommandParameterDescriptor(
                        Name: "reset",
                        Kind: CadCommandParameterKind.Keyword,
                        IsOptional: true,
                        Description: "Clears entries before START when set to RESET.")
                },
                Keywords: new[]
                {
                    new CadCommandKeywordDescriptor("START", "Start recording commands."),
                    new CadCommandKeywordDescriptor("STOP", "Stop recording commands."),
                    new CadCommandKeywordDescriptor("PAUSE", "Pause active recording."),
                    new CadCommandKeywordDescriptor("RESUME", "Resume paused recording."),
                    new CadCommandKeywordDescriptor("CLEAR", "Clear captured commands."),
                    new CadCommandKeywordDescriptor("STATUS", "Show recorder status."),
                    new CadCommandKeywordDescriptor("RESET", "Use with START to clear previous entries.")
                })
        });

    public bool CanExecute(CadCommandContext context)
    {
        return true;
    }

    public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
    {
        var action = context.Arguments.Count == 0
            ? "STATUS"
            : context.Arguments[0].Trim();

        switch (action.ToUpperInvariant())
        {
            case "START":
            {
                var reset = context.Arguments.Count > 1 &&
                            context.Arguments[1].Equals("RESET", StringComparison.OrdinalIgnoreCase);
                _recording.Start(clearExisting: reset);
                return ValueTask.FromResult(CadCommandResult.Ok(BuildStatusMessage()));
            }
            case "STOP":
                _recording.Stop();
                return ValueTask.FromResult(CadCommandResult.Ok(BuildStatusMessage()));
            case "PAUSE":
                _recording.Pause();
                return ValueTask.FromResult(CadCommandResult.Ok(BuildStatusMessage()));
            case "RESUME":
                _recording.Resume();
                return ValueTask.FromResult(CadCommandResult.Ok(BuildStatusMessage()));
            case "CLEAR":
                _recording.Clear();
                return ValueTask.FromResult(CadCommandResult.Ok(BuildStatusMessage()));
            case "STATUS":
                return ValueTask.FromResult(CadCommandResult.Ok(BuildStatusMessage()));
            default:
                return ValueTask.FromResult(CadCommandResult.Fail("Usage: SCRIPTREC [START|STOP|PAUSE|RESUME|CLEAR|STATUS] [RESET]"));
        }
    }

    private string BuildStatusMessage()
    {
        var snapshot = _recording.Snapshot;
        var mode = snapshot.IsRecording
            ? snapshot.IsPaused
                ? "Paused"
                : "Recording"
            : "Stopped";
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Script recorder: {mode}, entries={snapshot.EntryCount}. {snapshot.StatusMessage}");
    }
}
