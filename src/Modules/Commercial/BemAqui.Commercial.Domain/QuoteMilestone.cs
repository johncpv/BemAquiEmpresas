using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Domain;

/// <summary>
/// Marco de pagamento do orçamento (<c>commercial.quote_milestones</c>, COM-01b): vira <c>contract_milestones</c> linha a linha
/// na aprovação (COM-04a). O valor sai do % sobre o preço total e é refeito quando o preço muda (CA 6).
/// </summary>
public sealed class QuoteMilestone : Entity, IAuditable
{
    public Guid QuoteId { get; init; }

    public int Sequence { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Percent { get; set; }

    public decimal Amount { get; set; }

    public MilestoneTrigger Trigger { get; set; }

    public TriggerEvent? TriggerEvent { get; set; }

    public DateOnly? DueDate { get; set; }

    public int? DaysAfterEvent { get; set; }
}

/// <summary>Motivo da perda (COM-01b CA 11); o funil (COM-08) agrupa por ele.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QuoteLostReason>))]
public enum QuoteLostReason
{
    Price,
    Deadline,
    Competitor,
    CustomerGaveUp,
    NoAnswer,
    Other,
}
