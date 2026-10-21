using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<AmendmentStatus>))]
public enum AmendmentStatus
{
    Draft,
    PendingApproval,
    Approved,
    Rejected,
}

/// <summary>
/// Aditivo do contrato (<c>commercial.contract_amendments</c>, COM-07): muda valor, prazo e orçado sem tocar no contrato
/// original; aprovado, fica imutável. Número sequencial no contrato ("Aditivo 1").
/// </summary>
[DocumentName("O aditivo")]
public sealed class ContractAmendment : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid ContractId { get; init; }

    public int Number { get; init; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Variação do valor (positiva acrescenta marcos; negativa reduz títulos futuros).</summary>
    public decimal ValueDelta { get; set; }

    public int DaysDelta { get; set; }

    public AmendmentStatus Status { get; set; } = AmendmentStatus.Draft;

    /// <summary>Variação do orçado por natureza, em JSON (<c>{"Material": 100.00}</c>); sigilo comercial.</summary>
    [ProtectedSecret]
    public string BudgetDeltas { get; set; } = "{}";

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public Guid? ApprovedBy { get; set; }

    public bool SelfApproved { get; set; }

    public string? RejectionComment { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>Marco do acréscimo (<c>commercial.contract_amendment_milestones</c>): vira título a receber na aprovação.</summary>
public sealed class ContractAmendmentMilestone : Entity, IAuditable
{
    public Guid AmendmentId { get; init; }

    public int Sequence { get; init; }

    public string Description { get; init; } = string.Empty;

    public decimal Percent { get; init; }

    public decimal Amount { get; init; }

    public MilestoneTrigger Trigger { get; init; }

    public TriggerEvent? TriggerEvent { get; init; }

    public DateOnly? DueDate { get; init; }

    public int? DaysAfterEvent { get; init; }
}

/// <summary>Redução de um título futuro (<c>commercial.contract_amendment_reductions</c>), revalidada no Finance.</summary>
public sealed class ContractAmendmentReduction : Entity, IAuditable
{
    public Guid AmendmentId { get; init; }

    public Guid ReceivableId { get; init; }

    public decimal NewAmount { get; init; }
}
