using ACadInspector.Editing.Selection;
using Xunit;

namespace ACadInspector.Editing.Tests.Selection;

public sealed class CadSelectionSetTests
{
    [Fact]
    public void Apply_Replace_ReplacesSelection()
    {
        var set = new CadSelectionSet();
        var a = new object();
        var b = new object();

        Assert.True(set.Apply(new object?[] { a }, CadSelectionMode.Replace));
        Assert.True(set.Apply(new object?[] { b }, CadSelectionMode.Replace));

        Assert.Single(set.Items);
        Assert.Contains(b, set.Items);
        Assert.DoesNotContain(a, set.Items);
    }

    [Fact]
    public void Apply_Toggle_TogglesSelectionItem()
    {
        var set = new CadSelectionSet();
        var a = new object();

        Assert.True(set.Apply(new object?[] { a }, CadSelectionMode.Toggle));
        Assert.True(set.Contains(a));

        Assert.True(set.Apply(new object?[] { a }, CadSelectionMode.Toggle));
        Assert.False(set.Contains(a));
    }
}
