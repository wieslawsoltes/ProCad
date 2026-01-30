using ACadInspector.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ACadInspector.IO;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCadInspectorIO(this IServiceCollection services)
    {
        services.AddSingleton<ICadDocumentService, AcAdSharpDocumentService>();
        services.AddSingleton<IDxfTextService, DxfTextService>();
        return services;
    }
}
