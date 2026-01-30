using System;
using System.Collections.Generic;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class TableRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is TableEntity;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var table = (TableEntity)entity;
        if (table.Rows.Count == 0 || table.Columns.Count == 0)
        {
            return;
        }

        var basis = ResolveBasis(table);
        var xAxis = basis.XAxis;
        var yAxis = basis.YAxis;
        var origin = table.InsertPoint;

        var builder = context.GetLayerBuilder(table);
        var color = context.ResolveEntityColor(table);
        var thickness = context.ResolveLineWeight(table);
        var lineCap = context.ResolveLineCap(table);
        var lineJoin = context.ResolveLineJoin(table);
        var pattern = context.ResolveLinePattern(table);

        var columnPositions = BuildColumnPositions(table.Columns);
        var rowPositions = BuildRowPositions(table.Rows);

        RenderGridLines(
            table,
            builder,
            columnPositions,
            rowPositions,
            origin,
            xAxis,
            yAxis,
            transform,
            pattern,
            color,
            thickness,
            lineCap,
            lineJoin,
            context);

        RenderCells(
            table,
            columnPositions,
            rowPositions,
            origin,
            xAxis,
            yAxis,
            transform,
            context);
    }

    private static (XYZ XAxis, XYZ YAxis) ResolveBasis(TableEntity table)
    {
        var xAxis = table.HorizontalDirection;
        if (xAxis.IsZero())
        {
            xAxis = XYZ.AxisX;
        }

        xAxis = xAxis.Normalize();
        var normal = table.Normal.IsZero() ? XYZ.AxisZ : table.Normal.Normalize();
        var yAxis = XYZ.Cross(normal, xAxis);
        if (yAxis.IsZero())
        {
            yAxis = XYZ.AxisY;
        }
        else
        {
            yAxis = yAxis.Normalize();
        }

        return (xAxis, yAxis);
    }

    private static List<double> BuildColumnPositions(IReadOnlyList<TableEntity.Column> columns)
    {
        var positions = new List<double>(columns.Count + 1) { 0.0 };
        var x = 0.0;
        foreach (var column in columns)
        {
            x += Math.Max(0.0, column.Width);
            positions.Add(x);
        }

        return positions;
    }

    private static List<double> BuildRowPositions(IReadOnlyList<TableEntity.Row> rows)
    {
        var positions = new List<double>(rows.Count + 1) { 0.0 };
        var y = 0.0;
        foreach (var row in rows)
        {
            y += Math.Max(0.0, row.Height);
            positions.Add(y);
        }

        return positions;
    }

    private static void RenderGridLines(
        TableEntity table,
        RenderLayerBuilder builder,
        IReadOnlyList<double> columnPositions,
        IReadOnlyList<double> rowPositions,
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        Transform transform,
        RenderLinePattern pattern,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        RenderBuildContext context)
    {
        var totalHeight = rowPositions[^1];
        var totalWidth = columnPositions[^1];

        for (var i = 0; i < columnPositions.Count; i++)
        {
            var x = columnPositions[i];
            var start = origin + xAxis * x;
            var end = origin + xAxis * x - yAxis * totalHeight;
            RenderLinePatternStroker.AddLine(
                builder,
                RenderTransformUtils.Apply(transform, start),
                RenderTransformUtils.Apply(transform, end),
                pattern,
                color,
                thickness,
                lineCap,
                lineJoin,
                context.ShapeResolver,
                context.Settings);
        }

        for (var i = 0; i < rowPositions.Count; i++)
        {
            var y = rowPositions[i];
            var start = origin - yAxis * y;
            var end = origin + xAxis * totalWidth - yAxis * y;
            RenderLinePatternStroker.AddLine(
                builder,
                RenderTransformUtils.Apply(transform, start),
                RenderTransformUtils.Apply(transform, end),
                pattern,
                color,
                thickness,
                lineCap,
                lineJoin,
                context.ShapeResolver,
                context.Settings);
        }
    }

    private static void RenderCells(
        TableEntity table,
        IReadOnlyList<double> columnPositions,
        IReadOnlyList<double> rowPositions,
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        Transform transform,
        RenderBuildContext context)
    {
        var style = table.Style ?? TableStyle.Default;
        var defaultCellStyle = style.TableCellStyle ?? new TableEntity.CellStyle();
        var marginX = style.HorizontalCellMargin;
        var marginY = style.VerticalCellMargin;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var top = rowPositions[rowIndex];
            var bottom = rowPositions[rowIndex + 1];

            for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                if (columnIndex >= row.Cells.Count)
                {
                    continue;
                }

                var cell = row.Cells[columnIndex];
                var left = columnPositions[columnIndex];
                var right = columnPositions[columnIndex + 1];

                var cellStyle = cell.StyleOverride ?? defaultCellStyle;
                var fillColor = ResolveCellFillColor(cellStyle, defaultCellStyle, context.Settings.Background);
                if (cellStyle.IsFillColorOn)
                {
                    var quad = BuildCellQuad(origin, xAxis, yAxis, left, right, top, bottom, transform);
                    context.GetLayerBuilder(table).Add(new RenderFill(quad, fillColor));
                }

                var textValue = ResolveCellText(cell);
                if (string.IsNullOrWhiteSpace(textValue))
                {
                    continue;
                }

                var alignment = cellStyle.CellAlignment;
                var anchor = ResolveCellAnchor(
                    origin,
                    xAxis,
                    yAxis,
                    left,
                    right,
                    top,
                    bottom,
                    marginX,
                    marginY,
                    alignment);

                var text = CreateCellText(table, cell, cellStyle, textValue, anchor, alignment);
                context.Dispatcher.Append(text, transform, context);
            }
        }
    }

    private static IReadOnlyList<System.Numerics.Vector2> BuildCellQuad(
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        double left,
        double right,
        double top,
        double bottom,
        Transform transform)
    {
        var points = new List<System.Numerics.Vector2>(4)
        {
            RenderTransformUtils.Apply(transform, origin + xAxis * left - yAxis * top),
            RenderTransformUtils.Apply(transform, origin + xAxis * right - yAxis * top),
            RenderTransformUtils.Apply(transform, origin + xAxis * right - yAxis * bottom),
            RenderTransformUtils.Apply(transform, origin + xAxis * left - yAxis * bottom)
        };

        return points;
    }

    private static RenderColor ResolveCellFillColor(
        TableEntity.CellStyle cellStyle,
        TableEntity.CellStyle defaultStyle,
        RenderColor fallback)
    {
        var color = cellStyle.BackgroundColor;
        if (color.IsByLayer || color.IsByBlock)
        {
            color = defaultStyle.BackgroundColor;
        }

        if (color.IsByLayer || color.IsByBlock)
        {
            return fallback;
        }

        return new RenderColor(color.R, color.G, color.B, 255);
    }

    private static string ResolveCellText(TableEntity.Cell cell)
    {
        var content = cell.Content;
        if (content is null)
        {
            return string.Empty;
        }

        var value = content.Value;
        if (!string.IsNullOrWhiteSpace(value.FormattedValue))
        {
            return value.FormattedValue;
        }

        if (!string.IsNullOrWhiteSpace(value.Text))
        {
            return value.Text;
        }

        if (value.Value is not null)
        {
            return value.Value.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static XYZ ResolveCellAnchor(
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        double left,
        double right,
        double top,
        double bottom,
        double marginX,
        double marginY,
        TableEntity.Cell.CellAlignmentType alignment)
    {
        var innerLeft = left + marginX;
        var innerRight = right - marginX;
        var innerTop = top + marginY;
        var innerBottom = bottom - marginY;
        var centerX = (innerLeft + innerRight) * 0.5;
        var centerY = (innerTop + innerBottom) * 0.5;

        double x = centerX;
        double y = centerY;

        switch (alignment)
        {
            case TableEntity.Cell.CellAlignmentType.TopLeft:
                x = innerLeft;
                y = innerTop;
                break;
            case TableEntity.Cell.CellAlignmentType.TopCenter:
                x = centerX;
                y = innerTop;
                break;
            case TableEntity.Cell.CellAlignmentType.TopRight:
                x = innerRight;
                y = innerTop;
                break;
            case TableEntity.Cell.CellAlignmentType.MiddleLeft:
                x = innerLeft;
                y = centerY;
                break;
            case TableEntity.Cell.CellAlignmentType.MiddleCenter:
                x = centerX;
                y = centerY;
                break;
            case TableEntity.Cell.CellAlignmentType.MiddleRight:
                x = innerRight;
                y = centerY;
                break;
            case TableEntity.Cell.CellAlignmentType.BottomLeft:
                x = innerLeft;
                y = innerBottom;
                break;
            case TableEntity.Cell.CellAlignmentType.BottomCenter:
                x = centerX;
                y = innerBottom;
                break;
            case TableEntity.Cell.CellAlignmentType.BottomRight:
                x = innerRight;
                y = innerBottom;
                break;
        }

        return origin + xAxis * x - yAxis * y;
    }

    private static MText CreateCellText(
        TableEntity table,
        TableEntity.Cell cell,
        TableEntity.CellStyle cellStyle,
        string textValue,
        XYZ anchor,
        TableEntity.Cell.CellAlignmentType alignment)
    {
        var format = cell.Content?.Format;
        var height = format?.TextHeight ?? cellStyle.TextHeight;
        if (height <= 0)
        {
            height = 1.0;
        }

        var textStyle = format?.TextStyle ?? cellStyle.TextStyle ?? TextStyle.Default;
        var color = ResolveCellTextColor(table, cellStyle, format);

        return new MText
        {
            Value = textValue,
            InsertPoint = anchor,
            Height = height,
            AttachmentPoint = ResolveAttachmentPoint(alignment),
            Color = color,
            Layer = table.Layer,
            Style = textStyle
        };
    }

    private static Color ResolveCellTextColor(
        TableEntity table,
        TableEntity.CellStyle cellStyle,
        TableEntity.ContentFormat? format)
    {
        var color = format?.Color ?? cellStyle.TextColor;
        if (color.IsByLayer || color.IsByBlock)
        {
            return table.Color;
        }

        return color;
    }

    private static AttachmentPointType ResolveAttachmentPoint(TableEntity.Cell.CellAlignmentType alignment)
    {
        return alignment switch
        {
            TableEntity.Cell.CellAlignmentType.TopLeft => AttachmentPointType.TopLeft,
            TableEntity.Cell.CellAlignmentType.TopCenter => AttachmentPointType.TopCenter,
            TableEntity.Cell.CellAlignmentType.TopRight => AttachmentPointType.TopRight,
            TableEntity.Cell.CellAlignmentType.MiddleLeft => AttachmentPointType.MiddleLeft,
            TableEntity.Cell.CellAlignmentType.MiddleCenter => AttachmentPointType.MiddleCenter,
            TableEntity.Cell.CellAlignmentType.MiddleRight => AttachmentPointType.MiddleRight,
            TableEntity.Cell.CellAlignmentType.BottomLeft => AttachmentPointType.BottomLeft,
            TableEntity.Cell.CellAlignmentType.BottomCenter => AttachmentPointType.BottomCenter,
            TableEntity.Cell.CellAlignmentType.BottomRight => AttachmentPointType.BottomRight,
            _ => AttachmentPointType.MiddleCenter
        };
    }
}
