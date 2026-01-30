using System.Numerics;

namespace ACadInspector.Rendering;

public readonly struct RenderBounds
{
    public Vector2 Min { get; }
    public Vector2 Max { get; }

    public static RenderBounds Empty => new(new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));

    public RenderBounds(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }

    public bool IsEmpty => float.IsPositiveInfinity(Min.X) || float.IsPositiveInfinity(Min.Y);

    public Vector2 Size => IsEmpty ? Vector2.Zero : new Vector2(Max.X - Min.X, Max.Y - Min.Y);

    public RenderBounds Expand(Vector2 point)
    {
        if (IsEmpty)
        {
            return new RenderBounds(point, point);
        }

        var min = new Vector2(System.MathF.Min(Min.X, point.X), System.MathF.Min(Min.Y, point.Y));
        var max = new Vector2(System.MathF.Max(Max.X, point.X), System.MathF.Max(Max.Y, point.Y));
        return new RenderBounds(min, max);
    }

    public RenderBounds Expand(RenderBounds other)
    {
        if (other.IsEmpty)
        {
            return this;
        }

        if (IsEmpty)
        {
            return other;
        }

        var min = new Vector2(System.MathF.Min(Min.X, other.Min.X), System.MathF.Min(Min.Y, other.Min.Y));
        var max = new Vector2(System.MathF.Max(Max.X, other.Max.X), System.MathF.Max(Max.Y, other.Max.Y));
        return new RenderBounds(min, max);
    }
}
