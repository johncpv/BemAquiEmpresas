namespace BemAqui.BuildingBlocks.Tenancy;

/// <summary>
/// Padrão de um módulo que vale só para tenant novo (FIN-10a, ADR 0054): roda uma única vez, no passo
/// <c>NewTenantDefaults</c> do provisionamento, com o tenant ainda em <c>Provisioning</c>. Ao contrário do <see cref="ITenantSeeder"/>,
/// nunca roda no <c>TenantMigrationRunner</c> nem numa retomada de tenant já ativo, então pode trocar um valor que o seeder gravou.
/// </summary>
public interface ITenantProvisioningDefaults
{
    /// <summary>Ordem de execução (menor primeiro).</summary>
    int Order { get; }

    /// <summary>Grava os padrões e devolve uma linha para o log do provisionamento.</summary>
    Task<string> ApplyAsync(IServiceProvider tenantScope, CancellationToken cancellationToken);
}
