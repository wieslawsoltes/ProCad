using System;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace ACadInspector.Services;

public sealed class CadDocumentDockService
{
    private IRootDock? _layout;

    public void RegisterLayout(IRootDock layout)
    {
        _layout = layout;
    }

    public bool TryActivateDocument(Func<IDockable, bool> predicate)
    {
        var documentDock = FindDocumentDock(_layout);
        if (documentDock?.VisibleDockables is null)
        {
            return false;
        }

        foreach (var dockable in documentDock.VisibleDockables)
        {
            if (!predicate(dockable))
            {
                continue;
            }

            documentDock.ActiveDockable = dockable;
            return true;
        }

        return false;
    }

    public bool TryAddDocument(IDockable dockable)
    {
        var documentDock = FindDocumentDock(_layout);
        if (documentDock is null)
        {
            return false;
        }

        documentDock.AddDocument(dockable);
        documentDock.ActiveDockable = dockable;
        documentDock.DefaultDockable ??= dockable;
        return true;
    }

    private static IDocumentDock? FindDocumentDock(IDockable? dockable)
    {
        if (dockable is IDocumentDock documentDock)
        {
            return documentDock;
        }

        if (dockable is IDock dock && dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                var result = FindDocumentDock(child);
                if (result is not null)
                {
                    return result;
                }
            }
        }

        return null;
    }
}
