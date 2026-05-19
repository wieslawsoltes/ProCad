using System.Linq;
using System;
using ProCad.Rendering;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace ProCad.Tests.Rendering;

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

    [Fact]
    public void BuildScene_FallsBackToTextWhenCellBlockCannotResolve()
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
            ContentType = TableEntity.TableCellContentType.Block
        };
        content.Value.Text = "BLOCK_A";
        cell.Contents.Add(content);
        row.Cells.Add(cell);
        table.Rows.Add(row);

        document.Entities.Add(table);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var texts = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().ToArray();

        Assert.Contains(texts, text => text.Text == "BLOCK_A");
    }

    [Fact]
    public void BuildScene_ResolvesCellBlockByHandleWithoutTextFallback()
    {
        var document = new ACadSharp.CadDocument();
        var block = new BlockRecord("BLOCK_HANDLE");
        document.BlockRecords.Add(block);
        Assert.True(block.Handle != 0);

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
            ContentType = TableEntity.TableCellContentType.Block
        };
        content.Value.ValueType = TableEntity.CellValueType.Handle;
        content.Value.Value = block.Handle;
        cell.Contents.Add(content);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Entities.Add(table);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var texts = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().ToArray();

        Assert.Empty(texts);
    }

    [Fact]
    public void BuildScene_ResolvesCellBlockByValueStringNameWithoutTextFallback()
    {
        var document = new ACadSharp.CadDocument();
        var block = new BlockRecord("BLOCK_VALUE_ONLY");
        document.BlockRecords.Add(block);

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
            ContentType = TableEntity.TableCellContentType.Block
        };
        content.Value.ValueType = TableEntity.CellValueType.String;
        content.Value.Value = "BLOCK_VALUE_ONLY";
        cell.Contents.Add(content);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Entities.Add(table);

        var scene = CreateSceneBuilder().Build(document, new CadRenderSceneSettings());
        var texts = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderText>().ToArray();

        Assert.Empty(texts);
    }

    [Fact]
    public void BuildScene_ResolvesHandleTypedStringAsHandleBeforeName()
    {
        var document = new ACadSharp.CadDocument();
        var handleTarget = new BlockRecord("BLOCK_HANDLE_TARGET");
        handleTarget.Entities.Add(new Line
        {
            StartPoint = XYZ.Zero,
            EndPoint = new XYZ(1, 0, 0)
        });
        document.BlockRecords.Add(handleTarget);
        var handleToken = handleTarget.Handle.ToString("X");

        var conflictingName = new BlockRecord(handleToken);
        document.BlockRecords.Add(conflictingName);

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
            ContentType = TableEntity.TableCellContentType.Block
        };
        content.Value.ValueType = TableEntity.CellValueType.Handle;
        content.Value.Value = handleToken;
        cell.Contents.Add(content);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Entities.Add(table);

        var scene = CreateSceneBuilderWithInsert().Build(document, new CadRenderSceneSettings());
        var lines = scene.Layers.SelectMany(layer => layer.Primitives).OfType<RenderLine>().ToArray();

        Assert.Contains(lines, line =>
        {
            var dx = line.End.X - line.Start.X;
            var dy = line.End.Y - line.Start.Y;
            var length = MathF.Sqrt(dx * dx + dy * dy);
            return Math.Abs(length - 1f) <= 0.001f;
        });
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

    private static CadRenderSceneBuilder CreateSceneBuilderWithInsert()
    {
        var handlers = new IRenderEntityHandler[]
        {
            new TableRenderHandler(),
            new InsertRenderHandler(NullRenderXRefResolver.Instance),
            new LineRenderHandler(),
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
