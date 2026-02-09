using System.Diagnostics;
using ACadInspector.Editing.Selection;
using ACadInspector.Services;
using Xunit;

namespace ACadInspector.Tests.Services;

public sealed class CadSelectionPerfGateTests
{
    [Fact]
    public void ApplySelection_BulkReplace_CompletesWithinBudget()
    {
        const int itemCount = 50_000;
        const int budgetMilliseconds = 350;
        var selection = new CadSelectionService();
        var items = new object[itemCount];
        for (var index = 0; index < items.Length; index++)
        {
            items[index] = $"entity-{index}";
        }

        var stopwatch = Stopwatch.StartNew();
        var changed = selection.ApplySelection(items, CadSelectionMode.Replace);
        stopwatch.Stop();

        Assert.True(changed);
        Assert.Equal(itemCount, selection.SelectedObjects.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds <= budgetMilliseconds,
            $"Selection replace budget exceeded: {stopwatch.ElapsedMilliseconds} ms > {budgetMilliseconds} ms.");
    }
}
