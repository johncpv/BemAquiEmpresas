using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.BuildingBlocks.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Serviços comuns de persistência (auditoria, numeração, usuário atual padrão).</summary>
    public static IServiceCollection AddBemAquiPersistenceCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();
        services.TryAddScoped<AuditSaveChangesInterceptor>();
        services.TryAddScoped<INumberSequence, NumberSequenceService>();
        services.TryAddSingleton<AuditableEntityRegistry>();
        services.TryAddScoped<IPermissionChecker, DenyAllPermissionChecker>();
        services.TryAddScoped<AuditHistory>();
        services.TryAddSingleton<AttachmentOwnerRegistry>();
        services.TryAddSingleton<IFileStorage, LocalFileStorage>();
        services.TryAddSingleton<IVirusScanner, ClamAvScanner>();
        services.TryAddScoped<AttachmentService>();
        services.TryAddScoped<NotificationService>();
        services.TryAddScoped<INotificationService>(sp => sp.GetRequiredService<NotificationService>());
        return services;
    }

    /// <summary>Opções padrão do DbContext de tenant: Npgsql, snake_case e o interceptor de auditoria.</summary>
    public static DbContextOptionsBuilder UseBemAquiDefaults(this DbContextOptionsBuilder builder, string connectionString, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>());
    }
}
