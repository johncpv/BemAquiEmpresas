namespace BemAqui.Pos.Contracts;

/// <summary>Um evento do aparelho dentro da venda (PDV-07 CA 4), em ordem: o tipo (<c>SaleStarted</c>, <c>LineAdded</c>…), o instante no aparelho e quem.</summary>
public sealed record PosTrailEvent(DateTimeOffset At, string Type, Guid? OperatorId);

/// <summary>Subida de um evento da venda (venda, cancelamento, impressão): quando foi feito no aparelho e quando chegou ao servidor, e como entrou.</summary>
public sealed record PosTrailSync(Guid EventId, string Kind, DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt, string Outcome, bool ChainOk);

/// <summary>Aprovação de supervisor ligada à venda (PDV-04 e OFF-03): ação, operador, supervisor, motivo, antes e depois, com ou sem rede.</summary>
public sealed record PosTrailApproval(Guid Id, string Action, Guid OperatorId, Guid SupervisorId, string Reason, decimal BeforeAmount, decimal AfterAmount, bool Online,
    DateTimeOffset ApprovedAt);

/// <summary>Impressão do cupom da venda: a 1ª via (<c>Receipt</c>) e as reimpressões (<c>Reprint</c>, com a via).</summary>
public sealed record PosTrailPrint(Guid Id, string Kind, int Copy, Guid OperatorId, DateTimeOffset PrintedAt);

/// <summary>Pendência do PDV ligada à venda (OFF-02 CA 14): o tipo, se está aberta ou resolvida, se foi para a quarentena e o resumo.</summary>
public sealed record PosTrailDivergence(Guid Id, string Kind, string Status, bool Quarantined, string Summary, DateTimeOffset OccurredAt);

/// <summary>A trilha da venda do lado do caixa (PDV-07 CA 4). Vazia nas vendas que não vieram do PDV.</summary>
public sealed record PosSaleTrail(
    IReadOnlyList<PosTrailEvent> DeviceEvents,
    IReadOnlyList<PosTrailSync> Syncs,
    IReadOnlyList<PosTrailApproval> Approvals,
    IReadOnlyList<PosTrailPrint> Prints,
    IReadOnlyList<PosTrailDivergence> Divergences);

/// <summary>
/// Sessão de caixa aberta (PDV-07 CA 8): a loja, o caixa, o operador, desde quando, o último contato do aparelho (último evento recebido, última
/// sincronização ou último sinal) e o alerta de cancelamentos da loja (PP-22, em % das vendas do turno).
/// </summary>
public sealed record PosOpenSession(Guid SessionId, Guid StoreId, string StoreName, Guid RegisterId, int RegisterNumber, Guid OperatorId, DateTimeOffset OpenedAt,
    DateTimeOffset? LastContactAt, decimal CancelAlertPercent);

/// <summary>
/// Leituras do PDV para a retaguarda de vendas e os cards do Início (PDV-07, ADR 0028): o <c>Sales</c> e o <c>Dashboard</c> não leem tabela do
/// <c>Pos</c>; perguntam por aqui. Sem dado pessoal além dos ids.
/// </summary>
public interface IPosBackoffice
{
    /// <summary>A trilha da venda no caixa; <paramref name="registerId"/> é o caixa da venda (sem caixa, a trilha volta vazia).</summary>
    Task<PosSaleTrail> SaleTrailAsync(Guid saleId, Guid? registerId, CancellationToken cancellationToken = default);

    /// <summary>As sessões de caixa abertas agora, de todas as lojas ativas, da mais antiga para a mais nova.</summary>
    Task<IReadOnlyList<PosOpenSession>> OpenSessionsAsync(CancellationToken cancellationToken = default);
}
