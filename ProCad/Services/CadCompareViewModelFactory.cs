using System;
using ProCad.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ProCad.Services;

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
