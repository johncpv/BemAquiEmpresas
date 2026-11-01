using BemAqui.Assets.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Assets.Domain;

/// <summary>
/// Movimento do bem (<c>assets.asset_movements</c>, PAT-02): transferência de local, responsável e centro, baixa e reativação, com os
/// valores de antes e de depois. Imutável: nasce só pelas fábricas, sem setter público, e não tem rota de edição nem de exclusão
/// (CA 3); o erro se corrige com outro movimento. O motivo é texto livre e pode citar pessoas: <c>[PersonalData]</c> (ADR 0037).
/// </summary>
[DocumentName("Movimentação do bem")]
public sealed class AssetMovement : Entity, IAuditable
{
    private AssetMovement()
    {
    }

    public Guid AssetId { get; private init; }

    public AssetMovementKind Kind { get; private init; }

    public Guid? FromSiteId { get; private init; }

    public Guid? ToSiteId { get; private init; }

    public Guid? FromEmployeeId { get; private init; }

    public Guid? ToEmployeeId { get; private init; }

    public Guid? FromWorkCenterId { get; private init; }

    public Guid? ToWorkCenterId { get; private init; }

    /// <summary>Data do movimento (<c>date</c>), até hoje.</summary>
    public DateOnly MovedOn { get; private init; }

    [PersonalData]
    public string? Reason { get; private init; }

    public AssetDisposalReason? DisposalReason { get; private init; }

    /// <summary>Valor da venda (<c>numeric(18,2)</c>), só na baixa por venda e só para quem tem <c>assets.edit</c>.</summary>
    public decimal? DisposalValue { get; private init; }

    /// <summary>Comprador (<c>IParties</c>, papel Cliente), opcional, só na venda.</summary>
    public Guid? BuyerPartyId { get; private init; }

    public Guid? CreatedBy { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>Transferência (CA 1 e 4): o bem passa a mostrar os valores novos na mesma transação.</summary>
    public static AssetMovement Transfer(Asset asset, AssetPlacement to, DateOnly movedOn, string reason, Guid? by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(to);
        if (asset.Status == AssetStatus.Disposed)
        {
            throw new InvalidOperationException("Bem baixado não é movimentado.");
        }

        var movement = New(asset, AssetMovementKind.Transfer, to, movedOn, reason, by, at);
        asset.SiteId = to.SiteId;
        asset.ResponsibleEmployeeId = to.ResponsibleEmployeeId;
        asset.WorkCenterId = to.WorkCenterId;
        return movement;
    }

    /// <summary>Baixa (CA 6): o bem fica onde estava e passa a <c>Disposed</c>; valor e comprador só na venda.</summary>
    public static AssetMovement Disposal(Asset asset, AssetDisposalReason disposalReason, DateOnly movedOn, string? reason, decimal? value, Guid? buyer, Guid? by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Status == AssetStatus.Disposed)
        {
            throw new InvalidOperationException("O bem já está baixado.");
        }

        var sale = disposalReason == AssetDisposalReason.Sale;
        var movement = new AssetMovement
        {
            AssetId = asset.Id, Kind = AssetMovementKind.Disposal, FromSiteId = asset.SiteId, ToSiteId = asset.SiteId, FromEmployeeId = asset.ResponsibleEmployeeId,
            ToEmployeeId = asset.ResponsibleEmployeeId, FromWorkCenterId = asset.WorkCenterId, ToWorkCenterId = asset.WorkCenterId, MovedOn = movedOn, Reason = reason,
            DisposalReason = disposalReason, DisposalValue = sale ? value : null, BuyerPartyId = sale ? buyer : null, CreatedBy = by, CreatedAt = at,
        };
        asset.Status = AssetStatus.Disposed;
        return movement;
    }

    /// <summary>Reativação (CA 8): o bem baixado por engano volta a <c>Active</c>; nada é apagado.</summary>
    public static AssetMovement Reactivation(Asset asset, DateOnly movedOn, string reason, Guid? by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Status != AssetStatus.Disposed)
        {
            throw new InvalidOperationException("Só o bem baixado é reativado.");
        }

        var movement = New(asset, AssetMovementKind.Reactivation, AssetPlacement.Of(asset), movedOn, reason, by, at);
        asset.Status = AssetStatus.Active;
        return movement;
    }

    private static AssetMovement New(Asset asset, AssetMovementKind kind, AssetPlacement to, DateOnly movedOn, string reason, Guid? by, DateTimeOffset at) => new()
    {
        AssetId = asset.Id, Kind = kind, FromSiteId = asset.SiteId, ToSiteId = to.SiteId, FromEmployeeId = asset.ResponsibleEmployeeId, ToEmployeeId = to.ResponsibleEmployeeId,
        FromWorkCenterId = asset.WorkCenterId, ToWorkCenterId = to.WorkCenterId, MovedOn = movedOn, Reason = reason, CreatedBy = by, CreatedAt = at,
    };
}

/// <summary>Onde o bem está: local, responsável e centro de trabalho.</summary>
public sealed record AssetPlacement(Guid? SiteId, Guid? ResponsibleEmployeeId, Guid? WorkCenterId)
{
    public static AssetPlacement Of(Asset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new AssetPlacement(asset.SiteId, asset.ResponsibleEmployeeId, asset.WorkCenterId);
    }
}

/// <summary>Regras puras da movimentação (PAT-02).</summary>
public static class AssetMovementRules
{
    public const int ReasonMax = 200;

    /// <summary>Lote de transferência: até 500 bens por vez (como as etiquetas).</summary>
    public const int BatchMax = 500;

    /// <summary>Motivo sugerido no filtro "Responsável desligado" (CA 5).</summary>
    public const string TerminatedReason = "Responsável desligado";

    /// <summary>CA 2: nada mudou quando local, responsável e centro são os mesmos.</summary>
    public static bool Changes(AssetPlacement current, AssetPlacement target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);
        return current != target;
    }

    /// <summary>Destino do lote (CA 4): o que não foi escolhido fica como está em cada bem.</summary>
    public static AssetPlacement Merge(AssetPlacement current, Guid? siteId, Guid? responsibleEmployeeId, Guid? workCenterId)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new AssetPlacement(siteId ?? current.SiteId, responsibleEmployeeId ?? current.ResponsibleEmployeeId, workCenterId ?? current.WorkCenterId);
    }

    /// <summary>Data do movimento: até hoje e não antes do último movimento do bem (o histórico fica em ordem).</summary>
    public static bool IsValidDate(DateOnly movedOn, DateOnly today, DateOnly? lastMovement) =>
        movedOn <= today && movedOn.Year >= 1900 && (lastMovement is not { } last || movedOn >= last);

    /// <summary>Valor da venda (CA 6): de zero para cima, até 2 casas, cabe em <c>numeric(18,2)</c>.</summary>
    public static bool IsValidValue(decimal value) => AssetRules.IsValidValue(value);

    /// <summary>Descrição do título a receber da venda (CA 7).</summary>
    public static string SaleDescription(string tag, string name) => $"Venda do bem {tag} — {name}";

    /// <summary>Quem pode ser transferido ou baixado: o bem ativo ou em manutenção (CA 1 e 6).</summary>
    public static bool IsMovable(AssetStatus status) => status is AssetStatus.Active or AssetStatus.Maintenance;
}
