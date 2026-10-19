using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.BuildingBlocks.Lookups;

public static class LookupsServiceCollectionExtensions
{
    /// <summary>Consultas públicas (CAD-01b): HttpClient sem redirect e sem log, proteção global e o CEP.</summary>
    public static IServiceCollection AddBemAquiLookups(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<LookupOptions>(configuration.GetSection(LookupOptions.Section));
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<LookupGuard>();
        services.TryAddScoped<PublicDataHttp>();
        services.TryAddScoped<ICepLookup, BrasilApiCepLookup>();
        services.AddHttpClient(PublicDataHttp.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All })
            .RemoveAllLoggers();
        return services;
    }
}
