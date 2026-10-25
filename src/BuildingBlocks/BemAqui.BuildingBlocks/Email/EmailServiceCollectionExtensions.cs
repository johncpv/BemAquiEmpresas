using BemAqui.BuildingBlocks.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.BuildingBlocks.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>E-mail transacional por fila (FND-08): sender no outbox, job na fila <c>email</c>, SMTP e templates em sandbox.</summary>
    public static IServiceCollection AddBemAquiEmail(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.TryAddSingleton<EmailTemplateRenderer>();
        services.TryAddSingleton<IEmailTransport, SmtpEmailTransport>();
        services.TryAddScoped<TenantLinks>();
        services.TryAddScoped<IEmailSender, OutboxEmailSender>();
        services.TryAddScoped<EmailLogReader>();
        services.TryAddScoped<EmailDispatchLimiter>();
        services.TryAddTransient<SendEmailJob>();
        return services;
    }

    /// <summary>Só o transporte SMTP (CLI e alertas da plataforma, sem fila de tenant).</summary>
    public static IServiceCollection AddBemAquiEmailTransportOnly(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.TryAddSingleton<IEmailTransport, SmtpEmailTransport>();
        return services;
    }

    /// <summary>Registra o consumer que transforma EmailQueued em job do Hangfire.</summary>
    public static IntegrationEventRegistry AddEmailEvents(this IntegrationEventRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.AddHandler<EmailQueued, EmailQueuedConsumer>();
    }
}
