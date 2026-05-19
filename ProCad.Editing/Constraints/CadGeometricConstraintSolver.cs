using System.Numerics;
using System.Globalization;
using ProCad.Editing.EntityIndex;
using ProCad.Editing.Identifiers;
using ACadSharp.Entities;
using CSMath;

namespace ProCad.Editing.Constraints;

public sealed record CadGeometricConstraintSolveResult(
    int AppliedConstraintCount,
    int AdjustedEntityCount,
    IReadOnlyList<CadEntityId> AdjustedEntityIds,
    IReadOnlyList<string> Diagnostics);

public interface ICadGeometricConstraintSolver
{
    CadGeometricConstraintSolveResult Solve(
        ICadConstraintService constraints,
        ICadEntityIndex entityIndex,
        IReadOnlyCollection<CadEntityId>? dirtyEntityIds = null);
}

public sealed class CadGeometricConstraintSolver : ICadGeometricConstraintSolver
{
    private const int MaxIterations = 4;
    private const float Epsilon = 1e-4f;
    private readonly Dictionary<CadConstraintId, IReadOnlyList<FixedSnapshot>> _fixedSnapshots = new();

    public CadGeometricConstraintSolveResult Solve(
        ICadConstraintService constraints,
        ICadEntityIndex entityIndex,
        IReadOnlyCollection<CadEntityId>? dirtyEntityIds = null)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(entityIndex);

        var diagnostics = new List<string>();
        var all = constraints.GetConstraints()
            .Where(static constraint => IsSolvable(constraint.Kind))
            .OrderBy(static constraint => constraint.CreatedAtUtc)
            .ThenBy(static constraint => constraint.Id.Value)
            .ToArray();
        if (all.Length == 0)
        {
            return new CadGeometricConstraintSolveResult(0, 0, Array.Empty<CadEntityId>(), Array.Empty<string>());
        }

        var activeIds = new HashSet<CadConstraintId>(all.Select(static constraint => constraint.Id));
        var stale = _fixedSnapshots.Keys.Where(id => !activeIds.Contains(id)).ToArray();
        foreach (var id in stale)
        {
            _fixedSnapshots.Remove(id);
        }

        HashSet<CadEntityId>? dirty = null;
        if (dirtyEntityIds is not null && dirtyEntityIds.Count > 0)
        {
            dirty = new HashSet<CadEntityId>(dirtyEntityIds.Where(static id => !id.IsEmpty));
        }

        var adjustedEntityIds = new HashSet<CadEntityId>();
        var appliedCount = 0;
        var iterationLimit = dirty is null ? MaxIterations : 1;
        for (var iteration = 0; iteration < iterationLimit; iteration++)
        {
            var changedInIteration = false;
            var nextDirty = new HashSet<CadEntityId>();
            foreach (var constraint in all)
            {
                if (dirty is not null &&
                    dirty.Count > 0 &&
                    !constraint.References.Any(reference => dirty.Contains(reference.EntityId)))
                {
                    continue;
                }

                if (!TryApplyConstraint(constraints, constraint, entityIndex, diagnostics, out var changedIds))
                {
                    continue;
                }

                if (changedIds.Count == 0)
                {
                    continue;
                }

                changedInIteration = true;
                appliedCount++;
                foreach (var id in changedIds)
                {
                    adjustedEntityIds.Add(id);
                    nextDirty.Add(id);
                }
            }

            if (!changedInIteration)
            {
                break;
            }

            if (dirty is not null)
            {
                dirty = nextDirty;
                if (dirty.Count == 0)
                {
                    break;
                }
            }
        }

