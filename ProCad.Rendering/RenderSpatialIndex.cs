using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;

namespace ProCad.Rendering;

/// <summary>
/// Provides a static spatial index for render primitives to accelerate hit testing.
/// </summary>
public sealed class RenderSpatialIndex
{
    /// <summary>
    /// Gets an empty spatial index instance.
    /// </summary>
    public static readonly RenderSpatialIndex Empty = new(Array.Empty<RenderSpatialItem>(), Array.Empty<Node>(), 0, -1);

    private readonly RenderSpatialItem[] _items;
    private readonly Node[] _nodes;
    private readonly int _nodeCount;
    private readonly int _root;

    private RenderSpatialIndex(RenderSpatialItem[] items, Node[] nodes, int nodeCount, int root)
    {
        _items = items;
        _nodes = nodes;
        _nodeCount = nodeCount;
        _root = root;
    }

    /// <summary>
    /// Gets the number of indexed primitives.
    /// </summary>
    public int Count => _items.Length;

    /// <summary>
    /// Builds a spatial index for the provided render layers.
    /// </summary>
    public static RenderSpatialIndex Build(IReadOnlyList<RenderLayer> layers, RenderSpatialIndexOptions? options = null)
    {
        if (layers is null || layers.Count == 0)
        {
            return Empty;
        }

        var total = 0;
        for (var i = 0; i < layers.Count; i++)
        {
            total += layers[i].Primitives.Count;
        }

        if (total == 0)
        {
            return Empty;
        }

        var items = new RenderSpatialItem[total];
        var index = 0;
        for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            var primitives = layer.Primitives;
            for (var primitiveIndex = 0; primitiveIndex < primitives.Count; primitiveIndex++)
            {
                var primitive = primitives[primitiveIndex];
                items[index++] = new RenderSpatialItem(layer, primitive, layerIndex, primitiveIndex);
            }
        }

