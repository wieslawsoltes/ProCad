using System;
using System.Collections.Generic;
using ReactiveUI;
using ProCad.ViewModels;
using ProCad.Views;

namespace ProCad;

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

    public IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract = null)
        where TViewModel : class
    {
        return ResolveView(typeof(TViewModel)) as IViewFor<TViewModel>;
    }

    public IViewFor<TViewModel>? ResolveView<TViewModel>(TViewModel viewModel, string? contract = null)
        where TViewModel : class
    {
        return viewModel is null
            ? ResolveView<TViewModel>(contract)
            : ResolveView(viewModel.GetType()) as IViewFor<TViewModel>;
    }

    public IViewFor? ResolveView(object? instance, string? contract = null)
    {
        if (instance is null)
        {
            return null;
        }

        return ResolveView(instance.GetType());
    }

    private IViewFor? ResolveView(Type viewModelType)
    {
        return _factories.TryGetValue(viewModelType, out var factory)
            ? factory()
            : null;
    }
}
