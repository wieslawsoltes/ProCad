using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ProCad.Core;
using ProCad.Diagnostics;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadIoOptionsViewModel : CadToolViewModelBase, IFastPathDiagnosticsSource
{
    private readonly ObservableCollection<CadOptionRowViewModel> _readRows = new();
    private readonly ObservableCollection<CadOptionRowViewModel> _writeRows = new();

    public DataGridCollectionView ReadOptionsView { get; }
    public DataGridCollectionView WriteOptionsView { get; }

    public DataGridColumnDefinitionList ReadColumnDefinitions { get; }
    public DataGridColumnDefinitionList WriteColumnDefinitions { get; }

    public SortingModel ReadSortingModel { get; } = new();
    public FilteringModel ReadFilteringModel { get; } = new();
    public SearchModel ReadSearchModel { get; } = new();

    public SortingModel WriteSortingModel { get; } = new();
    public FilteringModel WriteFilteringModel { get; } = new();
    public SearchModel WriteSearchModel { get; } = new();
    public FastPathDiagnosticsService FastPathDiagnostics { get; }

    [Reactive]
    public partial string ReadSearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string ReadFilterText { get; set; } = string.Empty;

    [Reactive]
    public partial string WriteSearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string WriteFilterText { get; set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> ClearReadSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearReadFilterCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearWriteSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearWriteFilterCommand { get; }

    private readonly CadOptionRowViewModel _readSummaryInfo;
    private readonly CadOptionRowViewModel _clearDxfCache;
    private readonly CadOptionRowViewModel _createDxfDefaults;
    private readonly CadOptionRowViewModel _dwgCrcCheck;

    private readonly CadOptionRowViewModel _writeBinaryDxf;
    private readonly CadOptionRowViewModel _writeAllHeaderVariables;

    public CadIoOptionsViewModel(FastPathDiagnosticsService fastPathDiagnostics)
    {
        FastPathDiagnostics = fastPathDiagnostics;
        _readSummaryInfo = new CadOptionRowViewModel(
            "ReadSummaryInfo",
            "Read DWG summary information when available.",
            true);
        _clearDxfCache = new CadOptionRowViewModel(
            "ClearDxfCache",
            "Clear DXF cache before reading.",
            true);
        _createDxfDefaults = new CadOptionRowViewModel(
            "CreateDxfDefaults",
            "Create default DXF header values if missing.",
            false);
        _dwgCrcCheck = new CadOptionRowViewModel(
            "DwgCrcCheck",
            "Enable DWG CRC checks during read.",
            false);

        _writeBinaryDxf = new CadOptionRowViewModel(
            "WriteBinaryDxf",
            "Write DXF in binary format.",
            false);
        _writeAllHeaderVariables = new CadOptionRowViewModel(
            "WriteAllDxfHeaderVariables",
            "Write all DXF header variables.",
            false);

        _readRows.Add(_readSummaryInfo);
        _readRows.Add(_clearDxfCache);
        _readRows.Add(_createDxfDefaults);
        _readRows.Add(_dwgCrcCheck);

        _writeRows.Add(_writeBinaryDxf);
        _writeRows.Add(_writeAllHeaderVariables);

        ReadOptionsView = new DataGridCollectionView(_readRows);
        WriteOptionsView = new DataGridCollectionView(_writeRows);
        ReadColumnDefinitions = CadIoOptionsColumnDefinitions.Create();
        WriteColumnDefinitions = CadIoOptionsColumnDefinitions.Create();

        ReadSearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        ReadSearchModel.HighlightCurrent = true;
        ReadSearchModel.WrapNavigation = true;

        WriteSearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        WriteSearchModel.HighlightCurrent = true;
        WriteSearchModel.WrapNavigation = true;

        this.WhenAnyValue(x => x.ReadSearchText)
            .Subscribe(_ => ApplyReadSearch());
        this.WhenAnyValue(x => x.ReadFilterText)
            .Subscribe(_ => ApplyReadFilter());
        this.WhenAnyValue(x => x.WriteSearchText)
            .Subscribe(_ => ApplyWriteSearch());
        this.WhenAnyValue(x => x.WriteFilterText)
            .Subscribe(_ => ApplyWriteFilter());

        ClearReadSearchCommand = ReactiveCommand.Create(() => { ReadSearchText = string.Empty; });
        ClearReadFilterCommand = ReactiveCommand.Create(() => { ReadFilterText = string.Empty; });
        ClearWriteSearchCommand = ReactiveCommand.Create(() => { WriteSearchText = string.Empty; });
        ClearWriteFilterCommand = ReactiveCommand.Create(() => { WriteFilterText = string.Empty; });
    }

    private void ApplyReadSearch()
    {
        DataGridFilterHelper.ApplySearch(ReadSearchModel, ReadSearchText);
    }

    private void ApplyReadFilter()
    {
        DataGridFilterHelper.ApplyFilter(ReadFilteringModel, ReadColumnDefinitions, ReadFilterText);
    }

    private void ApplyWriteSearch()
    {
        DataGridFilterHelper.ApplySearch(WriteSearchModel, WriteSearchText);
    }

    private void ApplyWriteFilter()
    {
        DataGridFilterHelper.ApplyFilter(WriteFilteringModel, WriteColumnDefinitions, WriteFilterText);
    }

    public CadReadOptions BuildReadOptions(CadFileFormat format)
    {
        return new CadReadOptions(
            Format: format,
            ReadSummaryInfo: _readSummaryInfo.Value,
            ClearDxfCache: _clearDxfCache.Value,
            CreateDxfDefaults: _createDxfDefaults.Value,
            DwgCrcCheck: _dwgCrcCheck.Value);
    }

    public CadWriteOptions BuildWriteOptions(CadFileFormat format)
    {
        return new CadWriteOptions(
            Format: format,
            WriteBinaryDxf: _writeBinaryDxf.Value,
            WriteAllDxfHeaderVariables: _writeAllHeaderVariables.Value);
    }
}
