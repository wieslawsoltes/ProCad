using ACadInspector.Editing.Commands;
using ACadInspector.Editing.Clipboard;
using ACadInspector.Editing.Selection;
using ACadInspector.Editing.Sessions;
using ACadSharp;
using ACadSharp.Blocks;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Editing.Tests.Commands;

public sealed class CadGeometryCommandHandlersTests
{
    [Fact]
    public async Task LineUndoRedo_RoundTripsGeometry()
    {
        var (session, registry) = CreateHarness();

        var create = await registry.ExecuteAsync("LINE 0,0 10,0", session);
        Assert.True(create.Success);
        Assert.Single(session.Document.Entities.OfType<Line>());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Empty(session.Document.Entities.OfType<Line>());

        var redo = await registry.ExecuteAsync("REDO", session);
        Assert.True(redo.Success);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.Equal(0.0, line.StartPoint.X);
        Assert.Equal(0.0, line.StartPoint.Y);
        Assert.Equal(10.0, line.EndPoint.X);
        Assert.Equal(0.0, line.EndPoint.Y);
    }

    [Fact]
    public async Task Move_UsesSelection_AndSupportsUndo()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 1,1 2,2", session);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        session.SetSelection([line], CadSelectionMode.Replace);

        var move = await registry.ExecuteAsync("MOVE 3,4", session);
        Assert.True(move.Success);
        Assert.Equal(4.0, line.StartPoint.X);
        Assert.Equal(5.0, line.StartPoint.Y);
        Assert.Equal(5.0, line.EndPoint.X);
        Assert.Equal(6.0, line.EndPoint.Y);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(1.0, line.StartPoint.X);
        Assert.Equal(1.0, line.StartPoint.Y);
        Assert.Equal(2.0, line.EndPoint.X);
        Assert.Equal(2.0, line.EndPoint.Y);
    }

    [Fact]
    public async Task Copy_ByHandle_CreatesShiftedClone_AndSupportsUndo()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 4,0", session);

        var original = Assert.Single(session.Document.Entities.OfType<Line>());
        var handle = original.Handle.ToString("X");

        var copy = await registry.ExecuteAsync($"COPY 5,0 {handle}", session);
        Assert.True(copy.Success);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, static line => line.StartPoint.X == 5.0 && line.EndPoint.X == 9.0);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Erase_ByHandle_RemovesEntity_AndSupportsUndo()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 3,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var erase = await registry.ExecuteAsync($"ERASE {line.Handle:X}", session);
        Assert.True(erase.Success);
        Assert.Empty(session.Document.Entities.OfType<Line>());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Circle_MoveAndUndo_RoundTripsCenter()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("CIRCLE 2,3 4", session);
        Assert.True(create.Success);

        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());
        Assert.Equal(2.0, circle.Center.X);
        Assert.Equal(3.0, circle.Center.Y);
        Assert.Equal(4.0, circle.Radius);

        var move = await registry.ExecuteAsync("MOVE 5,-1", session);
        Assert.True(move.Success);
        Assert.Equal(7.0, circle.Center.X);
        Assert.Equal(2.0, circle.Center.Y);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(2.0, circle.Center.X);
        Assert.Equal(3.0, circle.Center.Y);
    }

    [Fact]
    public async Task Arc_Create_ParsesDegreeAngles()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("ARC 0,0 5 0 90", session);
        Assert.True(create.Success);

        var arc = Assert.Single(session.Document.Entities.OfType<Arc>());
        Assert.Equal(0.0, arc.Center.X);
        Assert.Equal(0.0, arc.Center.Y);
        Assert.Equal(5.0, arc.Radius);
        Assert.Equal(0.0, arc.StartAngle, 6);
        Assert.Equal(Math.PI / 2.0, arc.EndAngle, 6);
    }

    [Fact]
    public async Task Ellipse_CreateAndUndo_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("ELLIPSE 0,0 4,0 0.5", session);
        Assert.True(create.Success);

        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());
        Assert.Equal(0.0, ellipse.Center.X, 6);
        Assert.Equal(0.0, ellipse.Center.Y, 6);
        Assert.Equal(4.0, ellipse.MajorAxisEndPoint.X, 6);
        Assert.Equal(0.0, ellipse.MajorAxisEndPoint.Y, 6);
        Assert.Equal(0.5, ellipse.RadiusRatio, 6);
        Assert.Equal(0.0, ellipse.StartParameter, 6);
        Assert.Equal(Math.PI * 2.0, ellipse.EndParameter, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Empty(session.Document.Entities.OfType<Ellipse>());
    }

    [Fact]
    public async Task Ellipse_CreateWithAngles_ParsesParameters()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("ELLIPSE 0,0 4,0 0.25 0 180", session);
        Assert.True(create.Success);

        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());
        Assert.Equal(0.25, ellipse.RadiusRatio, 6);
        Assert.Equal(0.0, ellipse.StartParameter, 6);
        Assert.Equal(Math.PI, ellipse.EndParameter, 6);
    }

    [Fact]
    public async Task Spline_CreateAndUndo_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("SPLINE 0,0 5,0 10,5", session);
        Assert.True(create.Success);

        var spline = Assert.Single(session.Document.Entities.OfType<Spline>());
        Assert.Equal(3, spline.FitPoints.Count);
        Assert.NotEmpty(spline.ControlPoints);
        Assert.NotEmpty(spline.Knots);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Empty(session.Document.Entities.OfType<Spline>());
    }

    [Fact]
    public async Task Spline_CopyEraseUndo_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("SPLINE 0,0 5,0 10,5", session);
        var spline = Assert.Single(session.Document.Entities.OfType<Spline>());

        var copy = await registry.ExecuteAsync($"COPY 1,0 {spline.Handle:X}", session);
        Assert.True(copy.Success);
        Assert.Equal(2, session.Document.Entities.OfType<Spline>().Count());

        var copied = session.Document.Entities
            .OfType<Spline>()
            .Single(candidate => !ReferenceEquals(candidate, spline));
        Assert.Contains(copied.FitPoints, point => Math.Abs(point.X - 1.0) < 1e-6 && Math.Abs(point.Y - 0.0) < 1e-6);

        var erase = await registry.ExecuteAsync($"ERASE {spline.Handle:X}", session);
        Assert.True(erase.Success);
        Assert.Single(session.Document.Entities.OfType<Spline>());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(2, session.Document.Entities.OfType<Spline>().Count());
    }

    [Fact]
    public async Task Ellipse_MoveRotateScaleMirror_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("ELLIPSE 2,0 4,0 0.5", session);
        Assert.True(create.Success);

        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());

        var move = await registry.ExecuteAsync($"MOVE 1,2 {ellipse.Handle:X}", session);
        Assert.True(move.Success);
        Assert.Equal(3.0, ellipse.Center.X, 6);
        Assert.Equal(2.0, ellipse.Center.Y, 6);
        Assert.Equal(2.0, ellipse.MajorAxisEndPoint.X, 6);
        Assert.Equal(0.0, ellipse.MajorAxisEndPoint.Y, 6);

        var rotate = await registry.ExecuteAsync($"ROTATE 90 0,0 {ellipse.Handle:X}", session);
        Assert.True(rotate.Success);
        Assert.Equal(-2.0, ellipse.Center.X, 6);
        Assert.Equal(3.0, ellipse.Center.Y, 6);
        Assert.Equal(0.0, ellipse.MajorAxisEndPoint.X, 6);
        Assert.Equal(2.0, ellipse.MajorAxisEndPoint.Y, 6);

        var scale = await registry.ExecuteAsync($"SCALE 2 0,0 {ellipse.Handle:X}", session);
        Assert.True(scale.Success);
        Assert.Equal(-4.0, ellipse.Center.X, 6);
        Assert.Equal(6.0, ellipse.Center.Y, 6);
        Assert.Equal(0.0, ellipse.MajorAxisEndPoint.X, 6);
        Assert.Equal(4.0, ellipse.MajorAxisEndPoint.Y, 6);

        var mirror = await registry.ExecuteAsync($"MIRROR 0,0 1,0 {ellipse.Handle:X}", session);
        Assert.True(mirror.Success);
        Assert.Equal(-4.0, ellipse.Center.X, 6);
        Assert.Equal(-6.0, ellipse.Center.Y, 6);
        Assert.Equal(0.0, ellipse.MajorAxisEndPoint.X, 6);
        Assert.Equal(-4.0, ellipse.MajorAxisEndPoint.Y, 6);
    }

    [Fact]
    public async Task Spline_MoveRotateScaleMirror_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("SPLINE 0,0 3,0 3,2", session);
        Assert.True(create.Success);

        var spline = Assert.Single(session.Document.Entities.OfType<Spline>());

        var move = await registry.ExecuteAsync($"MOVE 1,0 {spline.Handle:X}", session);
        Assert.True(move.Success);
        Assert.Contains(spline.FitPoints, point => Approximately(point.X, 1.0) && Approximately(point.Y, 0.0));

        var rotate = await registry.ExecuteAsync($"ROTATE 90 0,0 {spline.Handle:X}", session);
        Assert.True(rotate.Success);
        Assert.Contains(spline.FitPoints, point => Approximately(point.X, 0.0) && Approximately(point.Y, 1.0));

        var scale = await registry.ExecuteAsync($"SCALE 2 0,0 {spline.Handle:X}", session);
        Assert.True(scale.Success);
        Assert.Contains(spline.FitPoints, point => Approximately(point.X, 0.0) && Approximately(point.Y, 2.0));

        var mirror = await registry.ExecuteAsync($"MIRROR 0,0 0,1 {spline.Handle:X}", session);
        Assert.True(mirror.Success);
        Assert.Contains(spline.FitPoints, point => Approximately(point.X, 4.0) && Approximately(point.Y, 8.0));
    }

    [Fact]
    public async Task Text_CreateMoveCopyEraseUndo_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("TEXT 1,2 2 30 \"Hello CAD\"", session);
        Assert.True(create.Success);

        var text = Assert.Single(session.Document.Entities.OfType<TextEntity>());
        Assert.Equal(1.0, text.InsertPoint.X, 6);
        Assert.Equal(2.0, text.InsertPoint.Y, 6);
        Assert.Equal(2.0, text.Height, 6);
        Assert.Equal(Math.PI / 6.0, text.Rotation, 6);
        Assert.Equal("Hello CAD", text.Value);

        var move = await registry.ExecuteAsync($"MOVE 3,0 {text.Handle:X}", session);
        Assert.True(move.Success);
        Assert.Equal(4.0, text.InsertPoint.X, 6);
        Assert.Equal(2.0, text.InsertPoint.Y, 6);

        var copy = await registry.ExecuteAsync($"COPY 1,1 {text.Handle:X}", session);
        Assert.True(copy.Success);
        Assert.Equal(2, session.Document.Entities.OfType<TextEntity>().Count());

        var erase = await registry.ExecuteAsync($"ERASE {text.Handle:X}", session);
        Assert.True(erase.Success);
        Assert.Single(session.Document.Entities.OfType<TextEntity>());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(2, session.Document.Entities.OfType<TextEntity>().Count());
    }

    [Fact]
    public async Task MText_CreateRotateScaleUndo_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("MTEXT 1,0 1.5 20 45 \"First\\PSecond\"", session);
        Assert.True(create.Success);

        var mtext = Assert.Single(session.Document.Entities.OfType<MText>());
        Assert.Equal(1.0, mtext.InsertPoint.X, 6);
        Assert.Equal(0.0, mtext.InsertPoint.Y, 6);
        Assert.Equal(1.5, mtext.Height, 6);
        Assert.Equal(20.0, mtext.RectangleWidth, 6);
        Assert.Equal(Math.PI / 4.0, mtext.Rotation, 6);

        var rotate = await registry.ExecuteAsync($"ROTATE 45 0,0 {mtext.Handle:X}", session);
        Assert.True(rotate.Success);
        Assert.Equal(Math.PI / 2.0, mtext.Rotation, 6);

        var scale = await registry.ExecuteAsync($"SCALE 2 0,0 {mtext.Handle:X}", session);
        Assert.True(scale.Success);
        Assert.Equal(3.0, mtext.Height, 6);
        Assert.Equal(40.0, mtext.RectangleWidth, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(1.5, mtext.Height, 6);
        Assert.Equal(20.0, mtext.RectangleWidth, 6);
    }

    [Fact]
    public async Task Insert_CreateMoveCopyAndUndo_Works()
    {
        var (session, registry) = CreateHarness();
        var block = new ACadSharp.Tables.BlockRecord("INS_TEST");
        block.Entities.Add(new Line
        {
            StartPoint = new XYZ(0, 0, 0),
            EndPoint = new XYZ(2, 0, 0)
        });
        session.Document.BlockRecords.Add(block);

        var create = await registry.ExecuteAsync("INSERT INS_TEST 1,2", session);
        Assert.True(create.Success);
        var insert = Assert.Single(session.Document.Entities.OfType<Insert>());
        Assert.Equal(1.0, insert.InsertPoint.X, 6);
        Assert.Equal(2.0, insert.InsertPoint.Y, 6);
        Assert.Equal("INS_TEST", insert.Block.Name);

        var move = await registry.ExecuteAsync($"MOVE 3,1 {insert.Handle:X}", session);
        Assert.True(move.Success);
        Assert.Equal(4.0, insert.InsertPoint.X, 6);
        Assert.Equal(3.0, insert.InsertPoint.Y, 6);

        var copy = await registry.ExecuteAsync($"COPY 5,0 {insert.Handle:X}", session);
        Assert.True(copy.Success);
        Assert.Equal(2, session.Document.Entities.OfType<Insert>().Count());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Single(session.Document.Entities.OfType<Insert>());
    }

    [Fact]
    public async Task Insert_CopyClipCutAndPasteClip_Works()
    {
        var (session, registry) = CreateHarness();
        var block = new ACadSharp.Tables.BlockRecord("INS_CLIP");
        block.Entities.Add(new Circle
        {
            Center = XYZ.Zero,
            Radius = 1
        });
        session.Document.BlockRecords.Add(block);
        Assert.True((await registry.ExecuteAsync("INSERT INS_CLIP 0,0", session)).Success);
        var insert = Assert.Single(session.Document.Entities.OfType<Insert>());

        var copy = await registry.ExecuteAsync($"COPYCLIP {insert.Handle:X}", session);
        Assert.True(copy.Success);

        var cut = await registry.ExecuteAsync($"CUT {insert.Handle:X}", session);
        Assert.True(cut.Success);
        Assert.Empty(session.Document.Entities.OfType<Insert>());

        var paste = await registry.ExecuteAsync("PASTECLIP 10,5", session);
        Assert.True(paste.Success);
        var pasted = Assert.Single(session.Document.Entities.OfType<Insert>());
        Assert.Equal(10.0, pasted.InsertPoint.X, 6);
        Assert.Equal(5.0, pasted.InsertPoint.Y, 6);
        Assert.Equal("INS_CLIP", pasted.Block.Name);
    }

    [Fact]
    public async Task XRef_ReloadAndBind_WorkflowUpdatesFlags()
    {
        var (session, registry) = CreateHarness();
        var xref = new ACadSharp.Tables.BlockRecord("XREF_A", "xref-a.dwg");
        xref.IsUnloaded = true;
        xref.Flags &= ~BlockTypeFlags.XRefResolved;
        session.Document.BlockRecords.Add(xref);

        var reload = await registry.ExecuteAsync("XREFRELOAD XREF_A", session);
        Assert.True(reload.Success);
        Assert.False(xref.IsUnloaded);
        Assert.True(xref.Flags.HasFlag(BlockTypeFlags.XRefResolved));

        var bind = await registry.ExecuteAsync("XREFBIND XREF_A", session);
        Assert.True(bind.Success);
        Assert.False(xref.Flags.HasFlag(BlockTypeFlags.XRef));
        Assert.False(xref.Flags.HasFlag(BlockTypeFlags.XRefOverlay));
        Assert.False(xref.Flags.HasFlag(BlockTypeFlags.XRefDependent));
        Assert.False(xref.Flags.HasFlag(BlockTypeFlags.XRefResolved));
        Assert.True(string.IsNullOrWhiteSpace(xref.BlockEntity.XRefPath));
    }

    [Fact]
    public async Task XRef_Detach_RemovesBlockAndAllInsertReferences()
    {
        var (session, registry) = CreateHarness();
        var xref = new ACadSharp.Tables.BlockRecord("XREF_B", "xref-b.dwg");
        session.Document.BlockRecords.Add(xref);

        var modelInsert = new Insert(xref) { InsertPoint = new XYZ(0, 0, 0) };
        session.Document.Entities.Add(modelInsert);
        session.EntityIndex.Register(modelInsert);

        var hostBlock = new ACadSharp.Tables.BlockRecord("HOST_BLOCK");
        var nestedInsert = new Insert(xref) { InsertPoint = new XYZ(10, 0, 0) };
        hostBlock.Entities.Add(nestedInsert);
        session.Document.BlockRecords.Add(hostBlock);
        session.EntityIndex.Register(nestedInsert);
        session.SetSelection([modelInsert], CadSelectionMode.Replace);

        var revisionBefore = session.Revision;
        var detach = await registry.ExecuteAsync("XREFDETACH XREF_B", session);
        Assert.True(detach.Success);
        Assert.True(session.Revision > revisionBefore);
        Assert.Empty(session.SelectionSet.Items);
        Assert.DoesNotContain(session.Document.Entities, static entity => entity is Insert insert && insert.Block?.Name == "XREF_B");
        Assert.DoesNotContain(hostBlock.Entities, static entity => entity is Insert insert && insert.Block?.Name == "XREF_B");
        Assert.False(session.Document.BlockRecords.TryGetValue("XREF_B", out _));
    }

    [Fact]
    public async Task XRef_Detach_UsesSelectedInsertWhenNameIsOmitted()
    {
        var (session, registry) = CreateHarness();
        var xref = new ACadSharp.Tables.BlockRecord("XREF_SEL", "xref-sel.dwg");
        session.Document.BlockRecords.Add(xref);

        var insert = new Insert(xref) { InsertPoint = new XYZ(2, 2, 0) };
        session.Document.Entities.Add(insert);
        session.EntityIndex.Register(insert);
        session.SetSelection([insert], CadSelectionMode.Replace);

        var detach = await registry.ExecuteAsync("XREFDETACH", session);
        Assert.True(detach.Success);
        Assert.False(session.Document.BlockRecords.TryGetValue("XREF_SEL", out _));
    }

    [Fact]
    public async Task TextAndMText_CopyClipPasteClip_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("TEXT 0,0 1 \"T1\"", session);
        await registry.ExecuteAsync("MTEXT 2,0 1 5 \"M1\"", session);

        var text = Assert.Single(session.Document.Entities.OfType<TextEntity>());
        var mtext = Assert.Single(session.Document.Entities.OfType<MText>());

        var copyClip = await registry.ExecuteAsync($"COPYCLIP {text.Handle:X} {mtext.Handle:X}", session);
        Assert.True(copyClip.Success);

        var paste = await registry.ExecuteAsync("PASTECLIP 10,10", session);
        Assert.True(paste.Success);

        Assert.Equal(2, session.Document.Entities.OfType<TextEntity>().Count());
        Assert.Equal(2, session.Document.Entities.OfType<MText>().Count());

        Assert.Contains(
            session.Document.Entities.OfType<TextEntity>(),
            candidate => Math.Abs(candidate.InsertPoint.X - 10.0) < 1e-6 &&
                         Math.Abs(candidate.InsertPoint.Y - 10.0) < 1e-6);
        Assert.Contains(
            session.Document.Entities.OfType<MText>(),
            candidate => Math.Abs(candidate.InsertPoint.X - 12.0) < 1e-6 &&
                         Math.Abs(candidate.InsertPoint.Y - 10.0) < 1e-6);
    }

    [Fact]
    public async Task CreateCommands_UseCurrentLayerLineTypeColorAndTextStyle()
    {
        var (session, registry) = CreateHarness();
        var layer = new ACadSharp.Tables.Layer("A-ANNO");
        var lineType = new ACadSharp.Tables.LineType("CENTER2");
        var textStyle = new ACadSharp.Tables.TextStyle("AnnoStyle");
        session.Document.Layers.Add(layer);
        session.Document.LineTypes.Add(lineType);
        session.Document.TextStyles.Add(textStyle);

        session.Document.Header.CurrentLayerName = layer.Name;
        session.Document.Header.CurrentLineTypeName = lineType.Name;
        session.Document.Header.CurrentTextStyleName = textStyle.Name;
        session.Document.Header.CurrentEntityColor = new ACadSharp.Color((short)2);
        session.Document.Header.CurrentEntityLineWeight = ACadSharp.LineWeightType.W50;
        session.Document.Header.CurrentEntityLinetypeScale = 1.75;

        var lineResult = await registry.ExecuteAsync("LINE 0,0 3,0", session);
        Assert.True(lineResult.Success);

        var textResult = await registry.ExecuteAsync("TEXT 1,1 2 0 \"Anno\"", session);
        Assert.True(textResult.Success);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.Equal(layer.Name, line.Layer.Name);
        Assert.Equal(lineType.Name, line.LineType.Name);
        Assert.Equal((short)2, line.Color.Index);
        Assert.Equal(ACadSharp.LineWeightType.W50, line.LineWeight);
        Assert.Equal(1.75, line.LineTypeScale, 6);

        var text = Assert.Single(session.Document.Entities.OfType<TextEntity>());
        Assert.Equal(layer.Name, text.Layer.Name);
        Assert.Equal(lineType.Name, text.LineType.Name);
        Assert.Equal(textStyle.Name, text.Style.Name);
    }

    [Fact]
    public async Task Copy_PreservesSourceLayerAndLineType()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 2,0", session);
        var source = Assert.Single(session.Document.Entities.OfType<Line>());

        var sourceLayer = new ACadSharp.Tables.Layer("SRC_LAYER");
        var sourceLineType = new ACadSharp.Tables.LineType("SRC_LTYPE");
        session.Document.Layers.Add(sourceLayer);
        session.Document.LineTypes.Add(sourceLineType);
        source.Layer = sourceLayer;
        source.LineType = sourceLineType;
        source.Color = new ACadSharp.Color((short)5);
        source.LineWeight = ACadSharp.LineWeightType.W70;
        source.LineTypeScale = 3.5;

        var copy = await registry.ExecuteAsync($"COPY 5,0 {source.Handle:X}", session);
        Assert.True(copy.Success);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        var clone = lines.Single(candidate => !ReferenceEquals(candidate, source));
        Assert.Equal(sourceLayer.Name, clone.Layer.Name);
        Assert.Equal(sourceLineType.Name, clone.LineType.Name);
        Assert.Equal((short)5, clone.Color.Index);
        Assert.Equal(ACadSharp.LineWeightType.W70, clone.LineWeight);
        Assert.Equal(3.5, clone.LineTypeScale, 6);
    }

    [Fact]
    public async Task CopyClipPasteClip_PreservesTextStyle()
    {
        var (session, registry) = CreateHarness();
        var textStyle = new ACadSharp.Tables.TextStyle("CLIP_STYLE");
        session.Document.TextStyles.Add(textStyle);
        session.Document.Header.CurrentTextStyleName = textStyle.Name;

        var create = await registry.ExecuteAsync("TEXT 0,0 1 0 \"Styled\"", session);
        Assert.True(create.Success);
        var source = Assert.Single(session.Document.Entities.OfType<TextEntity>());
        Assert.Equal(textStyle.Name, source.Style.Name);

        var copy = await registry.ExecuteAsync($"COPYCLIP {source.Handle:X}", session);
        Assert.True(copy.Success);

        var paste = await registry.ExecuteAsync("PASTECLIP 10,0", session);
        Assert.True(paste.Success);

        var texts = session.Document.Entities.OfType<TextEntity>().ToArray();
        Assert.Equal(2, texts.Length);
        Assert.All(texts, text => Assert.Equal(textStyle.Name, text.Style.Name));
    }

    [Fact]
    public async Task DimAndLeaderCommands_CreateRenderableEntities()
    {
        var (session, registry) = CreateHarness();

        var dimLinear = await registry.ExecuteAsync("DIMLINEAR 0,0 10,0 10,2", session);
        Assert.True(dimLinear.Success);
        Assert.NotEmpty(session.Document.Entities.OfType<Line>());
        Assert.NotEmpty(session.Document.Entities.OfType<TextEntity>());

        var leader = await registry.ExecuteAsync("LEADER 1,1 4,3 6,3", session);
        Assert.True(leader.Success);
        Assert.Contains(
            session.Document.Entities.OfType<LwPolyline>(),
            static polyline => !polyline.IsClosed);

        var mleader = await registry.ExecuteAsync("MLEADER 2,2 6,4", session);
        Assert.True(mleader.Success);
        Assert.True(session.Document.Entities.OfType<MText>().Count() >= 1);
    }

    [Fact]
    public async Task Array_Polar_Text_CreatesRotatedCopies()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("TEXT 1,0 1 0 \"A\"", session);
        var text = Assert.Single(session.Document.Entities.OfType<TextEntity>());

        var array = await registry.ExecuteAsync($"ARRAY POLAR 4 90 0,0 {text.Handle:X}", session);
        Assert.True(array.Success);
        Assert.Equal(4, session.Document.Entities.OfType<TextEntity>().Count());

        Assert.Contains(
            session.Document.Entities.OfType<TextEntity>(),
            candidate => Math.Abs(candidate.InsertPoint.X - 0.0) < 1e-6 &&
                         Math.Abs(candidate.InsertPoint.Y - 1.0) < 1e-6 &&
                         Math.Abs(candidate.Rotation - (Math.PI / 2.0)) < 1e-6);
    }

    [Fact]
    public async Task Align_MText_TranslatesInsertPoint()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("MTEXT 1,1 1 10 \"A\"", session);
        var mtext = Assert.Single(session.Document.Entities.OfType<MText>());

        var align = await registry.ExecuteAsync($"ALIGN 1,1 5,6 {mtext.Handle:X}", session);
        Assert.True(align.Success);
        Assert.Equal(5.0, mtext.InsertPoint.X, 6);
        Assert.Equal(6.0, mtext.InsertPoint.Y, 6);
    }

    [Fact]
    public async Task Align_EllipseAndSplineAndHatch_TranslatesGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("ELLIPSE 2,0 4,0 0.5", session);
        await registry.ExecuteAsync("SPLINE 0,0 2,0 2,2", session);
        await registry.ExecuteAsync("RECTANG 0,0 3,2", session);
        var boundary = session.Document.Entities.OfType<LwPolyline>().Last();
        await registry.ExecuteAsync($"HATCH {boundary.Handle:X}", session);

        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());
        var spline = Assert.Single(session.Document.Entities.OfType<Spline>());
        var hatch = Assert.Single(session.Document.Entities.OfType<Hatch>());

        var alignEllipse = await registry.ExecuteAsync($"ALIGN 2,0 7,5 {ellipse.Handle:X}", session);
        Assert.True(alignEllipse.Success);
        Assert.Equal(7.0, ellipse.Center.X, 6);
        Assert.Equal(5.0, ellipse.Center.Y, 6);

        var alignSpline = await registry.ExecuteAsync($"ALIGN 0,0 10,10 {spline.Handle:X}", session);
        Assert.True(alignSpline.Success);
        Assert.Contains(spline.FitPoints, point => Approximately(point.X, 10.0) && Approximately(point.Y, 10.0));
        Assert.Contains(spline.FitPoints, point => Approximately(point.X, 12.0) && Approximately(point.Y, 12.0));

        var alignHatch = await registry.ExecuteAsync($"ALIGN 0,0 20,5 {hatch.Handle:X}", session);
        Assert.True(alignHatch.Success);
        var loop = GetHatchLoopPoints(hatch);
        Assert.Contains(loop, point => Approximately(point.X, 20.0) && Approximately(point.Y, 5.0));
        Assert.Contains(loop, point => Approximately(point.X, 23.0) && Approximately(point.Y, 7.0));
    }

    [Fact]
    public async Task Hatch_FromClosedPolyline_CreateUndo_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RECTANG 0,0 4,3", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var hatch = await registry.ExecuteAsync($"HATCH {polyline.Handle:X}", session);
        Assert.True(hatch.Success);
        var created = Assert.Single(session.Document.Entities.OfType<Hatch>());
        Assert.True(created.IsSolid);
        Assert.Single(created.Paths);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Empty(session.Document.Entities.OfType<Hatch>());
    }

    [Fact]
    public async Task Hatch_WithPatternName_CreatesPatternFill()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RECTANG 0,0 2,1", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var hatch = await registry.ExecuteAsync($"HATCH ANSI31 {polyline.Handle:X}", session);
        Assert.True(hatch.Success);
        var created = Assert.Single(session.Document.Entities.OfType<Hatch>());
        Assert.False(created.IsSolid);
        Assert.Equal("ANSI31", created.Pattern.Name);
    }

    [Fact]
    public async Task Hatch_FromCircle_CreateUndo_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("CIRCLE 0,0 3", session);
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());

        var hatch = await registry.ExecuteAsync($"HATCH {circle.Handle:X}", session);
        Assert.True(hatch.Success, hatch.Message);
        var created = Assert.Single(session.Document.Entities.OfType<Hatch>());
        var loop = GetHatchLoopPoints(created);
        Assert.True(loop.Count >= 12);
        Assert.Contains(loop, point => Approximately(point.X, 3.0) && Approximately(point.Y, 0.0));

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success, undo.Message);
        Assert.Empty(session.Document.Entities.OfType<Hatch>());
    }

    [Fact]
    public async Task Hatch_FromCircleWithCustomNormal_PreservesNormal()
    {
        var (session, registry) = CreateHarness();
        var circle = new Circle
        {
            Center = new XYZ(0.0, 0.0, 0.0),
            Radius = 3.0,
            Normal = new XYZ(0.0, 0.0, -2.0)
        };
        session.Document.Entities.Add(circle);
        session.EntityIndex.Register(circle);

        var hatch = await registry.ExecuteAsync($"HATCH {circle.Handle:X}", session);
        Assert.True(hatch.Success, hatch.Message);
        var created = Assert.Single(session.Document.Entities.OfType<Hatch>());
        Assert.Equal(0.0, created.Normal.X, 6);
        Assert.Equal(0.0, created.Normal.Y, 6);
        Assert.Equal(-1.0, created.Normal.Z, 6);
    }

    [Fact]
    public async Task Hatch_FromClosedPolylineWithCustomNormal_PreservesNormal()
    {
        var (session, registry) = CreateHarness();
        var polyline = new LwPolyline(new[]
        {
            new XY(0.0, 0.0),
            new XY(4.0, 0.0),
            new XY(4.0, 3.0),
            new XY(0.0, 3.0)
        })
        {
            IsClosed = true,
            Normal = new XYZ(0.0, 0.0, -1.0)
        };
        session.Document.Entities.Add(polyline);
        session.EntityIndex.Register(polyline);

        var hatch = await registry.ExecuteAsync($"HATCH {polyline.Handle:X}", session);
        Assert.True(hatch.Success, hatch.Message);
        var created = Assert.Single(session.Document.Entities.OfType<Hatch>());
        Assert.Equal(0.0, created.Normal.X, 6);
        Assert.Equal(0.0, created.Normal.Y, 6);
        Assert.Equal(-1.0, created.Normal.Z, 6);
    }

    [Fact]
    public async Task Hatch_FromClosedEllipse_CreatesSolidHatch()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("ELLIPSE 1,2 4,0 0.5", session);
        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());

        var hatch = await registry.ExecuteAsync($"HATCH {ellipse.Handle:X}", session);
        Assert.True(hatch.Success, hatch.Message);
        var created = Assert.Single(session.Document.Entities.OfType<Hatch>());
        var loop = GetHatchLoopPoints(created);
        Assert.True(loop.Count >= 12);
        Assert.All(loop, point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y)));
    }

    [Fact]
    public async Task Hatch_FromClosedSpline_CreatesSolidHatch()
    {
        var (session, registry) = CreateHarness();
        var spline = new Spline
        {
            IsClosed = true
        };
        spline.FitPoints.Add(new XYZ(0.0, 0.0, 0.0));
        spline.FitPoints.Add(new XYZ(4.0, 0.0, 0.0));
        spline.FitPoints.Add(new XYZ(4.0, 3.0, 0.0));
        spline.FitPoints.Add(new XYZ(0.0, 3.0, 0.0));
        session.Document.Entities.Add(spline);
        session.EntityIndex.Register(spline);

        var hatch = await registry.ExecuteAsync($"HATCH {spline.Handle:X}", session);
        Assert.True(hatch.Success, hatch.Message);
        var created = Assert.Single(session.Document.Entities.OfType<Hatch>());
        var loop = GetHatchLoopPoints(created);
        Assert.Equal(4, loop.Count);
        Assert.Contains(loop, point => Approximately(point.X, 4.0) && Approximately(point.Y, 3.0));
    }

    [Fact]
    public async Task Hatch_FromOpenSpline_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("SPLINE 0,0 4,0 4,3", session);
        var spline = Assert.Single(session.Document.Entities.OfType<Spline>());

        var hatch = await registry.ExecuteAsync($"HATCH {spline.Handle:X}", session);
        Assert.False(hatch.Success);
        Assert.Empty(session.Document.Entities.OfType<Hatch>());
    }

    [Fact]
    public async Task Hatch_MoveCopyEraseUndo_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RECTANG 0,0 4,3", session);
        var boundary = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var hatchResult = await registry.ExecuteAsync($"HATCH {boundary.Handle:X}", session);
        Assert.True(hatchResult.Success);

        var hatch = Assert.Single(session.Document.Entities.OfType<Hatch>());

        var move = await registry.ExecuteAsync($"MOVE 2,1 {hatch.Handle:X}", session);
        Assert.True(move.Success);
        var movedLoop = GetHatchLoopPoints(hatch);
        Assert.Contains(movedLoop, point => Approximately(point.X, 2.0) && Approximately(point.Y, 1.0));
        Assert.Contains(movedLoop, point => Approximately(point.X, 6.0) && Approximately(point.Y, 4.0));

        var copy = await registry.ExecuteAsync($"COPY 10,0 {hatch.Handle:X}", session);
        Assert.True(copy.Success);
        Assert.Equal(2, session.Document.Entities.OfType<Hatch>().Count());

        var erase = await registry.ExecuteAsync($"ERASE {hatch.Handle:X}", session);
        Assert.True(erase.Success);
        Assert.Single(session.Document.Entities.OfType<Hatch>());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(2, session.Document.Entities.OfType<Hatch>().Count());
    }

    [Fact]
    public async Task Hatch_CutPasteClip_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RECTANG 0,0 3,2", session);
        var boundary = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var hatchResult = await registry.ExecuteAsync($"HATCH {boundary.Handle:X}", session);
        Assert.True(hatchResult.Success);
        var hatch = Assert.Single(session.Document.Entities.OfType<Hatch>());

        var cut = await registry.ExecuteAsync($"CUT {hatch.Handle:X}", session);
        Assert.True(cut.Success);
        Assert.Empty(session.Document.Entities.OfType<Hatch>());

        var paste = await registry.ExecuteAsync("PASTECLIP 10,5", session);
        Assert.True(paste.Success);
        var pasted = Assert.Single(session.Document.Entities.OfType<Hatch>());
        var pastedLoop = GetHatchLoopPoints(pasted);
        Assert.Contains(pastedLoop, point => Approximately(point.X, 10.0) && Approximately(point.Y, 5.0));
        Assert.Contains(pastedLoop, point => Approximately(point.X, 13.0) && Approximately(point.Y, 7.0));
    }

    [Fact]
    public async Task EllipseSpline_CutPasteClip_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("ELLIPSE 1,1 3,0 0.5", session);
        await registry.ExecuteAsync("SPLINE 0,0 2,0 2,2", session);

        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());
        var spline = Assert.Single(session.Document.Entities.OfType<Spline>());

        var cut = await registry.ExecuteAsync($"CUT {ellipse.Handle:X} {spline.Handle:X}", session);
        Assert.True(cut.Success);
        Assert.Empty(session.Document.Entities.OfType<Ellipse>());
        Assert.Empty(session.Document.Entities.OfType<Spline>());

        var paste = await registry.ExecuteAsync("PASTECLIP 10,5", session);
        Assert.True(paste.Success);

        var pastedEllipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());
        Assert.Equal(10.0, pastedEllipse.Center.X, 6);
        Assert.Equal(5.0, pastedEllipse.Center.Y, 6);

        var pastedSpline = Assert.Single(session.Document.Entities.OfType<Spline>());
        Assert.Contains(
            pastedSpline.FitPoints,
            point => Approximately(point.X, 9.0) && Approximately(point.Y, 4.0));
    }

    [Fact]
    public async Task EllipseSpline_CopyClipPasteClip_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("ELLIPSE 1,1 3,0 0.5", session);
        await registry.ExecuteAsync("SPLINE 0,0 2,0 2,2", session);

        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());
        var spline = Assert.Single(session.Document.Entities.OfType<Spline>());

        var copyClip = await registry.ExecuteAsync($"COPYCLIP {ellipse.Handle:X} {spline.Handle:X}", session);
        Assert.True(copyClip.Success);

        var paste = await registry.ExecuteAsync("PASTECLIP 10,5", session);
        Assert.True(paste.Success);

        Assert.Equal(2, session.Document.Entities.OfType<Ellipse>().Count());
        Assert.Equal(2, session.Document.Entities.OfType<Spline>().Count());

        var copiedEllipse = session.Document.Entities
            .OfType<Ellipse>()
            .Single(candidate => !ReferenceEquals(candidate, ellipse));
        Assert.Equal(10.0, copiedEllipse.Center.X, 6);
        Assert.Equal(5.0, copiedEllipse.Center.Y, 6);

        var copiedSpline = session.Document.Entities
            .OfType<Spline>()
            .Single(candidate => !ReferenceEquals(candidate, spline));
        Assert.Contains(
            copiedSpline.FitPoints,
            point => Approximately(point.X, 9.0) && Approximately(point.Y, 4.0));
    }

    [Fact]
    public async Task Boundary_FromHatch_CreatesClosedPolylines()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RECTANG 0,0 5,4", session);
        var sourcePolyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        await registry.ExecuteAsync($"HATCH {sourcePolyline.Handle:X}", session);
        var hatch = Assert.Single(session.Document.Entities.OfType<Hatch>());

        var boundary = await registry.ExecuteAsync($"BOUNDARY {hatch.Handle:X}", session);
        Assert.True(boundary.Success);

        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);
        Assert.Contains(polylines, candidate => !ReferenceEquals(candidate, sourcePolyline) && candidate.IsClosed);
    }

    [Fact]
    public async Task Boundary_FromCircle_CreatesClosedPolyline()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("CIRCLE 2,3 4", session);
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());

        var boundary = await registry.ExecuteAsync($"BOUNDARY {circle.Handle:X}", session);
        Assert.True(boundary.Success);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.True(polyline.IsClosed);
        Assert.True(polyline.Vertices.Count >= 16);
    }

    [Fact]
    public async Task Boundary_FromClosedEllipse_CreatesClosedPolyline()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("ELLIPSE 0,0 4,0 0.5", session);
        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());

        var boundary = await registry.ExecuteAsync($"BOUNDARY {ellipse.Handle:X}", session);
        Assert.True(boundary.Success);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.True(polyline.IsClosed);
        Assert.True(polyline.Vertices.Count >= 16);
    }

    [Fact]
    public async Task Boundary_FromOpenEllipse_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("ELLIPSE 0,0 4,0 0.5 0 180", session);
        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());

        var boundary = await registry.ExecuteAsync($"BOUNDARY {ellipse.Handle:X}", session);
        Assert.False(boundary.Success);
    }

    [Fact]
    public async Task Boundary_FromClosedSpline_CreatesClosedPolyline()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("SPLINE 0,0 4,0 4,3 0,3 CLOSE", session);
        var spline = Assert.Single(session.Document.Entities.OfType<Spline>());

        var boundary = await registry.ExecuteAsync($"BOUNDARY {spline.Handle:X}", session);
        Assert.True(boundary.Success);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.True(polyline.IsClosed);
        Assert.True(polyline.Vertices.Count >= 4);
    }

    [Fact]
    public async Task Pline_CopyEraseUndo_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("PLINE 0,0 2,0 2,2 CLOSE", session);
        Assert.True(create.Success);

        var original = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.True(original.IsClosed);
        Assert.Equal(3, original.Vertices.Count);

        var copy = await registry.ExecuteAsync($"COPY 10,0 {original.Handle:X}", session);
        Assert.True(copy.Success);

        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);
        Assert.Contains(polylines, polyline => polyline.Vertices.Any(v => v.Location.Equals(new XY(10, 0))));

        var erase = await registry.ExecuteAsync($"ERASE {original.Handle:X}", session);
        Assert.True(erase.Success);
        Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(2, session.Document.Entities.OfType<LwPolyline>().Count());
    }

    [Fact]
    public async Task Point_CreateRotateScaleUndo_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("POINT 1,0", session);
        Assert.True(create.Success);

        var point = Assert.Single(session.Document.Entities.OfType<Point>());
        Assert.Equal(1.0, point.Location.X);
        Assert.Equal(0.0, point.Location.Y);

        session.SetSelection([point], CadSelectionMode.Replace);

        var rotate = await registry.ExecuteAsync("ROTATE 90 0,0", session);
        Assert.True(rotate.Success);
        Assert.Equal(0.0, point.Location.X, 6);
        Assert.Equal(1.0, point.Location.Y, 6);

        var scale = await registry.ExecuteAsync("SCALE 2 0,0", session);
        Assert.True(scale.Success);
        Assert.Equal(0.0, point.Location.X, 6);
        Assert.Equal(2.0, point.Location.Y, 6);

        var undoScale = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undoScale.Success);
        Assert.Equal(0.0, point.Location.X, 6);
        Assert.Equal(1.0, point.Location.Y, 6);

        var undoRotate = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undoRotate.Success);
        Assert.Equal(1.0, point.Location.X, 6);
        Assert.Equal(0.0, point.Location.Y, 6);
    }

    [Fact]
    public async Task Rectang_CreateClosedPolyline_AndUndoRedo_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("RECTANG 1,2 5,6", session);
        Assert.True(create.Success);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.True(polyline.IsClosed);
        Assert.Equal(4, polyline.Vertices.Count);
        Assert.Equal(1.0, polyline.Vertices[0].Location.X);
        Assert.Equal(2.0, polyline.Vertices[0].Location.Y);
        Assert.Equal(5.0, polyline.Vertices[1].Location.X);
        Assert.Equal(2.0, polyline.Vertices[1].Location.Y);
        Assert.Equal(5.0, polyline.Vertices[2].Location.X);
        Assert.Equal(6.0, polyline.Vertices[2].Location.Y);
        Assert.Equal(1.0, polyline.Vertices[3].Location.X);
        Assert.Equal(6.0, polyline.Vertices[3].Location.Y);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Empty(session.Document.Entities.OfType<LwPolyline>());

        var redo = await registry.ExecuteAsync("REDO", session);
        Assert.True(redo.Success);
        Assert.Single(session.Document.Entities.OfType<LwPolyline>());
    }

    [Fact]
    public async Task Rotate_ByHandle_RotatesLine_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 1,0 2,0", session);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var rotate = await registry.ExecuteAsync($"ROTATE 90 0,0 {line.Handle:X}", session);
        Assert.True(rotate.Success);
        Assert.Equal(0.0, line.StartPoint.X, 6);
        Assert.Equal(1.0, line.StartPoint.Y, 6);
        Assert.Equal(0.0, line.EndPoint.X, 6);
        Assert.Equal(2.0, line.EndPoint.Y, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(1.0, line.StartPoint.X, 6);
        Assert.Equal(0.0, line.StartPoint.Y, 6);
        Assert.Equal(2.0, line.EndPoint.X, 6);
        Assert.Equal(0.0, line.EndPoint.Y, 6);
    }

    [Fact]
    public async Task Scale_ByHandle_ScalesCircle_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("CIRCLE 2,3 2", session);
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());

        var scale = await registry.ExecuteAsync($"SCALE 2 1,1 {circle.Handle:X}", session);
        Assert.True(scale.Success);
        Assert.Equal(3.0, circle.Center.X, 6);
        Assert.Equal(5.0, circle.Center.Y, 6);
        Assert.Equal(4.0, circle.Radius, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(2.0, circle.Center.X, 6);
        Assert.Equal(3.0, circle.Center.Y, 6);
        Assert.Equal(2.0, circle.Radius, 6);
    }

    [Fact]
    public async Task XLine_MoveCopyEraseUndo_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("XLINE 0,0 1,0", session);
        Assert.True(create.Success);

        var xline = Assert.Single(session.Document.Entities.OfType<XLine>());
        Assert.Equal(0.0, xline.FirstPoint.X, 6);
        Assert.Equal(0.0, xline.FirstPoint.Y, 6);
        Assert.Equal(1.0, xline.Direction.X, 6);
        Assert.Equal(0.0, xline.Direction.Y, 6);

        var move = await registry.ExecuteAsync("MOVE 2,3", session);
        Assert.True(move.Success);
        Assert.Equal(2.0, xline.FirstPoint.X, 6);
        Assert.Equal(3.0, xline.FirstPoint.Y, 6);
        Assert.Equal(1.0, xline.Direction.X, 6);
        Assert.Equal(0.0, xline.Direction.Y, 6);

        var copy = await registry.ExecuteAsync($"COPY 1,-1 {xline.Handle:X}", session);
        Assert.True(copy.Success);
        Assert.Equal(2, session.Document.Entities.OfType<XLine>().Count());
        Assert.Contains(
            session.Document.Entities.OfType<XLine>(),
            item => Math.Abs(item.FirstPoint.X - 3.0) < 1e-6 && Math.Abs(item.FirstPoint.Y - 2.0) < 1e-6);

        var erase = await registry.ExecuteAsync($"ERASE {xline.Handle:X}", session);
        Assert.True(erase.Success);
        Assert.Single(session.Document.Entities.OfType<XLine>());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(2, session.Document.Entities.OfType<XLine>().Count());
    }

    [Fact]
    public async Task Ray_RotateScaleUndo_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("RAY 1,1 2,1", session);
        Assert.True(create.Success);

        var ray = Assert.Single(session.Document.Entities.OfType<Ray>());
        Assert.Equal(1.0, ray.StartPoint.X, 6);
        Assert.Equal(1.0, ray.StartPoint.Y, 6);
        Assert.Equal(1.0, ray.Direction.X, 6);
        Assert.Equal(0.0, ray.Direction.Y, 6);

        var rotate = await registry.ExecuteAsync($"ROTATE 90 0,0 {ray.Handle:X}", session);
        Assert.True(rotate.Success);
        Assert.Equal(-1.0, ray.StartPoint.X, 6);
        Assert.Equal(1.0, ray.StartPoint.Y, 6);
        Assert.Equal(0.0, ray.Direction.X, 6);
        Assert.Equal(1.0, ray.Direction.Y, 6);

        var scale = await registry.ExecuteAsync($"SCALE 2 0,0 {ray.Handle:X}", session);
        Assert.True(scale.Success);
        Assert.Equal(-2.0, ray.StartPoint.X, 6);
        Assert.Equal(2.0, ray.StartPoint.Y, 6);
        Assert.Equal(0.0, ray.Direction.X, 6);
        Assert.Equal(1.0, ray.Direction.Y, 6);

        var undoScale = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undoScale.Success);
        Assert.Equal(-1.0, ray.StartPoint.X, 6);
        Assert.Equal(1.0, ray.StartPoint.Y, 6);
        Assert.Equal(0.0, ray.Direction.X, 6);
        Assert.Equal(1.0, ray.Direction.Y, 6);

        var undoRotate = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undoRotate.Success);
        Assert.Equal(1.0, ray.StartPoint.X, 6);
        Assert.Equal(1.0, ray.StartPoint.Y, 6);
        Assert.Equal(1.0, ray.Direction.X, 6);
        Assert.Equal(0.0, ray.Direction.Y, 6);
    }

    [Fact]
    public async Task Stretch_Line_MovesNearestEndpoint_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var stretch = await registry.ExecuteAsync($"STRETCH 0,2 0,0 {line.Handle:X}", session);
        Assert.True(stretch.Success);
        Assert.Equal(0.0, line.StartPoint.X, 6);
        Assert.Equal(2.0, line.StartPoint.Y, 6);
        Assert.Equal(10.0, line.EndPoint.X, 6);
        Assert.Equal(0.0, line.EndPoint.Y, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(0.0, line.StartPoint.X, 6);
        Assert.Equal(0.0, line.StartPoint.Y, 6);
        Assert.Equal(10.0, line.EndPoint.X, 6);
        Assert.Equal(0.0, line.EndPoint.Y, 6);
    }

    [Fact]
    public async Task Stretch_Polyline_MovesNearestVertex()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 5,0 5,5", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var stretch = await registry.ExecuteAsync($"STRETCH 1,1 5,0 {polyline.Handle:X}", session);
        Assert.True(stretch.Success);
        Assert.Equal(0.0, polyline.Vertices[0].Location.X, 6);
        Assert.Equal(0.0, polyline.Vertices[0].Location.Y, 6);
        Assert.Equal(6.0, polyline.Vertices[1].Location.X, 6);
        Assert.Equal(1.0, polyline.Vertices[1].Location.Y, 6);
        Assert.Equal(5.0, polyline.Vertices[2].Location.X, 6);
        Assert.Equal(5.0, polyline.Vertices[2].Location.Y, 6);
    }

    [Fact]
    public async Task Stretch_Circle_AdjustsRadius_WhenGripOnPerimeter()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("CIRCLE 0,0 2", session);
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());

        var stretch = await registry.ExecuteAsync($"STRETCH 1,0 2,0 {circle.Handle:X}", session);
        Assert.True(stretch.Success);
        Assert.Equal(3.0, circle.Radius, 6);
        Assert.Equal(0.0, circle.Center.X, 6);
        Assert.Equal(0.0, circle.Center.Y, 6);
    }

    [Fact]
    public async Task Stretch_TextAndMText_MoveInsertion()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("TEXT 1,1 1 0 \"T\"", session);
        await registry.ExecuteAsync("MTEXT 2,2 1 5 \"M\"", session);
        var text = Assert.Single(session.Document.Entities.OfType<TextEntity>());
        var mtext = Assert.Single(session.Document.Entities.OfType<MText>());

        var textStretch = await registry.ExecuteAsync($"STRETCH 3,0 1,1 {text.Handle:X}", session);
        Assert.True(textStretch.Success);
        Assert.Equal(4.0, text.InsertPoint.X, 6);
        Assert.Equal(1.0, text.InsertPoint.Y, 6);

        var mtextStretch = await registry.ExecuteAsync($"STRETCH -1,2 2,2 {mtext.Handle:X}", session);
        Assert.True(mtextStretch.Success);
        Assert.Equal(1.0, mtext.InsertPoint.X, 6);
        Assert.Equal(4.0, mtext.InsertPoint.Y, 6);
    }

    [Fact]
    public async Task Stretch_EllipseSplineAndHatch_ModifiesNearestGrip()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("ELLIPSE 0,0 4,0 0.5", session);
        await registry.ExecuteAsync("SPLINE 0,0 2,0 2,2", session);
        await registry.ExecuteAsync("RECTANG 0,0 3,2", session);
        var boundary = session.Document.Entities.OfType<LwPolyline>().Last();
        await registry.ExecuteAsync($"HATCH {boundary.Handle:X}", session);

        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());
        var spline = Assert.Single(session.Document.Entities.OfType<Spline>());
        var hatch = Assert.Single(session.Document.Entities.OfType<Hatch>());

        var stretchEllipse = await registry.ExecuteAsync($"STRETCH 1,0 4,0 {ellipse.Handle:X}", session);
        Assert.True(stretchEllipse.Success);
        Assert.Equal(5.0, ellipse.MajorAxisEndPoint.X, 6);

        var stretchSpline = await registry.ExecuteAsync($"STRETCH 0,1 2,2 {spline.Handle:X}", session);
        Assert.True(stretchSpline.Success);
        Assert.Contains(spline.FitPoints, point => Approximately(point.X, 2.0) && Approximately(point.Y, 3.0));

        var stretchHatch = await registry.ExecuteAsync($"STRETCH 1,0 0,0 {hatch.Handle:X}", session);
        Assert.True(stretchHatch.Success);
        var loop = GetHatchLoopPoints(hatch);
        Assert.Contains(loop, point => Approximately(point.X, 1.0) && Approximately(point.Y, 0.0));
    }

    [Fact]
    public async Task Stretch_WithoutGripPoint_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);

        var stretch = await registry.ExecuteAsync("STRETCH 1,0", session);
        Assert.False(stretch.Success);
    }

    [Fact]
    public async Task Array_Rectangular_Line_CreatesCopies_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 1,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var array = await registry.ExecuteAsync($"ARRAY 2 3 5 10 {line.Handle:X}", session);
        Assert.True(array.Success);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(6, lines.Length);
        Assert.Contains(lines, candidate =>
            Approximately(candidate.StartPoint.X, 20.0) &&
            Approximately(candidate.StartPoint.Y, 5.0) &&
            Approximately(candidate.EndPoint.X, 21.0) &&
            Approximately(candidate.EndPoint.Y, 5.0));

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Array_UsesSelection_WhenHandlesOmitted()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("POINT 0,0", session);
        var point = Assert.Single(session.Document.Entities.OfType<Point>());
        session.SetSelection([point], CadSelectionMode.Replace);

        var array = await registry.ExecuteAsync("ARRAY 2 2 1 1", session);
        Assert.True(array.Success);

        var points = session.Document.Entities.OfType<Point>().ToArray();
        Assert.Equal(4, points.Length);
        Assert.Contains(points, candidate => Approximately(candidate.Location.X, 1.0) && Approximately(candidate.Location.Y, 0.0));
        Assert.Contains(points, candidate => Approximately(candidate.Location.X, 0.0) && Approximately(candidate.Location.Y, 1.0));
        Assert.Contains(points, candidate => Approximately(candidate.Location.X, 1.0) && Approximately(candidate.Location.Y, 1.0));
    }

    [Fact]
    public async Task Array_InvalidDimensions_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 1,0", session);

        var array = await registry.ExecuteAsync("ARRAY 1 1 1 1", session);
        Assert.False(array.Success);
    }

    [Fact]
    public async Task Array_Polar_Line_CreatesRotatedCopies_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 1,0 2,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var array = await registry.ExecuteAsync($"ARRAY POLAR 4 90 0,0 {line.Handle:X}", session);
        Assert.True(array.Success);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(4, lines.Length);
        Assert.Contains(lines, candidate =>
            Approximately(candidate.StartPoint.X, 0.0) &&
            Approximately(candidate.StartPoint.Y, 1.0) &&
            Approximately(candidate.EndPoint.X, 0.0) &&
            Approximately(candidate.EndPoint.Y, 2.0));

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Array_EllipseSplineHatch_CreatesExpectedCopies()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("ELLIPSE 1,1 3,1 0.5", session);
        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());
        var rectangular = await registry.ExecuteAsync($"ARRAY 2 2 10 20 {ellipse.Handle:X}", session);
        Assert.True(rectangular.Success);
        Assert.Equal(4, session.Document.Entities.OfType<Ellipse>().Count());
        Assert.Contains(
            session.Document.Entities.OfType<Ellipse>(),
            candidate => Approximately(candidate.Center.X, 21.0) && Approximately(candidate.Center.Y, 11.0));

        await registry.ExecuteAsync("SPLINE 1,0 2,0 2,1", session);
        var spline = session.Document.Entities.OfType<Spline>().Last();
        var polar = await registry.ExecuteAsync($"ARRAY POLAR 4 90 0,0 {spline.Handle:X}", session);
        Assert.True(polar.Success);
        Assert.Equal(4, session.Document.Entities.OfType<Spline>().Count());
        Assert.Contains(
            session.Document.Entities.OfType<Spline>(),
            candidate => candidate.FitPoints.Any(point => Approximately(point.X, 0.0) && Approximately(point.Y, 1.0)));

        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        var path = session.Document.Entities.OfType<Line>().Last();
        await registry.ExecuteAsync("RECTANG 0,0 2,1", session);
        var boundary = session.Document.Entities.OfType<LwPolyline>().Last();
        await registry.ExecuteAsync($"HATCH {boundary.Handle:X}", session);
        var hatch = session.Document.Entities.OfType<Hatch>().Last();
        var pathArray = await registry.ExecuteAsync($"ARRAY PATH 3 {path.Handle:X} {hatch.Handle:X}", session);
        Assert.True(pathArray.Success);
        Assert.Equal(3, session.Document.Entities.OfType<Hatch>().Count());
    }

    [Fact]
    public async Task Array_PathMode_LinePath_CreatesCopies()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        var pathLine = Assert.Single(session.Document.Entities.OfType<Line>());
        await registry.ExecuteAsync("CIRCLE 0,0 1", session);
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());

        var array = await registry.ExecuteAsync($"ARRAY PATH 4 {pathLine.Handle:X} {circle.Handle:X}", session);
        Assert.True(array.Success);
        Assert.Equal(4, session.Document.Entities.OfType<Circle>().Count());
    }

    [Fact]
    public async Task Explode_OpenPolyline_CreatesSegments_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 5,0 5,5", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var explode = await registry.ExecuteAsync($"EXPLODE {polyline.Handle:X}", session);
        Assert.True(explode.Success);
        Assert.Empty(session.Document.Entities.OfType<LwPolyline>());
        Assert.Equal(2, session.Document.Entities.OfType<Line>().Count());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.Empty(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Explode_ClosedPolyline_CreatesClosedLoopSegments()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RECTANG 0,0 2,1", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var explode = await registry.ExecuteAsync($"EXPLODE {polyline.Handle:X}", session);
        Assert.True(explode.Success);
        Assert.Empty(session.Document.Entities.OfType<LwPolyline>());

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(4, lines.Length);
        Assert.Contains(lines, line =>
            Approximately(line.StartPoint.X, 0.0) &&
            Approximately(line.StartPoint.Y, 0.0) &&
            Approximately(line.EndPoint.X, 2.0) &&
            Approximately(line.EndPoint.Y, 0.0));
    }

    [Fact]
    public async Task Explode_UnsupportedType_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 1,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var explode = await registry.ExecuteAsync($"EXPLODE {line.Handle:X}", session);
        Assert.False(explode.Success);
    }

    [Fact]
    public async Task Explode_Circle_CreatesLineSegments_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("CIRCLE 0,0 2", session);
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());

        var explode = await registry.ExecuteAsync($"EXPLODE {circle.Handle:X}", session);
        Assert.True(explode.Success);
        Assert.Empty(session.Document.Entities.OfType<Circle>());
        Assert.True(session.Document.Entities.OfType<Line>().Count() >= 16);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Single(session.Document.Entities.OfType<Circle>());
        Assert.Empty(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Explode_Ellipse_CreatesLineSegments_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("ELLIPSE 1,1 4,0 0.5", session);
        var ellipse = Assert.Single(session.Document.Entities.OfType<Ellipse>());

        var explode = await registry.ExecuteAsync($"EXPLODE {ellipse.Handle:X}", session);
        Assert.True(explode.Success);
        Assert.Empty(session.Document.Entities.OfType<Ellipse>());
        Assert.True(session.Document.Entities.OfType<Line>().Count() >= 16);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Single(session.Document.Entities.OfType<Ellipse>());
        Assert.Empty(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Explode_Hatch_CreatesBoundarySegments_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RECTANG 0,0 3,2", session);
        var sourceBoundary = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        await registry.ExecuteAsync($"HATCH {sourceBoundary.Handle:X}", session);
        var hatch = Assert.Single(session.Document.Entities.OfType<Hatch>());

        var explode = await registry.ExecuteAsync($"EXPLODE {hatch.Handle:X}", session);
        Assert.True(explode.Success);
        Assert.Empty(session.Document.Entities.OfType<Hatch>());
        Assert.Equal(4, session.Document.Entities.OfType<Line>().Count());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Single(session.Document.Entities.OfType<Hatch>());
        Assert.Empty(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Align_OnePair_TranslatesGeometry_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 1,1 3,1", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var align = await registry.ExecuteAsync($"ALIGN 1,1 10,10 {line.Handle:X}", session);
        Assert.True(align.Success);
        Assert.Equal(10.0, line.StartPoint.X, 6);
        Assert.Equal(10.0, line.StartPoint.Y, 6);
        Assert.Equal(12.0, line.EndPoint.X, 6);
        Assert.Equal(10.0, line.EndPoint.Y, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(1.0, line.StartPoint.X, 6);
        Assert.Equal(1.0, line.StartPoint.Y, 6);
        Assert.Equal(3.0, line.EndPoint.X, 6);
        Assert.Equal(1.0, line.EndPoint.Y, 6);
    }

    [Fact]
    public async Task Align_TwoPair_RotatesAndTranslates()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 1,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var align = await registry.ExecuteAsync($"ALIGN 0,0 5,5 1,0 5,6 {line.Handle:X}", session);
        Assert.True(align.Success);
        Assert.Equal(5.0, line.StartPoint.X, 6);
        Assert.Equal(5.0, line.StartPoint.Y, 6);
        Assert.Equal(5.0, line.EndPoint.X, 6);
        Assert.Equal(6.0, line.EndPoint.Y, 6);
    }

    [Fact]
    public async Task MatchProp_TransfersEntityProperties_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 1,0", session);
        await registry.ExecuteAsync("LINE 2,0 3,0", session);
        var lines = session.Document.Entities.OfType<Line>().ToArray();
        var source = lines[0];
        var target = lines[1];

        var sourceLayer = new ACadSharp.Tables.Layer("L_SRC");
        session.Document.Layers.Add(sourceLayer);
        var sourceLineType = new ACadSharp.Tables.LineType("LT_SRC");
        session.Document.LineTypes.Add(sourceLineType);

        var targetLayer = new ACadSharp.Tables.Layer("L_TRG");
        session.Document.Layers.Add(targetLayer);
        var targetLineType = new ACadSharp.Tables.LineType("LT_TRG");
        session.Document.LineTypes.Add(targetLineType);

        source.Layer = sourceLayer;
        source.LineType = sourceLineType;
        source.Color = new ACadSharp.Color((short)1);
        source.LineWeight = ACadSharp.LineWeightType.W50;
        source.LineTypeScale = 2.5;
        source.IsInvisible = true;
        source.Transparency = new ACadSharp.Transparency(60);

        target.Layer = targetLayer;
        target.LineType = targetLineType;
        target.Color = new ACadSharp.Color((short)3);
        target.LineWeight = ACadSharp.LineWeightType.W9;
        target.LineTypeScale = 0.5;
        target.IsInvisible = false;
        target.Transparency = new ACadSharp.Transparency(20);

        var match = await registry.ExecuteAsync($"MATCHPROP {source.Handle:X} {target.Handle:X}", session);
        Assert.True(match.Success);
        Assert.Equal(source.Layer.Name, target.Layer.Name);
        Assert.Equal(source.LineType.Name, target.LineType.Name);
        Assert.Equal(source.Color.Index, target.Color.Index);
        Assert.Equal(source.LineWeight, target.LineWeight);
        Assert.Equal(source.LineTypeScale, target.LineTypeScale);
        Assert.Equal(source.IsInvisible, target.IsInvisible);
        Assert.Equal(source.Transparency.Value, target.Transparency.Value);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(targetLayer.Name, target.Layer.Name);
        Assert.Equal(targetLineType.Name, target.LineType.Name);
        Assert.Equal((short)3, target.Color.Index);
        Assert.Equal(ACadSharp.LineWeightType.W9, target.LineWeight);
        Assert.Equal(0.5, target.LineTypeScale, 6);
        Assert.False(target.IsInvisible);
        Assert.Equal((short)20, target.Transparency.Value);
    }

    [Fact]
    public async Task MatchProp_UsesSelectionFallbackForTargets()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 1,0", session);
        await registry.ExecuteAsync("LINE 2,0 3,0", session);
        var lines = session.Document.Entities.OfType<Line>().ToArray();
        var source = lines[0];
        var target = lines[1];

        source.Color = new ACadSharp.Color((short)2);
        target.Color = new ACadSharp.Color((short)5);
        session.SetSelection([target], CadSelectionMode.Replace);

        var match = await registry.ExecuteAsync($"MATCHPROP {source.Handle:X}", session);
        Assert.True(match.Success);
        Assert.Equal(source.Color.Index, target.Color.Index);
    }

    [Fact]
    public async Task Mirror_Line_AcrossYAxis_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 1,0 2,0", session);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var mirror = await registry.ExecuteAsync($"MIRROR 0,0 0,1 {line.Handle:X}", session);
        Assert.True(mirror.Success);
        Assert.Equal(-1.0, line.StartPoint.X, 6);
        Assert.Equal(0.0, line.StartPoint.Y, 6);
        Assert.Equal(-2.0, line.EndPoint.X, 6);
        Assert.Equal(0.0, line.EndPoint.Y, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(1.0, line.StartPoint.X, 6);
        Assert.Equal(0.0, line.StartPoint.Y, 6);
        Assert.Equal(2.0, line.EndPoint.X, 6);
        Assert.Equal(0.0, line.EndPoint.Y, 6);
    }

    [Fact]
    public async Task Mirror_Ray_AcrossXAxis_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RAY 1,1 2,2", session);

        var ray = Assert.Single(session.Document.Entities.OfType<Ray>());
        var mirror = await registry.ExecuteAsync($"MIRROR 0,0 1,0 {ray.Handle:X}", session);
        Assert.True(mirror.Success);
        Assert.Equal(1.0, ray.StartPoint.X, 6);
        Assert.Equal(-1.0, ray.StartPoint.Y, 6);
        Assert.Equal(Math.Sqrt(0.5), ray.Direction.X, 6);
        Assert.Equal(-Math.Sqrt(0.5), ray.Direction.Y, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(1.0, ray.StartPoint.X, 6);
        Assert.Equal(1.0, ray.StartPoint.Y, 6);
        Assert.Equal(Math.Sqrt(0.5), ray.Direction.X, 6);
        Assert.Equal(Math.Sqrt(0.5), ray.Direction.Y, 6);
    }

    [Fact]
    public async Task Polygon_CreateInscribed_AndUndoRedo_Works()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("POLYGON 4 0,0 2", session);
        Assert.True(create.Success);

        var polygon = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.True(polygon.IsClosed);
        Assert.Equal(4, polygon.Vertices.Count);
        Assert.Equal(2.0, polygon.Vertices[0].Location.X, 6);
        Assert.Equal(0.0, polygon.Vertices[0].Location.Y, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Empty(session.Document.Entities.OfType<LwPolyline>());

        var redo = await registry.ExecuteAsync("REDO", session);
        Assert.True(redo.Success);
        Assert.Single(session.Document.Entities.OfType<LwPolyline>());
    }

    [Fact]
    public async Task Polygon_CreateCircumscribed_AdjustsVertexRadius()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("POLYGON 6 0,0 1 CIRCUMSCRIBED", session);
        Assert.True(create.Success);

        var polygon = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var first = polygon.Vertices[0].Location;
        Assert.Equal(2.0 / Math.Sqrt(3.0), first.X, 6);
        Assert.Equal(0.0, first.Y, 6);
    }

    [Fact]
    public async Task CopyClip_ThenPasteClip_WithInsertionPoint_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 1,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var copyClip = await registry.ExecuteAsync($"COPYCLIP {line.Handle:X}", session);
        Assert.True(copyClip.Success);

        var paste = await registry.ExecuteAsync("PASTECLIP 10,0", session);
        Assert.True(paste.Success);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, candidate => Math.Abs(candidate.StartPoint.X - 10.0) < 1e-6 &&
                                            Math.Abs(candidate.EndPoint.X - 11.0) < 1e-6);
    }

    [Fact]
    public async Task Cut_ThenUndo_RestoresEntity()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("POINT 1,2", session);
        var point = Assert.Single(session.Document.Entities.OfType<Point>());

        var cut = await registry.ExecuteAsync($"CUT {point.Handle:X}", session);
        Assert.True(cut.Success);
        Assert.Empty(session.Document.Entities.OfType<Point>());

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        var restored = Assert.Single(session.Document.Entities.OfType<Point>());
        Assert.Equal(1.0, restored.Location.X, 6);
        Assert.Equal(2.0, restored.Location.Y, 6);
    }

    [Fact]
    public async Task PasteClip_WithoutClipboard_Fails()
    {
        var (session, registry) = CreateHarness();
        var paste = await registry.ExecuteAsync("PASTECLIP", session);
        Assert.False(paste.Success);
        Assert.Contains("Clipboard is empty", paste.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasteOrig_PastesAtClipboardBasePoint()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 1,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var copyClip = await registry.ExecuteAsync($"COPYCLIP {line.Handle:X}", session);
        Assert.True(copyClip.Success);

        var move = await registry.ExecuteAsync($"MOVE 5,0 {line.Handle:X}", session);
        Assert.True(move.Success);

        var pasteOrig = await registry.ExecuteAsync("PASTEORIG", session);
        Assert.True(pasteOrig.Success);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, candidate => Math.Abs(candidate.StartPoint.X - 0.0) < 1e-6 &&
                                            Math.Abs(candidate.EndPoint.X - 1.0) < 1e-6);
        Assert.Contains(lines, candidate => Math.Abs(candidate.StartPoint.X - 5.0) < 1e-6 &&
                                            Math.Abs(candidate.EndPoint.X - 6.0) < 1e-6);
    }

    [Fact]
    public async Task Offset_Line_DefaultLeft_CreatesParallelAndUndoWorks()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var offset = await registry.ExecuteAsync($"OFFSET 2 {line.Handle:X}", session);
        Assert.True(offset.Success);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, candidate => Math.Abs(candidate.StartPoint.Y - 2.0) < 1e-6 &&
                                            Math.Abs(candidate.EndPoint.Y - 2.0) < 1e-6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Offset_Circle_InnerAndOuter_Work()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("CIRCLE 0,0 5", session);
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());

        var inner = await registry.ExecuteAsync($"OFFSET 2 INNER {circle.Handle:X}", session);
        Assert.True(inner.Success);
        Assert.Contains(session.Document.Entities.OfType<Circle>(), candidate => Math.Abs(candidate.Radius - 3.0) < 1e-6);

        var outer = await registry.ExecuteAsync($"OFFSET 1 OUTER {circle.Handle:X}", session);
        Assert.True(outer.Success);
        Assert.Contains(session.Document.Entities.OfType<Circle>(), candidate => Math.Abs(candidate.Radius - 6.0) < 1e-6);
    }

    [Fact]
    public async Task Offset_OpenPolyline_Left_CreatesTranslatedPath()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("PLINE 0,0 10,0 10,10", session);
        Assert.True(create.Success);

        var source = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var offset = await registry.ExecuteAsync($"OFFSET 1 LEFT {source.Handle:X}", session);
        Assert.True(offset.Success);

        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);
        var created = polylines.First(poly => !ReferenceEquals(poly, source));
        Assert.False(created.IsClosed);
        Assert.Equal(3, created.Vertices.Count);
        Assert.Equal(0.0, created.Vertices[0].Location.X, 3);
        Assert.Equal(1.0, created.Vertices[0].Location.Y, 3);
        Assert.Equal(9.0, created.Vertices[1].Location.X, 3);
        Assert.Equal(1.0, created.Vertices[1].Location.Y, 3);
        Assert.Equal(9.0, created.Vertices[2].Location.X, 3);
        Assert.Equal(10.0, created.Vertices[2].Location.Y, 3);
    }

    [Fact]
    public async Task Offset_ClosedPolyline_OuterAndInner_Work()
    {
        var (session, registry) = CreateHarness();
        var create = await registry.ExecuteAsync("RECTANG 0,0 10,10", session);
        Assert.True(create.Success);

        var source = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var outer = await registry.ExecuteAsync($"OFFSET 1 OUTER {source.Handle:X}", session);
        Assert.True(outer.Success);

        var inner = await registry.ExecuteAsync($"OFFSET 1 INNER {source.Handle:X}", session);
        Assert.True(inner.Success);

        var candidates = session.Document.Entities.OfType<LwPolyline>().Where(poly => !ReferenceEquals(poly, source)).ToArray();
        Assert.Equal(2, candidates.Length);

        static (double MinX, double MinY, double MaxX, double MaxY) Bounds(LwPolyline polyline)
        {
            var xs = polyline.Vertices.Select(v => v.Location.X).ToArray();
            var ys = polyline.Vertices.Select(v => v.Location.Y).ToArray();
            return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
        }

        var outerBounds = candidates.Select(Bounds).First(b => b.MinX < -0.5 || b.MinY < -0.5);
        var innerBounds = candidates.Select(Bounds).First(b => b.MinX > 0.5 && b.MinY > 0.5);

        Assert.InRange(outerBounds.MinX, -1.05, -0.95);
        Assert.InRange(outerBounds.MinY, -1.05, -0.95);
        Assert.InRange(outerBounds.MaxX, 10.95, 11.05);
        Assert.InRange(outerBounds.MaxY, 10.95, 11.05);

        Assert.InRange(innerBounds.MinX, 0.95, 1.05);
        Assert.InRange(innerBounds.MinY, 0.95, 1.05);
        Assert.InRange(innerBounds.MaxX, 8.95, 9.05);
        Assert.InRange(innerBounds.MaxY, 8.95, 9.05);
    }

    [Fact]
    public async Task Offset_XLine_LeftAndRight_CreateParallelInfiniteLines()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("XLINE 0,0 1,0", session);
        var source = Assert.Single(session.Document.Entities.OfType<XLine>());

        var left = await registry.ExecuteAsync($"OFFSET 2 LEFT {source.Handle:X}", session);
        Assert.True(left.Success);

        var right = await registry.ExecuteAsync($"OFFSET 1 RIGHT {source.Handle:X}", session);
        Assert.True(right.Success);

        var xlines = session.Document.Entities.OfType<XLine>().ToArray();
        Assert.Equal(3, xlines.Length);
        Assert.Contains(xlines, candidate => !ReferenceEquals(candidate, source) && Approximately(candidate.FirstPoint.Y, 2.0));
        Assert.Contains(xlines, candidate => !ReferenceEquals(candidate, source) && Approximately(candidate.FirstPoint.Y, -1.0));
    }

    [Fact]
    public async Task Offset_Ray_CreatesParallelRay()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RAY 0,0 1,0", session);
        var source = Assert.Single(session.Document.Entities.OfType<Ray>());

        var offset = await registry.ExecuteAsync($"OFFSET 3 LEFT {source.Handle:X}", session);
        Assert.True(offset.Success);

        var rays = session.Document.Entities.OfType<Ray>().ToArray();
        Assert.Equal(2, rays.Length);
        Assert.Contains(rays, candidate => !ReferenceEquals(candidate, source) && Approximately(candidate.StartPoint.Y, 3.0));
    }

    [Fact]
    public async Task Offset_Ellipse_InnerAndOuter_Work()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("ELLIPSE 0,0 4,0 0.5", session);
        var source = Assert.Single(session.Document.Entities.OfType<Ellipse>());
        var sourceMajor = Math.Sqrt(
            source.MajorAxisEndPoint.X * source.MajorAxisEndPoint.X +
            source.MajorAxisEndPoint.Y * source.MajorAxisEndPoint.Y +
            source.MajorAxisEndPoint.Z * source.MajorAxisEndPoint.Z);

        var outer = await registry.ExecuteAsync($"OFFSET 1 OUTER {source.Handle:X}", session);
        Assert.True(outer.Success);

        var inner = await registry.ExecuteAsync($"OFFSET 0.5 INNER {source.Handle:X}", session);
        Assert.True(inner.Success);

        var ellipses = session.Document.Entities.OfType<Ellipse>().ToArray();
        Assert.Equal(3, ellipses.Length);

        static double MajorLength(Ellipse ellipse)
        {
            return Math.Sqrt(
                ellipse.MajorAxisEndPoint.X * ellipse.MajorAxisEndPoint.X +
                ellipse.MajorAxisEndPoint.Y * ellipse.MajorAxisEndPoint.Y +
                ellipse.MajorAxisEndPoint.Z * ellipse.MajorAxisEndPoint.Z);
        }

        Assert.Contains(ellipses, candidate => !ReferenceEquals(candidate, source) && MajorLength(candidate) > sourceMajor);
        Assert.Contains(ellipses, candidate => !ReferenceEquals(candidate, source) && MajorLength(candidate) < sourceMajor);
    }

    [Fact]
    public async Task Trim_LineByLine_End_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        await registry.ExecuteAsync("LINE 6,-5 6,5", session);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        var target = lines.Single(line => Math.Abs(line.StartPoint.Y) < 1e-6 && Math.Abs(line.EndPoint.Y) < 1e-6);
        var boundary = lines.Single(line => Math.Abs(line.StartPoint.X - 6.0) < 1e-6 && Math.Abs(line.EndPoint.X - 6.0) < 1e-6);

        var trim = await registry.ExecuteAsync($"TRIM {boundary.Handle:X} {target.Handle:X} END", session);
        Assert.True(trim.Success);
        Assert.Equal(0.0, target.StartPoint.X, 6);
        Assert.Equal(6.0, target.EndPoint.X, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(10.0, target.EndPoint.X, 6);
    }

    [Fact]
    public async Task Extend_LineByLine_End_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 4,0", session);
        await registry.ExecuteAsync("LINE 8,-5 8,5", session);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        var target = lines.Single(line => Math.Abs(line.StartPoint.Y) < 1e-6 && Math.Abs(line.EndPoint.Y) < 1e-6);
        var boundary = lines.Single(line => Math.Abs(line.StartPoint.X - 8.0) < 1e-6 && Math.Abs(line.EndPoint.X - 8.0) < 1e-6);

        var extend = await registry.ExecuteAsync($"EXTEND {boundary.Handle:X} {target.Handle:X} END", session);
        Assert.True(extend.Success);
        Assert.Equal(8.0, target.EndPoint.X, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(4.0, target.EndPoint.X, 6);
    }

    [Fact]
    public async Task Trim_LineByXLine_End_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        await registry.ExecuteAsync("XLINE 6,-2 6,3", session);

        var target = Assert.Single(session.Document.Entities.OfType<Line>());
        var boundary = Assert.Single(session.Document.Entities.OfType<XLine>());

        var trim = await registry.ExecuteAsync($"TRIM {boundary.Handle:X} {target.Handle:X} END", session);
        Assert.True(trim.Success);
        Assert.Equal(0.0, target.StartPoint.X, 6);
        Assert.Equal(6.0, target.EndPoint.X, 6);
    }

    [Fact]
    public async Task Extend_LineByRay_End_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 4,0", session);
        await registry.ExecuteAsync("RAY 8,-2 8,3", session);

        var target = Assert.Single(session.Document.Entities.OfType<Line>());
        var boundary = Assert.Single(session.Document.Entities.OfType<Ray>());

        var extend = await registry.ExecuteAsync($"EXTEND {boundary.Handle:X} {target.Handle:X} END", session);
        Assert.True(extend.Success);
        Assert.Equal(8.0, target.EndPoint.X, 6);
    }

    [Fact]
    public async Task Trim_OpenPolyline_End_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,5", session);
        await registry.ExecuteAsync("LINE -5,2 15,2", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var boundary = Assert.Single(session.Document.Entities.OfType<Line>());

        var trim = await registry.ExecuteAsync($"TRIM {boundary.Handle:X} {polyline.Handle:X} END", session);
        Assert.True(trim.Success, trim.Message);

        Assert.Equal(3, polyline.Vertices.Count);
        Assert.Equal(10.0, polyline.Vertices[1].Location.X, 6);
        Assert.Equal(0.0, polyline.Vertices[1].Location.Y, 6);
        Assert.Equal(10.0, polyline.Vertices[2].Location.X, 6);
        Assert.Equal(2.0, polyline.Vertices[2].Location.Y, 6);
    }

    [Fact]
    public async Task Extend_OpenPolyline_Start_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 4,0 10,0 10,5", session);
        await registry.ExecuteAsync("LINE 0,-5 0,5", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var boundary = Assert.Single(session.Document.Entities.OfType<Line>());

        var extend = await registry.ExecuteAsync($"EXTEND {boundary.Handle:X} {polyline.Handle:X} START", session);
        Assert.True(extend.Success, extend.Message);

        Assert.Equal(3, polyline.Vertices.Count);
        Assert.Equal(0.0, polyline.Vertices[0].Location.X, 6);
        Assert.Equal(0.0, polyline.Vertices[0].Location.Y, 6);
        Assert.Equal(10.0, polyline.Vertices[1].Location.X, 6);
        Assert.Equal(0.0, polyline.Vertices[1].Location.Y, 6);
    }

    [Fact]
    public async Task Trim_OpenPolylineWithArcSegment_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,5", session);
        await registry.ExecuteAsync("LINE -5,2 15,2", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].Bulge = 0.5;
        var boundary = Assert.Single(session.Document.Entities.OfType<Line>());

        var trim = await registry.ExecuteAsync($"TRIM {boundary.Handle:X} {polyline.Handle:X} END", session);
        Assert.False(trim.Success);
        Assert.Contains("arc segments", trim.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.5, polyline.Vertices[0].Bulge, 6);
        Assert.Equal(3, polyline.Vertices.Count);
    }

    [Fact]
    public async Task Trim_OpenPolylineWithVariableWidth_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,5", session);
        await registry.ExecuteAsync("LINE -5,2 15,2", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].StartWidth = 0.25;
        polyline.Vertices[0].EndWidth = 0.5;
        var boundary = Assert.Single(session.Document.Entities.OfType<Line>());

        var trim = await registry.ExecuteAsync($"TRIM {boundary.Handle:X} {polyline.Handle:X} END", session);
        Assert.False(trim.Success);
        Assert.Contains("variable-width", trim.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.25, polyline.Vertices[0].StartWidth, 6);
        Assert.Equal(0.5, polyline.Vertices[0].EndWidth, 6);
    }

    [Fact]
    public async Task Extend_OpenPolylineWithNonWorldNormal_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 4,0 10,0 10,5", session);
        await registry.ExecuteAsync("LINE 0,-5 0,5", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Normal = new XYZ(0, 0, -1);
        var boundary = Assert.Single(session.Document.Entities.OfType<Line>());

        var extend = await registry.ExecuteAsync($"EXTEND {boundary.Handle:X} {polyline.Handle:X} START", session);
        Assert.False(extend.Success);
        Assert.Contains("world-xy", extend.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(-1.0, polyline.Normal.Z, 6);
    }

    [Fact]
    public async Task Trim_ClosedPolylineTarget_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RECTANG 0,0 10,10", session);
        await registry.ExecuteAsync("LINE -5,2 15,2", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var boundary = Assert.Single(session.Document.Entities.OfType<Line>());

        var trim = await registry.ExecuteAsync($"TRIM {boundary.Handle:X} {polyline.Handle:X} END", session);
        Assert.False(trim.Success);
    }

    [Fact]
    public async Task Extend_OpenPolylineWithArcSegment_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 4,0 10,0 10,5", session);
        await registry.ExecuteAsync("LINE 0,-5 0,5", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].Bulge = 0.5;
        var boundary = Assert.Single(session.Document.Entities.OfType<Line>());

        var extend = await registry.ExecuteAsync($"EXTEND {boundary.Handle:X} {polyline.Handle:X} START", session);
        Assert.False(extend.Success);
        Assert.Contains("arc segments", extend.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.5, polyline.Vertices[0].Bulge, 6);
        Assert.Equal(3, polyline.Vertices.Count);
    }

    [Fact]
    public async Task Trim_LineByEllipse_End_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 20,0", session);
        await registry.ExecuteAsync("ELLIPSE 10,0 14,0 0.5", session);

        var target = Assert.Single(session.Document.Entities.OfType<Line>());
        var boundary = Assert.Single(session.Document.Entities.OfType<Ellipse>());

        var trim = await registry.ExecuteAsync($"TRIM {boundary.Handle:X} {target.Handle:X} END", session);
        Assert.True(trim.Success, trim.Message);
        Assert.InRange(target.EndPoint.X, 13.7, 14.3);
    }

    [Fact]
    public async Task Extend_LineByEllipse_End_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 5,0", session);
        await registry.ExecuteAsync("ELLIPSE 10,0 14,0 0.5", session);

        var target = Assert.Single(session.Document.Entities.OfType<Line>());
        var boundary = Assert.Single(session.Document.Entities.OfType<Ellipse>());

        var extend = await registry.ExecuteAsync($"EXTEND {boundary.Handle:X} {target.Handle:X} END", session);
        Assert.True(extend.Success, extend.Message);
        Assert.InRange(target.EndPoint.X, 5.7, 6.3);
    }

    [Fact]
    public async Task TrimExtend_LineByHatchBoundary_Work()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        await registry.ExecuteAsync("RECTANG 6,-1 8,1", session);
        var rect = session.Document.Entities.OfType<LwPolyline>().Last();
        await registry.ExecuteAsync($"HATCH {rect.Handle:X}", session);
        var hatch = Assert.Single(session.Document.Entities.OfType<Hatch>());
        var target = Assert.Single(session.Document.Entities.OfType<Line>());

        var trim = await registry.ExecuteAsync($"TRIM {hatch.Handle:X} {target.Handle:X} END", session);
        Assert.True(trim.Success, trim.Message);
        Assert.InRange(target.EndPoint.X, 7.7, 8.3);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.True(Approximately(target.EndPoint.X, 10.0));

        await registry.ExecuteAsync("LINE 0,0 5,0", session);
        var shortLine = session.Document.Entities.OfType<Line>().Single(candidate => Approximately(candidate.EndPoint.X, 5.0));

        var extend = await registry.ExecuteAsync($"EXTEND {hatch.Handle:X} {shortLine.Handle:X} END", session);
        Assert.True(extend.Success, extend.Message);
        Assert.InRange(shortLine.EndPoint.X, 5.7, 6.3);
    }

    [Fact]
    public async Task TrimAndExtend_LineByCircle_End_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE -10,0 10,0", session);
        await registry.ExecuteAsync("CIRCLE 0,0 5", session);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());

        var trim = await registry.ExecuteAsync($"TRIM {circle.Handle:X} {line.Handle:X} END", session);
        Assert.True(trim.Success);
        Assert.Equal(5.0, line.EndPoint.X, 6);

        var undoTrim = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undoTrim.Success);

        await registry.ExecuteAsync("LINE 0,0 2,0", session);
        var shortLine = session.Document.Entities.OfType<Line>().Single(candidate => Math.Abs(candidate.EndPoint.X - 2.0) < 1e-6);
        var extend = await registry.ExecuteAsync($"EXTEND {circle.Handle:X} {shortLine.Handle:X} END", session);
        Assert.True(extend.Success);
        Assert.Equal(5.0, shortLine.EndPoint.X, 6);
    }

    [Fact]
    public async Task Break_LineAtPoint_SplitsIntoTwo_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var brk = await registry.ExecuteAsync($"BREAK {line.Handle:X} 5,0", session);
        Assert.True(brk.Success);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, candidate => Math.Abs(candidate.StartPoint.X - 0.0) < 1e-6 &&
                                            Math.Abs(candidate.EndPoint.X - 5.0) < 1e-6);
        Assert.Contains(lines, candidate => Math.Abs(candidate.StartPoint.X - 5.0) < 1e-6 &&
                                            Math.Abs(candidate.EndPoint.X - 10.0) < 1e-6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        var restored = Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.Equal(0.0, restored.StartPoint.X, 6);
        Assert.Equal(10.0, restored.EndPoint.X, 6);
    }

    [Fact]
    public async Task Break_LineBetweenTwoPoints_RemovesMiddleSegment_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var brk = await registry.ExecuteAsync($"BREAK {line.Handle:X} 7,0 3,0", session);
        Assert.True(brk.Success, brk.Message);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Contains(
            lines,
            candidate => Approximately(candidate.StartPoint.X, 0.0) &&
                         Approximately(candidate.StartPoint.Y, 0.0) &&
                         Approximately(candidate.EndPoint.X, 3.0) &&
                         Approximately(candidate.EndPoint.Y, 0.0));
        Assert.Contains(
            lines,
            candidate => Approximately(candidate.StartPoint.X, 7.0) &&
                         Approximately(candidate.StartPoint.Y, 0.0) &&
                         Approximately(candidate.EndPoint.X, 10.0) &&
                         Approximately(candidate.EndPoint.Y, 0.0));

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success, undo.Message);
        var restored = Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.Equal(0.0, restored.StartPoint.X, 6);
        Assert.Equal(10.0, restored.EndPoint.X, 6);
    }

    [Fact]
    public async Task Break_LineBetweenEndpointAndPoint_KeepsSingleSegment()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var brk = await registry.ExecuteAsync($"BREAK {line.Handle:X} 0,0 6,0", session);
        Assert.True(brk.Success, brk.Message);

        var remaining = Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.Equal(6.0, remaining.StartPoint.X, 6);
        Assert.Equal(10.0, remaining.EndPoint.X, 6);
    }

    [Fact]
    public async Task Break_LineWithElevation_Using2DPoint_PreservesElevation()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0,5 10,0,5", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var brk = await registry.ExecuteAsync($"BREAK {line.Handle:X} 5,0", session);
        Assert.True(brk.Success, brk.Message);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, lines.Length);
        Assert.All(
            lines,
            candidate =>
            {
                Assert.Equal(5.0, candidate.StartPoint.Z, 6);
                Assert.Equal(5.0, candidate.EndPoint.Z, 6);
            });
    }

    [Fact]
    public async Task Break_LinePointOffLine_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var brk = await registry.ExecuteAsync($"BREAK {line.Handle:X} 5,2", session);
        Assert.False(brk.Success);
        var remaining = Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.Equal(0.0, remaining.StartPoint.X, 6);
        Assert.Equal(10.0, remaining.EndPoint.X, 6);
    }

    [Fact]
    public async Task Break_OpenPolylineAtPoint_SplitsIntoTwo_AndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,10", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var brk = await registry.ExecuteAsync($"BREAK {polyline.Handle:X} 5,0", session);
        Assert.True(brk.Success, brk.Message);

        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);
        Assert.Contains(
            polylines,
            candidate => candidate.Vertices.Count == 2 &&
                         Approximately(candidate.Vertices[0].Location.X, 0.0) &&
                         Approximately(candidate.Vertices[0].Location.Y, 0.0) &&
                         Approximately(candidate.Vertices[1].Location.X, 5.0) &&
                         Approximately(candidate.Vertices[1].Location.Y, 0.0));
        Assert.Contains(
            polylines,
            candidate => candidate.Vertices.Count == 3 &&
                         Approximately(candidate.Vertices[0].Location.X, 5.0) &&
                         Approximately(candidate.Vertices[0].Location.Y, 0.0) &&
                         Approximately(candidate.Vertices[1].Location.X, 10.0) &&
                         Approximately(candidate.Vertices[1].Location.Y, 0.0) &&
                         Approximately(candidate.Vertices[2].Location.X, 10.0) &&
                         Approximately(candidate.Vertices[2].Location.Y, 10.0));

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        var restored = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.Equal(3, restored.Vertices.Count);
        Assert.Equal(0.0, restored.Vertices[0].Location.X, 6);
        Assert.Equal(0.0, restored.Vertices[0].Location.Y, 6);
        Assert.Equal(10.0, restored.Vertices[1].Location.X, 6);
        Assert.Equal(0.0, restored.Vertices[1].Location.Y, 6);
        Assert.Equal(10.0, restored.Vertices[2].Location.X, 6);
        Assert.Equal(10.0, restored.Vertices[2].Location.Y, 6);
    }

    [Fact]
    public async Task Break_OpenPolylineWithElevation_Using2DPoint_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0,5 10,0,5 10,10,5", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var brk = await registry.ExecuteAsync($"BREAK {polyline.Handle:X} 5,0", session);
        Assert.True(brk.Success, brk.Message);

        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);
        Assert.All(polylines, candidate => Assert.Equal(5.0, candidate.Elevation, 6));
        Assert.Contains(
            polylines,
            candidate => candidate.Vertices.Count == 2 &&
                         Approximately(candidate.Vertices[1].Location.X, 5.0) &&
                         Approximately(candidate.Vertices[1].Location.Y, 0.0));
    }

    [Fact]
    public async Task Break_OpenPolylineAtEndpoint_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,10", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var brk = await registry.ExecuteAsync($"BREAK {polyline.Handle:X} 0,0", session);
        Assert.False(brk.Success);
        Assert.Single(session.Document.Entities.OfType<LwPolyline>());
    }

    [Fact]
    public async Task Break_OpenPolylineWithArcSegment_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,10", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].Bulge = 0.5;

        var brk = await registry.ExecuteAsync($"BREAK {polyline.Handle:X} 5,0", session);
        Assert.False(brk.Success);
        Assert.Contains("arc segments", brk.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.5, polyline.Vertices[0].Bulge, 6);
        Assert.Single(session.Document.Entities.OfType<LwPolyline>());
    }

    [Fact]
    public async Task Break_OpenPolylineWithVariableWidth_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,10", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].StartWidth = 0.25;
        polyline.Vertices[0].EndWidth = 0.5;

        var brk = await registry.ExecuteAsync($"BREAK {polyline.Handle:X} 5,0", session);
        Assert.False(brk.Success);
        Assert.Contains("variable-width", brk.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.25, polyline.Vertices[0].StartWidth, 6);
        Assert.Equal(0.5, polyline.Vertices[0].EndWidth, 6);
        Assert.Single(session.Document.Entities.OfType<LwPolyline>());
    }

    [Fact]
    public async Task Break_OpenPolylineWithNonWorldNormal_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,10", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Normal = new XYZ(0.0, 0.0, -1.0);

        var brk = await registry.ExecuteAsync($"BREAK {polyline.Handle:X} 5,0", session);
        Assert.False(brk.Success);
        Assert.Contains("world-xy", brk.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(-1.0, polyline.Normal.Z, 6);
        Assert.Single(session.Document.Entities.OfType<LwPolyline>());
    }

    [Fact]
    public async Task Break_OpenPolylineBetweenTwoPoints_RemovesMiddleSegment()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,10", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var brk = await registry.ExecuteAsync($"BREAK {polyline.Handle:X} 2,0 7,0", session);
        Assert.True(brk.Success, brk.Message);

        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);
        Assert.Contains(
            polylines,
            candidate => candidate.Vertices.Count == 2 &&
                         Approximately(candidate.Vertices[0].Location.X, 0.0) &&
                         Approximately(candidate.Vertices[0].Location.Y, 0.0) &&
                         Approximately(candidate.Vertices[1].Location.X, 2.0) &&
                         Approximately(candidate.Vertices[1].Location.Y, 0.0));
        Assert.Contains(
            polylines,
            candidate => candidate.Vertices.Count == 3 &&
                         Approximately(candidate.Vertices[0].Location.X, 7.0) &&
                         Approximately(candidate.Vertices[0].Location.Y, 0.0) &&
                         Approximately(candidate.Vertices[1].Location.X, 10.0) &&
                         Approximately(candidate.Vertices[1].Location.Y, 0.0) &&
                         Approximately(candidate.Vertices[2].Location.X, 10.0) &&
                         Approximately(candidate.Vertices[2].Location.Y, 10.0));
    }

    [Fact]
    public async Task Break_OpenPolylineBetweenEndpointPoints_DeletesPolyline()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,10", session);
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var brk = await registry.ExecuteAsync($"BREAK {polyline.Handle:X} 0,0 10,10", session);
        Assert.True(brk.Success, brk.Message);
        Assert.Empty(session.Document.Entities.OfType<LwPolyline>());
    }

    [Fact]
    public async Task Join_TwoCollinearLines_MergesAndUndoRestores()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 5,0", session);
        await registry.ExecuteAsync("LINE 5,0 10,0", session);

        var linesBefore = session.Document.Entities.OfType<Line>().ToArray();
        var l1 = linesBefore.Single(line => Math.Abs(line.StartPoint.X - 0.0) < 1e-6 || Math.Abs(line.EndPoint.X - 0.0) < 1e-6);
        var l2 = linesBefore.Single(line => !ReferenceEquals(line, l1));

        var join = await registry.ExecuteAsync($"JOIN {l1.Handle:X} {l2.Handle:X}", session);
        Assert.True(join.Success);

        var merged = Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.True(
            (Math.Abs(merged.StartPoint.X - 0.0) < 1e-6 && Math.Abs(merged.EndPoint.X - 10.0) < 1e-6) ||
            (Math.Abs(merged.StartPoint.X - 10.0) < 1e-6 && Math.Abs(merged.EndPoint.X - 0.0) < 1e-6));

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(2, session.Document.Entities.OfType<Line>().Count());
    }

    [Fact]
    public async Task Join_SeparateCommands_DoNotMergeUndoUnits()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 5,0", session);
        await registry.ExecuteAsync("LINE 5,0 10,0", session);
        await registry.ExecuteAsync("LINE 20,0 25,0", session);
        await registry.ExecuteAsync("LINE 25,0 30,0", session);

        var lines = session.Document.Entities
            .OfType<Line>()
            .OrderBy(static line => Math.Min(line.StartPoint.X, line.EndPoint.X))
            .ToArray();
        Assert.Equal(4, lines.Length);

        var firstJoin = await registry.ExecuteAsync(
            $"JOIN {lines[0].Handle:X} {lines[1].Handle:X}",
            session);
        Assert.True(firstJoin.Success, firstJoin.Message);

        var secondJoin = await registry.ExecuteAsync(
            $"JOIN {lines[2].Handle:X} {lines[3].Handle:X}",
            session);
        Assert.True(secondJoin.Success, secondJoin.Message);
        Assert.Equal(2, session.Document.Entities.OfType<Line>().Count());

        var undoSecondJoin = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undoSecondJoin.Success, undoSecondJoin.Message);
        Assert.Equal(3, session.Document.Entities.OfType<Line>().Count());

        var undoFirstJoin = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undoFirstJoin.Success, undoFirstJoin.Message);
        Assert.Equal(4, session.Document.Entities.OfType<Line>().Count());
    }

    [Fact]
    public async Task Join_ThreeCollinearLines_MergesAllAndUndoRestoresSingleStep()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 5,0", session);
        await registry.ExecuteAsync("LINE 5,0 10,0", session);
        await registry.ExecuteAsync("LINE 10,0 15,0", session);

        var linesBefore = session.Document.Entities.OfType<Line>().OrderBy(static line => Math.Min(line.StartPoint.X, line.EndPoint.X)).ToArray();
        Assert.Equal(3, linesBefore.Length);

        var join = await registry.ExecuteAsync(
            $"JOIN {linesBefore[0].Handle:X} {linesBefore[1].Handle:X} {linesBefore[2].Handle:X}",
            session);
        Assert.True(join.Success, join.Message);

        var merged = Assert.Single(session.Document.Entities.OfType<Line>());
        Assert.True(
            (Math.Abs(merged.StartPoint.X - 0.0) < 1e-6 && Math.Abs(merged.EndPoint.X - 15.0) < 1e-6) ||
            (Math.Abs(merged.StartPoint.X - 15.0) < 1e-6 && Math.Abs(merged.EndPoint.X - 0.0) < 1e-6));

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success, undo.Message);
        Assert.Equal(3, session.Document.Entities.OfType<Line>().Count());
    }

    [Fact]
    public async Task Join_MultiTargetWithIncompatibleEntity_JoinsCompatibleAndSkipsRemaining()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 5,0", session);
        await registry.ExecuteAsync("LINE 5,0 10,0", session);
        await registry.ExecuteAsync("LINE 100,0 110,0", session);

        var linesBefore = session.Document.Entities.OfType<Line>().OrderBy(static line => Math.Min(line.StartPoint.X, line.EndPoint.X)).ToArray();
        Assert.Equal(3, linesBefore.Length);

        var join = await registry.ExecuteAsync(
            $"JOIN {linesBefore[0].Handle:X} {linesBefore[1].Handle:X} {linesBefore[2].Handle:X}",
            session);
        Assert.True(join.Success, join.Message);
        Assert.Contains("skipped", join.Message, StringComparison.OrdinalIgnoreCase);

        var linesAfter = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, linesAfter.Length);
        Assert.Contains(
            linesAfter,
            line =>
                (Math.Abs(line.StartPoint.X - 0.0) < 1e-6 && Math.Abs(line.EndPoint.X - 10.0) < 1e-6) ||
                (Math.Abs(line.StartPoint.X - 10.0) < 1e-6 && Math.Abs(line.EndPoint.X - 0.0) < 1e-6));
        Assert.Contains(
            linesAfter,
            line =>
                (Math.Abs(line.StartPoint.X - 100.0) < 1e-6 && Math.Abs(line.EndPoint.X - 110.0) < 1e-6) ||
                (Math.Abs(line.StartPoint.X - 110.0) < 1e-6 && Math.Abs(line.EndPoint.X - 100.0) < 1e-6));
    }

    [Fact]
    public async Task Join_MultiTargetWithIsolatedFirstEntity_MergesJoinableRemainder()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 100,0 110,0", session); // Isolated, first in command order.
        await registry.ExecuteAsync("LINE 0,0 5,0", session);
        await registry.ExecuteAsync("LINE 5,0 10,0", session);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(3, lines.Length);

        var isolated = lines.Single(line => Math.Abs(line.StartPoint.X - 100.0) < 1e-6 || Math.Abs(line.EndPoint.X - 100.0) < 1e-6);
        var left = lines.Single(line => Math.Abs(line.StartPoint.X - 0.0) < 1e-6 || Math.Abs(line.EndPoint.X - 0.0) < 1e-6);
        var right = lines.Single(line => !ReferenceEquals(line, isolated) && !ReferenceEquals(line, left));

        var join = await registry.ExecuteAsync(
            $"JOIN {isolated.Handle:X} {left.Handle:X} {right.Handle:X}",
            session);
        Assert.True(join.Success, join.Message);
        Assert.Contains("skipped", join.Message, StringComparison.OrdinalIgnoreCase);

        var linesAfter = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, linesAfter.Length);
        Assert.Contains(
            linesAfter,
            line =>
                (Math.Abs(line.StartPoint.X - 0.0) < 1e-6 && Math.Abs(line.EndPoint.X - 10.0) < 1e-6) ||
                (Math.Abs(line.StartPoint.X - 10.0) < 1e-6 && Math.Abs(line.EndPoint.X - 0.0) < 1e-6));
        Assert.Contains(
            linesAfter,
            line =>
                (Math.Abs(line.StartPoint.X - 100.0) < 1e-6 && Math.Abs(line.EndPoint.X - 110.0) < 1e-6) ||
                (Math.Abs(line.StartPoint.X - 110.0) < 1e-6 && Math.Abs(line.EndPoint.X - 100.0) < 1e-6));
    }

    [Fact]
    public async Task Join_LineAndOpenPolyline_AppendsLineAndRemovesLine()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 5,0 5,5", session);
        await registry.ExecuteAsync("LINE 5,5 10,5", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var join = await registry.ExecuteAsync($"JOIN {polyline.Handle:X} {line.Handle:X}", session);
        Assert.True(join.Success);
        Assert.Empty(session.Document.Entities.OfType<Line>());
        var updated = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.Equal(4, updated.Vertices.Count);
        Assert.Equal(10.0, updated.Vertices[^1].Location.X, 6);
        Assert.Equal(5.0, updated.Vertices[^1].Location.Y, 6);
    }

    [Fact]
    public async Task Join_LineAndArcSegmentPolyline_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 5,0 5,5", session);
        await registry.ExecuteAsync("LINE 5,5 10,5", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].Bulge = 0.5;
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var join = await registry.ExecuteAsync($"JOIN {polyline.Handle:X} {line.Handle:X}", session);
        Assert.False(join.Success);
        Assert.Contains("arc segments", join.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.5, polyline.Vertices[0].Bulge, 6);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Join_LineAndVariableWidthPolyline_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 5,0 5,5", session);
        await registry.ExecuteAsync("LINE 5,5 10,5", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].StartWidth = 0.25;
        polyline.Vertices[0].EndWidth = 0.5;
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var join = await registry.ExecuteAsync($"JOIN {polyline.Handle:X} {line.Handle:X}", session);
        Assert.False(join.Success);
        Assert.Contains("variable-width", join.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.25, polyline.Vertices[0].StartWidth, 6);
        Assert.Equal(0.5, polyline.Vertices[0].EndWidth, 6);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Join_LineAndNonWorldPolyline_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 5,0 5,5", session);
        await registry.ExecuteAsync("LINE 5,5 10,5", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Normal = new XYZ(0.0, 0.0, -1.0);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var join = await registry.ExecuteAsync($"JOIN {polyline.Handle:X} {line.Handle:X}", session);
        Assert.False(join.Success);
        Assert.Contains("world-xy", join.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(-1.0, polyline.Normal.Z, 6);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Join_TwoOpenPolylines_AppendsVerticesAndSupportsUndo()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 5,0 5,5", session);
        await registry.ExecuteAsync("PLINE 5,5 10,5 10,10", session);

        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);

        var join = await registry.ExecuteAsync($"JOIN {polylines[0].Handle:X} {polylines[1].Handle:X}", session);
        Assert.True(join.Success);

        var merged = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.Equal(5, merged.Vertices.Count);
        Assert.Equal(0.0, merged.Vertices[0].Location.X, 6);
        Assert.Equal(0.0, merged.Vertices[0].Location.Y, 6);
        Assert.Equal(10.0, merged.Vertices[^1].Location.X, 6);
        Assert.Equal(10.0, merged.Vertices[^1].Location.Y, 6);

        var undo = await registry.ExecuteAsync("UNDO", session);
        Assert.True(undo.Success);
        Assert.Equal(2, session.Document.Entities.OfType<LwPolyline>().Count());
    }

    [Fact]
    public async Task Join_TwoOpenPolylines_WithSharedStart_ReversesSecondAndMerges()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 3,0", session);
        await registry.ExecuteAsync("PLINE 0,0 -4,0", session);

        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);

        var join = await registry.ExecuteAsync($"JOIN {polylines[0].Handle:X} {polylines[1].Handle:X}", session);
        Assert.True(join.Success);

        var merged = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.Equal(3, merged.Vertices.Count);
        Assert.Equal(-4.0, merged.Vertices[0].Location.X, 6);
        Assert.Equal(0.0, merged.Vertices[0].Location.Y, 6);
        Assert.Equal(0.0, merged.Vertices[1].Location.X, 6);
        Assert.Equal(0.0, merged.Vertices[1].Location.Y, 6);
        Assert.Equal(3.0, merged.Vertices[2].Location.X, 6);
        Assert.Equal(0.0, merged.Vertices[2].Location.Y, 6);
    }

    [Fact]
    public async Task Join_TwoOpenPolylines_WithSharedEnd_ReversesSecondAndMerges()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 3,0", session);
        await registry.ExecuteAsync("PLINE -4,0 3,0", session);

        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);

        var join = await registry.ExecuteAsync($"JOIN {polylines[0].Handle:X} {polylines[1].Handle:X}", session);
        Assert.True(join.Success);

        var merged = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.Equal(3, merged.Vertices.Count);
        Assert.Equal(0.0, merged.Vertices[0].Location.X, 6);
        Assert.Equal(0.0, merged.Vertices[0].Location.Y, 6);
        Assert.Equal(3.0, merged.Vertices[1].Location.X, 6);
        Assert.Equal(0.0, merged.Vertices[1].Location.Y, 6);
        Assert.Equal(-4.0, merged.Vertices[2].Location.X, 6);
        Assert.Equal(0.0, merged.Vertices[2].Location.Y, 6);
    }

    [Fact]
    public async Task Join_TwoOpenPolylines_WithSharedFirstStartSecondEnd_MergesForward()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 3,0", session);
        await registry.ExecuteAsync("PLINE -4,0 0,0", session);

        var polylines = session.Document.Entities.OfType<LwPolyline>().ToArray();
        Assert.Equal(2, polylines.Length);

        var join = await registry.ExecuteAsync($"JOIN {polylines[0].Handle:X} {polylines[1].Handle:X}", session);
        Assert.True(join.Success);

        var merged = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        Assert.Equal(3, merged.Vertices.Count);
        Assert.Equal(-4.0, merged.Vertices[0].Location.X, 6);
        Assert.Equal(0.0, merged.Vertices[0].Location.Y, 6);
        Assert.Equal(0.0, merged.Vertices[1].Location.X, 6);
        Assert.Equal(0.0, merged.Vertices[1].Location.Y, 6);
        Assert.Equal(3.0, merged.Vertices[2].Location.X, 6);
        Assert.Equal(0.0, merged.Vertices[2].Location.Y, 6);
    }

    [Fact]
    public async Task Offset_NegativeDistance_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 1,0", session);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var offset = await registry.ExecuteAsync($"OFFSET -1 {line.Handle:X}", session);
        Assert.False(offset.Success);
    }

    [Fact]
    public async Task Trim_ParallelLines_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        await registry.ExecuteAsync("LINE 0,5 10,5", session);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        var target = lines.Single(line => Math.Abs(line.StartPoint.Y - 0.0) < 1e-6);
        var boundary = lines.Single(line => Math.Abs(line.StartPoint.Y - 5.0) < 1e-6);

        var trim = await registry.ExecuteAsync($"TRIM {boundary.Handle:X} {target.Handle:X} END", session);
        Assert.False(trim.Success);
    }

    [Fact]
    public async Task Extend_NonIntersectingCircleBoundary_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 2,0", session);
        await registry.ExecuteAsync("CIRCLE 100,100 5", session);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());

        var extend = await registry.ExecuteAsync($"EXTEND {circle.Handle:X} {line.Handle:X} END", session);
        Assert.False(extend.Success);
    }

    [Fact]
    public async Task Fillet_PerpendicularLines_CreatesArcAndTrims()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        var first = lines[0];
        var second = lines[1];

        var fillet = await registry.ExecuteAsync($"FILLET 2 {first.Handle:X} {second.Handle:X}", session);
        Assert.True(fillet.Success);

        var updatedLines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, updatedLines.Length);

        var horizontal = updatedLines.Single(line => Math.Abs(line.StartPoint.Y - line.EndPoint.Y) < 1e-6);
        var vertical = updatedLines.Single(line => Math.Abs(line.StartPoint.X - line.EndPoint.X) < 1e-6);
        Assert.True(
            (Approximately(horizontal.StartPoint.X, 2.0) && Approximately(horizontal.EndPoint.X, 10.0)) ||
            (Approximately(horizontal.StartPoint.X, 10.0) && Approximately(horizontal.EndPoint.X, 2.0)));
        Assert.True(
            (Approximately(vertical.StartPoint.Y, 2.0) && Approximately(vertical.EndPoint.Y, 10.0)) ||
            (Approximately(vertical.StartPoint.Y, 10.0) && Approximately(vertical.EndPoint.Y, 2.0)));

        var arc = Assert.Single(session.Document.Entities.OfType<Arc>());
        Assert.Equal(2.0, arc.Center.X, 6);
        Assert.Equal(2.0, arc.Center.Y, 6);
        Assert.Equal(2.0, arc.Radius, 6);
    }

    [Fact]
    public async Task Fillet_LineAndOpenPolyline_CreatesArcAndTrimsPolylineEndpoint()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,6", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var fillet = await registry.ExecuteAsync($"FILLET 2 {polyline.Handle:X} {line.Handle:X}", session);
        Assert.True(fillet.Success);

        Assert.Equal(2.0, polyline.Vertices[0].Location.X, 6);
        Assert.Equal(0.0, polyline.Vertices[0].Location.Y, 6);
        Assert.True(
            (Approximately(line.StartPoint.Y, 2.0) && Approximately(line.EndPoint.Y, 10.0)) ||
            (Approximately(line.EndPoint.Y, 2.0) && Approximately(line.StartPoint.Y, 10.0)));

        Assert.Single(session.Document.Entities.OfType<Arc>());
    }

    [Fact]
    public async Task Fillet_LineAndArcSegmentPolyline_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,6", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].Bulge = 0.5;
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var fillet = await registry.ExecuteAsync($"FILLET 2 {polyline.Handle:X} {line.Handle:X}", session);
        Assert.False(fillet.Success);
        Assert.Contains("arc segments", fillet.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.5, polyline.Vertices[0].Bulge, 6);
        Assert.Empty(session.Document.Entities.OfType<Arc>());
    }

    [Fact]
    public async Task Fillet_LineAndVariableWidthPolyline_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,6", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].StartWidth = 0.25;
        polyline.Vertices[0].EndWidth = 0.5;
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var fillet = await registry.ExecuteAsync($"FILLET 2 {polyline.Handle:X} {line.Handle:X}", session);
        Assert.False(fillet.Success);
        Assert.Contains("variable-width", fillet.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.25, polyline.Vertices[0].StartWidth, 6);
        Assert.Equal(0.5, polyline.Vertices[0].EndWidth, 6);
        Assert.Empty(session.Document.Entities.OfType<Arc>());
    }

    [Fact]
    public async Task Fillet_LineAndNonWorldPolyline_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,6", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Normal = new XYZ(0.0, 0.0, -1.0);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var fillet = await registry.ExecuteAsync($"FILLET 2 {polyline.Handle:X} {line.Handle:X}", session);
        Assert.False(fillet.Success);
        Assert.Contains("world-xy", fillet.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(-1.0, polyline.Normal.Z, 6);
        Assert.Empty(session.Document.Entities.OfType<Arc>());
    }

    [Fact]
    public async Task Fillet_RadiusTooLarge_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        var fillet = await registry.ExecuteAsync($"FILLET 20 {lines[0].Handle:X} {lines[1].Handle:X}", session);
        Assert.False(fillet.Success);
        Assert.Empty(session.Document.Entities.OfType<Arc>());
    }

    [Fact]
    public async Task Chamfer_PerpendicularLines_CreatesLineAndTrims()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        var chamfer = await registry.ExecuteAsync($"CHAMFER 2 3 {lines[0].Handle:X} {lines[1].Handle:X}", session);
        Assert.True(chamfer.Success);

        var allLines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(3, allLines.Length);
        Assert.Contains(allLines, line =>
            (Approximately(line.StartPoint.X, 2.0) && Approximately(line.StartPoint.Y, 0.0) &&
             Approximately(line.EndPoint.X, 0.0) && Approximately(line.EndPoint.Y, 3.0)) ||
            (Approximately(line.EndPoint.X, 2.0) && Approximately(line.EndPoint.Y, 0.0) &&
             Approximately(line.StartPoint.X, 0.0) && Approximately(line.StartPoint.Y, 3.0)));
    }

    [Fact]
    public async Task Chamfer_LineAndOpenPolyline_CreatesLineAndTrimsPolylineEndpoint()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,6", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var chamfer = await registry.ExecuteAsync($"CHAMFER 2 3 {polyline.Handle:X} {line.Handle:X}", session);
        Assert.True(chamfer.Success);

        Assert.Equal(2.0, polyline.Vertices[0].Location.X, 6);
        Assert.Equal(0.0, polyline.Vertices[0].Location.Y, 6);
        Assert.True(
            (Approximately(line.StartPoint.Y, 3.0) && Approximately(line.EndPoint.Y, 10.0)) ||
            (Approximately(line.EndPoint.Y, 3.0) && Approximately(line.StartPoint.Y, 10.0)));

        var allLines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Equal(2, allLines.Length);
        Assert.Contains(allLines, candidate =>
            (Approximately(candidate.StartPoint.X, 2.0) && Approximately(candidate.StartPoint.Y, 0.0) &&
             Approximately(candidate.EndPoint.X, 0.0) && Approximately(candidate.EndPoint.Y, 3.0)) ||
            (Approximately(candidate.EndPoint.X, 2.0) && Approximately(candidate.EndPoint.Y, 0.0) &&
             Approximately(candidate.StartPoint.X, 0.0) && Approximately(candidate.StartPoint.Y, 3.0)));
    }

    [Fact]
    public async Task Chamfer_LineAndArcSegmentPolyline_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,6", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].Bulge = 0.5;
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var chamfer = await registry.ExecuteAsync($"CHAMFER 2 3 {polyline.Handle:X} {line.Handle:X}", session);
        Assert.False(chamfer.Success);
        Assert.Contains("arc segments", chamfer.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.5, polyline.Vertices[0].Bulge, 6);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Chamfer_LineAndVariableWidthPolyline_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,6", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Vertices[0].StartWidth = 0.25;
        polyline.Vertices[0].EndWidth = 0.5;
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var chamfer = await registry.ExecuteAsync($"CHAMFER 2 3 {polyline.Handle:X} {line.Handle:X}", session);
        Assert.False(chamfer.Success);
        Assert.Contains("variable-width", chamfer.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0.25, polyline.Vertices[0].StartWidth, 6);
        Assert.Equal(0.5, polyline.Vertices[0].EndWidth, 6);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Chamfer_LineAndNonWorldPolyline_FailsWithoutMutatingGeometry()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 10,0 10,6", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        polyline.Normal = new XYZ(0.0, 0.0, -1.0);
        var line = Assert.Single(session.Document.Entities.OfType<Line>());

        var chamfer = await registry.ExecuteAsync($"CHAMFER 2 3 {polyline.Handle:X} {line.Handle:X}", session);
        Assert.False(chamfer.Success);
        Assert.Contains("world-xy", chamfer.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(-1.0, polyline.Normal.Z, 6);
        Assert.Single(session.Document.Entities.OfType<Line>());
    }

    [Fact]
    public async Task Chamfer_SingleDistance_UsesSymmetricDistances()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        await registry.ExecuteAsync("LINE 0,0 0,10", session);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        var chamfer = await registry.ExecuteAsync($"CHAMFER 2 {lines[0].Handle:X} {lines[1].Handle:X}", session);
        Assert.True(chamfer.Success);

        var allLines = session.Document.Entities.OfType<Line>().ToArray();
        Assert.Contains(allLines, line =>
            (Approximately(line.StartPoint.X, 2.0) && Approximately(line.StartPoint.Y, 0.0) &&
             Approximately(line.EndPoint.X, 0.0) && Approximately(line.EndPoint.Y, 2.0)) ||
            (Approximately(line.EndPoint.X, 2.0) && Approximately(line.EndPoint.Y, 0.0) &&
             Approximately(line.StartPoint.X, 0.0) && Approximately(line.StartPoint.Y, 2.0)));
    }

    [Fact]
    public async Task Join_NonCollinearTouchingLines_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 5,0", session);
        await registry.ExecuteAsync("LINE 5,0 5,5", session);

        var lines = session.Document.Entities.OfType<Line>().ToArray();
        var join = await registry.ExecuteAsync($"JOIN {lines[0].Handle:X} {lines[1].Handle:X}", session);
        Assert.False(join.Success);
        Assert.Equal(2, session.Document.Entities.OfType<Line>().Count());
    }

    [Fact]
    public async Task Join_ClosedPolylineAndLine_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("RECTANG 0,0 5,5", session);
        await registry.ExecuteAsync("LINE 5,5 10,5", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var join = await registry.ExecuteAsync($"JOIN {polyline.Handle:X} {line.Handle:X}", session);
        Assert.False(join.Success);
    }

    [Fact]
    public async Task Trim_CircleBoundary_StartEndpoint_SelectsExpectedIntersection()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE -10,0 10,0", session);
        await registry.ExecuteAsync("CIRCLE 0,0 5", session);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());
        var trim = await registry.ExecuteAsync($"TRIM {circle.Handle:X} {line.Handle:X} START", session);
        Assert.True(trim.Success);
        Assert.Equal(-5.0, line.StartPoint.X, 6);
        Assert.Equal(10.0, line.EndPoint.X, 6);
    }

    [Fact]
    public async Task Trim_ArcBoundary_StartEndpoint_RespectsArcSweep()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE -10,0 10,0", session);
        await registry.ExecuteAsync("ARC 0,0 5 0 90", session);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var arc = Assert.Single(session.Document.Entities.OfType<Arc>());
        var trim = await registry.ExecuteAsync($"TRIM {arc.Handle:X} {line.Handle:X} START", session);

        Assert.True(trim.Success);
        Assert.Equal(5.0, line.StartPoint.X, 6);
        Assert.Equal(10.0, line.EndPoint.X, 6);
    }

    [Fact]
    public async Task Extend_CircleBoundary_StartEndpoint_SelectsExpectedIntersection()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE -2,0 2,0", session);
        await registry.ExecuteAsync("CIRCLE 0,0 5", session);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var circle = Assert.Single(session.Document.Entities.OfType<Circle>());
        var extend = await registry.ExecuteAsync($"EXTEND {circle.Handle:X} {line.Handle:X} START", session);
        Assert.True(extend.Success);
        Assert.Equal(-5.0, line.StartPoint.X, 6);
        Assert.Equal(2.0, line.EndPoint.X, 6);
    }

    [Fact]
    public async Task Trim_ZeroLengthTargetLine_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,-5 0,5", session);
        var boundary = Assert.Single(session.Document.Entities.OfType<Line>());

        var zeroLine = new Line
        {
            StartPoint = new XYZ(1.0, 1.0, 0.0),
            EndPoint = new XYZ(1.0, 1.0, 0.0)
        };
        session.Document.Entities.Add(zeroLine);
        session.EntityIndex.Register(zeroLine);

        var trim = await registry.ExecuteAsync($"TRIM {boundary.Handle:X} {zeroLine.Handle:X} END", session);
        Assert.False(trim.Success);
    }

    [Fact]
    public async Task Trim_LineByClosedPolyline_End_Works()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("LINE 0,0 10,0", session);
        await registry.ExecuteAsync("RECTANG 6,-2 8,2", session);

        var line = Assert.Single(session.Document.Entities.OfType<Line>());
        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());

        var trim = await registry.ExecuteAsync($"TRIM {polyline.Handle:X} {line.Handle:X} END", session);
        Assert.True(trim.Success);
        Assert.Equal(8.0, line.EndPoint.X, 6);
    }

    [Fact]
    public async Task Offset_DegeneratePolylineSegment_Fails()
    {
        var (session, registry) = CreateHarness();
        await registry.ExecuteAsync("PLINE 0,0 0,0 1,0", session);

        var polyline = Assert.Single(session.Document.Entities.OfType<LwPolyline>());
        var offset = await registry.ExecuteAsync($"OFFSET 1 {polyline.Handle:X}", session);
        Assert.False(offset.Success);
    }

    private static bool Approximately(double actual, double expected)
    {
        return Math.Abs(actual - expected) < 1e-6;
    }

    private static IReadOnlyList<XYZ> GetHatchLoopPoints(Hatch hatch)
    {
        var path = Assert.Single(hatch.Paths);
        if (path.Edges.Count == 1 && path.Edges[0] is Hatch.BoundaryPath.Polyline polyline)
        {
            return polyline.Vertices
                .Select(vertex => new XYZ(vertex.X, vertex.Y, hatch.Elevation))
                .ToArray();
        }

        var points = path.GetPoints()
            .Select(point => new XYZ(point.X, point.Y, hatch.Elevation))
            .ToArray();
        if (points.Length > 1 &&
            Approximately(points[0].X, points[^1].X) &&
            Approximately(points[0].Y, points[^1].Y))
        {
            points = points[..^1];
        }

        return points;
    }

    private static (CadDocumentSession Session, CadCommandRegistry Registry) CreateHarness()
    {
        var document = new CadDocument();
        var session = (CadDocumentSession)new CadEditorSessionFactory().Create(document);
        var clipboard = new InMemoryCadClipboardService();

        var registry = new CadCommandRegistry();
        registry.Register(new LineCadCommand());
        registry.Register(new XLineCadCommand());
        registry.Register(new RayCadCommand());
        registry.Register(new CircleCadCommand());
        registry.Register(new ArcCadCommand());
        registry.Register(new EllipseCadCommand());
        registry.Register(new SplineCadCommand());
        registry.Register(new TextCadCommand());
        registry.Register(new MTextCadCommand());
        registry.Register(new DimLinearCadCommand());
        registry.Register(new DimAlignedCadCommand());
        registry.Register(new DimRadiusCadCommand());
        registry.Register(new DimDiameterCadCommand());
        registry.Register(new DimAngularCadCommand());
        registry.Register(new LeaderCadCommand());
        registry.Register(new MLeaderCadCommand());
        registry.Register(new HatchCadCommand());
        registry.Register(new BoundaryCadCommand());
        registry.Register(new PlineCadCommand());
        registry.Register(new PointCadCommand());
        registry.Register(new InsertCadCommand());
        registry.Register(new XRefReloadCadCommand());
        registry.Register(new XRefBindCadCommand());
        registry.Register(new XRefDetachCadCommand());
        registry.Register(new RectangCadCommand());
        registry.Register(new PolygonCadCommand());
        registry.Register(new MoveCadCommand());
        registry.Register(new StretchCadCommand());
        registry.Register(new RotateCadCommand());
        registry.Register(new ScaleCadCommand());
        registry.Register(new MirrorCadCommand());
        registry.Register(new OffsetCadCommand());
        registry.Register(new TrimCadCommand());
        registry.Register(new ExtendCadCommand());
        registry.Register(new BreakCadCommand());
        registry.Register(new JoinCadCommand());
        registry.Register(new FilletCadCommand());
        registry.Register(new ChamferCadCommand());
        registry.Register(new ArrayCadCommand());
        registry.Register(new ExplodeCadCommand());
        registry.Register(new AlignCadCommand());
        registry.Register(new MatchPropCadCommand());
        registry.Register(new CopyClipCadCommand(clipboard));
        registry.Register(new CutCadCommand(clipboard));
        registry.Register(new PasteClipCadCommand(clipboard));
        registry.Register(new CopyCadCommand());
        registry.Register(new EraseCadCommand());
        registry.Register(new UndoCadCommand());
        registry.Register(new RedoCadCommand());

        return (session, registry);
    }
}
