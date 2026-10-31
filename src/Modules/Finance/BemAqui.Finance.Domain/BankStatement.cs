using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>De onde veio a linha do extrato do banco (BNK-03a): a API do banco ou um arquivo OFX.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementSource>))]
public enum StatementSource
{
    Api,
    Ofx,
}

/// <summary>
/// Situação da linha do extrato do banco: <c>Pending</c> (a conciliar), <c>Reconciled</c> (BNK-03b), <c>Ignored</c> (com motivo) e
/// <c>Undone</c> (o lote foi desfeito: a linha sai do extrato, mas continua no banco de dados).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementLineStatus>))]
public enum StatementLineStatus
{
    Pending,
    Reconciled,
    Ignored,
    Undone,
}

/// <summary>Tipo do lançamento no banco (Pix, boleto, TED, tarifa…), pelo tipo do Inter ou pelo <c>TRNTYPE</c>/memo do OFX.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementLineKind>))]
public enum StatementLineKind
{
    Pix,
    Boleto,
    Ted,
    Doc,
    Transfer,
    Fee,
    Interest,
    Yield,
    Tax,
    Card,
    Check,
    Deposit,
    Reversal,
    Other,
}

/// <summary>O que disparou a importação: o job diário, o "Buscar agora" ou o envio de um arquivo.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StatementTrigger>))]
public enum StatementTrigger
{
    Scheduled,
    Manual,
    Upload,
}

/// <summary>
/// Lote de importação do extrato do banco (<c>finance.bank_statement_batches</c>, BNK-03a CA 12): uma execução da API (job ou "Buscar
/// agora") ou um arquivo OFX (anexo do dono <c>BankStatementBatch</c>, com o SHA-256 único por conta enquanto o lote não for desfeito).
/// É o histórico da importação: quem, quando, origem, período, hash, contagens e o motivo do desfazer.
/// </summary>
[DocumentName("O lote do extrato")]
public sealed class BankStatementBatch : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid BankAccountId { get; init; }

    public StatementSource Source { get; init; }

    public StatementTrigger Trigger { get; init; }

    public DateOnly? PeriodFrom { get; set; }

    public DateOnly? PeriodTo { get; set; }

    /// <summary>Anexo com o arquivo OFX bruto (dono <c>BankStatementBatch</c>); nulo na API.</summary>
    public Guid? AttachmentId { get; set; }

    public string? FileName { get; init; }

    public string? FileSha256 { get; init; }

    /// <summary>Falso no OFX de contingência numa conta com API (D13): as linhas não são verificadas pelo banco.</summary>
    public bool BankVerified { get; init; } = true;

    public int NewCount { get; set; }

    public int DuplicateCount { get; set; }

    public int RejectedCount { get; set; }

    /// <summary>Motivos das recusas e avisos (ex.: "3 linhas antes da data de corte"), sem dado de linha.</summary>
    public string? Notes { get; set; }

    public DateTimeOffset ImportedAt { get; init; }

    public Guid? ImportedBy { get; init; }

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

/// <summary>
/// Linha do extrato do banco (<c>finance.bank_statement_lines</c>, BNK-03a): importada, nunca editada nem apagada (CA 9). Só muda a
/// situação: ignorada com motivo, conciliada (BNK-03b) ou desfeita com o lote. Única por <c>(conta, origem, external_id)</c> entre as não
/// desfeitas (CA 2). A criação fica registrada no lote; a auditoria guarda só as mudanças (<see cref="IAuditUpdatesOnly"/>).
/// </summary>
[DocumentName("A linha do extrato")]
public sealed class BankStatementLine : Entity, IAuditUpdatesOnly, IConcurrencyVersioned
{
    public Guid BankAccountId { get; init; }

    public Guid BatchId { get; init; }

    public DateOnly Date { get; init; }

    /// <summary>Com sinal: positivo entrou, negativo saiu.</summary>
    public decimal Amount { get; init; }

    public string Description { get; init; } = string.Empty;

    public StatementLineKind Kind { get; init; }

    [PersonalData]
    public string? CounterpartyName { get; init; }

    [PersonalData]
    public string? CounterpartyDocument { get; init; }

    /// <summary>Id do lançamento no banco (<c>idTransacao</c> da API ou <c>FITID</c> do OFX; sem FITID, o hash da linha).</summary>
    public string ExternalId { get; init; } = string.Empty;

    public StatementSource Source { get; init; }

    public bool BankVerified { get; init; } = true;

    /// <summary>Ordem da linha no arquivo ou na resposta do banco.</summary>
    public int Sequence { get; init; }

    public StatementLineStatus Status { get; private set; } = StatementLineStatus.Pending;

    public string? IgnoreReason { get; private set; }

    public DateTimeOffset? IgnoredAt { get; private set; }

    public Guid? IgnoredBy { get; private set; }

    /// <summary>Transação do sistema casada na conciliação (BNK-03b).</summary>
    public Guid? MatchedTransactionId { get; private set; }

    public DateTimeOffset ImportedAt { get; init; }

    public uint Version { get; set; }

    public bool IsDebit => Amount < 0;

    public void Ignore(string reason, Guid? by, DateTimeOffset at)
    {
        Status = StatementLineStatus.Ignored;
        IgnoreReason = reason;
        IgnoredBy = by;
        IgnoredAt = at;
    }

    /// <summary>Conciliada (BNK-03b CA 4): a primeira transação do vínculo fica em <see cref="MatchedTransactionId"/>.</summary>
    public void Reconcile(Guid transactionId)
    {
        Status = StatementLineStatus.Reconciled;
        MatchedTransactionId = transactionId;
    }

    /// <summary>Conciliação desfeita (CA 10): volta a pendente.</summary>
    public void Unreconcile()
    {
        Status = StatementLineStatus.Pending;
        MatchedTransactionId = null;
    }
}

/// <summary>Saldo do banco num dia (<c>finance.bank_statement_balances</c>, BNK-03a CA 3): um por conta, dia e origem.</summary>
public sealed class BankStatementBalance : Entity
{
    public Guid BankAccountId { get; init; }

    public DateOnly Date { get; init; }

    public decimal Balance { get; set; }

    public StatementSource Source { get; init; }

    public Guid? BatchId { get; set; }

    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>
/// Contingência do OFX numa conta com API (<c>finance.bank_ofx_contingencies</c>, D13, BNK-03a CA 6): liberada por quem tem
/// <c>finance.approve</c>, com motivo e prazo de até 30 dias; as linhas do OFX entram "não verificadas pelo banco".
/// </summary>
[DocumentName("A contingência do OFX")]
public sealed class BankOfxContingency : Entity, IAuditable
{
    public Guid BankAccountId { get; init; }

    public Guid? GrantedBy { get; init; }

    public DateTimeOffset GrantedAt { get; init; }

    public string Reason { get; init; } = string.Empty;

    /// <summary>Último dia (inclusive, data de São Paulo) em que o OFX entra nesta conta.</summary>
    public DateOnly ValidUntil { get; init; }

    public bool IsValidOn(DateOnly today) => ValidUntil >= today;
}
