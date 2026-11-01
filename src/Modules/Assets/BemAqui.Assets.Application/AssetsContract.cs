using BemAqui.Assets.Contracts;
using BemAqui.HR.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary><see cref="IAssets"/> para Produção e Planejamento (CA 15): só id, etiqueta, nome, tipo, centro e situação.</summary>
public sealed class AssetsContract(IAssetsDb db) : IAssets
{
    public async Task<IReadOnlyList<AssetInfo>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        var list = ids.Distinct().ToList();
        return await db.Assets.AsNoTracking().Where(a => list.Contains(a.Id))
            .Select(a => new AssetInfo(a.Id, a.Tag, a.Name, a.Type, a.WorkCenterId, a.Status)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssetInfo>> ByWorkCenterAsync(Guid workCenterId, CancellationToken cancellationToken = default) =>
        await db.Assets.AsNoTracking().Where(a => a.WorkCenterId == workCenterId && a.Status != AssetStatus.Disposed).OrderBy(a => a.Tag)
            .Select(a => new AssetInfo(a.Id, a.Tag, a.Name, a.Type, a.WorkCenterId, a.Status)).ToListAsync(cancellationToken);
}

/// <summary>
/// Provedores de uso do patrimônio (CA 14, ADR 0016): o bem guarda o id do funcionário (responsável), do centro de trabalho e do
/// terceiro (fornecedor); com bem não excluído ou movimento (PAT-02: antes e depois, comprador) apontando para ele, o dono só inativa (ou desliga).
/// </summary>
public sealed class AssetUsage(IAssetsDb db) : IPartyUsageProvider, IWorkCenterUsageProvider, IEmployeeUsageProvider
{
    private const string Usage = "bens do patrimônio";

    async Task<string?> IPartyUsageProvider.UsageAsync(Guid partyId, CancellationToken cancellationToken) =>
        await db.Assets.AnyAsync(a => a.SupplierPartyId == partyId, cancellationToken) || await db.AssetMovements.AnyAsync(m => m.BuyerPartyId == partyId, cancellationToken)
        || await db.MaintenanceOrders.AnyAsync(o => o.SupplierPartyId == partyId, cancellationToken)
        || await db.MaintenancePlans.AnyAsync(p => p.DefaultSupplierPartyId == partyId, cancellationToken) ? Usage : null;

    async Task<string?> IWorkCenterUsageProvider.UsageAsync(Guid workCenterId, CancellationToken cancellationToken) =>
        await db.Assets.AnyAsync(a => a.WorkCenterId == workCenterId, cancellationToken)
        || await db.AssetMovements.AnyAsync(m => m.FromWorkCenterId == workCenterId || m.ToWorkCenterId == workCenterId, cancellationToken) ? Usage : null;

    async Task<string?> IEmployeeUsageProvider.UsageAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.Assets.AnyAsync(a => a.ResponsibleEmployeeId == employeeId, cancellationToken)
        || await db.AssetMovements.AnyAsync(m => m.FromEmployeeId == employeeId || m.ToEmployeeId == employeeId, cancellationToken) ? Usage : null;
}

/// <summary>PAT-01a CA 12 com a PAT-02: o bem com movimentação tem histórico e não é excluído (usa "Baixar bem").</summary>
public sealed class AssetMovementUsage(IAssetsDb db) : IAssetUsageProvider
{
    public async Task<string?> UsageAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        await db.AssetMovements.AnyAsync(m => m.AssetId == assetId, cancellationToken) ? "movimentações" : null;
}

/// <summary>PAT-01a CA 12 com a PAT-03a: o bem com plano, leitura do horímetro ou OS de manutenção tem histórico e não é excluído.</summary>
public sealed class MaintenanceUsage(IAssetsDb db) : IAssetUsageProvider
{
    public async Task<string?> UsageAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        await db.MaintenanceOrders.AnyAsync(o => o.AssetId == assetId, cancellationToken)
        || await db.MaintenancePlans.AnyAsync(p => p.AssetId == assetId, cancellationToken)
        || await db.AssetMeterReadings.AnyAsync(r => r.AssetId == assetId, cancellationToken) ? "manutenções" : null;
}
