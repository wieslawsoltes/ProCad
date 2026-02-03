using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Objects.Evaluations;
using ACadSharp.Tables;
using CSMath;

namespace ACadInspector.Rendering;

public sealed class InsertRenderHandler : IRenderEntityHandler
{
    private readonly IRenderXRefResolver _xrefResolver;

    public InsertRenderHandler(IRenderXRefResolver xrefResolver)
    {
        _xrefResolver = xrefResolver ?? throw new ArgumentNullException(nameof(xrefResolver));
    }

    public bool CanHandle(Entity entity) => entity is Insert;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var insert = (Insert)entity;
        if (insert.Block is null)
        {
            return;
        }

        var insertTransform = BuildInsertTransform(insert);
        insertTransform = ApplyBlockBasePoint(insert, insertTransform);
        var baseTransform = RenderTransformUtils.Combine(transform, insertTransform);

        var rowCount = insert.RowCount < 1 ? 1 : insert.RowCount;
        var columnCount = insert.ColumnCount < 1 ? 1 : insert.ColumnCount;

        if (rowCount == 1 && columnCount == 1)
        {
            AppendBlockInstance(insert, baseTransform, context);
            return;
        }

        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                var offset = new XYZ(column * insert.ColumnSpacing, row * insert.RowSpacing, 0);
                var offsetTransform = new Transform(Matrix4.CreateTranslation(offset));
                var combined = RenderTransformUtils.Combine(baseTransform, offsetTransform);
                AppendBlockInstance(insert, combined, context);
            }
        }
    }

    private static Transform ApplyBlockBasePoint(Insert insert, Transform transform)
    {
        var basePoint = insert.Block?.BlockEntity?.BasePoint ?? XYZ.Zero;
        if (basePoint.Equals(XYZ.Zero))
        {
            return transform;
        }

        var offset = new XYZ(-basePoint.X, -basePoint.Y, -basePoint.Z);
        var baseTransform = new Transform(Matrix4.CreateTranslation(offset));
        return RenderTransformUtils.Combine(transform, baseTransform);
    }

    private static Transform BuildInsertTransform(Insert insert)
    {
        var normal = insert.Normal.IsZero() ? XYZ.AxisZ : insert.Normal;
        var world = Matrix4.GetArbitraryAxis(normal);
        var translation = Matrix4.CreateTranslation(insert.InsertPoint);
        var rotation = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, insert.Rotation);
        var scale = Matrix4.CreateScale(new XYZ(insert.XScale, insert.YScale, insert.ZScale));

        return new Transform(world * translation * rotation * scale);
    }

    private void AppendBlockInstance(Insert insert, Transform transform, RenderBuildContext context)
    {
        if (insert.Block is null)
        {
            return;
        }

        using var ownerScope = context.SelectionContext.EnterOwnerOverride(insert);
        var clipLoops = BuildClipLoops(insert, transform);
        var hasClip = clipLoops.Count > 0;
        var useXref = TryResolveXRef(insert.Block, context.Settings, out var xrefInfo);

        if (!hasClip && !useXref)
        {
            AppendBlockEntities(insert, transform, context);
            AppendAttributes(insert, transform, context);
            return;
        }

        var targetDocument = useXref ? xrefInfo.Document : context.Document;
        var settings = useXref ? WithSupportPaths(context.Settings, xrefInfo.Path) : context.Settings;
        var subContext = CreateSubContext(targetDocument, settings, context, context.SelectionContext);

        if (useXref)
        {
            var xrefTransform = BuildXRefTransform(transform, xrefInfo.Document);
            AppendXRefEntities(xrefInfo.Document, xrefTransform, subContext);
        }
        else
        {
            AppendBlockEntities(insert, transform, subContext);
        }

        AppendAttributes(insert, transform, subContext);
        MergeLayerPrimitives(subContext, context, clipLoops);

        if (hasClip)
        {
            AppendClipFrame(insert, clipLoops, context);
        }
    }

    private static RenderBuildContext CreateSubContext(
        CadDocument document,
        CadRenderSceneSettings settings,
        RenderBuildContext context,
        RenderSelectionContext selectionContext)
    {
        return new RenderBuildContext(
            document,
            settings,
            context.StyleResolver,
            context.LinePatternResolver,
            context.ShapeResolver,
            context.TextShaper,
            context.VisibilityResolver,
            context.GeometrySampler,
            context.EntityOrderResolver,
            context.Dispatcher,
            context.Diagnostics,
            context.Stats,
            context.ViewportOverrides,
            selectionContext);
    }

    private static void MergeLayerPrimitives(
        RenderBuildContext source,
        RenderBuildContext target,
        IReadOnlyList<IReadOnlyList<Vector2>> clipLoops)
    {
        var hasClip = clipLoops.Count > 0;
        foreach (var entry in source.Layers.Entries)
        {
            var builder = entry.Value;
            if (builder.Primitives.Count == 0)
            {
                continue;
            }

            var targetBuilder = target.Layers.GetLayerBuilder(entry.Key);
            if (hasClip)
            {
                targetBuilder.Add(new RenderClipGroup(clipLoops, builder.Primitives, RenderLoopFillMode.NonZero));
                foreach (var kvp in builder.Metadata)
                {
                    targetBuilder.AddMetadata(kvp.Key, kvp.Value);
                }
                continue;
            }

            foreach (var primitive in builder.Primitives)
            {
                if (builder.TryGetMetadata(primitive, out var metadata))
                {
                    targetBuilder.Add(primitive, metadata);
                }
                else
                {
                    targetBuilder.Add(primitive);
                }
            }
        }
    }

    private static void AppendClipFrame(
        Insert insert,
        IReadOnlyList<IReadOnlyList<Vector2>> clipLoops,
        RenderBuildContext context)
    {
        if (!context.Settings.XClipFrameVisibility.ShouldDisplay())
        {
            return;
        }

        if (clipLoops.Count == 0)
        {
            return;
        }

        var builder = context.GetLayerBuilder(insert);
        var color = context.ResolveEntityColor(insert);
        var thickness = context.ResolveLineWeight(insert);
        var lineCap = context.ResolveLineCap(insert);
        var lineJoin = context.ResolveLineJoin(insert);

        foreach (var loop in clipLoops)
        {
            if (loop is null || loop.Count < 2)
            {
                continue;
            }

            builder.Add(new RenderPolyline(
                loop,
                isClosed: true,
                color,
                thickness,
                lineCap,
                lineJoin));
        }
    }

    private bool TryResolveXRef(BlockRecord block, CadRenderSceneSettings settings, out RenderXRefInfo info)
    {
        info = default;
        if (block.Entities.Count > 0)
        {
            return false;
        }

        return _xrefResolver.TryResolve(block, settings, out info);
    }

    private static void AppendXRefEntities(CadDocument document, Transform transform, RenderBuildContext context)
    {
        if (document is null)
        {
            return;
        }

        AppendEntities(document.Entities, document.ModelSpace, transform, context);
    }

    private static void AppendEntities(
        IEnumerable<Entity> entities,
        BlockRecord? block,
        Transform transform,
        RenderBuildContext context)
    {
        var ordered = context.EntityOrderResolver.OrderEntities(entities, block);
        foreach (var child in ordered)
        {
            if (child is null)
            {
                continue;
            }

            if (!context.VisibilityResolver.ShouldRender(child, context.Settings))
            {
                continue;
            }

            context.Dispatcher.Append(child, transform, context);
        }
    }

    private static Transform BuildXRefTransform(Transform transform, CadDocument document)
    {
        var basePoint = document.Header?.ModelSpaceInsertionBase ?? XYZ.Zero;
        if (basePoint.Equals(XYZ.Zero))
        {
            return transform;
        }

        var offset = new XYZ(-basePoint.X, -basePoint.Y, -basePoint.Z);
        var offsetTransform = new Transform(Matrix4.CreateTranslation(offset));
        return RenderTransformUtils.Combine(transform, offsetTransform);
    }

    private static CadRenderSceneSettings WithSupportPaths(CadRenderSceneSettings settings, string? resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return settings;
        }

        var directory = Path.GetDirectoryName(resolvedPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return settings;
        }

        var supportPaths = MergeSupportPaths(settings.SupportPaths, directory);
        return new CadRenderSceneSettings
        {
            SupportPaths = supportPaths,
            Quality = settings.Quality,
            VisualStyle = settings.VisualStyle,
            Lighting = settings.Lighting,
            EnableHatchFills = settings.EnableHatchFills,
            EnableHatchPatterns = settings.EnableHatchPatterns,
            EnableHatchGradients = settings.EnableHatchGradients,
            HiddenLineSettings = settings.HiddenLineSettings,
            ShadeEdge = settings.ShadeEdge,
            ShadeDiffuseToAmbientPercentage = settings.ShadeDiffuseToAmbientPercentage,
            Background = settings.Background,
            FallbackColor = settings.FallbackColor,
            MillimetersPerUnit = settings.MillimetersPerUnit,
            DefaultLineWeightMm = settings.DefaultLineWeightMm,
            MinLineWeightMm = settings.MinLineWeightMm,
            DisplayLineWeight = settings.DisplayLineWeight,
            LineTypeDotLengthMm = settings.LineTypeDotLengthMm,
            PolylineArcPrecision = settings.PolylineArcPrecision,
            SplinePrecision = settings.SplinePrecision,
            CirclePrecision = settings.CirclePrecision,
            TextWidthFactor = settings.TextWidthFactor,
            PointDisplayMode = settings.PointDisplayMode,
            PointDisplaySize = settings.PointDisplaySize,
            QuickTextMode = settings.QuickTextMode,
            FillMode = settings.FillMode,
            PolylineLineTypeGeneration = settings.PolylineLineTypeGeneration,
            MirrorText = settings.MirrorText,
            XClipFrameVisibility = settings.XClipFrameVisibility,
            WipeoutFrameVisibility = settings.WipeoutFrameVisibility,
            UnderlayFrameVisibility = settings.UnderlayFrameVisibility,
            IsPaperSpace = settings.IsPaperSpace,
            PaperSpaceLineTypeScalingOverride = settings.PaperSpaceLineTypeScalingOverride,
            ViewportScale = settings.ViewportScale,
            ModelSpaceLineTypeScaling = settings.ModelSpaceLineTypeScaling,
            AnnotationScaleFactor = settings.AnnotationScaleFactor,
            IncludeInvisible = settings.IncludeInvisible,
            IncludeOffLayers = settings.IncludeOffLayers,
            IncludeUnsupportedAsPoints = settings.IncludeUnsupportedAsPoints,
            PerformanceBudget = settings.PerformanceBudget
        };
    }

    private static IReadOnlyList<string> MergeSupportPaths(
        IReadOnlyList<string> supportPaths,
        string extraPath)
    {
        var list = new List<string>(supportPaths.Count + 1);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in supportPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (seen.Add(path))
            {
                list.Add(path);
            }
        }

        if (seen.Add(extraPath))
        {
            list.Add(extraPath);
        }

        return list;
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>> BuildClipLoops(Insert insert, Transform transform)
    {
        var filter = insert.SpatialFilter;
        if (filter is null)
        {
            return Array.Empty<IReadOnlyList<Vector2>>();
        }

        var boundary = filter.BoundaryPoints;
        if (boundary is null || boundary.Count < 2)
        {
            return Array.Empty<IReadOnlyList<Vector2>>();
        }

        List<XYZ> points;
        if (boundary.Count == 2)
        {
            var minX = Math.Min(boundary[0].X, boundary[1].X);
            var minY = Math.Min(boundary[0].Y, boundary[1].Y);
            var maxX = Math.Max(boundary[0].X, boundary[1].X);
            var maxY = Math.Max(boundary[0].Y, boundary[1].Y);
            points = new List<XYZ>(4)
            {
                new XYZ(minX, minY, 0),
                new XYZ(maxX, minY, 0),
                new XYZ(maxX, maxY, 0),
                new XYZ(minX, maxY, 0)
            };
        }
        else
        {
            points = new List<XYZ>(boundary.Count);
            foreach (var point in boundary)
            {
                points.Add(new XYZ(point.X, point.Y, 0));
            }
        }

        var origin = filter.Origin;
        if (!origin.Equals(XYZ.Zero))
        {
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                points[i] = new XYZ(point.X + origin.X, point.Y + origin.Y, point.Z + origin.Z);
            }
        }

        var filterTransform = ResolveSpatialFilterTransform(filter);
        var combined = RenderTransformUtils.Combine(transform, filterTransform);

        var loop = new List<Vector2>(points.Count);
        foreach (var point in points)
        {
            var world = combined.ApplyTransform(point);
            loop.Add(RenderTransformUtils.ToVector2(world));
        }

        return new[] { loop };
    }

    private static Transform ResolveSpatialFilterTransform(SpatialFilter filter)
    {
        if (Matrix4.Inverse(filter.InsertTransform, out var inverse))
        {
            return new Transform(inverse);
        }

        return new Transform(Matrix4.Identity);
    }

    private static void AppendBlockEntities(Insert insert, Transform transform, RenderBuildContext context)
    {
        var dynamicInfo = DynamicBlockRepresentationResolver.Resolve(insert);
        var block = ResolveRenderBlock(insert, dynamicInfo, context);
        if (block is null)
        {
            return;
        }

        var visibilityFilter = TryCreateVisibilityFilter(insert, block, dynamicInfo?.Properties);
        var actionMap = TryCreateActionMap(block, dynamicInfo);
        var ordered = context.EntityOrderResolver.OrderEntities(block.Entities, block);

        foreach (var child in ordered)
        {
            if (child is AttributeDefinition or AttributeEntity)
            {
                continue;
            }

            if (visibilityFilter is not null && !visibilityFilter.IsVisible(child))
            {
                continue;
            }

            if (!context.VisibilityResolver.ShouldRender(child, context.Settings))
            {
                continue;
            }

            var childTransform = transform;
            if (actionMap is not null && actionMap.TryGetTransform(child, out var actionTransform))
            {
                childTransform = RenderTransformUtils.Combine(transform, actionTransform);
            }

            context.Dispatcher.Append(child, childTransform, context);
        }
    }

    private static void AppendAttributes(Insert insert, Transform transform, RenderBuildContext context)
    {
        var attributeMap = BuildAttributeMap(insert);
        var identity = new Transform(Matrix4.Identity);

        foreach (var attribute in insert.Attributes)
        {
            if (attribute.Flags.HasFlag(AttributeFlags.Hidden))
            {
                continue;
            }

            if (!context.VisibilityResolver.ShouldRender(attribute, context.Settings))
            {
                continue;
            }

            var attributeTransform = ResolveAttributeTransform(attribute, insert, transform, identity);
            AppendAttributeEntity(attribute, attributeTransform, context);
        }

        foreach (var definition in insert.Block.AttributeDefinitions)
        {
            if (!definition.Flags.HasFlag(AttributeFlags.Constant))
            {
                continue;
            }

            if (definition.Flags.HasFlag(AttributeFlags.Hidden))
            {
                continue;
            }

            if (!context.VisibilityResolver.ShouldRender(definition, context.Settings))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(definition.Tag) && attributeMap.ContainsKey(definition.Tag))
            {
                continue;
            }

            AppendAttributeDefinition(definition, transform, context);
        }
    }

    private static Dictionary<string, AttributeEntity> BuildAttributeMap(Insert insert)
    {
        var map = new Dictionary<string, AttributeEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in insert.Attributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Tag))
            {
                continue;
            }

            map[attribute.Tag] = attribute;
        }

        return map;
    }

    private static void AppendAttributeEntity(
        AttributeEntity attribute,
        Transform transform,
        RenderBuildContext context)
    {
        if (attribute.AttributeType != AttributeType.SingleLine && attribute.MText is not null)
        {
            context.Dispatcher.Append(attribute.MText, transform, context);
            return;
        }

        context.Dispatcher.Append(attribute, transform, context);
    }

    private static void AppendAttributeDefinition(
        AttributeDefinition definition,
        Transform transform,
        RenderBuildContext context)
    {
        if (definition.AttributeType != AttributeType.SingleLine && definition.MText is not null)
        {
            context.Dispatcher.Append(definition.MText, transform, context);
            return;
        }

        context.Dispatcher.Append(definition, transform, context);
    }

    private static Transform ResolveAttributeTransform(
        AttributeEntity attribute,
        Insert insert,
        Transform insertTransform,
        Transform identity)
    {
        if (string.IsNullOrWhiteSpace(attribute.Tag))
        {
            return identity;
        }

        var definition = insert.Block?.AttributeDefinitions
            .FirstOrDefault(def => string.Equals(def.Tag, attribute.Tag, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            return identity;
        }

        var defPoint = definition.InsertPoint;
        var worldPoint = insertTransform.ApplyTransform(defPoint);
        var distToDef = (attribute.InsertPoint - defPoint).GetLength();
        var distToWorld = (attribute.InsertPoint - worldPoint).GetLength();

        return distToWorld <= distToDef ? identity : insertTransform;
    }

    private static DynamicBlockVisibilityFilter? TryCreateVisibilityFilter(
        Insert insert,
        BlockRecord block,
        DynamicBlockPropertySet? properties)
    {
        if (block.IsAnonymous && block.Source is not null)
        {
            return null;
        }

        var visibilityParameter = ResolveVisibilityParameter(block);
        if (visibilityParameter is null)
        {
            return null;
        }

        var stateName = DynamicBlockVisibilityResolver.ResolveStateName(properties, visibilityParameter.States)
            ?? DynamicBlockVisibilityResolver.ResolveStateName(insert.XDictionary, visibilityParameter.States);
        if (string.IsNullOrWhiteSpace(stateName))
        {
            return null;
        }

        if (!visibilityParameter.States.TryGetValue(stateName, out var state))
        {
            return null;
        }

        return new DynamicBlockVisibilityFilter(visibilityParameter, state);
    }

    private static DynamicBlockActionMap? TryCreateActionMap(
        BlockRecord block,
        DynamicBlockRepresentationInfo? dynamicInfo)
    {
        if (block.IsAnonymous && block.Source is not null)
        {
            return null;
        }

        return DynamicBlockActionResolver.TryCreate(block, dynamicInfo?.Properties);
    }

    private static BlockRecord? ResolveRenderBlock(
        Insert insert,
        DynamicBlockRepresentationInfo? dynamicInfo,
        RenderBuildContext context)
    {
        var block = insert.Block;
        if (block is null)
        {
            return null;
        }

        var representationBlock = dynamicInfo?.RepresentationData?.Block;
        if (representationBlock is not null && representationBlock.IsAnonymous)
        {
            if (block.Source is not null && !ReferenceEquals(block.Source, representationBlock))
            {
                context.Diagnostics.TrackDynamicBlockMappingMismatch(insert, block.Source, representationBlock);
            }

            return representationBlock;
        }

        if (block.IsAnonymous && block.Source is not null)
        {
            if (representationBlock is not null && !ReferenceEquals(block, representationBlock))
            {
                context.Diagnostics.TrackDynamicBlockMappingMismatch(insert, block.Source, representationBlock);
            }

            return block;
        }

        return block;
    }

    private static BlockVisibilityParameter? ResolveVisibilityParameter(BlockRecord block)
    {
        var graph = block.EvaluationGraph;
        if (graph is null)
        {
            return null;
        }

        foreach (var node in graph.Nodes)
        {
            if (node?.Expression is BlockVisibilityParameter parameter)
            {
                return parameter;
            }
        }

        return null;
    }
}
