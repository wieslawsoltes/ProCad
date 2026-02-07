using ACadInspector.Core;
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
}
