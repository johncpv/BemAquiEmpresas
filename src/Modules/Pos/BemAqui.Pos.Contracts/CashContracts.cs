using BemAqui.SharedKernel;

namespace BemAqui.Pos.Contracts;

/// <summary>
/// Movimento de dinheiro da sessão de caixa gravado (outbox, CX-01 CA 9): o Financeiro faz a transferência entre o Cofre e a conta Caixa do PDV
/// (<paramref name="CashAccountId"/>, a do caixa). <paramref name="Kind"/>: <c>Opening</c> (Cofre → Caixa), <c>CashOut</c> (sangria, Caixa → Cofre)
/// ou <c>CashIn</c> (suprimento, Cofre → Caixa). Só ids e valores (ADR 0037 §2): o motivo fica no movimento do Pos. Reprocessar não duplica (a
/// transferência é única por <paramref name="MovementId"/>).
/// </summary>
public sealed record CashMovementRecorded(
    Guid MovementId,
    Guid SessionId,
    Guid StoreId,
    Guid RegisterId,
    int RegisterNumber,
    Guid CashAccountId,
    string Kind,
    decimal Amount,
    DateOnly BusinessDate,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public const string Opening = "Opening";
    public const string CashOut = "CashOut";
    public const string CashIn = "CashIn";

    /// <summary>CX-02 CA 12: recolhimento do fechamento (Caixa do PDV → Cofre).</summary>
    public const string Deposit = "Deposit";

    /// <summary>CX-02 CA 10: o recolhimento volta ao caixa na reabertura (Cofre → Caixa do PDV).</summary>
    public const string DepositReturn = "DepositReturn";
}

/// <summary>
/// Venda descartada no caixa (outbox, OFF-02): a venda ganhou número e saiu sem pagamento. Só ids (ADR 0037 §2): o motivo fica no registro de
/// eventos do caixa e no relatório de exceções por operador.
/// </summary>
public sealed record PosSaleDiscarded(Guid SaleId, string Number, Guid StoreId, Guid RegisterId, Guid? CashSessionId, Guid? OperatorId, DateTimeOffset DiscardedAt) : IIntegrationEvent;

/// <summary>Quebra de uma forma de pagamento no fechamento: esperado, contado e a diferença (sobra positiva, falta negativa). Só ids e valores.</summary>
public sealed record CashSessionBreak(Guid? PaymentMethodId, decimal Expected, decimal Counted, decimal Difference);

/// <summary>
/// Sessão de caixa fechada (outbox, CX-02): publicada quando a sessão chega a <c>Closed</c> (não em <c>Counted</c>), direto ou pela aprovação do
/// gerente. O Financeiro (CX-03) lança os resumos por forma e a quebra. Só ids e valores (ADR 0037 §2): os motivos ficam no <c>Pos</c>.
/// </summary>
public sealed record CashSessionClosed(
    Guid SessionId,
    Guid StoreId,
    Guid RegisterId,
    Guid CashAccountId,
    Guid OperatorId,
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    DateOnly BusinessDate,
    decimal KeptAmount,
    decimal DepositAmount,
    int ReopenCount,
    IReadOnlyList<CashSessionBreak> Breaks) : IIntegrationEvent;

/// <summary>Sessão de caixa reaberta (outbox, CX-02 CA 10): a CX-03 estorna os lançamentos do fechamento. Só ids.</summary>
public sealed record CashSessionReopened(Guid SessionId, Guid StoreId, Guid RegisterId, Guid ReopenedBy, DateTimeOffset ReopenedAt, int ReopenCount) : IIntegrationEvent;
