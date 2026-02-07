using ACadInspector.Editing.Commands;
using Xunit;

namespace ACadInspector.Editing.Tests.Commands;

public sealed class CadCommandDescriptorCatalogTests
{
    private static readonly string[] KnownCommandNames =
    [
        "LINE",
        "XLINE",
        "RAY",
        "CIRCLE",
        "ARC",
        "ELLIPSE",
        "SPLINE",
        "TEXT",
        "MTEXT",
        "DIMLINEAR",
        "DIMALIGNED",
        "DIMRADIUS",
        "DIMDIAMETER",
        "DIMANGULAR",
        "LEADER",
        "MLEADER",
        "HATCH",
        "BOUNDARY",
        "PLINE",
        "POINT",
        "INSERT",
        "XREFRELOAD",
        "XREFBIND",
        "XREFDETACH",
        "RECTANG",
        "POLYGON",
        "MOVE",
        "STRETCH",
        "ROTATE",
        "SCALE",
        "MIRROR",
        "OFFSET",
        "TRIM",
        "EXTEND",
        "BREAK",
        "JOIN",
        "FILLET",
        "CHAMFER",
        "ARRAY",
        "EXPLODE",
        "ALIGN",
        "MATCHPROP",
        "COPYCLIP",
        "CUT",
        "PASTECLIP",
        "COPY",
        "ERASE",
        "UNDO",
        "REDO",
        "CLEARSEL",
        "SCRIPT",
        "HELP"
    ];

    [Fact]
    public void Registry_UsesCatalogDescriptors_ForKnownCommandSet()
    {
        var registry = new CadCommandRegistry();
        foreach (var commandName in KnownCommandNames)
        {
            registry.Register(new StubCommand(commandName));
        }

        var descriptors = registry.GetCommandDescriptors();
        Assert.Equal(KnownCommandNames.Length, descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            Assert.NotEqual($"{descriptor.Name} command", descriptor.Description);
            Assert.NotEmpty(descriptor.Syntaxes);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Syntaxes[0].Usage));
        }
    }

    [Fact]
    public void Registry_FallsBackToDefaultDescriptor_ForUnknownCommand()
    {
        var registry = new CadCommandRegistry();
        registry.Register(new StubCommand("FOO"));

        var descriptors = registry.GetCommandDescriptors();
        var descriptor = Assert.Single(descriptors);
        Assert.Equal("FOO command", descriptor.Description);
        Assert.Equal("FOO", descriptor.Syntaxes[0].Usage);
    }

    private sealed class StubCommand : ICadCommandHandler
    {
        public StubCommand(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public IReadOnlyList<string> Aliases => Array.Empty<string>();

        public bool CanExecute(CadCommandContext context)
        {
            return true;
        }

        public ValueTask<CadCommandResult> ExecuteAsync(CadCommandContext context)
        {
            return ValueTask.FromResult(CadCommandResult.Ok(Name));
        }
    }
}
