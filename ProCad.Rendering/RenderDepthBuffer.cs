using System;

namespace ProCad.Rendering;

internal sealed class RenderDepthBuffer
{
    public const float EmptyDepth = float.NegativeInfinity;

    private float[] _depths = Array.Empty<float>();

    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool HasDepth { get; private set; }

    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            Width = 0;
            Height = 0;
            _depths = Array.Empty<float>();
            HasDepth = false;
            return;
        }

        var count = checked(width * height);
        if (_depths.Length != count)
        {
            _depths = new float[count];
        }

        Width = width;
        Height = height;
    }

    public void Clear()
    {
        if (_depths.Length == 0)
        {
            HasDepth = false;
            return;
        }

        Array.Fill(_depths, EmptyDepth);
        HasDepth = false;
    }

    public float GetDepth(int x, int y)
    {
        return _depths[y * Width + x];
    }

    public void SetDepth(int x, int y, float depth)
    {
        var index = y * Width + x;
        if (depth <= _depths[index])
        {
            return;
        }

        _depths[index] = depth;
        HasDepth = true;
    }
}
