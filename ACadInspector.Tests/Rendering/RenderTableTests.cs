using System.Linq;
using ACadInspector.Rendering;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Rendering;

public sealed class RenderTableTests
{
    [Fact]
    public void BuildScene_RendersTableGridAndText()
    {
        var document = new ACadSharp.CadDocument();
        var table = new TableEntity
        {
            InsertPoint = new XYZ(0, 0, 0),
            HorizontalDirection = XYZ.AxisX
        };

        table.Columns.Add(new TableEntity.Column { Width = 4.0 });
        var row = new TableEntity.Row { Height = 2.0 };
        var cell = new TableEntity.Cell();
        var content = new TableEntity.CellContent
        {
            ContentType = TableEntity.TableCellContentType.Value
        };
        content.Value.ValueType = TableEntity.CellValueType.String;
        content.Value.Text = "A1";
        cell.Contents.Add(content);
        row.Cells.Add(cell);
        table.Rows.Add(row);

        document.Entities.Add(table);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var primitives = scene.Layers.SelectMany(layer => layer.Primitives).ToArray();

        Assert.Contains(primitives, primitive => primitive is RenderLine || primitive is RenderPolyline);
        Assert.Contains(primitives, primitive => primitive is RenderText);
    }

    private static CadRenderSceneBuilder CreateSceneBuilder()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new TableRenderHandler(),
            new MTextRenderHandler(),
            new FallbackRenderHandler()
        };

        return new CadRenderSceneBuilder(
            new RenderEntityDispatcher(handlers),
            new DefaultRenderStyleResolver(),
            new DefaultRenderLinePatternResolver(),
            new DefaultRenderShapeResolver(),
            new DefaultRenderTextShaper(),
            new DefaultRenderEntityVisibilityResolver(),
            new DefaultRenderGeometrySampler(),
            new DefaultRenderEntityOrderResolver(),
            new RenderCacheStampProvider());
    }
}
