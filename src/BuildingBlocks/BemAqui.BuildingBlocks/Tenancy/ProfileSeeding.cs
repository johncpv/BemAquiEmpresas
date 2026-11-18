using BemAqui.BuildingBlocks.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Tenancy;

/// <summary>
/// Pacote de dados iniciais de um módulo para um perfil ou segmento (PERF-04). O módulo dono registra a implementação com a chave de
/// <see cref="BusinessProfileSeeds"/>. Regras: idempotente pela <c>seed_key</c>; não recria o que o tenant excluiu; nunca altera um valor
/// que já existe (o tenant pode ter mudado); roda dentro do escopo do tenant.
/// </summary>
public interface IProfileSeedPackage
{
    string Key { get; }

    Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken);
}

/// <summary>
/// Roda os pacotes dos perfis e segmentos ativos do tenant (PERF-04). É um <see cref="ITenantSeeder"/> com ordem alta: roda no
/// provisionamento depois dos seeders básicos (grupos, unidades, plano de contas) e a cada <c>migrate</c>, o que cobre o perfil
/// adicionado depois. A aba "Perfis" também o chama ao adicionar um perfil. Tenant só com <c>factory</c> não roda nada (CA 5).
/// </summary>
public sealed class ProfileSeedRunner(IEnumerable<IProfileSeedPackage> packages, ILogger<ProfileSeedRunner> logger) : ITenantSeeder
{
    public int Order => 900;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var tenant = tenantScope.GetRequiredService<TenantContextHolder>().Resolved;
        if (tenant is null)
        {
            return;
        }

        var byKey = packages.ToDictionary(p => p.Key, StringComparer.Ordinal);
        foreach (var key in BusinessProfileSeeds.PackagesFor(tenant.Profiles, tenant.Segments))
        {
            if (!byKey.TryGetValue(key, out var package))
            {
                // Ponto de extensão ainda sem módulo (ex.: catalog.retail até a CAT-01).
                LogMissing(logger, key, null);
                continue;
            }

            await package.SeedAsync(tenantScope, cancellationToken);
        }
    }

    private static readonly Action<ILogger, string, Exception?> LogMissing =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, "ProfileSeedPackageMissing"), "Pacote de dados iniciais {Package} ainda sem implementação.");
}
