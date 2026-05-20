using ProCad.Core;
using Microsoft.Extensions.DependencyInjection;

namespace ProCad.IO;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProCadIO(this IServiceCollection services)
    {
        services.AddSingleton<ICadDocumentService, AcAdSharpDocumentService>();
        services.AddSingleton<IDxfTextService, DxfTextService>();
        return services;
    }
}
