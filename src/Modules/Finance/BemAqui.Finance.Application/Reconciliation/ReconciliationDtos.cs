using System.Text.Json.Serialization;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application.Reconciliation;

/// <summary>
/// Linha do banco na conciliação: contraparte inteira só com <c>finance.edit</c>; <see cref="Blocked"/> com o motivo quando quem olha
/// importou o OFX dela (segregação, CA 5).
/// </summary>
public sealed record ReconLine(
    Guid Id, DateOnly Date, decimal Amount, string Description, StatementLineKind Kind, string? CounterpartyName, string? CounterpartyDocument, StatementSource Source,
    bool BankVerified, StatementLineStatus Status, string? ImportedByName, bool Blocked, string? BlockedReason, uint Version);

/// <summary>Transação do sistema na conciliação, com o documento de origem e o link ("Ver origem").</summary>
public sealed record ReconTransaction(
    Guid Id, DateOnly Date, decimal Amount, string Description, TransactionType Type, string? Origin, string? Link, bool PendingReview, bool Reconciled, uint Version);

/// <summary>Uma linha por par (§4.4): o banco à esquerda, o sistema à direita, motivo e confiança no meio.</summary>
public sealed record ReconPair(ReconLine Line, ReconTransaction Transaction, ReconciliationConfidence Confidence, string Reason);

/// <summary>O que fazer com a linha sem par.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LineActionKind>))]
public enum LineActionKind
{
    /// <summary>"Lançar como {tipo} sem OP" (CA 2).</summary>
    BankEntry,

    /// <summary>"Baixar o título {número} de {cliente}" (CA 3).</summary>
    Settle,

    /// <summary>Saída que não é tarifa, IOF nem juros: só "Vincular a uma OP paga" ou "Abrir OP para regularizar" (CA 8).</summary>
    PaymentOrder,
}

/// <summary>
/// Sugestão para a linha sem par: lançar sem OP (tipo e conta do plano preenchidos pela regra ou pela conta mais usada), baixar o título
/// (número, cliente, saldo e versão para o diálogo da FIN-01) ou o caminho da OP.
/// </summary>
public sealed record LineAction(
    LineActionKind Kind, string Text, BankEntryKind? EntryKind, Guid? AccountId, string? AccountName, Guid? RuleId, Guid? ReceivableId, string? ReceivableNumber, string? CustomerName,
    decimal? ReceivableBalance, uint? ReceivableVersion);

/// <summary>Linha em "Sem par no banco": a sugestão de ação (ou nenhuma) e se o mesmo tipo se repete ("Criar regra a partir desta linha").</summary>
public sealed record ReconUnpairedLine(ReconLine Line, LineAction? Action, bool CanBankEntry, bool SuggestRule);

/// <summary>Transação em "No sistema, não no banco": há quantos dias espera a linha do banco; mais de 5 dias, alerta (CA 12).</summary>
public sealed record ReconUnpairedTransaction(ReconTransaction Transaction, int DaysWithoutBank, bool Stale);

/// <summary>Conciliação feita (para ver e desfazer): as linhas, as transações e quem fez.</summary>
public sealed record ReconDone(
    Guid Id, ReconciliationKind Kind, ReconciliationConfidence? Confidence, string? Reason, IReadOnlyList<ReconLine> Lines, IReadOnlyList<ReconTransaction> Transactions,
    string? CreatedByName, DateTimeOffset CreatedAt, bool InClosedPeriod, uint Version);

/// <summary>Barra de totais: pendentes, conciliadas, ignoradas e o saldo do banco contra o do sistema no último dia com saldo do banco.</summary>
public sealed record ReconTotals(int Pending, int Reconciled, int Ignored, DateOnly? BalanceDate, decimal? BankBalance, decimal? SystemBalance, decimal? Difference);

/// <summary>
/// Tela "Conciliação" da conta e do período: pares sugeridos, "Sem par no banco", "No sistema, não no banco", as conciliadas, os totais,
/// até quando a conta está conciliada (e fechada) e o teto do lançamento sem OP a partir de linha de OFX.
/// </summary>
public sealed record ReconciliationView(
    Guid AccountId, string AccountName, DateOnly From, DateOnly To, bool HasStatement, IReadOnlyList<ReconPair> Pairs, IReadOnlyList<ReconUnpairedLine> BankOnly,
    IReadOnlyList<ReconUnpairedTransaction> SystemOnly, IReadOnlyList<ReconDone> Done, ReconTotals Totals, DateOnly? ReconciledUntil, DateOnly? ClosedUntil,
    decimal OfxBankEntryCap, bool CounterpartyMasked);

