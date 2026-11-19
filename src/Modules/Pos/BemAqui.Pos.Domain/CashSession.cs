using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Pos.Domain;

/// <summary>Situação da sessão de caixa (turno). A CX-02 acrescenta a contagem, a revisão e o fechamento.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CashSessionStatus>))]
public enum CashSessionStatus
{
    Open,
    Closed,
}

/// <summary>
/// Sessão de caixa (<c>pos.cash_sessions</c>, CX-01): o turno de um operador num caixa, do fundo de troco ao fechamento (CX-02). Nasce no aparelho
/// (id Guid v7 do aparelho, local-first) e sobe pela fila de saída (OFF-02). Uma sessão <c>Open</c> por caixa (índice único parcial). O total esperado
/// em dinheiro nunca fica aqui: é calculado no fechamento (conferência cega, CX-02).
/// </summary>
[DocumentName("Sessão de caixa")]
public sealed class CashSession : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid StoreId { get; init; }

    public Guid RegisterId { get; init; }

    /// <summary>Época do aparelho no caixa quando a sessão abriu (PDV-01): separa os aparelhos que passaram pelo caixa.</summary>
    public int DeviceEpoch { get; init; }

    /// <summary>Quem abriu (o operador da sessão por PIN do aparelho).</summary>
    public Guid OperatorId { get; init; }

    /// <summary>Fundo de troco, <c>numeric(18,2)</c>; zero é permitido (o aparelho pede confirmação).</summary>
    public decimal OpeningAmount { get; init; }

    /// <summary>Instante da abertura no aparelho (UTC).</summary>
    public DateTimeOffset OpenedAt { get; init; }

    public CashSessionStatus Status { get; set; } = CashSessionStatus.Open;

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Quando o servidor recebeu a abertura.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    /// <summary>Sessão com o id gerado no aparelho (local-first): reenviar a abertura acha a mesma sessão.</summary>
    public static CashSession FromDevice(Guid id, Guid storeId, Guid registerId, int deviceEpoch, Guid operatorId, decimal openingAmount, DateTimeOffset openedAt, DateTimeOffset now) =>
        new()
        {
            Id = id, StoreId = storeId, RegisterId = registerId, DeviceEpoch = deviceEpoch, OperatorId = operatorId, OpeningAmount = openingAmount,
            OpenedAt = openedAt, CreatedAt = now,
        };
}

/// <summary>Tipo do movimento de dinheiro da sessão: a abertura (fundo de troco), a sangria (retirada) e o suprimento (reforço).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CashMovementKind>))]
public enum CashMovementKind
{
    Opening,
    CashOut,
    CashIn,
}

/// <summary>
/// Movimento de dinheiro da sessão (<c>pos.cash_movements</c>, CX-01 CA 4, 5 e 9): fundo de troco, sangria ou suprimento, com o operador, o motivo
/// (sangria e suprimento) e o instante do aparelho. Idempotente pelo id do aparelho. Vira uma transferência no Financeiro (Cofre ↔ Caixa do PDV).
/// </summary>
[DocumentName("Movimento de caixa")]
public sealed class CashMovement : Entity, IAuditable
{
    public const int MaxReasonLength = 200;

    public Guid SessionId { get; init; }

    public CashMovementKind Kind { get; init; }

    /// <summary>Valor, <c>numeric(18,2)</c>: maior que zero (a abertura pode ser zero).</summary>
    public decimal Amount { get; init; }

    public string? Reason { get; init; }

    /// <summary>Quem registrou (toda ação com dinheiro leva o operador).</summary>
    public Guid OperatorId { get; init; }

    /// <summary>Instante do aparelho (UTC).</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// O aparelho marcou a sangria acima do dinheiro que ele calculou (CA 4): é aceita sem revelar o saldo e leva o fechamento a revisão (CX-02). É só
    /// um indício: o servidor refaz a conta na subida (OFF-02) e no fechamento.
    /// </summary>
    public bool AboveExpected { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Movimento com o id gerado no aparelho: reenviar acha o mesmo movimento.</summary>
    public static CashMovement FromDevice(Guid id, Guid sessionId, CashMovementKind kind, decimal amount, string? reason, Guid operatorId, DateTimeOffset occurredAt, bool aboveExpected, DateTimeOffset now) =>
        new()
        {
            Id = id, SessionId = sessionId, Kind = kind, Amount = amount, Reason = reason, OperatorId = operatorId, OccurredAt = occurredAt,
            AboveExpected = aboveExpected, CreatedAt = now,
        };

    public static bool NeedsReason(CashMovementKind kind) => kind is CashMovementKind.CashOut or CashMovementKind.CashIn;
}
