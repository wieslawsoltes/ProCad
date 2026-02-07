using System.Numerics;

namespace ACadInspector.Editing.Interaction;

[Flags]
public enum CadSnapMode
{
    None = 0,
    Endpoint = 1 << 0,
    Midpoint = 1 << 1,
    Center = 1 << 2,
    Node = 1 << 3,
    Quadrant = 1 << 4,
    Intersection = 1 << 5,
    Perpendicular = 1 << 6,
    Tangent = 1 << 7,
    Nearest = 1 << 8,
    ApparentIntersection = 1 << 9,
    Extension = 1 << 10,
    Parallel = 1 << 11,
    All = Endpoint | Midpoint | Center | Node | Quadrant | Intersection | Perpendicular |
          Tangent | Nearest | ApparentIntersection | Extension | Parallel
}

public readonly record struct CadSnapResult(
    Vector2 Point,
    CadSnapMode Mode,
    string Label);

public readonly record struct CadSnapCandidate(
    Vector2 Point,
    CadSnapMode Mode,
    string Label,
    float PriorityBias = 0f);

public interface ICadSnapService
{
    bool Enabled { get; set; }
    CadSnapMode EnabledModes { get; set; }

    bool TryResolve(Vector2 pickPoint, float tolerance, IReadOnlyList<CadSnapCandidate> candidates, out CadSnapResult result);
}

public sealed class CadSnapService : ICadSnapService
{
    public bool Enabled { get; set; } = true;
    public CadSnapMode EnabledModes { get; set; } = CadSnapMode.All;

    public bool TryResolve(Vector2 pickPoint, float tolerance, IReadOnlyList<CadSnapCandidate> candidates, out CadSnapResult result)
    {
        result = default;
        if (!Enabled || candidates.Count == 0 || tolerance <= 0f)
        {
            return false;
        }

        var toleranceSquared = tolerance * tolerance;
        var bestScore = float.PositiveInfinity;
        var bestIndex = -1;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (!EnabledModes.HasFlag(candidate.Mode))
            {
                continue;
            }

            var delta = candidate.Point - pickPoint;
            var distanceSquared = Vector2.Dot(delta, delta);
            if (distanceSquared > toleranceSquared)
            {
                continue;
            }

            var score = ResolveModePriority(candidate.Mode) + distanceSquared + candidate.PriorityBias;
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestIndex = index;
        }

        if (bestIndex < 0)
        {
            return false;
        }

        var best = candidates[bestIndex];
        result = new CadSnapResult(
            Point: best.Point,
            Mode: best.Mode,
            Label: best.Label);
        return true;
    }

    private static float ResolveModePriority(CadSnapMode mode)
    {
        return mode switch
        {
            CadSnapMode.Intersection => 0f,
            CadSnapMode.Endpoint => 0.05f,
            CadSnapMode.Midpoint => 0.1f,
            CadSnapMode.Center => 0.15f,
            CadSnapMode.Node => 0.2f,
            CadSnapMode.Quadrant => 0.25f,
            CadSnapMode.Perpendicular => 0.3f,
            CadSnapMode.Tangent => 0.35f,
            CadSnapMode.Extension => 0.4f,
            CadSnapMode.Parallel => 0.45f,
            CadSnapMode.ApparentIntersection => 0.5f,
            CadSnapMode.Nearest => 1f,
            _ => 2f
        };
    }
}

public interface ICadTrackingService
{
    bool Enabled { get; set; }
    bool OrthoEnabled { get; set; }
    bool PolarEnabled { get; set; }
    float PolarIncrementDegrees { get; set; }

    Vector2 Apply(Vector2 basePoint, Vector2 currentPoint);
}

public sealed class CadTrackingService : ICadTrackingService
{
    public bool Enabled { get; set; } = true;
    public bool OrthoEnabled { get; set; }
    public bool PolarEnabled { get; set; }
    public float PolarIncrementDegrees { get; set; } = 15f;

    public Vector2 Apply(Vector2 basePoint, Vector2 currentPoint)
    {
        if (!Enabled)
        {
            return currentPoint;
        }

        var delta = currentPoint - basePoint;
        if (delta.LengthSquared() <= float.Epsilon)
        {
            return currentPoint;
        }

        if (OrthoEnabled)
        {
            if (Math.Abs(delta.X) >= Math.Abs(delta.Y))
            {
                return new Vector2(currentPoint.X, basePoint.Y);
            }

            return new Vector2(basePoint.X, currentPoint.Y);
        }

        if (!PolarEnabled)
        {
            return currentPoint;
        }

        var increment = Math.Clamp(PolarIncrementDegrees, 1f, 180f) * (MathF.PI / 180f);
        var angle = MathF.Atan2(delta.Y, delta.X);
        var snappedAngle = MathF.Round(angle / increment) * increment;
        var length = delta.Length();
        if (length <= float.Epsilon)
        {
            return currentPoint;
        }

        return new Vector2(
            basePoint.X + MathF.Cos(snappedAngle) * length,
            basePoint.Y + MathF.Sin(snappedAngle) * length);
    }
}

public readonly record struct CadGripPoint(
    Vector2 Position,
    string Kind,
    string? Tag = null);

public interface ICadGripService
{
    IReadOnlyList<CadGripPoint> BuildGripSet(IReadOnlyList<CadGripPoint> controlPoints);
    bool TryResolveHotGrip(Vector2 pickPoint, float tolerance, IReadOnlyList<CadGripPoint> gripSet, out CadGripPoint grip);
}

public sealed class CadGripService : ICadGripService
{
    public IReadOnlyList<CadGripPoint> BuildGripSet(IReadOnlyList<CadGripPoint> controlPoints)
    {
        if (controlPoints.Count == 0)
        {
            return Array.Empty<CadGripPoint>();
        }

        var unique = new List<CadGripPoint>(controlPoints.Count);
        var seen = new HashSet<(long X, long Y, string Kind)>();
        for (var index = 0; index < controlPoints.Count; index++)
        {
            var point = controlPoints[index];
            var key = (
                X: Quantize(point.Position.X),
                Y: Quantize(point.Position.Y),
                Kind: point.Kind ?? string.Empty);
            if (!seen.Add(key))
            {
                continue;
            }

            unique.Add(point.Tag is null
                ? point with { Tag = index.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                : point);
        }

        return unique;
    }

    public bool TryResolveHotGrip(
        Vector2 pickPoint,
        float tolerance,
        IReadOnlyList<CadGripPoint> gripSet,
        out CadGripPoint grip)
    {
        grip = default;
        if (gripSet.Count == 0 || tolerance <= 0f)
        {
            return false;
        }

        var toleranceSquared = tolerance * tolerance;
        var bestDistanceSquared = float.PositiveInfinity;
        var bestIndex = -1;
        for (var index = 0; index < gripSet.Count; index++)
        {
            var candidate = gripSet[index];
            var delta = candidate.Position - pickPoint;
            var distanceSquared = Vector2.Dot(delta, delta);
            if (distanceSquared > toleranceSquared || distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            bestIndex = index;
        }

        if (bestIndex < 0)
        {
            return false;
        }

        grip = gripSet[bestIndex];
        return true;
    }

    private static long Quantize(float value)
    {
        return (long)MathF.Round(value * 10_000f);
    }
}
