using System;
using System.Collections.Generic;
using ReactiveUI;
using ACadInspector.ViewModels;
using ACadInspector.Views;

namespace ACadInspector;

public sealed class AppViewLocator : IViewLocator
{
    private readonly IReadOnlyDictionary<Type, Func<IViewFor>> _factories;

    public AppViewLocator()
    {
        _factories = new Dictionary<Type, Func<IViewFor>>
        {
            [typeof(WorkspaceViewModel)] = static () => new WorkspaceView()
        };
    }

    public IViewFor? ResolveView<T>(T? viewModel, string? contract = null)
    {
        if (viewModel is null)
        {
            return null;
        }

        return _factories.TryGetValue(viewModel.GetType(), out var factory)
            ? factory()
            : null;
    }
}
