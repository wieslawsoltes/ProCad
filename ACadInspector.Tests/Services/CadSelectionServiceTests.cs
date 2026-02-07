using ACadInspector.Editing.Selection;
using ACadInspector.Services;
using ACadSharp.Entities;
using Xunit;

namespace ACadInspector.Tests.Services;

public sealed class CadSelectionServiceTests
{
    [Fact]
    public void SetPrimarySelection_UpdatesPrimaryWithoutDroppingSelectionSet()
    {
        var service = new CadSelectionService();
        var first = new Line();
        var second = new Circle();

        service.ApplySelection(new object?[] { first, second }, CadSelectionMode.Replace);
        Assert.Null(service.SelectedObject);

        var changed = service.SetPrimarySelection(second);

        Assert.True(changed);
        Assert.Same(second, service.SelectedObject);
        Assert.Equal(2, service.SelectedObjects.Count);
        Assert.Contains(first, service.SelectedObjects);
        Assert.Contains(second, service.SelectedObjects);
    }

    [Fact]
    public void SetPrimarySelection_IgnoresObjectsOutsideSelectionSet()
    {
        var service = new CadSelectionService();
        var selected = new Line();
        var outside = new Circle();

        service.ApplySelection([selected], CadSelectionMode.Replace);

        var changed = service.SetPrimarySelection(outside);

        Assert.False(changed);
        Assert.Same(selected, service.SelectedObject);
        Assert.Single(service.SelectedObjects);
    }
}
