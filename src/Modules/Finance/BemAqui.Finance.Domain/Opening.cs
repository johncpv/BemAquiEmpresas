using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Implantação financeira do tenant (<c>finance.opening_setup</c>, FIN-00): uma linha só, com a data de corte e a conclusão.
/// A data de corte não muda depois do primeiro movimento; concluída, novos títulos de implantação são recusados.
/// </summary>
[DocumentName("A implantação")]
public sealed class OpeningSetup : Entity, IAuditable, IConcurrencyVersioned
{
    public DateOnly? CutoverDate { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? CompletedBy { get; set; }

    /// <summary>Justificativa das diferenças da conferência (CA 10).</summary>
    public string? Notes { get; set; }

    public uint Version { get; set; }
}

public enum OpeningBatchKind
{
    Receivables,
    Payables,
}

/// <summary>Lote de títulos de implantação lançado de uma vez (<c>finance.opening_batches</c>, FIN-00 CA 5 e 6).</summary>
public sealed class OpeningBatch : Entity, IAuditable
{
    public OpeningBatchKind Kind { get; init; }

    public int Count { get; init; }

    public decimal Total { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }
}
