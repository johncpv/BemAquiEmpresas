namespace BemAqui.Platform.Contracts;

/// <summary>Dados cadastrais do tenant no catálogo, para outros módulos (ex.: a importação confere o CNPJ, MIG-01a CA 6).</summary>
public interface ITenantProfile
{
    /// <summary>CNPJ normalizado (sem pontuação) ou null se o tenant não tem documento.</summary>
    Task<string?> DocumentAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
