using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ProCad.Rendering;
using ProCad.ViewModels;
using ACadSharp.Tables;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ProCad.Services;

public sealed class CadBlockPreviewService
{
    private readonly ICadRenderSceneBuilder _sceneBuilder;
    private readonly CadRenderSceneSettings _baseSettings;
    private readonly IBlockPreviewRenderer _previewRenderer;
    private readonly ConcurrentDictionary<BlockPreviewKey, Bitmap?> _cache;

    public CadBlockPreviewService(
        ICadRenderSceneBuilder sceneBuilder,
        CadRenderSceneSettings baseSettings,
        IBlockPreviewRenderer previewRenderer)
    {
        _sceneBuilder = sceneBuilder;
        _baseSettings = baseSettings;
        _previewRenderer = previewRenderer;
        _cache = new ConcurrentDictionary<BlockPreviewKey, Bitmap?>(new BlockPreviewKeyComparer());
    }

    public async Task<Bitmap?> GetPreviewAsync(
        CadDocumentViewModel documentViewModel,
        BlockRecord block,
        int size,
        bool renderAttributes,
        bool renderAttributeDefinitions,
        CancellationToken cancellationToken)
    {
        if (documentViewModel is null)
        {
            throw new ArgumentNullException(nameof(documentViewModel));
        }

        if (block is null)
        {
            throw new ArgumentNullException(nameof(block));
        }

        var key = new BlockPreviewKey(block, size, renderAttributes, renderAttributeDefinitions);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Bitmap? preview = null;
        try
        {
            var data = await Task.Run(
                () => BuildPreviewData(documentViewModel, block, size, renderAttributes, renderAttributeDefinitions),
                cancellationToken).ConfigureAwait(false);
            if (data is null || cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            preview = await Dispatcher.UIThread.InvokeAsync(
                () => new Bitmap(new System.IO.MemoryStream(data)),
                DispatcherPriority.Background,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }

        if (preview is not null && !cancellationToken.IsCancellationRequested)
        {
            _cache.TryAdd(key, preview);
        }

        return preview;
    }

    private byte[]? BuildPreviewData(
        CadDocumentViewModel documentViewModel,
        BlockRecord block,
        int size,
        bool renderAttributes,
        bool renderAttributeDefinitions)
    {
        var document = documentViewModel.Document;
        var selection = CadRenderSettingsBuilder.ResolveDefaultLayout(document);
        var baseSettings = _baseSettings.WithAttributeVisibility(renderAttributes, renderAttributeDefinitions);
        var settings = CadRenderSettingsBuilder.Build(document, documentViewModel.Path, baseSettings, selection);
        var scene = _sceneBuilder.BuildBlock(document, block, settings);
        if (scene.Bounds.IsEmpty)
        {
            return null;
        }

        var pixelSize = Math.Max(1, size);
        var viewTransform = BuildViewTransform(scene.Bounds, pixelSize, padding: 6f, out var baseScale);
        var snapshot = new CadRenderStateSnapshot(
            scene,
            showGrid: false,
            showAxes: false,
            enableInteractionOptimization: false,
            layerVisibilityOverrides: null,
            entityTypeVisibilityOverrides: null,
            zoom: 1.0,
            minPixelThickness: 0.6,
            baseScale: baseScale,
            viewTransform: viewTransform,
            showDebugOverlay: false,
            hoverBounds: null,
            selectionBounds: null,
            hoverAnnotation: null,
            selectionAnnotation: null,
            overlayScene: RenderOverlayScene.Empty,
            dynamicInput: null,
            debugBvhBounds: null);

        return _previewRenderer.Render(snapshot, pixelSize);
    }

    private static Matrix3x2 BuildViewTransform(RenderBounds bounds, int size, float padding, out double baseScale)
    {
        var sceneSize = bounds.Size;
        if (sceneSize.X <= 0 || sceneSize.Y <= 0)
        {
            baseScale = 1.0;
            return Matrix3x2.Identity;
        }

        var width = MathF.Max(1f, size - 2f * padding);
        var height = MathF.Max(1f, size - 2f * padding);
        var scaleX = width / sceneSize.X;
        var scaleY = height / sceneSize.Y;
        var scale = (float)Math.Max(0.00001, Math.Min(scaleX, scaleY));
        baseScale = scale;

        var sceneCenter = (bounds.Min + bounds.Max) * 0.5f;
        var center = new Vector2(size * 0.5f, size * 0.5f);

        return Matrix3x2.CreateTranslation(-sceneCenter)
            * Matrix3x2.CreateScale(scale, -scale)
            * Matrix3x2.CreateTranslation(center);
    }

    private readonly record struct BlockPreviewKey(
        BlockRecord Block,
        int Size,
        bool RenderAttributes,
        bool RenderAttributeDefinitions);

    private sealed class BlockPreviewKeyComparer : IEqualityComparer<BlockPreviewKey>
    {
        public bool Equals(BlockPreviewKey x, BlockPreviewKey y)
        {
            return ReferenceEquals(x.Block, y.Block) &&
                x.Size == y.Size &&
                x.RenderAttributes == y.RenderAttributes &&
                x.RenderAttributeDefinitions == y.RenderAttributeDefinitions;
        }

        public int GetHashCode(BlockPreviewKey obj)
        {
            return HashCode.Combine(
                RuntimeHelpers.GetHashCode(obj.Block),
                obj.Size,
                obj.RenderAttributes,
                obj.RenderAttributeDefinitions);
        }
    }
}
