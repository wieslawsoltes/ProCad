using System.Reactive.Linq;
using ACadInspector.Core;
using ACadInspector.Editing.Sessions;
using ACadInspector.Services;
using ACadInspector.ViewModels;
using ACadSharp;
using ACadSharp.Tables;
using Xunit;

namespace ACadInspector.Tests.ViewModels;

public sealed class CadTextStyleToolViewModelTests
{
    [Fact]
    public void NewAndDuplicateCommands_CreateAndSelectStyles()
    {
        var (viewModel, document, _) = CreateHarness();

        using (viewModel.NewStyleCommand.Execute().Subscribe())
        {
        }

        Assert.Equal(2, document.TextStyles.Count);
        var created = Assert.IsType<TextStyle>(viewModel.SelectedStyle?.Style);
        Assert.False(string.Equals(TextStyle.DefaultName, created.Name, System.StringComparison.OrdinalIgnoreCase));

        using (viewModel.DuplicateStyleCommand.Execute().Subscribe())
        {
        }

        Assert.Equal(3, document.TextStyles.Count);
        Assert.StartsWith(
            $"{created.Name}_Copy",
            viewModel.SelectedStyle?.Style.Name ?? string.Empty,
            System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyAndSetCurrentCommands_UpdateSelectedStyle()
    {
        var (viewModel, document, _) = CreateHarness();

        using (viewModel.NewStyleCommand.Execute().Subscribe())
        {
        }

        var style = Assert.IsType<TextStyle>(viewModel.SelectedStyle?.Style);

        viewModel.EditorName = "Anno";
        viewModel.EditorFontFile = "arial.ttf";
        viewModel.EditorBigFontFile = "gbcbig.shx";
        viewModel.EditorHeight = "2.5";
        viewModel.EditorWidth = "0.9";
        viewModel.EditorLastHeight = "3.1";
        viewModel.EditorObliqueDegrees = "15";
        viewModel.EditorIsShapeFile = true;
        viewModel.EditorIsVerticalText = true;
        viewModel.EditorMirrorBackward = true;
        viewModel.EditorMirrorUpsideDown = true;
        viewModel.EditorBold = true;
        viewModel.EditorItalic = true;

        Assert.True(viewModel.CanApplyChanges);

        using (viewModel.ApplyStyleCommand.Execute().Subscribe())
        {
        }

        Assert.Equal("Anno", style.Name);
        Assert.Equal("arial.ttf", style.Filename);
        Assert.Equal("gbcbig.shx", style.BigFontFilename);
        Assert.Equal(2.5, style.Height, 3);
        Assert.Equal(0.9, style.Width, 3);
        Assert.Equal(3.1, style.LastHeight, 3);
        Assert.Equal(15.0, style.ObliqueAngle * 180.0 / System.Math.PI, 3);
        Assert.True(style.Flags.HasFlag(StyleFlags.IsShape));
        Assert.True(style.Flags.HasFlag(StyleFlags.VerticalText));
        Assert.True(style.TrueType.HasFlag(FontFlags.Bold));
        Assert.True(style.TrueType.HasFlag(FontFlags.Italic));

        using (viewModel.SetCurrentStyleCommand.Execute().Subscribe())
        {
        }

        Assert.Equal(style.Name, document.Header.CurrentTextStyleName);
        Assert.True(viewModel.IsCurrentStyle);
        Assert.True(viewModel.SelectedStyle?.IsCurrent);
    }

    [Fact]
    public void DeleteCommand_RemovesSelectedNonDefaultStyleAndFallsBackToStandard()
    {
        var (viewModel, document, _) = CreateHarness();

        using (viewModel.NewStyleCommand.Execute().Subscribe())
        {
        }

        var created = Assert.IsType<TextStyle>(viewModel.SelectedStyle?.Style);
        using (viewModel.SetCurrentStyleCommand.Execute().Subscribe())
        {
        }
        Assert.Equal(created.Name, document.Header.CurrentTextStyleName);

        using (viewModel.DeleteStyleCommand.Execute().Subscribe())
        {
        }

        Assert.False(document.TextStyles.Contains(created.Name));
        Assert.True(document.TextStyles.Contains(TextStyle.DefaultName));
        Assert.Equal(TextStyle.DefaultName, document.Header.CurrentTextStyleName);
        Assert.Equal(TextStyle.DefaultName, viewModel.SelectedStyle?.Style.Name);
    }

    [Fact]
    public void ApplyCommand_RenameCurrentStyle_UpdatesCurrentStyleHeader()
    {
        var (viewModel, document, _) = CreateHarness();
        using (viewModel.NewStyleCommand.Execute().Subscribe())
        {
        }
        var created = Assert.IsType<TextStyle>(viewModel.SelectedStyle?.Style);
        using (viewModel.SetCurrentStyleCommand.Execute().Subscribe())
        {
        }

        Assert.Equal(created.Name, document.Header.CurrentTextStyleName);
        viewModel.EditorName = "StandardRenamed";
        Assert.True(viewModel.CanApplyChanges);

        using (viewModel.ApplyStyleCommand.Execute().Subscribe())
        {
        }

        Assert.Equal("StandardRenamed", document.Header.CurrentTextStyleName);
        Assert.Equal("StandardRenamed", viewModel.SelectedStyle?.Style.Name);
        Assert.True(viewModel.IsCurrentStyle);
    }

    [Fact]
    public void RevertCommand_RestoresBaselineAndClearsDirtyState()
    {
        var (viewModel, _, _) = CreateHarness();
        var baselineName = viewModel.EditorName;

        viewModel.EditorName = "TempName";
        Assert.True(viewModel.IsDirty);

        using (viewModel.RevertStyleCommand.Execute().Subscribe())
        {
        }

        Assert.Equal(baselineName, viewModel.EditorName);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanApplyChanges);
    }

    [Fact]
    public void ApplyCommand_InvalidShapeAndVerticalCombination_IsRejected()
    {
        var (viewModel, document, _) = CreateHarness();

        viewModel.EditorIsShapeFile = true;
        viewModel.EditorFontFile = string.Empty;

        Assert.Contains("Shape text styles require a font file.", viewModel.ValidationMessage, System.StringComparison.Ordinal);
        Assert.False(viewModel.CanApplyChanges);

        Assert.True(document.TextStyles.Contains(TextStyle.DefaultName));
        Assert.Equal(TextStyle.DefaultName, viewModel.SelectedStyle?.Style.Name);
    }

    private static (CadTextStyleToolViewModel ViewModel, CadDocument Document, CadDocumentContextService Context) CreateHarness()
    {
        var selectionService = new CadSelectionService();
        var documentContext = new CadDocumentContextService();
        var sessionHost = new CadEditorSessionHostService(
            new CadEditorSessionFactory(),
            documentContext,
            selectionService);
        var previewService = new CadStylePreviewService(new NullStylePreviewRenderer());
        var viewModel = new CadTextStyleToolViewModel(selectionService, documentContext, previewService, sessionHost);

        var document = new CadDocument();
        var documentViewModel = new CadDocumentViewModel(
            document,
            CadFileFormat.Dxf,
            path: null,
            displayName: "test",
            render: null!);

        documentContext.ActiveDocument = documentViewModel;
        selectionService.SelectedObject = document.Header.CurrentTextStyle;
        return (viewModel, document, documentContext);
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
