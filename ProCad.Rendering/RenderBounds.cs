using System.Numerics;

namespace ProCad.Rendering;

/// <summary>
/// Represents an axis-aligned bounding box in world space.
/// </summary>
public readonly struct RenderBounds
{
    public Vector2 Min { get; }
    public Vector2 Max { get; }

    /// <summary>
    /// Gets an empty bounds instance.
    /// </summary>
    public static RenderBounds Empty => new(new Vector2(float.PositiveInfinity), new Vector2(float.NegativeInfinity));

    public RenderBounds(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }

    public bool IsEmpty => float.IsPositiveInfinity(Min.X) || float.IsPositiveInfinity(Min.Y);

    /// <summary>
    /// Gets the minimum X coordinate.
    /// </summary>
    public float MinX => Min.X;

    /// <summary>
    /// Gets the minimum Y coordinate.
    /// </summary>
    public float MinY => Min.Y;

    /// <summary>
    /// Gets the maximum X coordinate.
    /// </summary>
    public float MaxX => Max.X;

    /// <summary>
    /// Gets the maximum Y coordinate.
    /// </summary>
    public float MaxY => Max.Y;

    public Vector2 Size => IsEmpty ? Vector2.Zero : new Vector2(Max.X - Min.X, Max.Y - Min.Y);

    /// <summary>
    /// Gets the center point of the bounds.
    /// </summary>
    public Vector2 Center => IsEmpty ? Vector2.Zero : (Min + Max) * 0.5f;

    /// <summary>
    /// Gets the half-size vector of the bounds.
    /// </summary>
    public Vector2 Extent => IsEmpty ? Vector2.Zero : (Max - Min) * 0.5f;

    /// <summary>
    /// Gets the area of the bounds.
    /// </summary>
    public float Area => IsEmpty ? 0f : (Max.X - Min.X) * (Max.Y - Min.Y);

    /// <summary>
    /// Gets the perimeter of the bounds.
    /// </summary>
    public float Perimeter => IsEmpty ? 0f : 2f * ((Max.X - Min.X) + (Max.Y - Min.Y));

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

    /// <summary>
    /// Inflates the bounds by the given amount in all directions.
    /// </summary>
    public RenderBounds Inflate(float amount)
    {
        if (IsEmpty || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount))
        {
            return this;
        }

        var delta = new Vector2(amount, amount);
        return new RenderBounds(Min - delta, Max + delta);
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

    /// <summary>
    /// Determines whether the bounds contains the provided point.
    /// </summary>
    public bool Contains(Vector2 point)
    {
        if (IsEmpty)
        {
            return false;
        }

        return point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
    }

    /// <summary>
    /// Determines whether two bounds intersect.
    /// </summary>
    public bool Intersects(RenderBounds other)
    {
        if (IsEmpty || other.IsEmpty)
        {
            return false;
        }

        return !(other.Min.X > Max.X ||
                 other.Max.X < Min.X ||
                 other.Min.Y > Max.Y ||
                 other.Max.Y < Min.Y);
    }

    /// <summary>
    /// Determines whether the bounds intersects a rectangle specified by min/max values.
    /// </summary>
    public bool Intersects(float minX, float minY, float maxX, float maxY)
    {
        if (IsEmpty)
        {
            return false;
        }

        return !(minX > Max.X ||
                 maxX < Min.X ||
                 minY > Max.Y ||
                 maxY < Min.Y);
    }
}
