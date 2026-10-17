using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Outbox, inbox e dispatcher. Os módulos registram eventos e handlers no <see cref="IntegrationEventRegistry"/>
    /// devolvido por <paramref name="configure"/>; os handlers são registrados como scoped.
    /// </summary>
    public static IServiceCollection AddBemAquiMessaging(this IServiceCollection services, IConfiguration configuration, Action<IntegrationEventRegistry>? configure = null, bool runDispatcher = true)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Section));

        var registry = new IntegrationEventRegistry();
        configure?.Invoke(registry);
        services.TryAddSingleton(registry);
        foreach (var handler in registry.HandlerTypes)
        {
            services.TryAddScoped(handler);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OutboxSignal>();
        services.TryAddScoped<IEventPublisher, OutboxEventPublisher>();
        services.TryAddScoped<OutboxProcessor>();
        if (runDispatcher)
        {
            services.AddHostedService<OutboxDispatcher>();
        }

        return services;
    }
}
