using BemAqui.BuildingBlocks.Tenancy;

namespace BemAqui.BuildingBlocks.Email;

/// <summary>
/// Link absoluto para o app do tenant, sempre com o host primário do catálogo (FND-08 CA 6): nunca com o
/// cabeçalho Host da requisição, que um atacante pode forjar para roubar tokens de convite e redefinição.
/// </summary>
public sealed class TenantLinks(TenantContextHolder tenant)
{
    public string Absolute(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var host = tenant.Resolved?.PrimaryHost;
        if (string.IsNullOrEmpty(host))
        {
            throw new InvalidOperationException("Link de e-mail sem tenant no escopo.");
        }

        return "https://" + host + (path.StartsWith('/') ? path : "/" + path);
    }
}
