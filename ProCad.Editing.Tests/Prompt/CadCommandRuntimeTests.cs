using ProCad.Editing.Commands;
using ProCad.Editing.Prompt;
using ProCad.Editing.Undo;
using Xunit;

namespace ProCad.Editing.Tests.Prompt;

public sealed class CadCommandRuntimeTests
{
    [Fact]
    public void Preview_ProvidesCompletionsAndParameterHelp()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());
        var intellisense = new CadCommandIntellisenseService(registry);
        var runtime = new CadCommandRuntime(registry, intellisense);

        var state = runtime.Preview("EC", 2);

        Assert.Contains(state.Completions, item => item.Value == "ECHO");
        Assert.Contains("Unknown command", state.ParameterHelp);
    }

    [Fact]
    public async Task SubmitAsync_EmptyInput_RepeatsLastCommand()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());
        var intellisense = new CadCommandIntellisenseService(registry);
        var runtime = new CadCommandRuntime(registry, intellisense);

        var first = await runtime.SubmitAsync("ECHO one", session: null);
        Assert.True(first.Result?.Success);
        Assert.Equal("one", first.Result?.Message);

        var second = await runtime.SubmitAsync(string.Empty, session: null);
        Assert.True(second.Result?.Success);
        Assert.Equal("one", second.Result?.Message);
    }

    [Fact]
    public async Task SubmitTokenAsync_AppendsTokenAndCommits()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());
        var intellisense = new CadCommandIntellisenseService(registry);
        var runtime = new CadCommandRuntime(registry, intellisense);

        runtime.BeginCommand("ECHO");
        var preview = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, "hello"), session: null);
        Assert.True(preview.Handled);
        Assert.Null(preview.Result);

        var result = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, "world"), session: null, commit: true);
        Assert.True(result.Result?.Success);
        Assert.Equal("hello world", result.Result?.Message);
    }

    [Fact]
    public async Task SubmitTokenAsync_KeywordUndo_RemovesLastToken()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());
        var intellisense = new CadCommandIntellisenseService(registry);
        var runtime = new CadCommandRuntime(registry, intellisense);

        runtime.BeginCommand("ECHO");
        await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, "one"), session: null);
        await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, "two"), session: null);
        await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Keyword, "UNDO"), session: null);

        var result = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, "three"), session: null, commit: true);

        Assert.True(result.Result?.Success);
        Assert.Equal("one three", result.Result?.Message);
    }

    [Fact]
    public async Task SubmitAsync_TransparentCommand_DoesNotDropActiveSession()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());
        var intellisense = new CadCommandIntellisenseService(registry);
        var runtime = new CadCommandRuntime(registry, intellisense);

        runtime.BeginCommand("ECHO");
        await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, "base"), session: null);

        var transparent = await runtime.SubmitAsync("'ECHO temp", session: null);
        Assert.True(transparent.Result?.Success);
        Assert.Equal("temp", transparent.Result?.Message);
        Assert.Equal("ECHO", runtime.State.ActiveCommand);

        var result = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, "tail"), session: null, commit: true);
        Assert.True(result.Result?.Success);
        Assert.Equal("base tail", result.Result?.Message);
    }

    [Fact]
    public async Task SubmitTokenAsync_EnforcesTypedSlots_ForCoordinateAndDistance()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new SlotCommand());
        var intellisense = new CadCommandIntellisenseService(registry);
        var runtime = new CadCommandRuntime(registry, intellisense);

        runtime.BeginCommand("SLOT");
        var invalid = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, "not-a-point"), session: null);
        Assert.False(invalid.Handled);
        Assert.Equal("Expected coordinate for 'point'.", invalid.State.LastMessage);

        var point = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Coordinate, "1,2"), session: null);
        Assert.True(point.Handled);

        var result = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Number, "4.5"), session: null, commit: true);
        Assert.True(result.Result?.Success);
        Assert.Equal("1,2|4.5", result.Result?.Message);
    }

    [Fact]
    public async Task SubmitTokenAsync_ResolvesKeywordBranchAndRejectsUnknownKeyword()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new SlotCommand());
        var intellisense = new CadCommandIntellisenseService(registry);
        var runtime = new CadCommandRuntime(registry, intellisense);

        runtime.BeginCommand("SLOT");
        var handle = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Handle, "0xA"), session: null);
        Assert.True(handle.Handled);

        var invalidKeyword = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Text, "invalid"), session: null);
        Assert.False(invalidKeyword.Handled);
        Assert.Contains("Expected keyword", invalidKeyword.State.LastMessage);

        var result = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Keyword, "keep"), session: null, commit: true);
        Assert.True(result.Result?.Success);
        Assert.Equal("0xA|KEEP", result.Result?.Message);
    }

    [Fact]
    public async Task SubmitAsync_RaisesCommandExecuted_WithAmbientSource()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());
        var intellisense = new CadCommandIntellisenseService(registry);
        var runtime = new CadCommandRuntime(registry, intellisense);
        var events = new List<CadCommandExecutedEventArgs>();
        runtime.CommandExecuted += (_, args) => events.Add(args);

        using var scope = CadUndoExecutionContext.Push(new CadUndoRecordOptions(Source: CadUndoSource.Tool));
        var resolution = await runtime.SubmitAsync("ECHO hi", session: null);

        Assert.True(resolution.Result?.Success);
        var commandEvent = Assert.Single(events);
        Assert.Equal("ECHO hi", commandEvent.Input);
        Assert.Equal("ECHO", commandEvent.CommandName);
        Assert.Equal(CadUndoSource.Tool, commandEvent.Source);
        Assert.False(commandEvent.IsTransparent);
    }

    [Fact]
    public async Task SubmitAsync_TransparentCommand_RaisesTransparentExecutionEvent()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());
        var intellisense = new CadCommandIntellisenseService(registry);
        var runtime = new CadCommandRuntime(registry, intellisense);
        var events = new List<CadCommandExecutedEventArgs>();
        runtime.CommandExecuted += (_, args) => events.Add(args);

        runtime.BeginCommand("ECHO");
        var resolution = await runtime.SubmitAsync("'ECHO transient", session: null);

        Assert.True(resolution.Result?.Success);
        var commandEvent = Assert.Single(events);
        Assert.True(commandEvent.IsTransparent);
        Assert.Equal("ECHO transient", commandEvent.Input);
    }

    private sealed class EchoCommand : ICadDescribedCommandHandler
    {
        public string Name => "ECHO";
        public IReadOnlyList<string> Aliases => ["E"];
        public CadCommandDescriptor Descriptor => new(
            Name,
            Aliases,
            "Echoes command arguments.",
            new[]
            {
                new CadCommandSyntax(
                    Usage: "ECHO [value]",
                    Description: "Returns provided arguments.",
                    Parameters: new[]
                    {
                        new CadCommandParameterDescriptor("value", CadCommandParameterKind.Text, IsOptional: true, IsVariadic: true)
                    },
                    Keywords: new[]
                    {
                        new CadCommandKeywordDescriptor("START"),
                        new CadCommandKeywordDescriptor("STOP")
                    })
            });

        public bool CanExecute(CadCommandContext context)
        {
            return true;
        }

        public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
        {
            return ValueTask.FromResult(CadCommandResult.Ok(string.Join(' ', context.Arguments)));
        }
    }

    private sealed class SlotCommand : ICadDescribedCommandHandler
    {
        public string Name => "SLOT";
        public IReadOnlyList<string> Aliases => [];
        public CadCommandDescriptor Descriptor => new(
            Name,
            Aliases,
            "Validates typed slot contracts.",
            new[]
            {
                new CadCommandSyntax(
                    Usage: "SLOT point distance",
                    Description: "Point + distance branch.",
                    Parameters: new[]
                    {
                        new CadCommandParameterDescriptor("point", CadCommandParameterKind.Coordinate),
                        new CadCommandParameterDescriptor("distance", CadCommandParameterKind.Distance)
                    },
                    Keywords: Array.Empty<CadCommandKeywordDescriptor>(),
                    BranchId: "point-distance"),
                new CadCommandSyntax(
                    Usage: "SLOT handle mode",
                    Description: "Handle + keyword branch.",
                    Parameters: new[]
                    {
                        new CadCommandParameterDescriptor("handle", CadCommandParameterKind.Handle),
                        new CadCommandParameterDescriptor("mode", CadCommandParameterKind.Keyword)
                    },
                    Keywords: new[]
                    {
                        new CadCommandKeywordDescriptor("KEEP"),
                        new CadCommandKeywordDescriptor("REPLACE")
                    },
                    BranchId: "handle-mode")
            });

        public bool CanExecute(CadCommandContext context)
        {
            return true;
        }

        public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
        {
            return ValueTask.FromResult(CadCommandResult.Ok(string.Join('|', context.Arguments)));
        }
    }
}
