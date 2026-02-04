using System;
using System.Collections.ObjectModel;
using ACadInspector.Core;
using ACadInspector.Diagnostics;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;

namespace ACadInspector.ViewModels;

public sealed class CadIoOptionsViewModel : CadToolViewModelBase, IFastPathDiagnosticsSource
{
    private readonly ObservableCollection<CadOptionRowViewModel> _readRows = new();
    private readonly ObservableCollection<CadOptionRowViewModel> _writeRows = new();

    public DataGridCollectionView ReadOptionsView { get; }
    public DataGridCollectionView WriteOptionsView { get; }

    public DataGridColumnDefinitionList ColumnDefinitions { get; }

    public SortingModel ReadSortingModel { get; } = new();
    public FilteringModel ReadFilteringModel { get; } = new();
    public SearchModel ReadSearchModel { get; } = new();

    public SortingModel WriteSortingModel { get; } = new();
    public FilteringModel WriteFilteringModel { get; } = new();
    public SearchModel WriteSearchModel { get; } = new();
    public FastPathDiagnosticsService FastPathDiagnostics { get; }

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
        ColumnDefinitions = CadIoOptionsColumnDefinitions.Create();
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
