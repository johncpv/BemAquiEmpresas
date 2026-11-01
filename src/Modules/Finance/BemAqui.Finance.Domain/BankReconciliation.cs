using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Como a conciliação foi feita (BNK-03b): sugestão aceita, escolha à mão, lançamento sem OP criado a partir da linha (FIN-04) ou baixa de
/// título a receber criada a partir da linha (FIN-01).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationKind>))]
public enum ReconciliationKind
{
    Suggested,
    Manual,
    BankEntry,
    Settlement,
}

/// <summary>Confiança da sugestão (CA 1): Alta ou Média. Linha "não verificada pelo banco" nunca tem Alta.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReconciliationConfidence>))]
public enum ReconciliationConfidence
{
    High,
    Medium,
}

/// <summary>
/// Conciliação bancária (<c>finance.bank_reconciliations</c>, BNK-03b): um vínculo entre linhas do extrato do banco e transações do sistema
/// da mesma conta (1×1, 1×N ou N×1, com a soma igual). Nunca apagada: desfazer marca quem, quando e o motivo, e solta as linhas e as
/// transações sem estornar nada (CA 10).
/// </summary>
[DocumentName("A conciliação")]
public sealed class BankReconciliation : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid BankAccountId { get; init; }

    public ReconciliationKind Kind { get; init; }

    public ReconciliationConfidence? Confidence { get; init; }

    /// <summary>Motivo da sugestão aceita ("Mesmo valor, 1 dia de diferença, CNPJ do fornecedor") ou "Escolhida à mão".</summary>
    public string? Reason { get; init; }

    /// <summary>Motivo de quem mexeu num período já fechado (CA 11).</summary>
    public string? ClosedPeriodReason { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? UndoneAt { get; private set; }

    public Guid? UndoneBy { get; private set; }

    public string? UndoReason { get; private set; }

    public uint Version { get; set; }

    public bool IsUndone => UndoneAt is not null;

    public void Undo(Guid? by, DateTimeOffset at, string reason)
    {
        UndoneAt = at;
        UndoneBy = by;
        UndoReason = reason;
    }
}

/// <summary>Vínculo de uma conciliação (<c>finance.bank_reconciliation_links</c>): a linha, a transação e o valor que coube ao par.</summary>
public sealed class BankReconciliationLink : Entity
{
    public Guid ReconciliationId { get; init; }

    public Guid StatementLineId { get; init; }

    public Guid TransactionId { get; init; }

    public decimal Amount { get; init; }
}

/// <summary>
/// Regra de tipo do extrato (<c>finance.bank_statement_rules</c>, BNK-03b CA 2): palavra da descrição e/ou tipo do banco → tipo do
/// lançamento sem OP e a conta do plano sugerida. Seed vazio (montado com extratos reais do Inter); editável com <c>finance.edit</c>.
/// </summary>
[DocumentName("A regra do extrato")]
public sealed class BankStatementRule : Entity, IAuditable, IConcurrencyVersioned
{
    /// <summary>Palavra ou trecho procurado na descrição da linha (sem diferenciar maiúsculas).</summary>
    public string? Keyword { get; set; }

    /// <summary>Tipo do lançamento no banco (Tarifa, Juros...).</summary>
    public StatementLineKind? LineKind { get; set; }

    public BankEntryKind EntryKind { get; set; }

    /// <summary>Conta do plano sugerida; sem ela, a mais usada para o tipo.</summary>
    public Guid? AccountId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public uint Version { get; set; }

    /// <summary>A regra vale para a linha? O sinal do tipo precisa ser o da linha; palavra e tipo do banco, quando houver, os dois.</summary>
    public bool Matches(string description, StatementLineKind kind, decimal amount)
    {
        if (!IsActive || amount == 0 || BankEntry.IsOutflow(EntryKind) != amount < 0 || (Keyword is null && LineKind is null))
        {
            return false;
        }

        if (LineKind is { } k && k != kind)
        {
            return false;
        }

        return Keyword is not { Length: > 0 } word || (description ?? string.Empty).Contains(word, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Fechamento da conciliação (<c>finance.bank_reconciliation_closings</c>, CA 11): conta, até a data, quem e quando, com os números do
/// fechamento. Depois dele, desfazer, ignorar linha ou lançar transação com data no período exige <c>finance.approve</c> e motivo.
/// </summary>
[DocumentName("O fechamento da conciliação")]
public sealed class BankReconciliationClosing : Entity, IAuditable
{
    public Guid BankAccountId { get; init; }

    public DateOnly UntilDate { get; init; }

    public int LineCount { get; init; }

    public decimal BankBalance { get; init; }

    public decimal SystemBalance { get; init; }

    public DateTimeOffset ClosedAt { get; init; }

    public Guid? ClosedBy { get; init; }
}
