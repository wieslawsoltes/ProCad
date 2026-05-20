using System.Linq;
using ProCad.Rendering;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

public sealed class RenderEntityOrderResolverTests
{
    [Fact]
    public void OrderEntities_UsesSortEntitiesTable()
    {
        var block = new ACadSharp.Tables.BlockRecord("ORDER_BLOCK");
        var first = new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(1, 0, 0)
        };
        var second = new Line
        {
            StartPoint = new XYZ(0, 1, 0),
            EndPoint = new XYZ(1, 1, 0)
        };
        block.Entities.Add(first);
        block.Entities.Add(second);

        var sortTable = block.CreateSortEntitiesTable();
        sortTable.Add(first, 20);
        sortTable.Add(second, 10);

        var resolver = new DefaultRenderEntityOrderResolver();
        var ordered = resolver.OrderEntities(block.Entities, block).ToArray();

        Assert.Equal(second, ordered[0]);
        Assert.Equal(first, ordered[1]);
    }
}
