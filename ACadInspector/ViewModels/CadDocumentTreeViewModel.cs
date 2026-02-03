using System;
using System.Collections.Generic;
using System.Reactive;
using System.Linq;
using System.Reactive.Linq;
using ACadInspector.Diagnostics;
using ACadInspector.Services;
using Avalonia.Controls;
using Avalonia.Controls.DataGridFiltering;
using Avalonia.Controls.DataGridHierarchical;
using Avalonia.Controls.DataGridSearching;
using Avalonia.Controls.DataGridSorting;
using Dock.Model.ReactiveUI.Controls;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace ACadInspector.ViewModels;

public sealed partial class CadDocumentTreeViewModel : Tool, IFastPathDiagnosticsSource
{
    private const string FilterColumnId = "Name";
    private const string FilterPropertyPath = "Item.Name";
    private readonly CadSelectionService _selectionService;
    private readonly CadDocumentContextService _documentContext;
    private readonly CadSelectionFocusService _focusService;
    private readonly Dictionary<object, CadDocumentTreeNode> _nodeMap = new();
    private readonly Dictionary<string, CadDocumentTreeNode> _handleMap = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CadDocumentTreeNode> _roots = Array.Empty<CadDocumentTreeNode>();
    private bool _suppressSelection;
    private bool _isSwitchingDocument;
    private CadDocumentViewModel? _currentDocument;

    public HierarchicalModel<CadDocumentTreeNode> TreeModel { get; }
    public DataGridColumnDefinitionList ColumnDefinitions { get; }
    public FastPathDiagnosticsService FastPathDiagnostics { get; }

    public SortingModel SortingModel { get; } = new();
    public FilteringModel FilteringModel { get; } = new();
    public SearchModel SearchModel { get; } = new();

    [Reactive]
    public partial object? SelectedItem { get; set; }

    [Reactive]
    public partial string SearchText { get; set; } = string.Empty;

    [Reactive]
    public partial string FilterText { get; set; } = string.Empty;

    [Reactive]
    public partial string SearchSummary { get; set; } = "No results";

    public ReactiveCommand<Unit, Unit> NextSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearSearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearFilterCommand { get; }

    public CadDocumentTreeViewModel(
        CadSelectionService selectionService,
        CadDocumentContextService documentContext,
        CadSelectionFocusService focusService,
        FastPathDiagnosticsService fastPathDiagnostics)
    {
        _selectionService = selectionService;
        _documentContext = documentContext;
        _focusService = focusService;
        FastPathDiagnostics = fastPathDiagnostics;
        var options = new HierarchicalOptions<CadDocumentTreeNode>
        {
            ChildrenSelector = static node => node.Children,
            IsLeafSelector = static node => node.Children.Count == 0,
            AutoExpandRoot = true,
            MaxAutoExpandDepth = 1
        };

        TreeModel = new HierarchicalModel<CadDocumentTreeNode>(options);
        ColumnDefinitions = CadDocumentTreeColumnDefinitions.Create();

        this.WhenAnyValue(x => x.SearchText)
            .Subscribe(_ => ApplySearch());

        this.WhenAnyValue(x => x.FilterText)
            .Subscribe(_ => ApplyFilter());

        this.WhenAnyValue(x => x.SelectedItem)
            .Subscribe(OnSelectedItemChanged);

        _selectionService.WhenAnyValue(x => x.SelectedObject)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnSelectedObjectChanged);

        SearchModel.ResultsChanged += (_, _) => UpdateSearchSummary();
        SearchModel.CurrentChanged += (_, _) => UpdateSearchSummary();
        SearchModel.HighlightMode = SearchHighlightMode.TextAndCell;
        SearchModel.HighlightCurrent = true;
        SearchModel.WrapNavigation = true;

