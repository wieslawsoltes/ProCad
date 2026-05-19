using System;
using System.Collections.Generic;

namespace ProCad.Rendering;

public sealed class RenderStats
{
    public static RenderStats Empty { get; } = new RenderStats(
        entityCount: 0,
        visibleEntityCount: 0,
        layerCount: 0,
        primitiveCount: 0,
        primitiveCounts: new Dictionary<string, int>(StringComparer.Ordinal),
        buildMilliseconds: 0,
        budgetViolations: Array.Empty<RenderBudgetViolation>());

    public int EntityCount { get; }
    public int VisibleEntityCount { get; }
    public int LayerCount { get; }
    public int PrimitiveCount { get; }
    public IReadOnlyDictionary<string, int> PrimitiveCounts { get; }
    public double BuildMilliseconds { get; }
    public IReadOnlyList<RenderBudgetViolation> BudgetViolations { get; }

    public bool IsWithinBudget => BudgetViolations.Count == 0;

    internal RenderStats(
        int entityCount,
        int visibleEntityCount,
        int layerCount,
        int primitiveCount,
        IReadOnlyDictionary<string, int> primitiveCounts,
        double buildMilliseconds,
        IReadOnlyList<RenderBudgetViolation> budgetViolations)
    {
        EntityCount = entityCount;
        VisibleEntityCount = visibleEntityCount;
        LayerCount = layerCount;
        PrimitiveCount = primitiveCount;
        PrimitiveCounts = primitiveCounts;
        BuildMilliseconds = buildMilliseconds;
        BudgetViolations = budgetViolations;
    }

    internal static RenderStats Build(
        RenderStatsAccumulator accumulator,
        IReadOnlyList<RenderLayer> layers,
        TimeSpan buildTime,
        RenderPerformanceBudget? budget)
    {
        var primitiveCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var primitiveTotal = 0;

        foreach (var layer in layers)
        {
            foreach (var primitive in layer.Primitives)
            {
                AccumulatePrimitive(primitive, primitiveCounts, ref primitiveTotal);
            }
        }

        var budgetViolations = EvaluateBudget(accumulator, layers.Count, primitiveTotal, buildTime, budget);
        return new RenderStats(
            accumulator.EntityCount,
            accumulator.VisibleEntityCount,
            layers.Count,
            primitiveTotal,
            primitiveCounts,
            buildTime.TotalMilliseconds,
            budgetViolations);
    }

    private static void AccumulatePrimitive(
        IRenderPrimitive primitive,
        Dictionary<string, int> counts,
        ref int total)
    {
        if (primitive is null)
        {
            return;
        }

        total++;
        var name = primitive.GetType().Name;
        counts.TryGetValue(name, out var count);
        counts[name] = count + 1;

        if (primitive is RenderClipGroup clipGroup)
        {
            foreach (var child in clipGroup.Primitives)
            {
                AccumulatePrimitive(child, counts, ref total);
            }
        }
    }

    private static IReadOnlyList<RenderBudgetViolation> EvaluateBudget(
        RenderStatsAccumulator accumulator,
        int layerCount,
        int primitiveCount,
        TimeSpan buildTime,
        RenderPerformanceBudget? budget)
    {
        if (budget is null)
        {
            return Array.Empty<RenderBudgetViolation>();
        }

        var violations = new List<RenderBudgetViolation>();
        AddViolationIfNeeded(violations, "entities.total", accumulator.EntityCount, budget.MaxEntities);
        AddViolationIfNeeded(violations, "entities.visible", accumulator.VisibleEntityCount, budget.MaxVisibleEntities);
        AddViolationIfNeeded(violations, "layers", layerCount, budget.MaxLayers);
        AddViolationIfNeeded(violations, "primitives.total", primitiveCount, budget.MaxPrimitives);
        AddViolationIfNeeded(violations, "build.ms", buildTime.TotalMilliseconds, budget.MaxBuildMilliseconds);
        return violations;
    }

    private static void AddViolationIfNeeded(
        List<RenderBudgetViolation> violations,
        string metric,
        int actual,
        int? limit)
    {
        if (limit.HasValue && actual > limit.Value)
        {
            violations.Add(new RenderBudgetViolation(metric, actual, limit.Value));
        }
    }

    private static void AddViolationIfNeeded(
        List<RenderBudgetViolation> violations,
        string metric,
        double actual,
        double? limit)
    {
        if (limit.HasValue && actual > limit.Value)
        {
            violations.Add(new RenderBudgetViolation(metric, actual, limit.Value));
        }
    }
}
