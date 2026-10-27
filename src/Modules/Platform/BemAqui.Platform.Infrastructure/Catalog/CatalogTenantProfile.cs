using BemAqui.Platform.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Infrastructure.Catalog;

internal sealed class CatalogTenantProfile(CatalogDbContext db) : ITenantProfile
{
    public async Task<string?> DocumentAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var document = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Document).FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(document) ? null : BrazilianDocuments.Normalize(document);
    }

    public async Task<FiscalEnvironmentAllowance> FiscalEnvironmentAllowedAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => (FiscalEnvironmentAllowance?)t.FiscalEnvironmentAllowed).FirstOrDefaultAsync(cancellationToken)
            ?? FiscalEnvironmentAllowance.None;
}
