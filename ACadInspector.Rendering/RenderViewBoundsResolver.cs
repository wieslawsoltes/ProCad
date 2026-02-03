using System;
using System.Numerics;
using ACadSharp;
using ACadSharp.Header;

namespace ACadInspector.Rendering;

internal static class RenderViewBoundsResolver
{
    private const float DefaultExtentSize = 1000f;
    private const float Epsilon = 0.0001f;

    public static RenderBounds Resolve(CadDocument document, CadRenderSceneSettings settings)
    {
        if (document?.Header is null)
        {
            return FallbackBounds();
        }

        var header = document.Header;
        if (settings.IsPaperSpace)
        {
            var paper = ToBounds(header.PaperSpaceExtMin, header.PaperSpaceExtMax);
            if (IsValid(paper))
            {
                return paper;
            }
        }
        else
        {
            var model = ToBounds(header.ModelSpaceExtMin, header.ModelSpaceExtMax);
            if (IsValid(model))
            {
                return model;
            }

            var limits = ToBounds(header.ModelSpaceLimitsMin, header.ModelSpaceLimitsMax);
            if (IsValid(limits))
            {
                return limits;
            }
        }

        return FallbackBounds();
    }

    private static RenderBounds ToBounds(CSMath.XYZ min, CSMath.XYZ max)
    {
        var minVec = new Vector2((float)min.X, (float)min.Y);
        var maxVec = new Vector2((float)max.X, (float)max.Y);
        var bounds = new RenderBounds(minVec, maxVec);
        return Normalize(bounds);
    }

    private static RenderBounds ToBounds(CSMath.XY min, CSMath.XY max)
    {
        var minVec = new Vector2((float)min.X, (float)min.Y);
        var maxVec = new Vector2((float)max.X, (float)max.Y);
        var bounds = new RenderBounds(minVec, maxVec);
        return Normalize(bounds);
    }

    private static RenderBounds Normalize(RenderBounds bounds)
    {
        if (bounds.IsEmpty)
        {
            return bounds;
        }

        var min = bounds.Min;
        var max = bounds.Max;
        if (min.X <= max.X && min.Y <= max.Y)
        {
            return bounds;
        }

        var fixedMin = new Vector2(MathF.Min(min.X, max.X), MathF.Min(min.Y, max.Y));
        var fixedMax = new Vector2(MathF.Max(min.X, max.X), MathF.Max(min.Y, max.Y));
        return new RenderBounds(fixedMin, fixedMax);
    }

    private static bool IsValid(RenderBounds bounds)
    {
        if (bounds.IsEmpty)
        {
            return false;
        }

        var size = bounds.Size;
        if (float.IsNaN(size.X) || float.IsNaN(size.Y) || float.IsInfinity(size.X) || float.IsInfinity(size.Y))
        {
            return false;
        }

        return size.X > Epsilon && size.Y > Epsilon;
    }

    private static RenderBounds FallbackBounds()
    {
        var min = new Vector2(-DefaultExtentSize, -DefaultExtentSize);
        var max = new Vector2(DefaultExtentSize, DefaultExtentSize);
        return new RenderBounds(min, max);
    }
}
