using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Pos.Domain;

/// <summary>Ação do balcão que o supervisor aprova (PDV-04): o código é a ação do catálogo (<c>pos.{acao}</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SupervisorAction>))]
public enum SupervisorAction
{
    Discount,
    ChangePrice,
    CancelItem,
    CancelSale,

    /// <summary>PDV-06: devolução em dinheiro ou fora do prazo da loja (<c>pos.return</c> do supervisor); <c>SaleId</c> é o id da devolução.</summary>
    Return,

    /// <summary>PDV-06, revisão de segurança (achado 5): reemissão do código do vale-troca perdido (<c>pos.return</c> do supervisor); <c>SaleId</c> é o id da devolução que emitiu o vale.</summary>
    CreditReissue,
}

public static class SupervisorActions
{
    /// <summary>A permissão do catálogo que a ação pede (do operador, ou do supervisor que aprova).</summary>
    public static string Permission(SupervisorAction action) => action switch
    {
        SupervisorAction.Discount => "pos.discount",
        SupervisorAction.ChangePrice => "pos.change_price",
        SupervisorAction.CancelItem => "pos.cancel_item",
        SupervisorAction.CancelSale => "pos.cancel_sale",
        SupervisorAction.Return or SupervisorAction.CreditReissue => "pos.return",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static string Label(SupervisorAction action) => action switch
    {
        SupervisorAction.Discount => "Desconto acima do limite",
        SupervisorAction.ChangePrice => "Troca de preço",
        SupervisorAction.CancelItem => "Cancelamento de item",
        SupervisorAction.CancelSale => "Cancelamento de venda",
        SupervisorAction.Return => "Devolução (dinheiro ou fora do prazo)",
        SupervisorAction.CreditReissue => "Reemissão do código do vale-troca",
        _ => action.ToString(),
    };

    public static SupervisorAction? FromPermission(string permission) => permission switch
    {
        "pos.discount" => SupervisorAction.Discount,
        "pos.change_price" => SupervisorAction.ChangePrice,
        "pos.cancel_item" => SupervisorAction.CancelItem,
        "pos.cancel_sale" => SupervisorAction.CancelSale,
        "pos.return" => SupervisorAction.Return,
        _ => null,
    };
}

/// <summary>
/// Aprovação de supervisor no balcão (<c>pos.supervisor_approvals</c>, PDV-04 CA 10, exceção nomeada da PP-11 na ADR 0048 §5): o supervisor digitou o
/// PIN no aparelho do caixa, o servidor conferiu que ele tem a ação e que não é o operador, e gravou a trilha. Libera <b>só aquela ação</b>: o evento
/// do aparelho leva o id e a subida confere ação, venda, operador e supervisor, e marca o uso (uma venda só). Valores em reais, <c>numeric(18,2)</c>;
/// nada de dado pessoal além dos ids.
/// </summary>
[DocumentName("Aprovação de supervisor")]
public sealed class SupervisorApproval : Entity, IAuditable
{
    public const int MaxReasonLength = 140;
    public const int MaxRefundLength = 60;

    /// <summary>A ação aprovada tem de acontecer até este tempo depois da aprovação (revisão de segurança, achado 6); depois disso, quarentena.</summary>
    public static readonly TimeSpan MaxUseDelay = TimeSpan.FromMinutes(10);

    public Guid StoreId { get; init; }

    public Guid RegisterId { get; init; }

    public Guid DeviceId { get; init; }

    public SupervisorAction Action { get; init; }

    public Guid OperatorId { get; init; }

    public Guid SupervisorId { get; init; }

    public string Reason { get; init; } = string.Empty;

    /// <summary>A venda (no aparelho, o id da venda em andamento ou da concluída) e a linha, quando a ação é de uma linha.</summary>
    public Guid SaleId { get; init; }

    public Guid? LineId { get; init; }

    /// <summary>Antes e depois: o valor da linha ou da venda (desconto, cancelamento) ou o preço unitário (troca de preço).</summary>
    public decimal BeforeAmount { get; init; }

    public decimal AfterAmount { get; init; }

    /// <summary>
    /// Aprovada com rede (PDV-04). <c>false</c> (OFF-03 CA 7): aprovada sem rede no aparelho com o código do supervisor (PP-39) e conferida na subida
    /// (prova, pessoas, permissão no instante e limites da loja); o id é o do aparelho.
    /// </summary>
    public bool Online { get; init; } = true;

    /// <summary>OFF-03 CA 13: a sessão de caixa da aprovação sem rede (os limites por sessão somam por ela).</summary>
    public Guid? CashSessionId { get; init; }

    public DateTimeOffset ApprovedAt { get; init; }

    /// <summary>Quando a venda com a aprovação chegou ao servidor (o id não serve para outra venda nem outra ação).</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public Guid? UsedByEventId { get; private set; }

    /// <summary>Cancelamento de venda com cartão: o NSU do estorno na maquininha, que o operador informou.</summary>
    public string? RefundReference { get; private set; }

    public static SupervisorApproval Create(Guid storeId, Guid registerId, Guid deviceId, SupervisorAction action, Guid operatorId, Guid supervisorId, string reason,
        Guid saleId, Guid? lineId, decimal before, decimal after, DateTimeOffset at) =>
        new()
        {
            Id = Guid.CreateVersion7(), StoreId = storeId, RegisterId = registerId, DeviceId = deviceId, Action = action, OperatorId = operatorId, SupervisorId = supervisorId,
            Reason = reason, SaleId = saleId, LineId = lineId, BeforeAmount = before, AfterAmount = after, ApprovedAt = at,
        };

    /// <summary>OFF-03 CA 7: a aprovação feita sem rede, com o id gerado no aparelho, depois de conferida na subida.</summary>
    public static SupervisorApproval Offline(Guid id, Guid storeId, Guid registerId, Guid deviceId, Guid cashSessionId, SupervisorAction action, Guid operatorId,
        Guid supervisorId, string reason, Guid saleId, Guid? lineId, decimal before, decimal after, DateTimeOffset at) =>
        new()
        {
            Id = id, StoreId = storeId, RegisterId = registerId, DeviceId = deviceId, CashSessionId = cashSessionId, Action = action, OperatorId = operatorId,
            SupervisorId = supervisorId, Reason = reason, SaleId = saleId, LineId = lineId, BeforeAmount = before, AfterAmount = after, ApprovedAt = at, Online = false,
        };

    /// <summary>O valor que a aprovação mexe (o desconto, o item ou a venda cancelados): conta nos limites sem rede (OFF-03 CA 13).</summary>
    public decimal Impact => Math.Abs(BeforeAmount - AfterAmount);

    /// <summary>Marca o uso pelo evento da subida. O mesmo evento de novo (reenvio) não muda nada.</summary>
    public void Use(Guid eventId, DateTimeOffset at, string? refund = null)
    {
        UsedAt ??= at;
        UsedByEventId ??= eventId;
        RefundReference ??= string.IsNullOrWhiteSpace(refund) ? null : refund.Trim();
    }

    /// <summary>PDV-06: a devolução aprovada foi concluída com esta aprovação (o id da devolução fica no lugar do evento).</summary>
    public void UseForReturn(Guid returnId, DateTimeOffset at)
    {
        UsedAt ??= at;
        UsedByEventId ??= returnId;
    }
}
