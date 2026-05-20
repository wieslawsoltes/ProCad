using System;
using System.Collections.Generic;
using System.Numerics;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Rendering;

internal sealed class ShxShapeRenderer
{
    private const float ArcStepRadians = MathF.PI / 12f;
    private static readonly float[] VecX =
    {
        1f, 1f, 1f, 0.5f, 0f, -0.5f, -1f, -1f, -1f, -1f, -1f, -0.5f, 0f, 0.5f, 1f, 1f
    };

    private static readonly float[] VecY =
    {
        0f, 0.5f, 1f, 1f, 1f, 1f, 1f, 0.5f, 0f, -0.5f, -1f, -1f, -1f, -1f, -1f, -0.5f
    };

    private readonly ShxShapeFile _shapeFile;

    public ShxShapeRenderer(ShxShapeFile shapeFile)
    {
        _shapeFile = shapeFile;
    }

    public bool TryRenderShape(int shapeNumber, out RenderShapeGeometry geometry)
    {
        geometry = null!;
        if (!_shapeFile.TryGetCodes(shapeNumber, out var codes))
        {
            return false;
        }

        var context = new ShapeRenderContext();
        RenderShape(codes, context, new HashSet<int> { shapeNumber });
        geometry = new RenderShapeGeometry(context.Contours);
        return true;
    }

    private void RenderShape(int[] codes, ShapeRenderContext context, HashSet<int> stack)
    {
        var index = 0;
        var skipNext = false;

        while (index < codes.Length)
        {
            var code = codes[index++];

            if (code > 15 && !skipNext)
            {
                DrawVector(context, code);
            }
            else if (code == 0)
            {
                break;
            }
            else if (code == 1 && !skipNext)
            {
                context.PenDown = true;
            }
            else if (code == 2 && !skipNext)
            {
                context.PenDown = false;
            }
            else if (code is 3 or 4)
            {
                var factor = codes[index++];
                if (!skipNext && factor != 0)
                {
                    if (code == 3)
                    {
                        context.VectorLength /= factor;
                    }
                    else
                    {
                        context.VectorLength *= factor;
                    }
                }
            }
            else if (code == 5 && !skipNext)
            {
                context.Push();
            }
            else if (code == 6 && !skipNext)
            {
                context.Pop();
            }
            else if (code == 7)
            {
                var subShape = codes[index++];
                if (!skipNext && stack.Add(subShape))
                {
                    if (_shapeFile.TryGetCodes(subShape, out var subCodes))
                    {
                        RenderShape(subCodes, context, stack);
                    }
                    stack.Remove(subShape);
                }
            }
            else if (code == 8)
            {
                var x = codes[index++];
                var y = codes[index++];
                if (!skipNext)
                {
                    DrawDisplacement(context, x, y);
                }
            }
            else if (code == 9)
            {
                while (true)
                {
                    var x = codes[index++];
                    var y = codes[index++];
                    if (x == 0 && y == 0)
                    {
                        break;
                    }

                    if (!skipNext)
                    {
                        DrawDisplacement(context, x, y);
                    }
                }
            }
            else if (code == 10)
            {
                var radius = codes[index++];
                var specs = codes[index++];
                if (!skipNext)
                {
                    DrawOctantArc(context, radius, specs);
                }
            }
            else if (code == 11)
            {
                var startOffset = codes[index++];
                var endOffset = codes[index++];
                var radius = (codes[index++] << 8) + codes[index++];
                var specs = codes[index++];
                if (!skipNext)
                {
                    DrawFractionalArc(context, startOffset, endOffset, radius, specs);
                }
            }
            else if (code == 12)
            {
                var x = codes[index++];
                var y = codes[index++];
                var bulge = codes[index++];
                if (!skipNext)
                {
                    DrawBulge(context, x, y, bulge);
                }
            }
            else if (code == 13)
            {
                while (true)
                {
                    var x = codes[index++];
                    var y = codes[index++];
                    if (x == 0 && y == 0)
                    {
                        break;
                    }

                    var bulge = codes[index++];
                    if (!skipNext)
                    {
                        DrawBulge(context, x, y, bulge);
                    }
                }
            }
            else if (code == 14)
            {
                skipNext = true;
                continue;
            }

            skipNext = false;
        }
    }

    private static void DrawVector(ShapeRenderContext context, int code)
    {
        var angle = code & 0xF;
        var length = (code >> 4) & 0xF;
        DrawDisplacement(context, VecX[angle] * length, VecY[angle] * length);
    }

    private static void DrawDisplacement(ShapeRenderContext context, float x, float y)
    {
        var target = context.Current + new Vector2(x, y) * context.VectorLength;
        if (context.PenDown)
        {
            context.LineTo(target);
        }
        else
        {
            context.MoveTo(target);
        }
    }

