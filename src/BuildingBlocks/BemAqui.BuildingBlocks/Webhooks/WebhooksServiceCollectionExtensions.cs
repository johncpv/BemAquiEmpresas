using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.BuildingBlocks.Webhooks;

public static class WebhooksServiceCollectionExtensions
{
    /// <summary>Recepção, processamento, expurgo e telas dos webhooks (FND-16). Os provedores entram com <see cref="AddWebhookProvider"/>.</summary>
    public static IServiceCollection AddBemAquiWebhooks(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<WebhookOptions>(configuration.GetSection(WebhookOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => new WebhookProviderRegistry(sp.GetServices<WebhookProvider>()));
        services.TryAddSingleton<WebhookRejectionMonitor>();
        services.TryAddScoped<WebhookReceiver>();
        services.TryAddScoped<WebhookEventsAdmin>();
        services.TryAddScoped<WebhookCredentialService>();
        services.TryAddScoped<IStepUpTwoFactor, DenyStepUpTwoFactor>();
        services.AddAuditableEntity<WebhookCredential>("settings");
        return services;
    }

    /// <summary>Registra um provedor e o handler dele (scoped, resolvido no escopo do tenant).</summary>
    public static IServiceCollection AddWebhookProvider(this IServiceCollection services, WebhookProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        services.AddSingleton(provider);
        services.TryAddScoped(provider.HandlerType);
        return services;
    }

    /// <summary>A mensagem do outbox e o consumidor que enfileira o processamento (ADR 0031 §3).</summary>
    public static IntegrationEventRegistry AddWebhookEvents(this IntegrationEventRegistry events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.AddHandler<WebhookReceived, WebhookReceivedConsumer>();
    }
}
