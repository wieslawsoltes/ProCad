using System.Collections.Generic;
using ACadInspector.Rendering;
using ACadInspector.ViewModels;
using ACadSharp.Entities;

namespace ACadInspector.Services;

public sealed class CadSelectionTool : ICadTool
{
    private readonly RenderHitTestEngine _hitTestEngine = new();
    private readonly List<RenderHitTestResult> _hitResults = new();

    public string Id => "selection";
    public string DisplayName => "Selection";

    public void Activate(in CadToolContext context)
    {
        // No-op for now.
    }

    public void Deactivate(in CadToolContext context)
    {
        context.AnnotationService.ClearHover();
    }

    public void HandleInput(in CadToolInput input, in CadToolContext context)
    {
        switch (input.Kind)
        {
            case CadToolInputKind.Hover:
                HandleHover(input.HitTest, context);
                break;
            case CadToolInputKind.Select:
                HandleSelect(input.HitTest, context);
                break;
            case CadToolInputKind.ClearHover:
                context.AnnotationService.ClearHover();
                break;
        }
    }

    private void HandleHover(CadRenderHitTestRequest? request, in CadToolContext context)
    {
        if (request is null)
        {
            return;
        }

        var scene = context.Scene;
        if (scene is null)
        {
            context.AnnotationService.ClearHover();
            return;
        }

        var hit = ResolveHit(scene, context.SpatialIndex, request.Value);
        if (hit is null)
        {
            context.AnnotationService.ClearHover();
            return;
        }

        var entity = hit.Value.OwnerEntity ?? hit.Value.SourceEntity;
        context.AnnotationService.UpdateHover(entity, hit.Value.Bounds, hit.Value.Primitive);
    }

    private void HandleSelect(CadRenderHitTestRequest? request, in CadToolContext context)
    {
        if (request is null)
        {
            return;
        }

        var scene = context.Scene;
        if (scene is null)
        {
            context.SelectionService.SelectedObject = null;
            context.AnnotationService.UpdateSelection(null, null);
            return;
        }

        var hit = ResolveHit(scene, context.SpatialIndex, request.Value);
        var target = hit?.OwnerEntity ?? hit?.SourceEntity;

        context.SelectionService.SelectedObject = target;
        context.AnnotationService.UpdateSelection(target, hit?.Bounds, hit?.Primitive);
    }

    private RenderHitTestResult? ResolveHit(RenderScene scene, RenderSpatialIndex? index, CadRenderHitTestRequest request)
    {
        _hitResults.Clear();
        _hitTestEngine.HitTestPoint(scene, index, request.WorldPoint, request.Tolerance, _hitResults);

        for (var i = 0; i < _hitResults.Count; i++)
        {
            var hit = _hitResults[i];
            if (hit.OwnerEntity is not null || hit.SourceEntity is not null)
            {
                return hit;
            }
        }

        return null;
    }
}
