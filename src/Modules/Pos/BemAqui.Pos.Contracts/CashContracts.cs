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
}
