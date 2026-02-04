using System;
using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ACadInspector.Rendering;
using ACadSharp;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadBlockEditorViewModel : CadDocumentViewModelBase
{
    public CadDocument Document { get; }
    public BlockRecord Block { get; }
    public CadRenderViewModel Render { get; }
    public IReadOnlyList<UnitsType> UnitOptions { get; }

    private readonly ICadRenderSceneBuilder _sceneBuilder;
    private readonly CadRenderSceneSettings _baseSettings;
    private readonly CadRenderLayoutSelection _layoutSelection;
    private readonly string? _documentPath;
    private CancellationTokenSource? _rebuildCts;
    private bool _suppressUpdates;

    [Reactive]
    public partial string BlockName { get; set; }

    [Reactive]
    public partial string BlockDescription { get; set; }

    [Reactive]
    public partial UnitsType BlockUnits { get; set; }

    [Reactive]
    public partial bool ShowAttributes { get; set; } = true;

    [Reactive]
    public partial bool ShowAttributeDefinitions { get; set; } = true;

    public CadBlockEditorViewModel(
        CadDocument document,
        BlockRecord block,
        CadRenderViewModel render,
        ICadRenderSceneBuilder sceneBuilder,
        CadRenderSceneSettings baseSettings,
        CadRenderLayoutSelection layoutSelection,
        string? documentPath)
    {
        Document = document;
        Block = block;
        Render = render;
        _sceneBuilder = sceneBuilder;
        _baseSettings = baseSettings;
        _layoutSelection = layoutSelection;
        _documentPath = documentPath;
        UnitOptions = (UnitsType[])Enum.GetValues(typeof(UnitsType));
        BlockName = block.Name;
        BlockDescription = block.BlockEntity?.Comments ?? string.Empty;
        BlockUnits = block.Units;
        Title = $"Block: {block.Name}";

        this.WhenAnyValue(x => x.BlockName)
            .Skip(1)
            .Subscribe(UpdateBlockName);

        this.WhenAnyValue(x => x.BlockDescription)
            .Skip(1)
            .Subscribe(UpdateBlockDescription);

        this.WhenAnyValue(x => x.BlockUnits)
            .Skip(1)
            .Subscribe(UpdateBlockUnits);

        this.WhenAnyValue(x => x.ShowAttributes, x => x.ShowAttributeDefinitions)
            .Skip(1)
            .Subscribe(_ => QueueRebuild());
    }

    private void QueueRebuild()
    {
        _ = RebuildSceneAsync();
    }

    private void UpdateBlockName(string? value)
    {
        if (_suppressUpdates)
        {
            return;
        }

        var next = value?.Trim();
        if (string.IsNullOrWhiteSpace(next))
        {
            _suppressUpdates = true;
            BlockName = Block.Name;
            _suppressUpdates = false;
            return;
        }

        if (!string.Equals(Block.Name, next, StringComparison.Ordinal))
        {
            Block.Name = next;
            Title = $"Block: {next}";
        }
    }

    private void UpdateBlockDescription(string? value)
    {
        if (_suppressUpdates)
        {
            return;
        }

        var next = value?.Trim() ?? string.Empty;
        if (Block.BlockEntity is null)
        {
            return;
        }

        if (!string.Equals(Block.BlockEntity.Comments, next, StringComparison.Ordinal))
        {
            Block.BlockEntity.Comments = next;
        }
    }

    private void UpdateBlockUnits(UnitsType units)
    {
        if (_suppressUpdates)
        {
            return;
        }

        if (Block.Units != units)
        {
            Block.Units = units;
        }
    }

    private async Task RebuildSceneAsync()
    {
        _rebuildCts?.Cancel();
        var cts = new CancellationTokenSource();
        _rebuildCts = cts;

        var baseSettings = _baseSettings.WithAttributeVisibility(ShowAttributes, ShowAttributeDefinitions);
        var settings = CadRenderSettingsBuilder.Build(Document, _documentPath, baseSettings, _layoutSelection);

        RenderScene? scene = null;
        try
        {
            scene = await Task.Run(() => _sceneBuilder.BuildBlock(Document, Block, settings), cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        RxApp.MainThreadScheduler.Schedule(() => Render.Scene = scene);
    }
}