        var canNavigate = Observable
            .FromEventPattern<SearchResultsChangedEventArgs>(
                handler => SearchModel.ResultsChanged += handler,
                handler => SearchModel.ResultsChanged -= handler)
            .Select(_ => SearchModel.Results.Count > 0)
            .StartWith(SearchModel.Results.Count > 0)
            .DistinctUntilChanged();
        NextSearchCommand = ReactiveCommand.Create(() => { SearchModel.MoveNext(); }, canNavigate);
        PreviousSearchCommand = ReactiveCommand.Create(() => { SearchModel.MovePrevious(); }, canNavigate);
        ClearSearchCommand = ReactiveCommand.Create(() => { SearchText = string.Empty; });
        ClearFilterCommand = ReactiveCommand.Create(() => { FilterText = string.Empty; });
    }

    public void LoadDocument(CadDocumentViewModel? document)
    {
        if (document?.Document is null)
        {
            TreeModel.SetRoots(Array.Empty<CadDocumentTreeNode>());
            _nodeMap.Clear();
            _handleMap.Clear();
            _roots = Array.Empty<CadDocumentTreeNode>();
            _currentDocument = null;
            ApplyFilter();
            return;
        }

        _documentContext.Register(document);
        IReadOnlyList<CadDocumentTreeNode> roots = CadDocumentTreeBuilder.Build(document.Document, document.Title);
        TreeModel.SetRoots(roots);
        RebuildNodeMap(roots);
        _roots = roots;
        _currentDocument = document;
        ApplyFilter();
        SelectFromService();
    }

    private void OnSelectedItemChanged(object? item)
    {
        if (_suppressSelection)
        {
            return;
        }

        var node = ResolveNode(item);
        _documentContext.TrySetActiveFromSelection(node?.Source);
        _selectionService.SelectedObject = node?.Source;
        if (node?.Source is ACadSharp.Entities.Entity)
        {
            _focusService.RequestFocus(node.Source);
        }
    }

    private void OnSelectedObjectChanged(object? selected)
    {
        if (_suppressSelection)
        {
            return;
        }

        if (_isSwitchingDocument)
        {
            return;
        }

        var document = _documentContext.ResolveViewModel(selected);
        if (document is not null && !ReferenceEquals(document, _currentDocument))
        {
            _isSwitchingDocument = true;
            LoadDocument(document);
            _isSwitchingDocument = false;
        }

        _suppressSelection = true;
        SelectedItem = ResolveItemForSelection(selected);
        _suppressSelection = false;
    }

    private void SelectFromService()
    {
        OnSelectedObjectChanged(_selectionService.SelectedObject);
    }

    private void RebuildNodeMap(IReadOnlyList<CadDocumentTreeNode> roots)
    {
        _nodeMap.Clear();
        _handleMap.Clear();
        foreach (var root in roots)
        {
            AddNode(root);
        }
    }

    private void AddNode(CadDocumentTreeNode node)
    {
        if (node.Source is not null)
        {
            _nodeMap[node.Source] = node;
        }
        if (!string.IsNullOrWhiteSpace(node.Handle))
        {
            _handleMap[node.Handle] = node;
        }

        foreach (var child in node.Children)
        {
            AddNode(child);
        }
    }

    private static CadDocumentTreeNode? ResolveNode(object? item)
    {
        if (item is CadDocumentTreeNode node)
        {
            return node;
        }

        if (item is HierarchicalNode hierarchical &&
            hierarchical.Item is CadDocumentTreeNode hierarchicalNode)
        {
            return hierarchicalNode;
        }

        return null;
    }

    private object? ResolveItemForSelection(object? selected)
    {
        if (selected is null)
        {
            return null;
        }

        if (!_nodeMap.TryGetValue(selected, out var node))
        {
            if (selected is ACadSharp.IHandledCadObject handled)
            {
                var handle = handled.Handle.ToString("X");
                if (!_handleMap.TryGetValue(handle, out node))
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        if (TryGetPath(node, out var path))
        {
            TreeModel.Expand(path);
        }

        if (TreeModel is IHierarchicalModelExpander expander &&
            expander.TryExpandToItem(node, out var expanded))
        {
            return expanded;
        }

        var hierNode = ((HierarchicalModel)TreeModel).FindNode(node);
        return hierNode is null ? node : hierNode;
    }

    private bool TryGetPath(CadDocumentTreeNode target, out List<CadDocumentTreeNode> path)
    {
        path = new List<CadDocumentTreeNode>();
        foreach (var root in _roots)
        {
            if (TryCollectPath(root, target, path))
            {
                return true;
            }
        }

        path.Clear();
        return false;
    }

    private static bool TryCollectPath(
        CadDocumentTreeNode current,
        CadDocumentTreeNode target,
        List<CadDocumentTreeNode> path)
    {
        path.Add(current);
        if (ReferenceEquals(current, target))
        {
            return true;
        }

        foreach (var child in current.Children)
        {
            if (TryCollectPath(child, target, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private void ApplySearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            SearchModel.Clear();
            UpdateSearchSummary();
            return;
        }

        var descriptor = new SearchDescriptor(
            SearchText.Trim(),
            matchMode: SearchMatchMode.Contains,
            termMode: SearchTermCombineMode.Any,
            scope: SearchScope.VisibleColumns,
            comparison: StringComparison.OrdinalIgnoreCase);

        SearchModel.SetOrUpdate(descriptor);
        UpdateSearchSummary();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(FilterText) || _roots.Count == 0)
        {
            FilteringModel.Remove(FilterColumnId);
            return;
        }

        var text = FilterText.Trim();
        var matches = BuildMatchSet(_roots, text);
        FilteringModel.SetOrUpdate(new FilteringDescriptor(
            columnId: FilterColumnId,
            @operator: FilteringOperator.Custom,
            propertyPath: FilterPropertyPath,
            predicate: item => MatchesFilter(item, matches)));
    }

    private void UpdateSearchSummary()
    {
        var count = SearchModel.Results.Count;
        var current = SearchModel.CurrentIndex >= 0 ? SearchModel.CurrentIndex + 1 : 0;

        if (count == 0)
        {
            SearchSummary = "No results";
        }
        else if (current == 0)
        {
            SearchSummary = $"{count:n0} results";
        }
        else
        {
            SearchSummary = $"{current:n0} of {count:n0}";
        }
    }

    private static bool MatchesFilter(object item, HashSet<CadDocumentTreeNode> matches)
    {
        if (item is HierarchicalNode node && node.Item is CadDocumentTreeNode treeNode)
        {
            return matches.Contains(treeNode);
        }

        if (item is CadDocumentTreeNode directNode)
        {
            return matches.Contains(directNode);
        }

        return false;
    }

    private static HashSet<CadDocumentTreeNode> BuildMatchSet(IReadOnlyList<CadDocumentTreeNode> roots, string text)
    {
        var matches = new HashSet<CadDocumentTreeNode>();
        foreach (var root in roots)
        {
            CollectMatches(root, text, matches);
        }

        return matches;
    }

    private static bool CollectMatches(CadDocumentTreeNode node, string text, HashSet<CadDocumentTreeNode> matches)
    {
        var isMatch =
            node.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            node.Kind.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            node.TypeName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            node.HandleText.Contains(text, StringComparison.OrdinalIgnoreCase);

        var childMatch = false;
        foreach (var child in node.Children)
        {
            if (CollectMatches(child, text, matches))
            {
                childMatch = true;
            }
        }

        if (isMatch || childMatch)
        {
            matches.Add(node);
            return true;
        }

        return false;
    }
}
