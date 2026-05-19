using System;
using ProCad.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

namespace ProCad.Services;

public sealed class WorkspaceViewModelFactory
{
    private readonly IServiceProvider _services;

    public WorkspaceViewModelFactory(IServiceProvider services)
    {
        _services = services;
    }

    public WorkspaceViewModel Create(IScreen hostScreen)
    {
        return ActivatorUtilities.CreateInstance<WorkspaceViewModel>(_services, hostScreen);
    }
}