public sealed record VersionedRef(Guid Id, uint Version);

/// <summary>
/// Conciliar (CA 4): uma linha com uma ou mais transações, ou várias linhas com uma transação. <see cref="Accepted"/> diz que a pessoa
/// aceitou a sugestão: o servidor confere que o par ainda é o sugerido e grava a sugestão; senão fica "Escolhida à mão".
/// </summary>
public sealed record ReconcileInput(IReadOnlyList<VersionedRef> Lines, IReadOnlyList<VersionedRef> Transactions, bool Accepted = false);

public sealed record ReconcilePairInput(Guid LineId, uint LineVersion, Guid TransactionId, uint TransactionVersion);

/// <summary>"Aceitar todas as de confiança alta" (CA 4): os pares que a tela mostrou.</summary>
public sealed record AcceptHighInput(DateOnly From, DateOnly To, IReadOnlyList<ReconcilePairInput> Pairs);

public sealed record RefusedPair(Guid LineId, string Reason);

public sealed record AcceptHighResult(int Reconciled, IReadOnlyList<RefusedPair> Refused);

public sealed record UndoReconciliationInput(string Reason, uint Version);

/// <summary>
/// "Lançar sem OP" a partir da linha (CA 6): o mesmo da FIN-04 com data e valor vindos da linha e travados; o motivo da data serve também
/// para o período fechado.
/// </summary>
public sealed record LineBankEntryInput(BankEntryKind Kind, Guid? AccountId, Guid? CostCenterId, string? Reason, string? BackdateReason, uint LineVersion);

/// <summary>"Baixar o título" a partir da linha (CA 7): o diálogo da FIN-01 com a data e a conta da linha; o total precisa ser o valor da linha.</summary>
public sealed record LineSettlementInput(
    Guid ReceivableId, decimal Principal, decimal Interest, decimal Fine, decimal Discount, string? BackdateReason, uint ReceivableVersion, uint LineVersion, bool CancelBillingSlip = false);

/// <summary>Resultado de conciliar: a conciliação e as linhas que ela fechou.</summary>
public sealed record ReconcileResult(Guid ReconciliationId, ReconciliationKind Kind, int Lines, int Transactions, decimal Amount);

/// <summary>"Fechar conciliação até dd/mm" (CA 11).</summary>
public sealed record CloseReconciliationInput(DateOnly Until);

public sealed record ReconciliationClosingView(Guid Id, DateOnly Until, int Lines, decimal BankBalance, decimal SystemBalance, string? ClosedByName, DateTimeOffset ClosedAt);

/// <summary>Prévia do fechamento para o <c>ConfirmDialog</c> (impacto em lista): linhas, totais e a diferença zero.</summary>
public sealed record ClosingPreview(
    DateOnly Until, DateOnly? From, bool Allowed, string? Refusal, int Lines, int Reconciled, int Ignored, decimal Credits, decimal Debits, decimal? BankBalance, decimal SystemBalance,
    decimal? Difference);

/// <summary>Uma entrada do histórico da linha ou da transação (CA 15).</summary>
public sealed record ReconHistoryEntry(
    Guid ReconciliationId, ReconciliationKind Kind, ReconciliationConfidence? Confidence, string? Reason, string? ClosedPeriodReason, string? CreatedByName, DateTimeOffset CreatedAt,
    string? UndoneByName, DateTimeOffset? UndoneAt, string? UndoReason, IReadOnlyList<ReconLine> Lines, IReadOnlyList<ReconTransaction> Transactions);

/// <summary>Histórico: quem importou a linha (quando é de linha) e as conciliações, a mais recente primeiro.</summary>
public sealed record ReconHistory(string? ImportedByName, DateTimeOffset? ImportedAt, StatementSource? Source, IReadOnlyList<ReconHistoryEntry> Entries);

public sealed record BankStatementRuleInput(string? Keyword, StatementLineKind? LineKind, BankEntryKind EntryKind, Guid? AccountId, bool IsActive = true, uint Version = 0);

public sealed record BankStatementRuleView(Guid Id, string? Keyword, StatementLineKind? LineKind, BankEntryKind EntryKind, Guid? AccountId, string? AccountName, bool IsActive, uint Version);
