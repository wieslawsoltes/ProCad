using System;
using ACadInspector.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ACadInspector.Services;

public sealed class CadCompareViewModelFactory
{
    private readonly IServiceProvider _services;

    public CadCompareViewModelFactory(IServiceProvider services)
    {
        _services = services;
    }

    public CadCompareViewModel Create()
    {
        return ActivatorUtilities.CreateInstance<CadCompareViewModel>(_services);
    }
}