    private static void DrawOctantArc(ShapeRenderContext context, float radius, int specs)
    {
        var (startOctant, octantSpan, ccw) = DecodeOctantSpecs(specs);
        if (octantSpan == 0)
        {
            octantSpan = 8;
        }

        var startAngle = MathF.PI / 4f * startOctant;
        var endAngle = startAngle + (ccw ? 1f : -1f) * MathF.PI / 4f * octantSpan;
        DrawArcAlignedToCurrent(context, radius * context.VectorLength, startAngle, endAngle, ccw);
    }

    private static void DrawFractionalArc(
        ShapeRenderContext context,
        int startOffset,
        int endOffset,
        int radius,
        int specs)
    {
        var (startOctant, octantSpan, ccw) = DecodeOctantSpecs(specs);
        if (endOffset == 0)
        {
            endOffset = 256;
        }

        var binaryDeg = 45f / 256f;
        var startOffsetAngle = startOffset * binaryDeg;
        var endOffsetAngle = endOffset * binaryDeg;

        float startAngle;
        float endAngle;

        if (ccw)
        {
            var endOctant = startOctant + octantSpan - 1;
            startAngle = startOctant * 45f + startOffsetAngle;
            endAngle = endOctant * 45f + endOffsetAngle;
        }
        else
        {
            var endOctant = startOctant - octantSpan + 1;
            startAngle = startOctant * 45f - startOffsetAngle;
            endAngle = endOctant * 45f - endOffsetAngle;
        }

        DrawArcAlignedToCurrent(
            context,
            radius * context.VectorLength,
            MathF.PI / 180f * startAngle,
            MathF.PI / 180f * endAngle,
            ccw);
    }

    private static void DrawArcAlignedToCurrent(
        ShapeRenderContext context,
        float radius,
        float startAngle,
        float endAngle,
        bool ccw)
    {
        var pathStart = ccw ? startAngle : endAngle;
        var pathEnd = ccw ? endAngle : startAngle;

        if (ccw && pathEnd < pathStart)
        {
            pathEnd += MathF.Tau;
        }
        else if (!ccw && pathEnd > pathStart)
        {
            pathEnd -= MathF.Tau;
        }

        var center = context.Current - new Vector2(
            radius * MathF.Cos(pathStart),
            radius * MathF.Sin(pathStart));

        DrawArcWithCenter(context, center, radius, pathStart, pathEnd);
    }

    private static void DrawBulge(ShapeRenderContext context, int x, int y, int bulge)
    {
        if (!context.PenDown || bulge == 0)
        {
            DrawDisplacement(context, x, y);
            return;
        }

        var start = context.Current;
        var end = start + new Vector2(x, y) * context.VectorLength;
        var bulgeValue = bulge / 127f;
        var center = Arc.GetCenter(
            new XY(start.X, start.Y),
            new XY(end.X, end.Y),
            bulgeValue,
            out var radius);

        var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
        var endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);
        var ccw = bulgeValue > 0f;

        var startRad = (float)startAngle;
        var endRad = (float)endAngle;

        if (ccw && endRad < startRad)
        {
            endRad += MathF.Tau;
        }
        else if (!ccw && endRad > startRad)
        {
            endRad -= MathF.Tau;
        }

        DrawArcWithCenter(context, new Vector2((float)center.X, (float)center.Y), (float)radius, startRad, endRad);
    }

    private static void DrawArcWithCenter(
        ShapeRenderContext context,
        Vector2 center,
        float radius,
        float startAngle,
        float endAngle)
    {
        var sweep = endAngle - startAngle;
        var segments = Math.Max(4, (int)MathF.Ceiling(MathF.Abs(sweep) / ArcStepRadians));

        for (var i = 1; i <= segments; i++)
        {
            var t = (float)i / segments;
            var angle = startAngle + sweep * t;
            var point = center + new Vector2(radius * MathF.Cos(angle), radius * MathF.Sin(angle));
            if (context.PenDown)
            {
                context.LineTo(point);
            }
            else
            {
                context.MoveTo(point);
            }
        }
    }

    private static (int StartOctant, int OctantSpan, bool Ccw) DecodeOctantSpecs(int specs)
    {
        var ccw = true;
        if (specs < 0)
        {
            ccw = false;
            specs = -specs;
        }

        var start = (specs >> 4) & 0xF;
        var span = specs & 0xF;
        return (start, span, ccw);
    }

    private sealed class ShapeRenderContext
    {
        private readonly Stack<Vector2> _stack = new();

        public List<IReadOnlyList<Vector2>> Contours { get; } = new();
        public bool PenDown { get; set; } = true;
        public float VectorLength { get; set; } = 1f;
        public Vector2 Current { get; private set; }

        private List<Vector2>? _currentContour;

        public void MoveTo(Vector2 target)
        {
            Current = target;
            _currentContour = null;
        }

        public void LineTo(Vector2 target)
        {
            if (_currentContour == null)
            {
                _currentContour = new List<Vector2> { Current };
                Contours.Add(_currentContour);
            }

            _currentContour.Add(target);
            Current = target;
        }

        public void Push()
        {
            _stack.Push(Current);
        }

        public void Pop()
        {
            if (_stack.Count == 0)
            {
                return;
            }

            MoveTo(_stack.Pop());
        }
    }
}
