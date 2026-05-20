using ProCad.Editing.Commands;
using Xunit;

namespace ProCad.Editing.Tests.Commands;

public sealed class CadCommandRegistryTests
{
    [Fact]
    public async Task ExecuteAsync_ResolvesAliasAndExecutesCommand()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());

        var result = await registry.ExecuteAsync("ECHO hello world", session: null);

        Assert.True(result.Success);
        Assert.Equal("hello world", result.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesQuotedArguments()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());

        var result = await registry.ExecuteAsync("ECHO \"hello world\" next", session: null);

        Assert.True(result.Success);
        Assert.Equal("hello world next", result.Message);
    }

    [Fact]
    public void GetCommandDescriptors_ReturnsRegisteredMetadata()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());

        var descriptors = registry.GetCommandDescriptors();
        var descriptor = Assert.Single(descriptors);
        Assert.Equal("ECHO", descriptor.Name);
        Assert.Contains("E", descriptor.Aliases);
    }

    [Fact]
    public void GetCompletions_ResolvesCommandsAndKeywords()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new EchoCommand());

        var commandMatches = registry.GetCompletions("EC", 2);
        Assert.Contains(commandMatches, item => item.Value == "ECHO");

        var keywordMatches = registry.GetCompletions("ECHO st", "ECHO st".Length);
        Assert.Contains(keywordMatches, item => item.Value == "STOP");
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
                        new CadCommandParameterDescriptor("value", CadCommandParameterKind.Text, IsOptional: true)
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
}