        var buildOptions = options ?? RenderSpatialIndexOptions.Default;
        var nodes = new Node[Math.Max(1, total * 2)];
        var nodeCount = 0;
        var root = BuildNode(items, 0, items.Length, nodes, ref nodeCount, buildOptions.MaxLeafSize);
        return new RenderSpatialIndex(items, nodes, nodeCount, root);
    }

    /// <summary>
    /// Queries the spatial index for primitives intersecting the provided bounds.
    /// </summary>
    public void Query(RenderBounds bounds, List<RenderSpatialHit> results, bool includeHiddenLayers = false)
    {
        if (_root < 0 || results is null)
        {
            return;
        }

        if (_nodeCount == 0)
        {
            return;
        }

        var stack = ArrayPool<int>.Shared.Rent(_nodeCount);
        var stackSize = 0;
        stack[stackSize++] = _root;

        while (stackSize > 0)
        {
            var nodeIndex = stack[--stackSize];
            var node = _nodes[nodeIndex];
            if (!node.Bounds.Intersects(bounds))
            {
                continue;
            }

            if (node.IsLeaf)
            {
                var start = node.Start;
                var end = start + node.Count;
                for (var i = start; i < end; i++)
                {
                    ref readonly var item = ref _items[i];
                    if (!includeHiddenLayers && !item.Layer.IsVisible)
                    {
                        continue;
                    }

                    if (!item.Bounds.Intersects(bounds))
                    {
                        continue;
                    }

                    results.Add(new RenderSpatialHit(item.Layer, item.Primitive, item.Bounds, item.LayerIndex, item.PrimitiveIndex));
                }

                continue;
            }

            stack[stackSize++] = node.Left;
            stack[stackSize++] = node.Right;
        }

        ArrayPool<int>.Shared.Return(stack);
    }

    /// <summary>
    /// Queries the spatial index for primitives intersecting a point with a tolerance.
    /// </summary>
    public void QueryPoint(Vector2 point, float tolerance, List<RenderSpatialHit> results, bool includeHiddenLayers = false)
    {
        var amount = tolerance;
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount))
        {
            amount = 0f;
        }

        var delta = new Vector2(amount, amount);
        var bounds = new RenderBounds(point - delta, point + delta);
        Query(bounds, results, includeHiddenLayers);
    }

    public void CollectNodeBounds(int maxDepth, List<RenderBounds> results)
    {
        if (_root < 0 || results is null || _nodeCount == 0)
        {
            return;
        }

        results.Clear();
        if (maxDepth < 0)
        {
            return;
        }

        var nodeStack = ArrayPool<int>.Shared.Rent(_nodeCount);
        var depthStack = ArrayPool<int>.Shared.Rent(_nodeCount);
        var stackSize = 0;
        nodeStack[stackSize] = _root;
        depthStack[stackSize] = 0;
        stackSize++;

        while (stackSize > 0)
        {
            stackSize--;
            var nodeIndex = nodeStack[stackSize];
            var depth = depthStack[stackSize];
            var node = _nodes[nodeIndex];

            results.Add(node.Bounds);
            if (depth >= maxDepth || node.IsLeaf)
            {
                continue;
            }

            nodeStack[stackSize] = node.Left;
            depthStack[stackSize] = depth + 1;
            stackSize++;
            nodeStack[stackSize] = node.Right;
            depthStack[stackSize] = depth + 1;
            stackSize++;
        }

        ArrayPool<int>.Shared.Return(nodeStack);
        ArrayPool<int>.Shared.Return(depthStack);
    }

    private static int BuildNode(
        RenderSpatialItem[] items,
        int start,
        int count,
        Node[] nodes,
        ref int nodeCount,
        int maxLeafSize)
    {
        var bounds = ComputeBounds(items, start, count);
        var nodeIndex = nodeCount++;

        if (count <= maxLeafSize)
        {
            nodes[nodeIndex] = Node.Leaf(bounds, start, count);
            return nodeIndex;
        }

        var centroidBounds = ComputeCentroidBounds(items, start, count);
        var extent = centroidBounds.Size;
        var axis = extent.X >= extent.Y ? 0 : 1;
        var splitIndex = TryFindSahSplit(items, start, count, axis, out var splitValue)
            ? PartitionByValue(items, start, count, axis, splitValue)
            : -1;

        if (splitIndex <= start || splitIndex >= start + count)
        {
            var mid = start + (count / 2);
            SelectNth(items, start, count, mid, axis);
            splitIndex = mid;
        }

        var left = BuildNode(items, start, splitIndex - start, nodes, ref nodeCount, maxLeafSize);
        var right = BuildNode(items, splitIndex, start + count - splitIndex, nodes, ref nodeCount, maxLeafSize);
        nodes[nodeIndex] = Node.Internal(bounds, left, right);
        return nodeIndex;
    }

    private static RenderBounds ComputeBounds(RenderSpatialItem[] items, int start, int count)
    {
        var bounds = RenderBounds.Empty;
        var end = start + count;
        for (var i = start; i < end; i++)
        {
            bounds = bounds.Expand(items[i].Bounds);
        }

        return bounds;
    }

    private static RenderBounds ComputeCentroidBounds(RenderSpatialItem[] items, int start, int count)
    {
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        var end = start + count;

        for (var i = start; i < end; i++)
        {
            var centroid = items[i].Centroid;
            if (centroid.X < min.X)
            {
                min.X = centroid.X;
            }

            if (centroid.Y < min.Y)
            {
                min.Y = centroid.Y;
            }

            if (centroid.X > max.X)
            {
                max.X = centroid.X;
            }

            if (centroid.Y > max.Y)
            {
                max.Y = centroid.Y;
            }
        }

        return new RenderBounds(min, max);
    }

    private static void SelectNth(RenderSpatialItem[] items, int start, int count, int nth, int axis)
    {
        var left = start;
        var right = start + count - 1;

        while (true)
        {
            if (left == right)
            {
                return;
            }

            var pivotIndex = (left + right) >> 1;
            pivotIndex = Partition(items, left, right, pivotIndex, axis);
            if (nth == pivotIndex)
            {
                return;
            }

            if (nth < pivotIndex)
            {
                right = pivotIndex - 1;
            }
            else
            {
                left = pivotIndex + 1;
            }
        }
    }

    private static int Partition(RenderSpatialItem[] items, int left, int right, int pivotIndex, int axis)
    {
        var pivotValue = GetCentroid(items[pivotIndex], axis);
        Swap(items, pivotIndex, right);
        var storeIndex = left;

        for (var i = left; i < right; i++)
        {
            if (GetCentroid(items[i], axis) < pivotValue)
            {
                Swap(items, storeIndex, i);
                storeIndex++;
            }
        }

        Swap(items, right, storeIndex);
        return storeIndex;
    }

    private static int PartitionByValue(RenderSpatialItem[] items, int start, int count, int axis, float splitValue)
    {
        var left = start;
        var right = start + count - 1;

        while (left <= right)
        {
            if (GetCentroid(items[left], axis) < splitValue)
            {
                left++;
                continue;
            }

            Swap(items, left, right);
            right--;
        }

        return left;
    }

    private static bool TryFindSahSplit(RenderSpatialItem[] items, int start, int count, int axis, out float splitValue)
    {
        const int BinCount = 8;
        splitValue = 0f;
        if (count <= BinCount)
        {
            return false;
        }

        var centroidBounds = ComputeCentroidBounds(items, start, count);
        var min = axis == 0 ? centroidBounds.Min.X : centroidBounds.Min.Y;
        var max = axis == 0 ? centroidBounds.Max.X : centroidBounds.Max.Y;
        var extent = max - min;
        if (extent <= float.Epsilon || float.IsNaN(extent) || float.IsInfinity(extent))
        {
            return false;
        }

        Span<int> counts = stackalloc int[BinCount];
        Span<RenderBounds> bounds = stackalloc RenderBounds[BinCount];
        for (var i = 0; i < BinCount; i++)
        {
            counts[i] = 0;
            bounds[i] = RenderBounds.Empty;
        }

        var end = start + count;
        var invExtent = 1f / extent;
        for (var i = start; i < end; i++)
        {
            var centroid = GetCentroid(items[i], axis);
            var normalized = (centroid - min) * invExtent;
            var bin = (int)(normalized * BinCount);
            if (bin < 0)
            {
                bin = 0;
            }
            else if (bin >= BinCount)
            {
                bin = BinCount - 1;
            }

            counts[bin]++;
            bounds[bin] = bounds[bin].Expand(items[i].Bounds);
        }

        Span<int> leftCounts = stackalloc int[BinCount];
        Span<RenderBounds> leftBounds = stackalloc RenderBounds[BinCount];
        var leftCount = 0;
        var leftBound = RenderBounds.Empty;
        for (var i = 0; i < BinCount; i++)
        {
            leftCount += counts[i];
            leftBound = leftBound.Expand(bounds[i]);
            leftCounts[i] = leftCount;
            leftBounds[i] = leftBound;
        }

        Span<int> rightCounts = stackalloc int[BinCount];
        Span<RenderBounds> rightBounds = stackalloc RenderBounds[BinCount];
        var rightCount = 0;
        var rightBound = RenderBounds.Empty;
        for (var i = BinCount - 1; i >= 0; i--)
        {
            rightCount += counts[i];
            rightBound = rightBound.Expand(bounds[i]);
            rightCounts[i] = rightCount;
            rightBounds[i] = rightBound;
        }

        var bestCost = float.PositiveInfinity;
        var bestSplit = -1;
        for (var i = 0; i < BinCount - 1; i++)
        {
            var leftItems = leftCounts[i];
            var rightItems = rightCounts[i + 1];
            if (leftItems == 0 || rightItems == 0)
            {
                continue;
            }

            var cost = leftBounds[i].Area * leftItems + rightBounds[i + 1].Area * rightItems;
            if (cost < bestCost)
            {
                bestCost = cost;
                bestSplit = i;
            }
        }

        if (bestSplit < 0)
        {
            return false;
        }

        var splitT = (bestSplit + 1f) / BinCount;
        splitValue = min + extent * splitT;
        return true;
    }

    private static float GetCentroid(RenderSpatialItem item, int axis)
    {
        return axis == 0 ? item.Centroid.X : item.Centroid.Y;
    }

    private static void Swap(RenderSpatialItem[] items, int left, int right)
    {
        if (left == right)
        {
            return;
        }

        (items[left], items[right]) = (items[right], items[left]);
    }

    private readonly struct Node
    {
        public RenderBounds Bounds { get; }
        public int Left { get; }
        public int Right { get; }
        public int Start { get; }
        public int Count { get; }

        private Node(RenderBounds bounds, int left, int right, int start, int count)
        {
            Bounds = bounds;
            Left = left;
            Right = right;
            Start = start;
            Count = count;
        }

        public bool IsLeaf => Left < 0;

        public static Node Leaf(RenderBounds bounds, int start, int count)
        {
            return new Node(bounds, -1, -1, start, count);
        }

        public static Node Internal(RenderBounds bounds, int left, int right)
        {
            return new Node(bounds, left, right, -1, 0);
        }
    }
}

