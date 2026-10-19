using BemAqui.Migrations.Application;
using BemAqui.Migrations.Application.Loaders;
using BemAqui.Migrations.Application.Snapshot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Migrations.Infrastructure;

public static class MigrationInfrastructure
{
    /// <summary>Importação do Dolibarr (MIG-01a): executor, staging, leitura do snapshot e a verificação diária do prazo.</summary>
    public static IServiceCollection AddMigrationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<MigrationOptions>(configuration.GetSection(MigrationOptions.Section));
        services.TryAddScoped<ImportRunner>();
        services.TryAddScoped<StagingService>();
        services.TryAddScoped<ImportQueries>();
        services.TryAddSingleton<ISnapshotSourceFactory, SnapshotSourceFactory>();
        // Cargas da MIG-01b, na ordem do Order: condições, contas, terceiros, contatos.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDolibarrLoader, PaymentTermsLoader>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDolibarrLoader, BankAccountsLoader>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDolibarrLoader, PartiesLoader>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDolibarrLoader, ContactsLoader>());
        return services;
    }
}
