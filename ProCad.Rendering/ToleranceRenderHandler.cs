using System;
using System.Collections.Generic;
using System.Numerics;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.Text;
using CSMath;

namespace ProCad.Rendering;

public sealed class ToleranceRenderHandler : IRenderEntityHandler
{
    private const float PaddingFactor = 0.25f;

    public bool CanHandle(Entity entity) => entity is Tolerance;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var tolerance = (Tolerance)entity;
        if (string.IsNullOrWhiteSpace(tolerance.Text))
        {
            return;
        }

        var builder = context.GetLayerBuilder(tolerance);
        var color = context.ResolveEntityColor(tolerance);
        var thickness = context.ResolveLineWeight(tolerance);
        var lineCap = context.ResolveLineCap(tolerance);
        var lineJoin = context.ResolveLineJoin(tolerance);

        var style = tolerance.Style ?? DimensionStyle.Default;
        var textStyle = style.Style ?? TextStyle.Default;
        var height = (float)style.TextHeight;
        if (height <= 0f)
        {
            height = 1f;
        }

        var raw = TextProcessor.Parse(tolerance.Text, out _);
        var lines = SplitLines(raw);
        if (lines.Count == 0)
        {
            return;
        }

        var layouts = new List<RenderTextLayout>(lines.Count);
        var maxWidth = 0f;
        foreach (var line in lines)
        {
            var layout = context.TextShaper.Shape(new TextEntity
            {
                Value = line,
                Height = height,
                Style = textStyle
            }, context.Settings);
            layouts.Add(layout);
            if (layout.Width > maxWidth)
            {
                maxWidth = layout.Width;
            }
        }

        var lineHeight = height * 1.2f;
        var totalHeight = lineHeight * lines.Count;
        var padding = height * PaddingFactor;
        var frameWidth = maxWidth + padding * 2f;
        var frameHeight = totalHeight + padding * 2f;

        var rotation = ResolveRotation(tolerance.Direction);
        var (anchor, worldRotation, scale) = ResolveTransform(transform, tolerance.InsertionPoint, rotation);

        var frameOffset = new Vector2(0f, -frameHeight);
        var frame = RenderTextUtils.BuildTextQuad(
            anchor,
            frameOffset,
            frameWidth * scale,
            frameHeight * scale,
            1f,
            worldRotation,
            0f,
            mirrorX: false,
            mirrorY: false);
        builder.Add(new RenderPolyline(frame, isClosed: true, color, thickness, lineCap, lineJoin));

        var y = padding;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var layout = layouts[i];
            var offset = new Vector2(padding, -y - lineHeight);
            builder.Add(new RenderText(
                layout.Text,
                anchor,
                offset,
                layout.Width * scale,
                layout.Height * scale,
                height * scale,
                1f,
                worldRotation,
                0f,
                false,
                false,
                false,
                false,
                color,
                ResolveFontFamily(textStyle)));
            y += lineHeight;
        }
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return lines;
        }

        var normalized = text.Replace("\\P", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            lines.Add(line);
        }

        return lines;
    }

    private static float ResolveRotation(XYZ direction)
    {
        var dir = direction.IsZero() ? XYZ.AxisX : direction;
        return MathF.Atan2((float)dir.Y, (float)dir.X);
    }

    private static (Vector2 Anchor, float Rotation, float Scale) ResolveTransform(
        Transform transform,
        XYZ anchorPoint,
        float rotation)
    {
        if (RenderTransformUtils.IsIdentity(transform))
        {
            return (RenderTransformUtils.ToVector2(anchorPoint), rotation, 1f);
        }

        var worldAnchor = transform.ApplyTransform(anchorPoint);
        var direction = new XYZ(Math.Cos(rotation), Math.Sin(rotation), 0);
        var worldDir = transform.ApplyTransform(anchorPoint + direction);
        var delta = new Vector2((float)(worldDir.X - worldAnchor.X), (float)(worldDir.Y - worldAnchor.Y));
        var scale = delta.Length();
        if (scale <= 0f)
        {
            scale = 1f;
        }

        var worldRotation = MathF.Atan2(delta.Y, delta.X);
        return (new Vector2((float)worldAnchor.X, (float)worldAnchor.Y), worldRotation, scale);
    }

    private static string? ResolveFontFamily(TextStyle style)
    {
        if (style is null || string.IsNullOrWhiteSpace(style.Filename))
        {
            return null;
        }

        return System.IO.Path.GetFileNameWithoutExtension(style.Filename);
    }
}
