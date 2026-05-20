using System;
using Dock.Model.ReactiveUI.Controls;

namespace ProCad.ViewModels;

public abstract class CadToolViewModelBase : Tool
{
    protected CadToolViewModelBase()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = GetType().Name;
        }
    }
}

public abstract class CadDocumentViewModelBase : Document
{
    protected CadDocumentViewModelBase()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = $"{GetType().Name}-{Guid.NewGuid():N}";
        }
    }
}
