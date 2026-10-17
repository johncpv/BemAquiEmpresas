namespace BemAqui.BuildingBlocks.Tenancy;

/// <summary>
/// Dados iniciais de um módulo no banco do tenant (catálogo de módulos e grupos na FND-06, plano de contas na Fase 1...).
/// Roda no provisionamento, dentro do escopo do tenant. Precisa ser idempotente: rodar de novo não duplica nada.
/// </summary>
public interface ITenantSeeder
{
    /// <summary>Ordem de execução (menor primeiro).</summary>
    int Order { get; }

    Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken);
}
