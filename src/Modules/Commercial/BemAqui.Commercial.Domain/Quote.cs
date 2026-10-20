using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Domain;

/// <summary>Situação do orçamento (04-fluxos §1). Nesta story: Draft e Sent; as demais entram na COM-01b, COM-02 e COM-04a.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuoteStatus>))]
public enum QuoteStatus
{
    Draft,
    Sent,

    /// <summary>Perdido (COM-01b CA 11), com motivo.</summary>
    Rejected,

    /// <summary>Vencido sem resposta (COM-02 CA 10).</summary>
    Expired,

    /// <summary>Trocado por uma revisão mais recente (COM-02 CA 1).</summary>
    Superseded,

    /// <summary>Aprovado: virou contrato e projeto (COM-04a).</summary>
    Approved,
}

/// <summary>Modo de preço gravado no orçamento (P-14): o que valia quando ele nasceu.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuotePricingMode>))]
public enum QuotePricingMode
{
    Markup,
    Margin,
}

[JsonConverter(typeof(JsonStringEnumConverter<QuoteItemKind>))]
public enum QuoteItemKind
{
    Equipment,
    Product,
    Service,
}

/// <summary>
/// Orçamento (<c>commercial.quotes</c>, COM-01a, PRD §5.4): pré-proposta sem efeito financeiro. Número com revisão
/// (ADR 0016 adendo M7): <c>(number, revision)</c> único e um só <c>is_current</c> por número.
/// </summary>
[DocumentName("O orçamento")]
public sealed class Quote : Entity, IAuditable, IConcurrencyVersioned, ISoftDeletable
{
    public string Number { get; set; } = string.Empty;

    public int Revision { get; set; }

    public bool IsCurrent { get; set; } = true;

    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    public Guid PartyId { get; set; }

    public Guid? ContactId { get; set; }

    public Guid? SiteAddressId { get; set; }

    /// <summary>Prazo de entrega prometido, em dias corridos depois da aprovação.</summary>
    public int? DeliveryDays { get; set; }

    public string? Notes { get; set; }

    public QuotePricingMode PricingMode { get; set; }

    /// <summary>Sigilo comercial (D7): no histórico aparece só como alterado.</summary>
    [ProtectedSecret]
    public decimal TotalCost { get; set; }

    public decimal TotalPrice { get; set; }

    /// <summary>(preço − custo) ÷ preço, em %; nulo com preço zero. Sigilo comercial no histórico.</summary>
    [ProtectedSecret]
    public decimal? MarginPct { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Validade (COM-01b CA 8): hoje + <c>quotes.validity_days</c> ao nascer.</summary>
    public DateOnly? ValidUntil { get; set; }

    public QuoteLostReason? LostReason { get; set; }

    public string? LostDetail { get; set; }

    public DateTimeOffset? LostAt { get; set; }

    public Guid? LostBy { get; set; }

    /// <summary>Copiado de outro orçamento (CA 13); não é revisão.</summary>
    public Guid? CopiedFromQuoteId { get; set; }

    /// <summary>Revisão anterior (COM-02).</summary>
    public Guid? ParentQuoteId { get; set; }

    public DateTimeOffset? ExpiredAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public uint Version { get; set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>Item do orçamento (<c>commercial.quote_items</c>): custo unitário = Σ linhas da composição para uma unidade.</summary>
public sealed class QuoteItem : Entity, IAuditable
{
    public Guid QuoteId { get; init; }

    public int Sequence { get; set; }

    public QuoteItemKind Kind { get; set; }

    public Guid? ItemId { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    [ProtectedSecret]
    public decimal UnitCost { get; set; }

    /// <summary>Markup ou margem do item, conforme o modo do orçamento; nulo usa o % padrão do tenant.</summary>
    [ProtectedSecret]
    public decimal? Pct { get; set; }

    public decimal UnitPrice { get; set; }

    public bool PriceOverridden { get; set; }
}

/// <summary>
/// Linha da composição de custo (<c>commercial.quote_item_costs</c>): snapshot do custo unitário (6 casas); cargo, centro,
/// item e instalador são só ids (ADR 0006). Total = round(quantidade × custo unitário, 2).
/// </summary>
public sealed class QuoteItemCost : Entity, IAuditable
{
    public Guid QuoteItemId { get; init; }

    public CostType CostType { get; init; }

    public string? Description { get; init; }

    public Guid? ItemId { get; init; }

    public Guid? WorkCenterId { get; init; }

    public Guid? PositionId { get; init; }

    public Guid? PartyId { get; init; }

    /// <summary>Quantidade (material) ou horas (mão de obra); 1 nas linhas de valor.</summary>
    public decimal Quantity { get; init; }

    public decimal UnitCost { get; set; }

    public decimal Total { get; set; }
}

/// <summary>Parâmetro do Commercial (<c>commercial.parameters</c>, ADR 0016 adendo M6); vazio = não definido.</summary>
public sealed class CommercialParameter : Entity, IAuditable
{
    public string Key { get; init; } = string.Empty;

    public decimal? Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