/// <summary>
/// Configuration options for the spatial index builder.
/// </summary>
public sealed class RenderSpatialIndexOptions
{
    /// <summary>
    /// Gets the default spatial index options.
    /// </summary>
    public static readonly RenderSpatialIndexOptions Default = new(maxLeafSize: 8);

    /// <summary>
    /// Gets the maximum number of primitives stored per leaf node.
    /// </summary>
    public int MaxLeafSize { get; }

    public RenderSpatialIndexOptions(int maxLeafSize)
    {
        MaxLeafSize = Math.Max(2, maxLeafSize);
    }
}

/// <summary>
/// Describes a primitive entry stored in the spatial index.
/// </summary>
public readonly struct RenderSpatialItem
{
    /// <summary>
    /// Gets the layer that owns the primitive.
    /// </summary>
    public RenderLayer Layer { get; }

    /// <summary>
    /// Gets the referenced render primitive.
    /// </summary>
    public IRenderPrimitive Primitive { get; }

    /// <summary>
    /// Gets the bounds of the primitive.
    /// </summary>
    public RenderBounds Bounds { get; }

    /// <summary>
    /// Gets the centroid of the bounds.
    /// </summary>
    public Vector2 Centroid { get; }

    /// <summary>
    /// Gets the layer index in the source scene.
    /// </summary>
    public int LayerIndex { get; }

    /// <summary>
    /// Gets the primitive index within its layer.
    /// </summary>
    public int PrimitiveIndex { get; }

    public RenderSpatialItem(RenderLayer layer, IRenderPrimitive primitive, int layerIndex, int primitiveIndex)
    {
        Layer = layer;
        Primitive = primitive;
        Bounds = primitive.Bounds;
        Centroid = Bounds.Center;
        LayerIndex = layerIndex;
        PrimitiveIndex = primitiveIndex;
    }
}

/// <summary>
/// Represents a hit candidate returned from a spatial index query.
/// </summary>
public readonly struct RenderSpatialHit
{
    /// <summary>
    /// Gets the layer that owns the primitive.
    /// </summary>
    public RenderLayer Layer { get; }

    /// <summary>
    /// Gets the referenced render primitive.
    /// </summary>
    public IRenderPrimitive Primitive { get; }

    /// <summary>
    /// Gets the bounds of the primitive.
    /// </summary>
    public RenderBounds Bounds { get; }

    /// <summary>
    /// Gets the layer index in the source scene.
    /// </summary>
    public int LayerIndex { get; }

    /// <summary>
    /// Gets the primitive index within its layer.
    /// </summary>
    public int PrimitiveIndex { get; }

    public RenderSpatialHit(RenderLayer layer, IRenderPrimitive primitive, RenderBounds bounds, int layerIndex, int primitiveIndex)
    {
        Layer = layer;
        Primitive = primitive;
        Bounds = bounds;
        LayerIndex = layerIndex;
        PrimitiveIndex = primitiveIndex;
    }
}
