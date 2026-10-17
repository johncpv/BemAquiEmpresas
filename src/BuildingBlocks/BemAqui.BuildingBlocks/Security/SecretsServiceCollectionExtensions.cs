using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BemAqui.BuildingBlocks.Security;

public static class SecretsServiceCollectionExtensions
{
    public const string ApplicationName = "bemaqui-empresas";

    /// <summary>Chaves novas a cada 90 dias; as antigas continuam decifrando (ADR 0007).</summary>
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// Data Protection com key ring no catálogo (<c>security.data_protection_keys</c>) protegido por certificado,
    /// <see cref="ISecretProtector"/> e a verificação de subida que recusa key ring inacessível ou vazio.
    /// </summary>
    public static IServiceCollection AddBemAquiSecrets(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SecretsOptions.Section);
        var options = section.Get<SecretsOptions>() ?? new SecretsOptions();
        services.Configure<SecretsOptions>(section);

        var connectionString = configuration.GetConnectionString("Catalog");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Catalog não configurada: o key ring do Data Protection fica no banco de catálogo.");
        }

        var certificate = ProtectionCertificate.Load(options, environment);

        services.AddDbContext<DataProtectionKeysDbContext>(b => ConfigureKeysContext(b, connectionString));
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<DataProtectionKeysDbContext>()
            .ProtectKeysWithCertificate(certificate)
            .UnprotectKeysWithAnyCertificate(certificate)
            .SetDefaultKeyLifetime(KeyLifetime);

        services.AddSingleton(new ProtectionCertificateInfo(certificate.Subject, certificate.Thumbprint, certificate.NotAfter.ToUniversalTime()));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddHostedService<SecretsStartupCheck>();
        return services;
    }

    internal static TBuilder ConfigureKeysContext<TBuilder>(TBuilder builder, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        builder
            .UseNpgsql(connectionString, o => o.MigrationsHistoryTable(DataProtectionKeysDbContext.MigrationsHistoryTable, DataProtectionKeysDbContext.Schema))
            .UseSnakeCaseNamingConvention();
        return builder;
    }
}
