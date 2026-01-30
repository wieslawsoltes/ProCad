using System;
using System.Collections.Generic;
using ACadSharp.Entities;
using ACadSharp.Objects.Evaluations;
using ACadSharp.Tables;
using CSMath;

namespace ACadInspector.Rendering;

internal sealed class DynamicBlockActionMap
{
    private readonly Dictionary<ulong, Transform> _byHandle = new();
    private readonly Dictionary<Entity, Transform> _byReference =
        new(ReferenceEqualityComparer.Instance);

    public bool TryGetTransform(Entity entity, out Transform transform)
    {
        transform = default!;
        if (entity is null)
        {
            return false;
        }

        var key = ResolveEntityKey(entity);
        if (key != 0 && _byHandle.TryGetValue(key, out var found) && found is not null)
        {
            transform = found;
            return true;
        }

        if (_byReference.TryGetValue(entity, out found) && found is not null)
        {
            transform = found;
            return true;
        }

        return false;
    }

    public void Add(Entity entity, Transform transform)
    {
        if (entity is null)
        {
            return;
        }

        var key = ResolveEntityKey(entity);
        if (key != 0)
        {
            if (_byHandle.TryGetValue(key, out var existing))
            {
                _byHandle[key] = RenderTransformUtils.Combine(transform, existing);
            }
            else
            {
                _byHandle[key] = transform;
            }

            return;
        }

        if (_byReference.TryGetValue(entity, out var current))
        {
            _byReference[entity] = RenderTransformUtils.Combine(transform, current);
        }
        else
        {
            _byReference[entity] = transform;
        }
    }

    public bool IsEmpty => _byHandle.Count == 0 && _byReference.Count == 0;

    private static ulong ResolveEntityKey(Entity entity)
    {
        if (DynamicBlockVisibilityFilter.TryGetBlockRepHandle(entity, out var handle))
        {
            return handle;
        }

        return entity.Handle;
    }
}

internal static class DynamicBlockActionResolver
{
    public static DynamicBlockActionMap? TryCreate(BlockRecord block, DynamicBlockPropertySet? properties)
    {
        if (block?.EvaluationGraph is null || properties is null)
        {
            return null;
        }

        var expressions = new List<EvaluationExpression>();
        foreach (var node in block.EvaluationGraph.Nodes)
        {
            if (node?.Expression is not null)
            {
                expressions.Add(node.Expression);
            }
        }

        return TryCreateFromExpressions(expressions, properties);
    }

    internal static DynamicBlockActionMap? TryCreateFromExpressions(
        IEnumerable<EvaluationExpression> expressions,
        DynamicBlockPropertySet? properties)
    {
        if (properties is null)
        {
            return null;
        }

        var flipParameters = new List<BlockFlipParameter>();
        var linearParameters = new List<BlockLinearParameter>();
        var flipActions = new List<BlockFlipAction>();
        var otherActions = new List<BlockAction>();

        foreach (var expression in expressions)
        {
            switch (expression)
            {
                case BlockFlipParameter flipParam:
                    flipParameters.Add(flipParam);
                    break;
                case BlockLinearParameter linearParam:
                    linearParameters.Add(linearParam);
                    break;
                case BlockFlipAction flipAction:
                    flipActions.Add(flipAction);
                    break;
                case BlockAction action:
                    otherActions.Add(action);
                    break;
            }
        }

        var map = new DynamicBlockActionMap();
        ApplyFlipActions(flipParameters, flipActions, properties, map);
        ApplyStretchActions(linearParameters, otherActions, properties, map);

        return map.IsEmpty ? null : map;
    }

    private static void ApplyFlipActions(
        IReadOnlyList<BlockFlipParameter> parameters,
        IReadOnlyList<BlockFlipAction> actions,
        DynamicBlockPropertySet properties,
        DynamicBlockActionMap map)
    {
        if (parameters.Count == 0 || actions.Count == 0)
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            if (!IsFlipActive(parameter, properties))
            {
                continue;
            }

            var transform = BuildFlipTransform(parameter);
            foreach (var action in actions)
            {
                if (!MatchesAction(parameter.ElementName, action.ElementName, parameters.Count))
                {
                    continue;
                }

                foreach (var entity in action.Entities)
                {
                    map.Add(entity, transform);
                }
            }
        }
    }

    private static void ApplyStretchActions(
        IReadOnlyList<BlockLinearParameter> parameters,
        IReadOnlyList<BlockAction> actions,
        DynamicBlockPropertySet properties,
        DynamicBlockActionMap map)
    {
        if (parameters.Count == 0 || actions.Count == 0)
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            if (!TryResolveLinearValue(parameter, properties, out var value))
            {
                continue;
            }

            var baseLength = parameter.FirstPoint.DistanceFrom(parameter.SecondPoint);
            if (baseLength <= 0)
            {
                continue;
            }

            var delta = value - baseLength;
            if (Math.Abs(delta) < 1e-6)
            {
                continue;
            }

            var dir = (parameter.SecondPoint - parameter.FirstPoint).Normalize();
            if (dir.IsZero())
            {
                continue;
            }

            var offset = dir * delta;
            var transform = Transform.CreateTranslation(offset);

            foreach (var action in actions)
            {
                if (!MatchesAction(parameter.ElementName, action.ElementName, parameters.Count))
                {
                    continue;
                }

                foreach (var entity in action.Entities)
                {
                    map.Add(entity, transform);
                }
            }
        }
    }

    private static bool IsFlipActive(BlockFlipParameter parameter, DynamicBlockPropertySet properties)
    {
        if (!string.IsNullOrWhiteSpace(parameter.FlippedStateName) &&
            properties.ContainsString(parameter.FlippedStateName))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(parameter.BaseStateName) &&
            properties.ContainsString(parameter.BaseStateName))
        {
            return false;
        }

        return false;
    }

    private static bool TryResolveLinearValue(
        BlockLinearParameter parameter,
        DynamicBlockPropertySet properties,
        out double value)
    {
        return properties.TryGetNumericValue(
            parameter.ElementName,
            parameter.Label,
            parameter.Id,
            parameter.Value1071,
            out value);
    }

    private static bool MatchesAction(string? parameterName, string? actionName, int parameterCount)
    {
        if (parameterCount <= 1)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(parameterName) || string.IsNullOrWhiteSpace(actionName))
        {
            return false;
        }

        return actionName.Contains(parameterName, StringComparison.OrdinalIgnoreCase);
    }

    private static Transform BuildFlipTransform(BlockFlipParameter parameter)
    {
        var start = parameter.FirstPoint;
        var end = parameter.SecondPoint;
        var dir = (end - start).Normalize();
        if (dir.IsZero())
        {
            return new Transform(Matrix4.Identity);
        }

        var angle = Math.Atan2(dir.Y, dir.X);
        var toOrigin = Transform.CreateTranslation(new XYZ(-start.X, -start.Y, -start.Z));
        var align = Transform.CreateRotation(XYZ.AxisZ, -angle);
        var mirror = Transform.CreateScaling(new XYZ(1, -1, 1));
        var unalign = Transform.CreateRotation(XYZ.AxisZ, angle);
        var back = Transform.CreateTranslation(start);

        var result = RenderTransformUtils.Combine(align, toOrigin);
        result = RenderTransformUtils.Combine(mirror, result);
        result = RenderTransformUtils.Combine(unalign, result);
        result = RenderTransformUtils.Combine(back, result);
        return result;
    }
}
