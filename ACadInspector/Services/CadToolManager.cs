using System;
using System.Collections.Generic;
using System.Linq;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.Services;

public sealed partial class CadToolManager : ReactiveObject
{
    private readonly Dictionary<string, ICadTool> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    [Reactive]
    public partial ICadTool? ActiveTool { get; private set; }

    public IReadOnlyList<ICadTool> Tools => _tools.Values.ToList();

    public void RegisterTool(ICadTool tool, in CadToolContext context, bool activate = false)
    {
        if (tool is null)
        {
            throw new ArgumentNullException(nameof(tool));
        }

        _tools[tool.Id] = tool;
        if (activate || ActiveTool is null)
        {
            SetActiveTool(tool.Id, context);
        }
    }

    public bool SetActiveTool(string id, in CadToolContext context)
    {
        if (!_tools.TryGetValue(id, out var tool))
        {
            return false;
        }

        if (ReferenceEquals(ActiveTool, tool))
        {
            return true;
        }

        ActiveTool?.Deactivate(context);
        ActiveTool = tool;
        ActiveTool.Activate(context);
        return true;
    }

    public void HandleInput(in CadToolInput input, in CadToolContext context)
    {
        ActiveTool?.HandleInput(input, context);
    }
}
