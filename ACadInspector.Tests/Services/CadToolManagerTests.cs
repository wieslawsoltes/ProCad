using ACadInspector.Services;
using Xunit;

namespace ACadInspector.Tests.Services;

public sealed class CadToolManagerTests
{
    [Fact]
    public void RegisterTool_SetsActiveAndCallsActivate()
    {
        var manager = new CadToolManager();
        var context = new CadToolContext(scene: null, spatialIndex: null, new CadSelectionService(), new CadSelectionAnnotationService());
        var tool = new TestTool();

        manager.RegisterTool(tool, context, activate: true);

        Assert.Same(tool, manager.ActiveTool);
        Assert.Equal(1, tool.ActivateCount);
    }

    [Fact]
    public void SetActiveTool_CallsDeactivateOnPreviousTool()
    {
        var manager = new CadToolManager();
        var context = new CadToolContext(scene: null, spatialIndex: null, new CadSelectionService(), new CadSelectionAnnotationService());
        var first = new TestTool { ToolId = "first" };
        var second = new TestTool { ToolId = "second" };

        manager.RegisterTool(first, context, activate: true);
        manager.RegisterTool(second, context);

        Assert.True(manager.SetActiveTool("second", context));

        Assert.Equal(1, first.DeactivateCount);
        Assert.Equal(1, second.ActivateCount);
    }

    private sealed class TestTool : ICadTool
    {
        public string ToolId { get; set; } = "test";
        public string Id => ToolId;
        public string DisplayName => "Test";
        public int ActivateCount { get; private set; }
        public int DeactivateCount { get; private set; }
        public int HandleCount { get; private set; }

        public void Activate(in CadToolContext context)
        {
            ActivateCount++;
        }

        public void Deactivate(in CadToolContext context)
        {
            DeactivateCount++;
        }

        public void HandleInput(in CadToolInput input, in CadToolContext context)
        {
            HandleCount++;
        }
    }
}
