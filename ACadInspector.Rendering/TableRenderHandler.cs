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

        var columnPositions = BuildColumnPositions(table.Columns);
        var rowPositions = BuildRowPositions(table.Rows);
        var spans = BuildCellSpans(table);
        var styles = BuildEffectiveStyles(table, spans);

        RenderBorders(
            table,
            builder,
            columnPositions,
            rowPositions,
            spans,
            styles,
            origin,
            xAxis,
            yAxis,
            transform,
            color,
            thickness,
            lineCap,
            lineJoin,
            context);

        RenderCells(
            table,
            columnPositions,
            rowPositions,
            spans,
            styles,
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

    private static void RenderBorders(
        TableEntity table,
        RenderLayerBuilder builder,
        IReadOnlyList<double> columnPositions,
        IReadOnlyList<double> rowPositions,
        CellSpanInfo[,] spans,
        EffectiveCellStyle[,] styles,
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        Transform transform,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        RenderBuildContext context)
    {
        var rows = spans.GetLength(0);
        var cols = spans.GetLength(1);
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var span = spans[row, col];
                if (!span.IsMaster)
                {
                    continue;
                }

                var style = styles[row, col];
                var left = columnPositions[col];
                var right = columnPositions[Math.Min(col + span.ColumnSpan, columnPositions.Count - 1)];
                var top = rowPositions[row];
                var bottom = rowPositions[Math.Min(row + span.RowSpan, rowPositions.Count - 1)];

                var hasTopNeighbor = row > 0 && spans[row - 1, col].RegionId == span.RegionId;
                var hasBottomNeighbor = row + span.RowSpan < rows && spans[row + span.RowSpan, col].RegionId == span.RegionId;
                var hasLeftNeighbor = col > 0 && spans[row, col - 1].RegionId == span.RegionId;
                var hasRightNeighbor = col + span.ColumnSpan < cols && spans[row, col + span.ColumnSpan].RegionId == span.RegionId;

                if (!hasTopNeighbor)
                {
                    DrawHorizontalBorder(
                        builder,
                        style,
                        table,
                        EdgeType.Top,
                        origin,
                        xAxis,
                        yAxis,
                        left,
                        right,
                        top,
                        transform,
                        color,
                        thickness,
                        lineCap,
                        lineJoin,
                        context.Settings,
                        isOuter: row == 0);
                }

                if (!hasBottomNeighbor)
                {
                    DrawHorizontalBorder(
                        builder,
                        style,
                        table,
                        EdgeType.Bottom,
                        origin,
                        xAxis,
                        yAxis,
                        left,
                        right,
                        bottom,
                        transform,
                        color,
                        thickness,
                        lineCap,
                        lineJoin,
                        context.Settings,
                        isOuter: row + span.RowSpan >= rows);
                }

                if (!hasLeftNeighbor)
                {
                    DrawVerticalBorder(
                        builder,
                        style,
                        table,
                        EdgeType.Left,
                        origin,
                        xAxis,
                        yAxis,
                        left,
                        top,
                        bottom,
                        transform,
                        color,
                        thickness,
                        lineCap,
                        lineJoin,
                        context.Settings,
                        isOuter: col == 0);
                }

                if (!hasRightNeighbor)
                {
                    DrawVerticalBorder(
                        builder,
                        style,
                        table,
                        EdgeType.Right,
                        origin,
                        xAxis,
                        yAxis,
                        right,
                        top,
                        bottom,
                        transform,
                        color,
                        thickness,
                        lineCap,
                        lineJoin,
                        context.Settings,
                        isOuter: col + span.ColumnSpan >= cols);
                }
            }
        }
    }

    private static void RenderCells(
        TableEntity table,
        IReadOnlyList<double> columnPositions,
        IReadOnlyList<double> rowPositions,
        CellSpanInfo[,] spans,
        EffectiveCellStyle[,] styles,
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        Transform transform,
        RenderBuildContext context)
    {
        var tableStyle = table.Style ?? TableStyle.Default;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];

            for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                if (columnIndex >= row.Cells.Count)
                {
                    continue;
                }

                var span = spans[rowIndex, columnIndex];
                if (!span.IsMaster)
                {
                    continue;
                }

                var cell = row.Cells[columnIndex];
                var left = columnPositions[columnIndex];
                var right = columnPositions[Math.Min(columnIndex + span.ColumnSpan, columnPositions.Count - 1)];
                var top = rowPositions[rowIndex];
                var bottom = rowPositions[Math.Min(rowIndex + span.RowSpan, rowPositions.Count - 1)];

                var cellStyle = styles[rowIndex, columnIndex];
                var fillColor = ResolveCellFillColor(cellStyle, context.Settings.Background);
                if (cellStyle.IsFillColorOn)
                {
                    var quad = BuildCellQuad(origin, xAxis, yAxis, left, right, top, bottom, transform);
                    context.GetLayerBuilder(table).Add(new RenderFill(quad, fillColor));
                }

                var alignment = cellStyle.Alignment;
                var anchor = ResolveCellAnchor(
                    origin,
                    xAxis,
                    yAxis,
                    left,
                    right,
                    top,
                    bottom,
                    cellStyle.MarginLeft,
                    cellStyle.MarginTop,
                    cellStyle.MarginRight,
                    cellStyle.MarginBottom,
                    alignment);

                if (TryCreateCellBlock(table, cell, cellStyle, anchor, out var blockEntity))
                {
                    context.Dispatcher.Append(blockEntity!, transform, context);
                    continue;
                }

                var textValue = ResolveCellText(cell);
                if (string.IsNullOrWhiteSpace(textValue))
                {
                    continue;
                }

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
        EffectiveCellStyle cellStyle,
        RenderColor fallback)
    {
        var color = cellStyle.BackgroundColor;
        if (color.IsByLayer || color.IsByBlock)
        {
            return fallback;
        }

        return new RenderColor(color.R, color.G, color.B, 255);
    }

    private static string ResolveCellText(TableEntity.Cell cell)
    {
        if (cell.Contents is null || cell.Contents.Count == 0)
        {
            return string.Empty;
        }
        
        var lines = new List<string>(cell.Contents.Count);
        foreach (var content in cell.Contents)
        {
            if (content.ContentType == TableEntity.TableCellContentType.Block)
            {
                // TODO: Render block content inside table cells.
                continue;
            }

            var text = ResolveContentText(content);
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text);
            }
        }

        return lines.Count == 0 ? string.Empty : string.Join("\\P", lines);
    }

    private static string ResolveContentText(TableEntity.CellContent content)
    {
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
        double marginLeft,
        double marginTop,
        double marginRight,
        double marginBottom,
        TableEntity.Cell.CellAlignmentType alignment)
    {
        var innerLeft = left + marginLeft;
        var innerRight = right - marginRight;
        var innerTop = top + marginTop;
        var innerBottom = bottom - marginBottom;
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
        EffectiveCellStyle cellStyle,
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
        EffectiveCellStyle cellStyle,
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

    private static bool TryCreateCellBlock(
        TableEntity table,
        TableEntity.Cell cell,
        EffectiveCellStyle style,
        XYZ anchor,
        out Entity? entity)
    {
        entity = null;
        var content = cell.Content;
        if (content is null)
        {
            return false;
        }

        if (content.ContentType != TableEntity.TableCellContentType.Block && cell.Type != TableEntity.CellType.Block)
        {
            return false;
        }

        var block = ResolveCellBlock(table, content);
        if (block is null)
        {
            // TODO: ACadSharp does not currently hydrate table cell block handles into the cell content.
            return false;
        }

        var scale = cell.BlockScale > 0 ? cell.BlockScale : 1.0;
        entity = new Insert(block)
        {
            InsertPoint = anchor,
            XScale = scale,
            YScale = scale,
            ZScale = scale,
            Rotation = cell.Rotation,
            Normal = table.Normal,
            Color = table.Color,
            LineWeight = table.LineWeight,
            Layer = table.Layer
        };
        return true;
    }

    private static BlockRecord? ResolveCellBlock(TableEntity table, TableEntity.CellContent content)
    {
        if (content.Value?.Value is BlockRecord record)
        {
            return record;
        }

        var name = content.Value?.Text ?? content.Value?.FormattedValue;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var document = table.Document;
        if (document is null)
        {
            return null;
        }

        return document.BlockRecords.TryGetValue(name, out var block) ? block : null;
    }

    private static CellSpanInfo[,] BuildCellSpans(TableEntity table)
    {
        var rows = table.Rows.Count;
        var cols = table.Columns.Count;
        var spans = new CellSpanInfo[rows, cols];

        var regionId = 1;
        for (var row = 0; row < rows; row++)
        {
            var rowCells = table.Rows[row].Cells;
            for (var col = 0; col < cols; col++)
            {
                if (spans[row, col].IsAssigned)
                {
                    continue;
                }

                if (col >= rowCells.Count)
                {
                    spans[row, col] = CellSpanInfo.CreateMaster(regionId++, row, col, 1, 1);
                    continue;
                }

                var cell = rowCells[col];
                var spanX = Math.Max(1, cell.BorderWidth);
                var spanY = Math.Max(1, cell.BorderHeight);
                var isMaster = spanX > 1 || spanY > 1;
                if (!isMaster && cell.MergedValue != 0)
                {
                    isMaster = true;
                }

                if (!isMaster)
                {
                    spans[row, col] = CellSpanInfo.CreateMaster(regionId++, row, col, 1, 1);
                    continue;
                }

                var id = regionId++;
                spans[row, col] = CellSpanInfo.CreateMaster(id, row, col, spanY, spanX);

                for (var r = row; r < Math.Min(rows, row + spanY); r++)
                {
                    for (var c = col; c < Math.Min(cols, col + spanX); c++)
                    {
                        if (r == row && c == col)
                        {
                            continue;
                        }

                        spans[r, c] = CellSpanInfo.CreateCovered(id, row, col);
                    }
                }
            }
        }

        return spans;
    }

    private static EffectiveCellStyle[,] BuildEffectiveStyles(TableEntity table, CellSpanInfo[,] spans)
    {
        var rows = spans.GetLength(0);
        var cols = spans.GetLength(1);
        var styles = new EffectiveCellStyle[rows, cols];
        var cache = new Dictionary<(int Row, int Col), EffectiveCellStyle>();

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var span = spans[row, col];
                var key = (span.MasterRow, span.MasterCol);
                if (!cache.TryGetValue(key, out var style))
                {
                    var cell = table.Rows[span.MasterRow].Cells[span.MasterCol];
                    style = ResolveEffectiveStyle(table, span.MasterRow, span.MasterCol, cell);
                    cache[key] = style;
                }

                styles[row, col] = style;
            }
        }

        return styles;
    }

    private static EffectiveCellStyle ResolveEffectiveStyle(TableEntity table, int row, int col, TableEntity.Cell cell)
    {
        var tableStyle = table.Style ?? TableStyle.Default;
        var baseStyle = tableStyle.TableCellStyle ?? new TableEntity.CellStyle();
        var rowStyle = ResolveRowStyle(tableStyle, row);

        var effective = EffectiveCellStyle.FromBase(baseStyle);
        effective.ApplyBase(rowStyle);
        effective.SetVirtualEdges(cell);

        var columnOverride = table.Columns[col].CellStyleOverride;
        if (HasStyleOverrides(columnOverride))
        {
            effective.ApplyOverrides(columnOverride);
        }

        var rowOverride = table.Rows[row].CellStyleOverride;
        if (HasStyleOverrides(rowOverride))
        {
            effective.ApplyOverrides(rowOverride);
        }

        if (HasStyleOverrides(cell.StyleOverride))
        {
            effective.ApplyOverrides(cell.StyleOverride, allowHeuristics: true);
        }

        effective.ApplyMargins(tableStyle);
        return effective;
    }

    private static TableEntity.CellStyle ResolveRowStyle(TableStyle style, int rowIndex)
    {
        if (!style.SuppressTitle && rowIndex == 0)
        {
            return style.TitleCellStyle ?? new TableEntity.CellStyle();
        }

        var headerIndex = style.SuppressTitle ? 0 : 1;
        if (!style.SuppressHeaderRow && rowIndex == headerIndex)
        {
            return style.HeaderCellStyle ?? new TableEntity.CellStyle();
        }

        return style.DataCellStyle ?? new TableEntity.CellStyle();
    }

    private static bool HasStyleOverrides(TableEntity.CellStyle style)
    {
        if (style is null)
        {
            return false;
        }

        if (style.HasData)
        {
            return true;
        }

        if (style.PropertyOverrideFlags != TableEntity.TableCellStylePropertyFlags.None ||
            style.TableCellStylePropertyFlags != TableEntity.TableCellStylePropertyFlags.None ||
            style.MarginOverrideFlags != TableEntity.MarginFlags.None)
        {
            return true;
        }

        if (style.CellAlignment != TableEntity.Cell.CellAlignmentType.None ||
            style.TextHeight > 0 ||
            style.TextStyle is not null ||
            style.IsFillColorOn)
        {
            return true;
        }

        if (!style.BackgroundColor.IsByLayer && !style.BackgroundColor.IsByBlock)
        {
            return true;
        }

        if (!style.TextColor.IsByLayer && !style.TextColor.IsByBlock)
        {
            return true;
        }

        return HasBorderOverrides(style.TopBorder) ||
               HasBorderOverrides(style.BottomBorder) ||
               HasBorderOverrides(style.LeftBorder) ||
               HasBorderOverrides(style.RightBorder) ||
               HasBorderOverrides(style.HorizontalInsideBorder) ||
               HasBorderOverrides(style.VerticalInsideBorder);
    }

    private static bool HasBorderOverrides(TableEntity.CellBorder border)
    {
        return border.PropertyOverrideFlags != TableEntity.TableBorderPropertyFlags.None ||
               border.LineWeight != 0 ||
               border.DoubleLineSpacing > 0 ||
               border.IsInvisible ||
               (!border.Color.IsByLayer && !border.Color.IsByBlock);
    }

    private static void DrawHorizontalBorder(
        RenderLayerBuilder builder,
        EffectiveCellStyle style,
        TableEntity table,
        EdgeType edge,
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        double left,
        double right,
        double y,
        Transform transform,
        RenderColor baseColor,
        float baseThickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        CadRenderSceneSettings settings,
        bool isOuter)
    {
        if (style.IsVirtual(edge))
        {
            return;
        }

        var border = style.ResolveBorder(edge, isOuter);
        if (border.IsInvisible)
        {
            return;
        }

        var start = origin + xAxis * left - yAxis * y;
        var end = origin + xAxis * right - yAxis * y;
        AddBorderLine(
            builder,
            RenderTransformUtils.Apply(transform, start),
            RenderTransformUtils.Apply(transform, end),
            border,
            baseColor,
            baseThickness,
            lineCap,
            lineJoin,
            settings,
            yAxis);
    }

    private static void DrawVerticalBorder(
        RenderLayerBuilder builder,
        EffectiveCellStyle style,
        TableEntity table,
        EdgeType edge,
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        double x,
        double top,
        double bottom,
        Transform transform,
        RenderColor baseColor,
        float baseThickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        CadRenderSceneSettings settings,
        bool isOuter)
    {
        if (style.IsVirtual(edge))
        {
            return;
        }

        var border = style.ResolveBorder(edge, isOuter);
        if (border.IsInvisible)
        {
            return;
        }

        var start = origin + xAxis * x - yAxis * top;
        var end = origin + xAxis * x - yAxis * bottom;
        AddBorderLine(
            builder,
            RenderTransformUtils.Apply(transform, start),
            RenderTransformUtils.Apply(transform, end),
            border,
            baseColor,
            baseThickness,
            lineCap,
            lineJoin,
            settings,
            xAxis);
    }

    private static void AddBorderLine(
        RenderLayerBuilder builder,
        System.Numerics.Vector2 start,
        System.Numerics.Vector2 end,
        TableEntity.CellBorder border,
        RenderColor baseColor,
        float baseThickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        CadRenderSceneSettings settings,
        XYZ offsetAxis)
    {
        var color = ResolveBorderColor(border, baseColor);
        var thickness = ResolveBorderThickness(border, baseThickness, settings);
        if (border.Type == TableEntity.BorderType.Double && border.DoubleLineSpacing > 0)
        {
            // Draw two parallel lines offset by half the spacing.
            var offset = (float)(border.DoubleLineSpacing * 0.5);
            var normal = new System.Numerics.Vector2((float)offsetAxis.X, (float)offsetAxis.Y);
            if (normal.LengthSquared() > 0)
            {
                normal = System.Numerics.Vector2.Normalize(normal) * offset;
                builder.Add(new RenderLine(start + normal, end + normal, color, thickness, lineCap, lineJoin));
                builder.Add(new RenderLine(start - normal, end - normal, color, thickness, lineCap, lineJoin));
                return;
            }
        }

        builder.Add(new RenderLine(start, end, color, thickness, lineCap, lineJoin));
    }

    private static RenderColor ResolveBorderColor(TableEntity.CellBorder border, RenderColor baseColor)
    {
        var borderColor = border.Color;
        if (borderColor.IsByLayer || borderColor.IsByBlock)
        {
            return baseColor;
        }

        return new RenderColor(borderColor.R, borderColor.G, borderColor.B, baseColor.A);
    }

    private static float ResolveBorderThickness(
        TableEntity.CellBorder border,
        float baseThickness,
        CadRenderSceneSettings settings)
    {
        var lineWeight = border.LineWeight;
        if (lineWeight == 0 || lineWeight == LineWeightType.ByLayer || lineWeight == LineWeightType.ByBlock)
        {
            return baseThickness;
        }

        return RenderStyleUtils.ResolveLineWeight(lineWeight, settings);
    }

    private enum EdgeType
    {
        Top,
        Bottom,
        Left,
        Right
    }

    private readonly struct CellSpanInfo
    {
        public int RegionId { get; }
        public int MasterRow { get; }
        public int MasterCol { get; }
        public int RowSpan { get; }
        public int ColumnSpan { get; }
        public bool IsMaster { get; }
        public bool IsAssigned => RegionId != 0;

        private CellSpanInfo(int regionId, int masterRow, int masterCol, int rowSpan, int columnSpan, bool isMaster)
        {
            RegionId = regionId;
            MasterRow = masterRow;
            MasterCol = masterCol;
            RowSpan = rowSpan;
            ColumnSpan = columnSpan;
            IsMaster = isMaster;
        }

        public static CellSpanInfo CreateMaster(int regionId, int row, int col, int rowSpan, int colSpan)
            => new CellSpanInfo(regionId, row, col, rowSpan, colSpan, true);

        public static CellSpanInfo CreateCovered(int regionId, int masterRow, int masterCol)
            => new CellSpanInfo(regionId, masterRow, masterCol, 1, 1, false);
    }

    private sealed class EffectiveCellStyle
    {
        public TableEntity.Cell.CellAlignmentType Alignment { get; private set; }
        public double TextHeight { get; private set; }
        public TextStyle? TextStyle { get; private set; }
        public Color TextColor { get; private set; }
        public bool IsFillColorOn { get; private set; }
        public Color BackgroundColor { get; private set; }
        public double MarginLeft { get; private set; }
        public double MarginRight { get; private set; }
        public double MarginTop { get; private set; }
        public double MarginBottom { get; private set; }
        public TableEntity.CellBorder TopBorder { get; private set; }
        public TableEntity.CellBorder BottomBorder { get; private set; }
        public TableEntity.CellBorder LeftBorder { get; private set; }
        public TableEntity.CellBorder RightBorder { get; private set; }
        public TableEntity.CellBorder HorizontalInsideBorder { get; private set; }
        public TableEntity.CellBorder VerticalInsideBorder { get; private set; }
        public TableEntity.Cell.VirtualEdgeFlags VirtualEdges { get; private set; }

        private EffectiveCellStyle()
        {
            Alignment = TableEntity.Cell.CellAlignmentType.MiddleCenter;
            TextHeight = 0;
            TextColor = Color.ByLayer;
            BackgroundColor = Color.ByLayer;
            TopBorder = new TableEntity.CellBorder(TableEntity.CellEdgeFlags.Top);
            BottomBorder = new TableEntity.CellBorder(TableEntity.CellEdgeFlags.Bottom);
            LeftBorder = new TableEntity.CellBorder(TableEntity.CellEdgeFlags.Left);
            RightBorder = new TableEntity.CellBorder(TableEntity.CellEdgeFlags.Right);
            HorizontalInsideBorder = new TableEntity.CellBorder(TableEntity.CellEdgeFlags.InsideHorizontal);
            VerticalInsideBorder = new TableEntity.CellBorder(TableEntity.CellEdgeFlags.InsideVertical);
        }

        public static EffectiveCellStyle FromBase(TableEntity.CellStyle style)
        {
            var effective = new EffectiveCellStyle();
            effective.ApplyAll(style);
            return effective;
        }

        public void ApplyBase(TableEntity.CellStyle style)
        {
            ApplyAll(style);
        }

        public void ApplyOverrides(TableEntity.CellStyle style, bool allowHeuristics = false)
        {
            var flags = style.PropertyOverrideFlags;
            if (flags == TableEntity.TableCellStylePropertyFlags.None && allowHeuristics)
            {
                flags = DeriveOverrideFlags(style);
            }

            if (flags.HasFlag(TableEntity.TableCellStylePropertyFlags.Alignment) && style.CellAlignment != TableEntity.Cell.CellAlignmentType.None)
            {
                Alignment = style.CellAlignment;
            }

            if (flags.HasFlag(TableEntity.TableCellStylePropertyFlags.TextHeight) && style.TextHeight > 0)
            {
                TextHeight = style.TextHeight;
            }

            if (flags.HasFlag(TableEntity.TableCellStylePropertyFlags.TextStyle) && style.TextStyle is not null)
            {
                TextStyle = style.TextStyle;
            }

            if (flags.HasFlag(TableEntity.TableCellStylePropertyFlags.ContentColor) && !style.TextColor.IsByLayer && !style.TextColor.IsByBlock)
            {
                TextColor = style.TextColor;
            }

            if (flags.HasFlag(TableEntity.TableCellStylePropertyFlags.BackgroundColor))
            {
                BackgroundColor = style.BackgroundColor;
                IsFillColorOn = style.IsFillColorOn;
            }

            if (style.MarginOverrideFlags.HasFlag(TableEntity.MarginFlags.Override))
            {
                if (style.VerticalMargin > 0)
                {
                    MarginTop = style.VerticalMargin;
                    MarginBottom = style.VerticalMargin;
                }

                if (style.HorizontalMargin > 0)
                {
                    MarginLeft = style.HorizontalMargin;
                    MarginRight = style.HorizontalMargin;
                }

                if (style.BottomMargin > 0)
                {
                    MarginBottom = style.BottomMargin;
                }

                if (style.RightMargin > 0)
                {
                    MarginRight = style.RightMargin;
                }
            }

            ApplyBorderOverrides(TopBorder, style.TopBorder);
            ApplyBorderOverrides(BottomBorder, style.BottomBorder);
            ApplyBorderOverrides(LeftBorder, style.LeftBorder);
            ApplyBorderOverrides(RightBorder, style.RightBorder);
            ApplyBorderOverrides(HorizontalInsideBorder, style.HorizontalInsideBorder);
            ApplyBorderOverrides(VerticalInsideBorder, style.VerticalInsideBorder);
        }

        public void ApplyMargins(TableStyle tableStyle)
        {
            if (MarginLeft <= 0)
            {
                MarginLeft = tableStyle.HorizontalCellMargin;
            }
            if (MarginRight <= 0)
            {
                MarginRight = tableStyle.HorizontalCellMargin;
            }
            if (MarginTop <= 0)
            {
                MarginTop = tableStyle.VerticalCellMargin;
            }
            if (MarginBottom <= 0)
            {
                MarginBottom = tableStyle.VerticalCellMargin;
            }
        }

        public bool IsVirtual(EdgeType edge)
        {
            return edge switch
            {
                EdgeType.Top => VirtualEdges.HasFlag(TableEntity.Cell.VirtualEdgeFlags.Top),
                EdgeType.Bottom => VirtualEdges.HasFlag(TableEntity.Cell.VirtualEdgeFlags.Bottom),
                EdgeType.Left => VirtualEdges.HasFlag(TableEntity.Cell.VirtualEdgeFlags.Left),
                EdgeType.Right => VirtualEdges.HasFlag(TableEntity.Cell.VirtualEdgeFlags.Right),
                _ => false
            };
        }

        public void SetVirtualEdges(TableEntity.Cell cell)
        {
            VirtualEdges = (TableEntity.Cell.VirtualEdgeFlags)cell.VirtualEdgeFlag;
        }

        public TableEntity.CellBorder ResolveBorder(EdgeType edge, bool isOuter)
        {
            return edge switch
            {
                EdgeType.Top => SelectBorder(TopBorder, HorizontalInsideBorder, isOuter),
                EdgeType.Bottom => SelectBorder(BottomBorder, HorizontalInsideBorder, isOuter),
                EdgeType.Left => SelectBorder(LeftBorder, VerticalInsideBorder, isOuter),
                EdgeType.Right => SelectBorder(RightBorder, VerticalInsideBorder, isOuter),
                _ => TopBorder
            };
        }

        private static TableEntity.CellBorder SelectBorder(
            TableEntity.CellBorder edgeBorder,
            TableEntity.CellBorder insideBorder,
            bool isOuter)
        {
            if (isOuter)
            {
                return edgeBorder;
            }

            return edgeBorder.PropertyOverrideFlags != TableEntity.TableBorderPropertyFlags.None ? edgeBorder : insideBorder;
        }

        private void ApplyAll(TableEntity.CellStyle style)
        {
            if (style.CellAlignment != TableEntity.Cell.CellAlignmentType.None)
            {
                Alignment = style.CellAlignment;
            }

            if (style.TextHeight > 0)
            {
                TextHeight = style.TextHeight;
            }

            if (style.TextStyle is not null)
            {
                TextStyle = style.TextStyle;
            }

            TextColor = style.TextColor;
            BackgroundColor = style.BackgroundColor;
            IsFillColorOn = style.IsFillColorOn;

            MarginLeft = style.HorizontalMargin;
            MarginRight = style.HorizontalMargin;
            MarginTop = style.VerticalMargin;
            MarginBottom = style.VerticalMargin;

            CopyBorder(TopBorder, style.TopBorder);
            CopyBorder(BottomBorder, style.BottomBorder);
            CopyBorder(LeftBorder, style.LeftBorder);
            CopyBorder(RightBorder, style.RightBorder);
            CopyBorder(HorizontalInsideBorder, style.HorizontalInsideBorder);
            CopyBorder(VerticalInsideBorder, style.VerticalInsideBorder);
        }

        private static void CopyBorder(TableEntity.CellBorder target, TableEntity.CellBorder source)
        {
            if (source is null)
            {
                return;
            }

            target.Color = source.Color;
            target.LineWeight = source.LineWeight;
            target.IsInvisible = source.IsInvisible;
            target.DoubleLineSpacing = source.DoubleLineSpacing;
            target.Type = source.Type;
            target.PropertyOverrideFlags = source.PropertyOverrideFlags;
        }

        private static void ApplyBorderOverrides(TableEntity.CellBorder target, TableEntity.CellBorder source)
        {
            if (source is null)
            {
                return;
            }

            if (source.PropertyOverrideFlags == TableEntity.TableBorderPropertyFlags.None)
            {
                return;
            }

            if (source.PropertyOverrideFlags.HasFlag(TableEntity.TableBorderPropertyFlags.Color))
            {
                target.Color = source.Color;
            }

            if (source.PropertyOverrideFlags.HasFlag(TableEntity.TableBorderPropertyFlags.LineWeight))
            {
                target.LineWeight = source.LineWeight;
            }

            if (source.PropertyOverrideFlags.HasFlag(TableEntity.TableBorderPropertyFlags.Invisibility))
            {
                target.IsInvisible = source.IsInvisible;
            }

            if (source.PropertyOverrideFlags.HasFlag(TableEntity.TableBorderPropertyFlags.DoubleLineSpacing))
            {
                target.DoubleLineSpacing = source.DoubleLineSpacing;
                target.Type = source.Type;
            }
        }

        private static TableEntity.TableCellStylePropertyFlags DeriveOverrideFlags(TableEntity.CellStyle style)
        {
            var flags = TableEntity.TableCellStylePropertyFlags.None;
            if (style.CellAlignment != TableEntity.Cell.CellAlignmentType.None)
            {
                flags |= TableEntity.TableCellStylePropertyFlags.Alignment;
            }
            if (style.TextHeight > 0)
            {
                flags |= TableEntity.TableCellStylePropertyFlags.TextHeight;
            }
            if (style.TextStyle is not null)
            {
                flags |= TableEntity.TableCellStylePropertyFlags.TextStyle;
            }
            if (!style.TextColor.IsByLayer && !style.TextColor.IsByBlock)
            {
                flags |= TableEntity.TableCellStylePropertyFlags.ContentColor;
            }
            if (!style.BackgroundColor.IsByLayer && !style.BackgroundColor.IsByBlock)
            {
                flags |= TableEntity.TableCellStylePropertyFlags.BackgroundColor;
            }
            return flags;
        }
    }
}
