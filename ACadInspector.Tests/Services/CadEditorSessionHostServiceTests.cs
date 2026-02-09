using ACadInspector.Core;
using ACadInspector.Editing.Identifiers;
using ACadInspector.Editing.Operations;
using ACadInspector.Editing.Selection;
using ACadInspector.Editing.Sessions;
using ACadInspector.Services;
using ACadInspector.ViewModels;
using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace ACadInspector.Tests.Services;

public sealed class CadEditorSessionHostServiceTests
{
    [Fact]
    public void SyncSelectionToSession_FiltersOutEntitiesNotInSessionIndex()
    {
        var localDocument = new CadDocument();
        var localLine = new Line(new XYZ(0d, 0d, 0d), new XYZ(5d, 0d, 0d));
        localDocument.Entities.Add(localLine);

        var foreignDocument = new CadDocument();
        var foreignLine = new Line(new XYZ(10d, 0d, 0d), new XYZ(15d, 0d, 0d));
        foreignDocument.Entities.Add(foreignLine);

        var selection = new CadSelectionService();
        var context = new CadDocumentContextService();
        context.Register(new CadDocumentViewModel(localDocument, CadFileFormat.Dxf, path: null, displayName: "Local", render: null!));

        var host = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var session = host.GetOrCreate(localDocument);

        selection.ApplySelection(new object?[] { localLine, foreignLine }, CadSelectionMode.Replace);
        host.SyncSelectionToSession(session);

        Assert.Single(session.SelectionSet.Items);
        Assert.Contains(localLine, session.SelectionSet.Items);
        Assert.DoesNotContain(foreignLine, session.SelectionSet.Items);
    }

    [Fact]
    public void SyncSelectionToUi_FiltersOutEntitiesNotInSessionIndex()
    {
        var localDocument = new CadDocument();
        var localLine = new Line(new XYZ(0d, 0d, 0d), new XYZ(5d, 0d, 0d));
        localDocument.Entities.Add(localLine);

        var foreignDocument = new CadDocument();
        var foreignLine = new Line(new XYZ(10d, 0d, 0d), new XYZ(15d, 0d, 0d));
        foreignDocument.Entities.Add(foreignLine);

        var selection = new CadSelectionService();
        var context = new CadDocumentContextService();
        context.Register(new CadDocumentViewModel(localDocument, CadFileFormat.Dxf, path: null, displayName: "Local", render: null!));

        var host = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var session = host.GetOrCreate(localDocument);
        session.SetSelection(new object?[] { localLine, foreignLine }, CadSelectionMode.Replace);

        host.SyncSelectionToUi(session);

        Assert.Single(selection.SelectedObjects);
        Assert.Same(localLine, selection.SelectedObject);
        Assert.DoesNotContain(foreignLine, selection.SelectedObjects);
    }

    [Fact]
    public void UnregisterDocument_RemovesSessionAndRaisesSessionRemoved()
    {
        var document = new CadDocument();
        var selection = new CadSelectionService();
        var context = new CadDocumentContextService();
        context.Register(new CadDocumentViewModel(document, CadFileFormat.Dxf, path: null, displayName: "Local", render: null!));

        var host = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var session = host.GetOrCreate(document);
        ICadEditorSession? removedSession = null;
        host.SessionRemoved += (_, removed) => removedSession = removed;

        var unregistered = context.Unregister(document);

        Assert.True(unregistered);
        Assert.Same(session, removedSession);
        Assert.False(host.TryGet(document, out _));
    }

    [Fact]
    public void NotifySessionChanged_ReemitsSelectionForMutatedSelectedEntity()
    {
        var document = new CadDocument();
        var line = new Line(new XYZ(0d, 0d, 0d), new XYZ(5d, 0d, 0d));
        document.Entities.Add(line);

        var selection = new CadSelectionService();
        var context = new CadDocumentContextService();
        context.Register(new CadDocumentViewModel(document, CadFileFormat.Dxf, path: null, displayName: "Local", render: null!));

        var host = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var session = (CadDocumentSession)host.GetOrCreate(document);

        selection.SelectedObject = line;
        var initialStamp = selection.SelectionStamp;

        Assert.True(session.EntityIndex.TryGetId(line, out var lineId));
        var batch = CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: 1,
            operations:
            [
                CadOperationPayloadCodec.TransformLine(
                    lineId,
                    line.StartPoint,
                    line.EndPoint,
                    new XYZ(2d, 1d, 0d),
                    new XYZ(7d, 1d, 0d))
            ]);
        session.Apply(batch);

        host.NotifySessionChanged(session);

        Assert.Same(line, selection.SelectedObject);
        Assert.True(selection.SelectionStamp > initialStamp);
        Assert.Equal(2d, line.StartPoint.X, 6);
        Assert.Equal(1d, line.StartPoint.Y, 6);
    }

    [Fact]
    public void NotifySessionChanged_ClearsSelectionWhenSelectedEntityIsDeleted()
    {
        var document = new CadDocument();
        var line = new Line(new XYZ(0d, 0d, 0d), new XYZ(5d, 0d, 0d));
        document.Entities.Add(line);

        var selection = new CadSelectionService();
        var context = new CadDocumentContextService();
        context.Register(new CadDocumentViewModel(document, CadFileFormat.Dxf, path: null, displayName: "Local", render: null!));

        var host = new CadEditorSessionHostService(new CadEditorSessionFactory(), context, selection);
        var session = (CadDocumentSession)host.GetOrCreate(document);

        selection.SelectedObject = line;
        Assert.True(session.EntityIndex.TryGetId(line, out var lineId));

        var deleteBatch = CadOperationBatch.Create(
            actorId: session.SessionId.Value,
            baseVersion: session.Revision,
            sequence: 2,
            operations:
            [
                CadOperationPayloadCodec.DeleteLine(
                    lineId,
                    line.StartPoint,
                    line.EndPoint)
            ]);
        session.Apply(deleteBatch);

        host.NotifySessionChanged(session);

        Assert.Null(selection.SelectedObject);
        Assert.Empty(selection.SelectedObjects);
    }
}
