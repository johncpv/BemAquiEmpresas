using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Pos.Domain;

/// <summary>
/// Situação da sessão de caixa (turno). CX-02: <c>Open → Counting</c> ("Fechar caixa") <c>→ Counted</c> (contagem confirmada; espera as vendas
/// da sessão subirem) <c>→ Closed</c> (quebra dentro da tolerância) ou <c>PendingReview</c> (acima dela, ou sangria acima do calculado)
/// <c>→ Closed</c> (aprovação). A última sessão <c>Closed</c> do caixa pode voltar a <c>Open</c> (reabertura).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CashSessionStatus>))]
public enum CashSessionStatus
{
    Open,
    Closed,
    Counting,
    Counted,
    PendingReview,
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

    /// <summary>CX-02: quem fechou (o operador que contou, ou quem aprovou o fechamento em revisão).</summary>
    public Guid? ClosedBy { get; set; }

    /// <summary>CX-02 CA 1: "Fechar caixa" no aparelho (instante do aparelho); depois dele nada entra na sessão.</summary>
    public DateTimeOffset? CountStartedAt { get; set; }

    /// <summary>CX-02 CA 4: a contagem vigente (id do aparelho) e quem contou. Uma só por fechamento; recontar é do supervisor.</summary>
    public Guid? CountId { get; set; }

    public Guid? CountedBy { get; set; }

    public DateTimeOffset? CountedAt { get; set; }

    /// <summary>Vendas que o aparelho concluiu na sessão: o fechamento espera todas subirem antes de calcular a quebra ("Aguardando envio das vendas").</summary>
    public int? DeviceSalesCount { get; set; }

    /// <summary>CX-02 CA 12: o fundo de troco que fica na gaveta para o próximo turno e o recolhimento ao Cofre (contado em dinheiro − fundo que fica).</summary>
    public decimal? KeptAmount { get; set; }

    public decimal? DepositAmount { get; set; }

    /// <summary>O movimento do recolhimento da contagem vigente (fica fora do esperado: o dinheiro foi contado antes de sair).</summary>
    public Guid? DepositMovementId { get; set; }

    /// <summary>CX-02 CA 6: por que a sessão foi para revisão.</summary>
    public CashReviewReasons ReviewReasons { get; set; }

    /// <summary>CX-02 CA 7: quem aprovou o fechamento em revisão, quando e o motivo.</summary>
    public Guid? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? ApprovalReason { get; set; }

    /// <summary>CX-02 CA 10: reaberturas (a última: quem, quando e o motivo; o histórico fica na auditoria).</summary>
    public int ReopenCount { get; set; }

    public Guid? ReopenedBy { get; set; }

    public DateTimeOffset? ReopenedAt { get; set; }

    public string? ReopenReason { get; set; }

    /// <summary>Quando o servidor recebeu a abertura.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    /// <summary>OFF-03 CA 6: a sessão foi aberta sem rede (entrada do operador conferida no aparelho).</summary>
    public bool OpenedOffline { get; init; }

    /// <summary>OFF-03 CA 8: a contagem do fechamento foi feita sem rede (o servidor calcula o esperado e a quebra quando tudo sobe).</summary>
    public bool ClosedOffline { get; set; }

    /// <summary>Sessão com o id gerado no aparelho (local-first): reenviar a abertura acha a mesma sessão.</summary>
    public static CashSession FromDevice(Guid id, Guid storeId, Guid registerId, int deviceEpoch, Guid operatorId, decimal openingAmount, DateTimeOffset openedAt, DateTimeOffset now,
        bool offline = false) =>
        new()
        {
            Id = id, StoreId = storeId, RegisterId = registerId, DeviceEpoch = deviceEpoch, OperatorId = operatorId, OpeningAmount = openingAmount,
            OpenedAt = openedAt, CreatedAt = now, OpenedOffline = offline,
        };
}

/// <summary>Tipo do movimento de dinheiro da sessão: a abertura (fundo de troco), a sangria (retirada) e o suprimento (reforço).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CashMovementKind>))]
public enum CashMovementKind
{
    Opening,
    CashOut,
    CashIn,

    /// <summary>CX-02 CA 12: recolhimento do fechamento (Caixa do PDV → Cofre): o dinheiro contado menos o fundo que fica.</summary>
    Deposit,

    /// <summary>CX-02 CA 10: a reabertura devolve o recolhimento à gaveta (Cofre → Caixa do PDV), para a nova contagem fechar.</summary>
    DepositReturn,

    /// <summary>PDV-06: devolução em dinheiro ao cliente (com supervisor): sai da gaveta e entra no esperado do fechamento.</summary>
    Refund,
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

    /// <summary>Efeito no dinheiro da gaveta (CX-02); o fundo de troco entra pela abertura da sessão, não por aqui.</summary>
    public static decimal DrawerEffect(CashMovementKind kind, decimal amount) => kind switch
    {
        CashMovementKind.CashIn or CashMovementKind.DepositReturn => amount,
        CashMovementKind.CashOut or CashMovementKind.Deposit or CashMovementKind.Refund => -amount,
        _ => 0m,
    };
}
