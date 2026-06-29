using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using ProCad.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadRenderOptionsToolViewModel : CadToolViewModelBase
{
    private readonly SerialDisposable _activeSubscriptions = new();
    private CadRenderViewModel? _activeRender;
    private bool _isSyncing;

    [Reactive]
    public partial bool HasActiveRender { get; set; }

    [Reactive]
    public partial bool ShowEmptyState { get; set; } = true;

    [Reactive]
    public partial string ActiveDocumentTitle { get; set; } = "No active document";

    [Reactive]
    public partial bool ShowGrid { get; set; } = true;

    [Reactive]
    public partial bool ShowAxes { get; set; } = true;

    [Reactive]
    public partial bool EnableDashPatternRendering { get; set; } = true;

    [Reactive]
    public partial bool EnableColorRendering { get; set; } = true;

    [Reactive]
    public partial bool EnableInteractionOptimization { get; set; }

    [Reactive]
    public partial bool FitOnLoad { get; set; } = true;

    public CadRenderOptionsToolViewModel(CadDocumentContextService documentContext)
    {
        documentContext.WhenAnyValue(x => x.ActiveDocument)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(SetActiveDocument);

        SetActiveDocument(documentContext.ActiveDocument);

        this.WhenAnyValue(x => x.ShowGrid)
            .Skip(1)
            .Subscribe(value => UpdateActiveRender(render => render.ShowGrid = value));

        this.WhenAnyValue(x => x.ShowAxes)
            .Skip(1)
            .Subscribe(value => UpdateActiveRender(render => render.ShowAxes = value));

        this.WhenAnyValue(x => x.EnableDashPatternRendering)
            .Skip(1)
            .Subscribe(value => UpdateActiveRender(render => render.EnableDashPatternRendering = value));

        this.WhenAnyValue(x => x.EnableColorRendering)
            .Skip(1)
            .Subscribe(value => UpdateActiveRender(render => render.EnableColorRendering = value));

        this.WhenAnyValue(x => x.EnableInteractionOptimization)
            .Skip(1)
            .Subscribe(value => UpdateActiveRender(render => render.EnableInteractionOptimization = value));

        this.WhenAnyValue(x => x.FitOnLoad)
            .Skip(1)
            .Subscribe(value => UpdateActiveRender(render => render.FitOnLoad = value));
    }

    private void SetActiveDocument(CadDocumentViewModel? document)
    {
        _activeSubscriptions.Disposable = Disposable.Empty;

        var render = document?.Render;
        _activeRender = render;
        HasActiveRender = render is not null;
        ShowEmptyState = render is null;
        ActiveDocumentTitle = ResolveDocumentTitle(document?.Title);

        if (render is null)
        {
            ResetOptions();
            return;
        }

        var subscriptions = new CompositeDisposable();

        if (document is not null)
        {
            subscriptions.Add(document.WhenAnyValue(x => x.Title)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(title => ActiveDocumentTitle = ResolveDocumentTitle(title)));
        }

        subscriptions.Add(render.WhenAnyValue(
                x => x.ShowGrid,
                x => x.ShowAxes,
                x => x.EnableDashPatternRendering,
                x => x.EnableColorRendering,
                x => x.EnableInteractionOptimization,
                x => x.FitOnLoad)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(_ => SyncFromRender(render)));

        _activeSubscriptions.Disposable = subscriptions;

        SyncFromRender(render);
    }

    private static string ResolveDocumentTitle(string? title)
    {
        return string.IsNullOrWhiteSpace(title) ? "No active document" : title;
    }

    private void SyncFromRender(CadRenderViewModel render)
    {
        _isSyncing = true;
        ShowGrid = render.ShowGrid;
        ShowAxes = render.ShowAxes;
        EnableDashPatternRendering = render.EnableDashPatternRendering;
        EnableColorRendering = render.EnableColorRendering;
        EnableInteractionOptimization = render.EnableInteractionOptimization;
        FitOnLoad = render.FitOnLoad;
        _isSyncing = false;
    }

    private void ResetOptions()
    {
        _isSyncing = true;
        ShowGrid = true;
        ShowAxes = true;
        EnableDashPatternRendering = true;
        EnableColorRendering = true;
        EnableInteractionOptimization = false;
        FitOnLoad = true;
        _isSyncing = false;
    }

    private void UpdateActiveRender(System.Action<CadRenderViewModel> update)
    {
        if (_isSyncing || _activeRender is null)
        {
            return;
        }

        update(_activeRender);
    }
}
