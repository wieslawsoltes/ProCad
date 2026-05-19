using System.Linq;
using ProCad.Core;
using ProCad.Editing.Sessions;
using ProCad.Services;
using ProCad.ViewModels;
using ACadSharp;
using ACadSharp.Tables;
using Xunit;

namespace ProCad.Tests.ViewModels;

public sealed class CadLineTypeToolViewModelTests
{
    [Fact]
    public void NewAndDuplicateCommands_CreateAndSelectLineTypes()
    {
        var (viewModel, document) = CreateHarness();

        using (viewModel.NewLineTypeCommand.Execute().Subscribe())
        {
        }

        Assert.True(document.LineTypes.Count >= 4);
        var created = Assert.IsType<LineType>(viewModel.SelectedLineType?.LineType);
        Assert.False(string.Equals(created.Name, LineType.ByLayerName, System.StringComparison.OrdinalIgnoreCase));
        Assert.True(created.Segments.Any());

        using (viewModel.DuplicateLineTypeCommand.Execute().Subscribe())
        {
        }

        var duplicated = Assert.IsType<LineType>(viewModel.SelectedLineType?.LineType);
        Assert.StartsWith($"{created.Name}_Copy", duplicated.Name, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(created.Segments.Count(), duplicated.Segments.Count());
    }

    [Fact]
    public void ApplyAndSetCurrentCommands_UpdateLineTypeAndSegments()
    {
        var (viewModel, document) = CreateHarness();
        using (viewModel.NewLineTypeCommand.Execute().Subscribe())
        {
        }

        var lineType = Assert.IsType<LineType>(viewModel.SelectedLineType?.LineType);
        viewModel.EditorName = "DASH_TEXT";
        viewModel.EditorDescription = "Dash text pattern";

        while (viewModel.SegmentRows.Count > 0)
        {
            viewModel.SelectedSegment = viewModel.SegmentRows[0];
            using (viewModel.RemoveSegmentCommand.Execute().Subscribe())
            {
            }
        }

        using (viewModel.AddDashSegmentCommand.Execute().Subscribe())
        {
        }
        using (viewModel.AddSpaceSegmentCommand.Execute().Subscribe())
        {
        }

        viewModel.SegmentRows[0].Kind = CadLineTypeSegmentKind.Dash;
        viewModel.SegmentRows[0].Length = "0.75";
        viewModel.SegmentRows[0].IsText = true;
        viewModel.SegmentRows[0].TextValue = "GAS";
        viewModel.SegmentRows[0].StyleName = TextStyle.DefaultName;
        viewModel.SegmentRows[0].Scale = "1.2";
        viewModel.SegmentRows[0].RotationDegrees = "30";
        viewModel.SegmentRows[0].OffsetX = "0.1";
        viewModel.SegmentRows[0].OffsetY = "0.2";
        viewModel.SegmentRows[0].RotationIsAbsolute = true;

        viewModel.SegmentRows[1].Kind = CadLineTypeSegmentKind.Space;
        viewModel.SegmentRows[1].Length = "0.3";

        Assert.True(viewModel.CanApplyChanges);
        using (viewModel.ApplyLineTypeCommand.Execute().Subscribe())
        {
        }

        Assert.Equal("DASH_TEXT", lineType.Name);
        Assert.Equal("Dash text pattern", lineType.Description);
        var segments = lineType.Segments.ToList();
        Assert.Equal(2, segments.Count);
        Assert.True(segments[0].IsText);
        Assert.Equal("GAS", segments[0].Text);
        Assert.Equal(0.75, segments[0].Length, 3);
        Assert.Equal(1.2, segments[0].Scale, 3);
        Assert.Equal(30.0, segments[0].Rotation * 180.0 / System.Math.PI, 3);
        Assert.True(segments[0].Flags.HasFlag(LineTypeShapeFlags.RotationIsAbsolute));
        Assert.Equal(-0.3, segments[1].Length, 3);

        using (viewModel.SetCurrentLineTypeCommand.Execute().Subscribe())
        {
        }

        Assert.Equal(lineType.Name, document.Header.CurrentLineTypeName);
        Assert.True(viewModel.IsCurrentLineType);
    }

    [Fact]
    public void DeleteCommand_RemovesSelectedCustomLineTypeAndFallsBackToByLayer()
    {
        var (viewModel, document) = CreateHarness();
        using (viewModel.NewLineTypeCommand.Execute().Subscribe())
        {
        }

        var created = Assert.IsType<LineType>(viewModel.SelectedLineType?.LineType);
        using (viewModel.SetCurrentLineTypeCommand.Execute().Subscribe())
        {
        }
        Assert.Equal(created.Name, document.Header.CurrentLineTypeName);

        using (viewModel.DeleteLineTypeCommand.Execute().Subscribe())
        {
        }

        Assert.False(document.LineTypes.Contains(created.Name));
        Assert.Equal(LineType.ByLayerName, document.Header.CurrentLineTypeName);
        Assert.Equal(LineType.ByLayerName, viewModel.SelectedLineType?.LineType.Name);
    }

    [Fact]
    public void ProtectedLineType_CannotBeDeleted()
    {
        var (viewModel, _) = CreateHarness();

        Assert.Equal(LineType.ByLayerName, viewModel.SelectedLineType?.LineType.Name);
        Assert.False(viewModel.CanDeleteLineType);
    }

    [Fact]
    public void ApplyCommand_RenameCurrentLineType_UpdatesHeaderCurrentName()
    {
        var (viewModel, document) = CreateHarness();
        using (viewModel.NewLineTypeCommand.Execute().Subscribe())
        {
        }

        var baseLineType = Assert.IsType<LineType>(viewModel.SelectedLineType?.LineType);
        using (viewModel.SetCurrentLineTypeCommand.Execute().Subscribe())
        {
        }
        Assert.Equal(baseLineType.Name, document.Header.CurrentLineTypeName);

        viewModel.EditorName = "CURRENT_RENAMED";
        Assert.True(viewModel.CanApplyChanges);

        using (viewModel.ApplyLineTypeCommand.Execute().Subscribe())
        {
        }

        Assert.Equal("CURRENT_RENAMED", baseLineType.Name);
        Assert.Equal("CURRENT_RENAMED", document.Header.CurrentLineTypeName);
        Assert.True(viewModel.IsCurrentLineType);
    }

    [Fact]
    public void ApplyCommand_ShapeSegmentRequiresPositiveShapeNumber()
    {
        var (viewModel, document) = CreateHarness();
        using (viewModel.NewLineTypeCommand.Execute().Subscribe())
        {
        }

        while (viewModel.SegmentRows.Count > 0)
        {
            viewModel.SelectedSegment = viewModel.SegmentRows[0];
            using (viewModel.RemoveSegmentCommand.Execute().Subscribe())
            {
            }
        }

        using (viewModel.AddDashSegmentCommand.Execute().Subscribe())
        {
        }

        var segment = Assert.Single(viewModel.SegmentRows);
        segment.IsShape = true;
        segment.ShapeNumber = "0";
        segment.StyleName = TextStyle.DefaultName;

        Assert.Contains("shape number must be greater than 0", viewModel.ValidationMessage, System.StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanApplyChanges);
        Assert.Contains(document.LineTypes, lineType => lineType.Name == viewModel.SelectedLineType?.LineType.Name);
    }

    [Fact]
    public void RevertCommand_RestoresSegmentStateAndClearsDirty()
    {
        var (viewModel, _) = CreateHarness();
        using (viewModel.NewLineTypeCommand.Execute().Subscribe())
        {
        }

        var originalSummary = viewModel.SegmentSummaryText;
        using (viewModel.AddDotSegmentCommand.Execute().Subscribe())
        {
        }

        Assert.True(viewModel.IsDirty);
        Assert.NotEqual(originalSummary, viewModel.SegmentSummaryText);

        using (viewModel.RevertLineTypeCommand.Execute().Subscribe())
        {
        }

        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanApplyChanges);
        Assert.Equal(originalSummary, viewModel.SegmentSummaryText);
    }

    private static (CadLineTypeToolViewModel ViewModel, CadDocument Document) CreateHarness()
    {
        var selectionService = new CadSelectionService();
        var documentContext = new CadDocumentContextService();
        var sessionHost = new CadEditorSessionHostService(
            new CadEditorSessionFactory(),
            documentContext,
            selectionService);
        var previewService = new CadStylePreviewService(new NullStylePreviewRenderer());
        var viewModel = new CadLineTypeToolViewModel(selectionService, documentContext, previewService, sessionHost);

        var document = new CadDocument();
        var documentViewModel = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "test",
            render: null!);

        documentContext.ActiveDocument = documentViewModel;
        selectionService.SelectedObject = document.Header.CurrentLineType;
        return (viewModel, document);
    }

    private sealed class NullStylePreviewRenderer : IStylePreviewRenderer
    {
        public byte[]? RenderTextStyle(TextStyle style, int size)
        {
            return null;
        }

        public byte[]? RenderLineType(LineType lineType, int size)
        {
            return null;
        }

        public byte[]? RenderDimensionStyle(DimensionStyle style, int size)
        {
            return null;
        }
    }
}
