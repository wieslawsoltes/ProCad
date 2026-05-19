using ProCad.Editing.Commands;
using ProCad.Editing.Prompt;
using ProCad.Editing.Sessions;
using ACadSharp;
using Xunit;

namespace ProCad.Tests.Prompt;

public sealed class CadCommandRuntimeStateTests
{
    [Fact]
    public void Preview_TypedCommandText_DoesNotActivateSession()
    {
        var runtime = CreateRuntime();

        var state = runtime.Preview("LINE", 4);

        Assert.False(state.IsActive);
        Assert.Null(state.ActiveCommand);
        Assert.Equal("Command", state.Prompt);
    }

    [Fact]
    public void Cancel_ThenPreviewText_DoesNotReactivateSession()
    {
        var runtime = CreateRuntime();
        runtime.BeginCommand("LINE");
        Assert.True(runtime.State.IsActive);

        runtime.Cancel();
        var preview = runtime.Preview("LINE 0,0", 8);

        Assert.False(preview.IsActive);
        Assert.Null(preview.ActiveCommand);
        Assert.Equal("Command", preview.Prompt);
    }

    [Fact]
    public async Task SubmitAsync_IncompleteCommand_KeepsSessionActiveForContinuation()
    {
        var runtime = CreateRuntime();
        var session = new CadEditorSessionFactory().Create(new CadDocument());

        var resolution = await runtime.SubmitAsync("LINE 0,0", session);

        Assert.True(resolution.Handled);
        Assert.NotNull(resolution.Result);
        Assert.False(resolution.Result!.Success);
        Assert.True(runtime.State.IsActive);
        Assert.Equal("LINE", runtime.State.ActiveCommand);
    }

    [Fact]
    public async Task SubmitTokenAsync_CommitCompletesSession()
    {
        var runtime = CreateRuntime();
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        runtime.BeginCommand("LINE");

        await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"), session, commit: false);
        Assert.True(runtime.State.IsActive);

        var resolution = await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Coordinate, "5,0"), session, commit: true);

        Assert.True(resolution.Handled);
        Assert.NotNull(resolution.Result);
        Assert.True(resolution.Result!.Success);
        Assert.False(runtime.State.IsActive);
        Assert.Null(runtime.State.ActiveCommand);
        Assert.Equal("Command", runtime.State.Prompt);
    }

    [Fact]
    public async Task SubmitTokenAsync_EmptyCommitWithoutTokens_CancelsActiveSession()
    {
        var runtime = CreateRuntime();
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        runtime.BeginCommand("LINE");

        var resolution = await runtime.SubmitTokenAsync(
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: true);

        Assert.True(resolution.Handled);
        Assert.Null(resolution.Result);
        Assert.False(runtime.State.IsActive);
        Assert.Equal("*Cancel*", runtime.State.LastMessage);
    }

    [Fact]
    public async Task SubmitTokenAsync_EmptyCommitWithCollectedTokens_ExecutesActiveInput()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new PlineCadCommand());
        var runtime = new CadCommandRuntime(registry, new CadCommandIntellisenseService(registry));
        var session = new CadEditorSessionFactory().Create(new CadDocument());
        runtime.BeginCommand("PLINE");

        await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Coordinate, "0,0"), session, commit: false);
        await runtime.SubmitTokenAsync(new CadPromptToken(CadPromptTokenType.Coordinate, "6,0"), session, commit: false);

        var resolution = await runtime.SubmitTokenAsync(
            new CadPromptToken(CadPromptTokenType.Raw, string.Empty),
            session,
            commit: true);

        Assert.True(resolution.Handled);
        Assert.NotNull(resolution.Result);
        Assert.True(resolution.Result!.Success);
        Assert.False(runtime.State.IsActive);
    }

    private static CadCommandRuntime CreateRuntime()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new LineCadCommand());
        return new CadCommandRuntime(registry, new CadCommandIntellisenseService(registry));
    }
}
