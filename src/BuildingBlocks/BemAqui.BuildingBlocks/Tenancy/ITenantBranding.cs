namespace BemAqui.BuildingBlocks.Tenancy;

/// <summary>Nome e logo do tenant para a tela de login (CAD-06 CA 4 e 7). <c>LogoVersion</c> muda quando o logo muda (cache).</summary>
public sealed record TenantBrand(string? Name, string? LogoVersion);

/// <summary>Marca do tenant do escopo, lida do perfil da empresa no banco do tenant (fonte da verdade, ADR 0016).</summary>
public interface ITenantBranding
{
    Task<TenantBrand> GetAsync(CancellationToken cancellationToken = default);
}
