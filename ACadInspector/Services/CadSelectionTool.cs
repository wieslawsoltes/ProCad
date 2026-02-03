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

        context.AnnotationService.UpdateHover(hit.Value.entity, hit.Value.bounds);
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
        var target = hit?.entity;

        context.SelectionService.SelectedObject = target;
        context.AnnotationService.UpdateSelection(target, hit?.bounds);
    }

    private (Entity? entity, RenderBounds bounds)? ResolveHit(RenderScene scene, RenderSpatialIndex? index, CadRenderHitTestRequest request)
    {
        _hitResults.Clear();
        _hitTestEngine.HitTestPoint(scene, index, request.WorldPoint, request.Tolerance, _hitResults);

        for (var i = 0; i < _hitResults.Count; i++)
        {
            var hit = _hitResults[i];
            var entity = hit.OwnerEntity ?? hit.SourceEntity;
            if (entity is not null)
            {
                return (entity, hit.Bounds);
            }
        }

        return null;
    }
}
