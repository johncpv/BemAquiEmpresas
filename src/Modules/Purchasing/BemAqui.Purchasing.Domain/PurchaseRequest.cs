using BemAqui.Purchasing.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Domain;

/// <summary>
/// Solicitação de compra (<c>purchasing.purchase_requests</c>, CMP-01): número <c>SC-AAAA-0001</c>, origem, destino (projeto ou
/// estoque geral) e a OS da falta. Sem aprovação e sem efeito de estoque nem financeiro (ponto 32): o controle nasce no pedido. A
/// situação é derivada das linhas (<see cref="PurchaseRequestRules.DeriveStatus"/>).
/// </summary>
[DocumentName("A solicitação")]
public sealed class PurchaseRequest : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; init; } = string.Empty;

    public PurchaseRequestOrigin Origin { get; init; }

    /// <summary>Destino: o projeto; null = estoque geral (R-CMP-6).</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>OS da falta (origem <c>BomShortage</c>); null nas demais.</summary>
    public Guid? ProductionOrderId { get; init; }

    public PurchaseRequestStatus Status { get; set; } = PurchaseRequestStatus.Open;

    public string? Notes { get; init; }

    /// <summary>Dia da execução do job do mínimo (origem <c>MinStock</c>): uma SC por execução do dia.</summary>
    public DateOnly? RunDate { get; init; }

    /// <summary>Quem enviou (manual); null quando o sistema abriu.</summary>
    public Guid? RequestedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Linha da solicitação (<c>purchasing.purchase_request_lines</c>, CMP-01): item, quantidade na unidade de estoque e a data de
/// necessidade. As automáticas guardam a OS e a linha da BOM (falta) ou só o item (mínimo); a linha editada à mão
/// (<see cref="ManuallyEdited"/>) não é mais sobrescrita pela recomposição nem pelo job (CA 13).
/// </summary>
[DocumentName("A linha da solicitação")]
public sealed class PurchaseRequestLine : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid RequestId { get; init; }

    /// <summary>Cópia da origem da SC, para as chaves das linhas automáticas (uma aberta por OS e item; uma aberta de mínimo por item).</summary>
    public PurchaseRequestOrigin Origin { get; init; }

    public int Sequence { get; init; }

    public Guid ItemId { get; init; }

    /// <summary>Na unidade de estoque (<c>numeric(18,4)</c>).</summary>
    public decimal Quantity { get; set; }

    /// <summary>Quantidade e unidade digitadas (manual), quando não era a de estoque.</summary>
    public decimal? EnteredQuantity { get; set; }

    public Guid? EnteredUnitId { get; set; }

    public DateOnly NeededBy { get; set; }

    public Guid? ProductionOrderId { get; init; }

    public Guid? BomLineId { get; set; }

    public Guid? ProjectId { get; init; }

    public PurchaseRequestStatus Status { get; set; } = PurchaseRequestStatus.Open;

    public bool ManuallyEdited { get; set; }

    /// <summary>"Calculado na implantação" (D3, padrão 11): nasceu na recomposição das faltas que já existiam.</summary>
    public bool CalculatedOnOnboarding { get; init; }

    /// <summary>Quanto já virou pedido (CMP-03a); 0 nesta story.</summary>
    public decimal OrderedQuantity { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Parâmetro de Compras (<c>purchasing.parameters</c>, ADR 0016 adendo M6): vazio = padrão. <c>default_lead_days</c> (15) é editado em
/// Configurações → Parâmetros com <c>settings.edit</c>; <c>shortages_backfilled</c> marca a recomposição da implantação (D3).
/// </summary>
public sealed class PurchasingParameter : Entity, IAuditable
{
    public string Key { get; init; } = string.Empty;

    public decimal? Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
