using System;

namespace ProCad.Rendering;

public enum RenderGeometryKind
{
    Circle = 0,
    Arc = 1,
    Ellipse = 2,
    Spline = 3,
    Polyline = 4
}

public enum RenderTextLayoutKind
{
    Text = 0,
    MText = 1
}

public readonly struct RenderGeometryCacheKey : IEquatable<RenderGeometryCacheKey>
{
    public ulong Handle { get; }
    public RenderGeometryKind Kind { get; }
    public int Precision { get; }
    public long RegenStamp { get; }

    public RenderGeometryCacheKey(ulong handle, RenderGeometryKind kind, int precision, long regenStamp)
    {
        Handle = handle;
        Kind = kind;
        Precision = precision;
        RegenStamp = regenStamp;
    }

    public bool Equals(RenderGeometryCacheKey other)
    {
        return Handle == other.Handle
            && Kind == other.Kind
            && Precision == other.Precision
            && RegenStamp == other.RegenStamp;
    }

    public override bool Equals(object? obj)
    {
        return obj is RenderGeometryCacheKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Handle, Kind, Precision, RegenStamp);
    }
}

public readonly struct RenderTextLayoutCacheKey : IEquatable<RenderTextLayoutCacheKey>
{
    private static readonly StringComparer TextComparer = StringComparer.Ordinal;

    public RenderTextLayoutKind Kind { get; }
    public string Text { get; }
    public float Height { get; }
    public float LineSpacing { get; }
    public float SettingsWidthFactor { get; }
    public ulong StyleHandle { get; }
    public float MaxWidth { get; }

    public RenderTextLayoutCacheKey(
        RenderTextLayoutKind kind,
        string text,
        float height,
        float lineSpacing,
        float settingsWidthFactor,
        ulong styleHandle,
        float maxWidth)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        Height = height;
        LineSpacing = lineSpacing;
        SettingsWidthFactor = settingsWidthFactor;
        StyleHandle = styleHandle;
        MaxWidth = maxWidth;
    }

    public bool Equals(RenderTextLayoutCacheKey other)
    {
        return Kind == other.Kind
            && Height.Equals(other.Height)
            && LineSpacing.Equals(other.LineSpacing)
            && SettingsWidthFactor.Equals(other.SettingsWidthFactor)
            && StyleHandle == other.StyleHandle
            && MaxWidth.Equals(other.MaxWidth)
            && TextComparer.Equals(Text, other.Text);
    }

    public override bool Equals(object? obj)
    {
        return obj is RenderTextLayoutCacheKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Kind,
            Height,
            LineSpacing,
            SettingsWidthFactor,
            StyleHandle,
            MaxWidth,
            TextComparer.GetHashCode(Text));
    }
}