        return new CadGeometricConstraintSolveResult(
            appliedCount,
            adjustedEntityIds.Count,
            adjustedEntityIds.ToArray(),
            diagnostics);
    }

    private bool TryApplyConstraint(
        ICadConstraintService constraintService,
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        List<string> diagnostics,
        out IReadOnlyList<CadEntityId> changedEntityIds)
    {
        changedEntityIds = Array.Empty<CadEntityId>();
        try
        {
            return constraint.Kind switch
            {
                CadConstraintKind.Coincident => ApplyCoincident(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Concentric => ApplyConcentric(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Collinear => ApplyCollinear(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Parallel => ApplyParallel(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Perpendicular => ApplyPerpendicular(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Horizontal => ApplyHorizontal(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Vertical => ApplyVertical(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Tangent => ApplyTangent(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Equal => ApplyEqual(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Symmetric => ApplySymmetric(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Fixed => ApplyFixed(constraint, entityIndex, out changedEntityIds),
                CadConstraintKind.Distance => ApplyDistance(constraintService, constraint, entityIndex, diagnostics, out changedEntityIds),
                CadConstraintKind.AlignedDistance => ApplyAlignedDistance(constraintService, constraint, entityIndex, diagnostics, out changedEntityIds),
                CadConstraintKind.Angle => ApplyAngle(constraintService, constraint, entityIndex, diagnostics, out changedEntityIds),
                CadConstraintKind.Radius => ApplyRadius(constraintService, constraint, entityIndex, diagnostics, out changedEntityIds),
                CadConstraintKind.Diameter => ApplyDiameter(constraintService, constraint, entityIndex, diagnostics, out changedEntityIds),
                _ => false
            };
        }
        catch (Exception ex)
        {
            diagnostics.Add($"{constraint.Kind}:{constraint.Id.Value:D}:{ex.Message}");
            changedEntityIds = Array.Empty<CadEntityId>();
            return false;
        }
    }

    private static bool ApplyCoincident(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (constraint.References.Count < 2 ||
            !TryGetAnchor(entityIndex, constraint.References[0], out var anchor))
        {
            return false;
        }

        var touched = new HashSet<CadEntityId>();
        for (var index = 1; index < constraint.References.Count; index++)
        {
            var reference = constraint.References[index];
            if (!TrySetAnchor(entityIndex, reference, anchor))
            {
                continue;
            }

            touched.Add(reference.EntityId);
        }

        changed = touched.ToArray();
        return touched.Count > 0;
    }

    private static bool ApplyConcentric(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (constraint.References.Count < 2 ||
            !TryGetEntity(entityIndex, constraint.References[0].EntityId, out var source) ||
            !TryGetCenter(source, out var center))
        {
            return false;
        }

        var touched = new HashSet<CadEntityId>();
        for (var index = 1; index < constraint.References.Count; index++)
        {
            var targetRef = constraint.References[index];
            if (!TryGetEntity(entityIndex, targetRef.EntityId, out var target))
            {
                continue;
            }

            if (TrySetCenter(target, center))
            {
                touched.Add(targetRef.EntityId);
            }
        }

        changed = touched.ToArray();
        return touched.Count > 0;
    }

    private static bool ApplyCollinear(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (!TryGetTwoLines(constraint, entityIndex, out var source, out var target, out var targetId))
        {
            return false;
        }

        if (!TryGetDirection(source, out var sourceDirection))
        {
            return false;
        }

        var targetLength = LineLength(target);
        var targetMid = (ToVector(target.StartPoint) + ToVector(target.EndPoint)) * 0.5f;
        var projectedMid = ProjectOnLine(targetMid, ToVector(source.StartPoint), sourceDirection);
        SetLineByMidpointAndDirection(target, projectedMid, sourceDirection, targetLength);
        changed = [targetId];
        return true;
    }

    private static bool ApplyParallel(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (!TryGetTwoLines(constraint, entityIndex, out var source, out var target, out var targetId) ||
            !TryGetDirection(source, out var direction))
        {
            return false;
        }

        var targetLength = LineLength(target);
        var targetMid = (ToVector(target.StartPoint) + ToVector(target.EndPoint)) * 0.5f;
        SetLineByMidpointAndDirection(target, targetMid, direction, targetLength);
        changed = [targetId];
        return true;
    }

    private static bool ApplyPerpendicular(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (!TryGetTwoLines(constraint, entityIndex, out var source, out var target, out var targetId) ||
            !TryGetDirection(source, out var direction))
        {
            return false;
        }

        var perpendicular = new Vector2(-direction.Y, direction.X);
        if (!TryNormalize(perpendicular, out var normalized))
        {
            return false;
        }

        var targetLength = LineLength(target);
        var targetMid = (ToVector(target.StartPoint) + ToVector(target.EndPoint)) * 0.5f;
        SetLineByMidpointAndDirection(target, targetMid, normalized, targetLength);
        changed = [targetId];
        return true;
    }

    private static bool ApplyHorizontal(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        var touched = new HashSet<CadEntityId>();
        foreach (var reference in constraint.References)
        {
            if (!TryGetEntity(entityIndex, reference.EntityId, out var entity) || entity is not Line line)
            {
                continue;
            }

            var y = line.StartPoint.Y;
            line.EndPoint = new XYZ(line.EndPoint.X, y, line.EndPoint.Z);
            touched.Add(reference.EntityId);
        }

        changed = touched.ToArray();
        return touched.Count > 0;
    }

    private static bool ApplyVertical(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        var touched = new HashSet<CadEntityId>();
        foreach (var reference in constraint.References)
        {
            if (!TryGetEntity(entityIndex, reference.EntityId, out var entity) || entity is not Line line)
            {
                continue;
            }

            var x = line.StartPoint.X;
            line.EndPoint = new XYZ(x, line.EndPoint.Y, line.EndPoint.Z);
            touched.Add(reference.EntityId);
        }

        changed = touched.ToArray();
        return touched.Count > 0;
    }

    private static bool ApplyTangent(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (constraint.References.Count < 2)
        {
            return false;
        }

        if (!TryGetEntity(entityIndex, constraint.References[0].EntityId, out var first) ||
            !TryGetEntity(entityIndex, constraint.References[1].EntityId, out var second))
        {
            return false;
        }

        if (first is Line line && TryGetCircleLike(second, out var center, out var radius))
        {
            AlignLineTangentToCircle(line, center, radius);
            changed = [constraint.References[0].EntityId];
            return true;
        }

        if (second is Line lineSecond && TryGetCircleLike(first, out center, out radius))
        {
            AlignLineTangentToCircle(lineSecond, center, radius);
            changed = [constraint.References[1].EntityId];
            return true;
        }

        if (TryGetCircleLike(first, out var firstCenter, out var firstRadius) &&
            TryGetCircleLike(second, out var secondCenter, out var secondRadius) &&
            TrySetCircleLikeCenter(second, firstCenter + NormalizeOrDefault(secondCenter - firstCenter) * (float)(firstRadius + secondRadius)))
        {
            changed = [constraint.References[1].EntityId];
            return true;
        }

        return false;
    }

    private static bool ApplyEqual(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (constraint.References.Count < 2 ||
            !TryGetEntity(entityIndex, constraint.References[0].EntityId, out var source) ||
            !TryGetEntity(entityIndex, constraint.References[1].EntityId, out var target))
        {
            return false;
        }

        if (source is Line sourceLine && target is Line targetLine)
        {
            var length = LineLength(sourceLine);
            if (!TryGetDirection(targetLine, out var direction))
            {
                return false;
            }

            var mid = (ToVector(targetLine.StartPoint) + ToVector(targetLine.EndPoint)) * 0.5f;
            SetLineByMidpointAndDirection(targetLine, mid, direction, length);
            changed = [constraint.References[1].EntityId];
            return true;
        }

        if (TryGetCircleLike(source, out _, out var sourceRadius) &&
            TrySetCircleLikeRadius(target, sourceRadius))
        {
            changed = [constraint.References[1].EntityId];
            return true;
        }

        return false;
    }

    private static bool ApplyDistance(
        ICadConstraintService constraintService,
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        List<string> diagnostics,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (constraint.References.Count < 2 ||
            !TryGetAnchor(entityIndex, constraint.References[0], out var first) ||
            !TryGetAnchor(entityIndex, constraint.References[1], out var second))
        {
            return false;
        }

        var measured = Vector2.Distance(first, second);
        if (!constraint.IsDriving)
        {
            UpdateDrivenScalar(constraintService, constraint, "Distance", measured, diagnostics);
            return false;
        }

        if (!TryGetScalar(constraint, diagnostics, "Distance", measured, out var target))
        {
            return false;
        }

        var direction = NormalizeOrDefault(second - first);
        var next = first + direction * (float)target;
        if (!TrySetAnchor(entityIndex, constraint.References[1], next))
        {
            return false;
        }

        changed = [constraint.References[1].EntityId];
        return true;
    }

    private static bool ApplyAlignedDistance(
        ICadConstraintService constraintService,
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        List<string> diagnostics,
        out IReadOnlyList<CadEntityId> changed)
    {
        return ApplyDistance(constraintService, constraint, entityIndex, diagnostics, out changed);
    }

    private static bool ApplyAngle(
        ICadConstraintService constraintService,
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        List<string> diagnostics,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (!TryGetTwoLines(constraint, entityIndex, out var source, out var target, out var targetId) ||
            !TryGetDirection(source, out var sourceDirection) ||
            !TryGetDirection(target, out var targetDirection))
        {
            return false;
        }

        var measured = AngleBetweenDegrees(sourceDirection, targetDirection);
        if (!constraint.IsDriving)
        {
            UpdateDrivenScalar(constraintService, constraint, "Angle", measured, diagnostics);
            return false;
        }

        if (!TryGetScalar(constraint, diagnostics, "Angle", measured, out var targetAngleDegrees))
        {
            return false;
        }

        var baseAngle = Math.Atan2(sourceDirection.Y, sourceDirection.X);
        var radians = DegreesToRadians(targetAngleDegrees);
        var desiredDirection = new Vector2((float)Math.Cos(baseAngle + radians), (float)Math.Sin(baseAngle + radians));
        var midpoint = (ToVector(target.StartPoint) + ToVector(target.EndPoint)) * 0.5f;
        var length = Math.Max(LineLength(target), Epsilon);
        SetLineByMidpointAndDirection(target, midpoint, desiredDirection, length);
        changed = [targetId];
        return true;
    }

    private static bool ApplyRadius(
        ICadConstraintService constraintService,
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        List<string> diagnostics,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (constraint.References.Count < 1 ||
            !TryGetEntity(entityIndex, constraint.References[0].EntityId, out var entity) ||
            !TryGetCircleLike(entity, out _, out var measured))
        {
            return false;
        }

        if (!constraint.IsDriving)
        {
            UpdateDrivenScalar(constraintService, constraint, "Radius", measured, diagnostics);
            return false;
        }

        if (!TryGetScalar(constraint, diagnostics, "Radius", measured, out var targetRadius))
        {
            return false;
        }

        if (!TrySetCircleLikeRadius(entity, targetRadius))
        {
            return false;
        }

        changed = [constraint.References[0].EntityId];
        return true;
    }

    private static bool ApplyDiameter(
        ICadConstraintService constraintService,
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        List<string> diagnostics,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (constraint.References.Count < 1 ||
            !TryGetEntity(entityIndex, constraint.References[0].EntityId, out var entity) ||
            !TryGetCircleLike(entity, out _, out var measuredRadius))
        {
            return false;
        }

        var measuredDiameter = measuredRadius * 2d;
        if (!constraint.IsDriving)
        {
            UpdateDrivenScalar(constraintService, constraint, "Diameter", measuredDiameter, diagnostics);
            return false;
        }

        if (!TryGetScalar(constraint, diagnostics, "Diameter", measuredDiameter, out var targetDiameter))
        {
            return false;
        }

        if (!TrySetCircleLikeRadius(entity, targetDiameter * 0.5d))
        {
            return false;
        }

        changed = [constraint.References[0].EntityId];
        return true;
    }

    private static bool ApplySymmetric(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (constraint.References.Count < 2)
        {
            return false;
        }

        Vector2 axisStart;
        Vector2 axisEnd;
        var targetStartIndex = 1;
        if (TryGetEntity(entityIndex, constraint.References[0].EntityId, out var axisEntity) &&
            axisEntity is Line axisLine)
        {
            axisStart = ToVector(axisLine.StartPoint);
            axisEnd = ToVector(axisLine.EndPoint);
        }
        else
        {
            if (constraint.References.Count < 3 ||
                !TryGetAnchor(entityIndex, constraint.References[0], out axisStart) ||
                !TryGetAnchor(entityIndex, constraint.References[1], out axisEnd))
            {
                return false;
            }

            targetStartIndex = 2;
        }

        if (Vector2.DistanceSquared(axisStart, axisEnd) <= Epsilon * Epsilon)
        {
            return false;
        }

        var touched = new HashSet<CadEntityId>();
        for (var index = targetStartIndex; index < constraint.References.Count; index++)
        {
            var reference = constraint.References[index];
            if (!TryGetEntity(entityIndex, reference.EntityId, out var entity))
            {
                continue;
            }

            if (entity is Line targetLine && reference.VertexIndex is null)
            {
                var mirroredStart = MirrorPoint(ToVector(targetLine.StartPoint), axisStart, axisEnd);
                var mirroredEnd = MirrorPoint(ToVector(targetLine.EndPoint), axisStart, axisEnd);
                targetLine.StartPoint = ToXyz(mirroredStart, targetLine.StartPoint.Z);
                targetLine.EndPoint = ToXyz(mirroredEnd, targetLine.EndPoint.Z);
                touched.Add(reference.EntityId);
                continue;
            }

            if (!TryGetAnchor(entityIndex, reference, out var anchor))
            {
                continue;
            }

            var mirrored = MirrorPoint(anchor, axisStart, axisEnd);
            if (TrySetAnchor(entityIndex, reference, mirrored))
            {
                touched.Add(reference.EntityId);
            }
        }

        changed = touched.ToArray();
        return touched.Count > 0;
    }

    private bool ApplyFixed(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out IReadOnlyList<CadEntityId> changed)
    {
        changed = Array.Empty<CadEntityId>();
        if (constraint.References.Count == 0)
        {
            return false;
        }

        if (!_fixedSnapshots.TryGetValue(constraint.Id, out var snapshots))
        {
            var captured = new List<FixedSnapshot>(constraint.References.Count);
            foreach (var reference in constraint.References)
            {
                if (!TryGetEntity(entityIndex, reference.EntityId, out var entity) ||
                    !TryCaptureSnapshot(reference.EntityId, entity, out var snapshot))
                {
                    continue;
                }

                captured.Add(snapshot);
            }

            if (captured.Count == 0)
            {
                return false;
            }

            snapshots = captured;
            _fixedSnapshots[constraint.Id] = snapshots;
        }

        var touched = new HashSet<CadEntityId>();
        foreach (var snapshot in snapshots)
        {
            if (!TryGetEntity(entityIndex, snapshot.EntityId, out var entity))
            {
                continue;
            }

            if (RestoreSnapshot(entity, snapshot))
            {
                touched.Add(snapshot.EntityId);
            }
        }

        changed = touched.ToArray();
        return touched.Count > 0;
    }

    private static bool TryGetTwoLines(
        CadConstraint constraint,
        ICadEntityIndex entityIndex,
        out Line first,
        out Line second,
        out CadEntityId secondId)
    {
        first = null!;
        second = null!;
        secondId = default;
        if (constraint.References.Count < 2 ||
            !TryGetEntity(entityIndex, constraint.References[0].EntityId, out var firstEntity) ||
            !TryGetEntity(entityIndex, constraint.References[1].EntityId, out var secondEntity) ||
            firstEntity is not Line firstLine ||
            secondEntity is not Line secondLine)
        {
            return false;
        }

        first = firstLine;
        second = secondLine;
        secondId = constraint.References[1].EntityId;
        return true;
    }

    private static bool TryGetAnchor(ICadEntityIndex index, CadConstraintReference reference, out Vector2 anchor)
    {
        anchor = default;
        if (!TryGetEntity(index, reference.EntityId, out var entity))
        {
            return false;
        }

        switch (entity)
        {
            case Point point:
                anchor = ToVector(point.Location);
                return true;
            case Line line when reference.VertexIndex == 0:
                anchor = ToVector(line.StartPoint);
                return true;
            case Line line when reference.VertexIndex == 1:
                anchor = ToVector(line.EndPoint);
                return true;
            case Line line:
                anchor = (ToVector(line.StartPoint) + ToVector(line.EndPoint)) * 0.5f;
                return true;
            case Arc arc:
                anchor = ToVector(arc.Center);
                return true;
            case Circle circle:
                anchor = ToVector(circle.Center);
                return true;
            case Ellipse ellipse:
                anchor = ToVector(ellipse.Center);
                return true;
            case MText mtext:
                anchor = ToVector(mtext.InsertPoint);
                return true;
            case TextEntity text:
                anchor = ToVector(text.InsertPoint);
                return true;
            default:
                return false;
        }
    }

    private static bool TrySetAnchor(ICadEntityIndex index, CadConstraintReference reference, Vector2 anchor)
    {
        if (!TryGetEntity(index, reference.EntityId, out var entity))
        {
            return false;
        }

        switch (entity)
        {
            case Point point:
                point.Location = ToXyz(anchor, point.Location.Z);
                return true;
            case Line line when reference.VertexIndex == 0:
                line.StartPoint = ToXyz(anchor, line.StartPoint.Z);
                return true;
            case Line line when reference.VertexIndex == 1:
                line.EndPoint = ToXyz(anchor, line.EndPoint.Z);
                return true;
            case Line line:
            {
                var currentMid = (ToVector(line.StartPoint) + ToVector(line.EndPoint)) * 0.5f;
                var delta = anchor - currentMid;
                line.StartPoint = ToXyz(ToVector(line.StartPoint) + delta, line.StartPoint.Z);
                line.EndPoint = ToXyz(ToVector(line.EndPoint) + delta, line.EndPoint.Z);
                return true;
            }
            case Arc arc:
                arc.Center = ToXyz(anchor, arc.Center.Z);
                return true;
            case Circle circle:
                circle.Center = ToXyz(anchor, circle.Center.Z);
                return true;
            case Ellipse ellipse:
                ellipse.Center = ToXyz(anchor, ellipse.Center.Z);
                return true;
            case MText mtext:
                mtext.InsertPoint = ToXyz(anchor, mtext.InsertPoint.Z);
                return true;
            case TextEntity text:
                text.InsertPoint = ToXyz(anchor, text.InsertPoint.Z);
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetCenter(Entity entity, out Vector2 center)
    {
        center = default;
        return entity switch
        {
            Arc arc => TrySet(out center, ToVector(arc.Center)),
            Circle circle => TrySet(out center, ToVector(circle.Center)),
            Ellipse ellipse => TrySet(out center, ToVector(ellipse.Center)),
            _ => false
        };
    }

    private static bool TrySetCenter(Entity entity, Vector2 center)
    {
        switch (entity)
        {
            case Arc arc:
                arc.Center = ToXyz(center, arc.Center.Z);
                return true;
            case Circle circle:
                circle.Center = ToXyz(center, circle.Center.Z);
                return true;
            case Ellipse ellipse:
                ellipse.Center = ToXyz(center, ellipse.Center.Z);
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetEntity(ICadEntityIndex entityIndex, CadEntityId id, out Entity entity)
    {
        return entityIndex.TryGetEntity(id, out entity!);
    }

    private static bool TryGetDirection(Line line, out Vector2 direction)
    {
        var raw = ToVector(line.EndPoint) - ToVector(line.StartPoint);
        return TryNormalize(raw, out direction);
    }

    private static bool TryNormalize(Vector2 value, out Vector2 normalized)
    {
        normalized = default;
        var length = value.Length();
        if (length <= Epsilon)
        {
            return false;
        }

        normalized = value / length;
        return true;
    }

    private static Vector2 NormalizeOrDefault(Vector2 value)
    {
        return TryNormalize(value, out var normalized) ? normalized : Vector2.UnitX;
    }

    private static float LineLength(Line line)
    {
        return Vector2.Distance(ToVector(line.StartPoint), ToVector(line.EndPoint));
    }

    private static void SetLineByMidpointAndDirection(Line line, Vector2 midpoint, Vector2 direction, float length)
    {
        var normalized = NormalizeOrDefault(direction);
        var half = normalized * Math.Max(length, Epsilon) * 0.5f;
        line.StartPoint = ToXyz(midpoint - half, line.StartPoint.Z);
        line.EndPoint = ToXyz(midpoint + half, line.EndPoint.Z);
    }

    private static Vector2 ProjectOnLine(Vector2 point, Vector2 linePoint, Vector2 lineDirection)
    {
        var normalized = NormalizeOrDefault(lineDirection);
        var t = Vector2.Dot(point - linePoint, normalized);
        return linePoint + normalized * t;
    }

    private static void AlignLineTangentToCircle(Line line, Vector2 center, double radius)
    {
        var midpoint = (ToVector(line.StartPoint) + ToVector(line.EndPoint)) * 0.5f;
        var toMid = midpoint - center;
        var radial = NormalizeOrDefault(toMid);
        midpoint = center + radial * (float)Math.Abs(radius);
        var tangentDirection = NormalizeOrDefault(new Vector2(-radial.Y, radial.X));
        var length = Math.Max(LineLength(line), Epsilon);
        SetLineByMidpointAndDirection(line, midpoint, tangentDirection, length);
    }

    private static bool TryGetCircleLike(Entity entity, out Vector2 center, out double radius)
    {
        center = default;
        radius = 0d;
        switch (entity)
        {
            case Arc arc:
                center = ToVector(arc.Center);
                radius = arc.Radius;
                return true;
            case Circle circle:
                center = ToVector(circle.Center);
                radius = circle.Radius;
                return true;
            default:
                return false;
        }
    }

    private static bool TrySetCircleLikeCenter(Entity entity, Vector2 center)
    {
        switch (entity)
        {
            case Arc arc:
                arc.Center = ToXyz(center, arc.Center.Z);
                return true;
            case Circle circle:
                circle.Center = ToXyz(center, circle.Center.Z);
                return true;
            default:
                return false;
        }
    }

    private static bool TrySetCircleLikeRadius(Entity entity, double radius)
    {
        switch (entity)
        {
            case Arc arc:
                arc.Radius = radius;
                return true;
            case Circle circle:
                circle.Radius = radius;
                return true;
            default:
                return false;
        }
    }

    private static Vector2 MirrorPoint(Vector2 point, Vector2 axisStart, Vector2 axisEnd)
    {
        var axis = axisEnd - axisStart;
        if (!TryNormalize(axis, out var axisDir))
        {
            return point;
        }

        var relative = point - axisStart;
        var projection = axisDir * Vector2.Dot(relative, axisDir);
        var perpendicular = relative - projection;
        return axisStart + projection - perpendicular;
    }

    private static bool TryCaptureSnapshot(CadEntityId id, Entity entity, out FixedSnapshot snapshot)
    {
        snapshot = entity switch
        {
            Line line => new FixedSnapshot(
                id,
                Kind: "LINE",
                A: ToVector(line.StartPoint),
                B: ToVector(line.EndPoint),
                S0: 0d,
                S1: 0d,
                S2: 0d),
            Arc arc => new FixedSnapshot(
                id,
                Kind: "ARC",
                A: ToVector(arc.Center),
                B: default,
                S0: arc.Radius,
                S1: arc.StartAngle,
                S2: arc.EndAngle),
            Circle circle => new FixedSnapshot(
                id,
                Kind: "CIRCLE",
                A: ToVector(circle.Center),
                B: default,
                S0: circle.Radius,
                S1: 0d,
                S2: 0d),
            Point point => new FixedSnapshot(
                id,
                Kind: "POINT",
                A: ToVector(point.Location),
                B: default,
                S0: 0d,
                S1: 0d,
                S2: 0d),
            _ => null!
        };

        return snapshot is not null;
    }

    private static bool RestoreSnapshot(Entity entity, FixedSnapshot snapshot)
    {
        switch (entity)
        {
            case Line line when string.Equals(snapshot.Kind, "LINE", StringComparison.Ordinal):
                line.StartPoint = ToXyz(snapshot.A, line.StartPoint.Z);
                line.EndPoint = ToXyz(snapshot.B, line.EndPoint.Z);
                return true;
            case Arc arc when string.Equals(snapshot.Kind, "ARC", StringComparison.Ordinal):
                arc.Center = ToXyz(snapshot.A, arc.Center.Z);
                arc.Radius = snapshot.S0;
                arc.StartAngle = snapshot.S1;
                arc.EndAngle = snapshot.S2;
                return true;
            case Circle circle when string.Equals(snapshot.Kind, "CIRCLE", StringComparison.Ordinal):
                circle.Center = ToXyz(snapshot.A, circle.Center.Z);
                circle.Radius = snapshot.S0;
                return true;
            case Point point when string.Equals(snapshot.Kind, "POINT", StringComparison.Ordinal):
                point.Location = ToXyz(snapshot.A, point.Location.Z);
                return true;
            default:
                return false;
        }
    }

    private static Vector2 ToVector(XYZ xyz)
    {
        return new Vector2((float)xyz.X, (float)xyz.Y);
    }

    private static XYZ ToXyz(Vector2 value, double z)
    {
        return new XYZ(value.X, value.Y, z);
    }

    private static bool TrySet(out Vector2 target, Vector2 value)
    {
        target = value;
        return true;
    }

    private static bool TryGetScalar(
        CadConstraint constraint,
        List<string> diagnostics,
        string semanticName,
        double fallback,
        out double value)
    {
        var candidates = semanticName switch
        {
            "Distance" => new[] { "Distance", "Value", "distance", "value" },
            "Angle" => new[] { "Angle", "Value", "angle", "value" },
            "Radius" => new[] { "Radius", "Value", "radius", "value" },
            "Diameter" => new[] { "Diameter", "Value", "diameter", "value" },
            _ => new[] { semanticName, "Value", semanticName.ToLowerInvariant(), "value" }
        };

        foreach (var key in candidates)
        {
            if (!constraint.Parameters.TryGetValue(key, out var token))
            {
                continue;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }

            diagnostics.Add($"{constraint.Kind}:{constraint.Id.Value:D}:invalid scalar '{token}' for {semanticName}.");
            value = fallback;
            return false;
        }

        diagnostics.Add($"{constraint.Kind}:{constraint.Id.Value:D}:missing scalar for {semanticName}.");
        value = fallback;
        return false;
    }

    private static void UpdateDrivenScalar(
        ICadConstraintService constraintService,
        CadConstraint constraint,
        string semanticName,
        double measuredValue,
        List<string> diagnostics)
    {
        var key = semanticName;
        var current = new Dictionary<string, string>(constraint.Parameters, StringComparer.Ordinal)
        {
            [key] = measuredValue.ToString("0.###", CultureInfo.InvariantCulture)
        };

        if (!constraintService.TrySetConstraintParameters(constraint.Id, current, isDriving: false, out _))
        {
            diagnostics.Add($"{constraint.Kind}:{constraint.Id.Value:D}:failed to update driven scalar '{key}'.");
        }
    }

    private static double AngleBetweenDegrees(Vector2 first, Vector2 second)
    {
        var firstNorm = NormalizeOrDefault(first);
        var secondNorm = NormalizeOrDefault(second);
        var dot = Math.Clamp(Vector2.Dot(firstNorm, secondNorm), -1f, 1f);
        return RadiansToDegrees(Math.Acos(dot));
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180d);
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * (180d / Math.PI);
    }

    private static bool IsSolvable(CadConstraintKind kind)
    {
        return kind switch
        {
            CadConstraintKind.Coincident => true,
            CadConstraintKind.Concentric => true,
            CadConstraintKind.Collinear => true,
            CadConstraintKind.Parallel => true,
            CadConstraintKind.Perpendicular => true,
            CadConstraintKind.Horizontal => true,
            CadConstraintKind.Vertical => true,
            CadConstraintKind.Tangent => true,
            CadConstraintKind.Equal => true,
            CadConstraintKind.Symmetric => true,
            CadConstraintKind.Fixed => true,
            CadConstraintKind.Distance => true,
            CadConstraintKind.AlignedDistance => true,
            CadConstraintKind.Angle => true,
            CadConstraintKind.Radius => true,
            CadConstraintKind.Diameter => true,
            _ => false
        };
    }

    private sealed record FixedSnapshot(
        CadEntityId EntityId,
        string Kind,
        Vector2 A,
        Vector2 B,
        double S0,
        double S1,
        double S2);
}
