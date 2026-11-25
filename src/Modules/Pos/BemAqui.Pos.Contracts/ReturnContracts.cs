using BemAqui.SharedKernel;

namespace BemAqui.Pos.Contracts;

/// <summary>
/// O caixa onde a devolução acontece (PDV-06): a loja e o caixa do aparelho (do cookie, nunca do corpo), o local de estoque da loja, os prazos da loja
/// (troca e validade do vale) e a sessão aberta do caixa, com o dinheiro que ela tem agora (fundo + vendas em dinheiro + suprimentos − sangrias −
/// devoluções em dinheiro). Sem sessão aberta (ou já em "Fechar caixa"), <c>CashSessionId</c> é nulo e o dinheiro, zero.
/// </summary>
public sealed record PosReturnContext(
    Guid StoreId,
    string StoreName,
    Guid RegisterId,
    int RegisterNumber,
    Guid DeviceId,
    Guid StockLocationId,
    int ExchangeDays,
    int StoreCreditValidityDays,
    Guid? CashSessionId,
    decimal CashInDrawer);

/// <summary>
/// Apoio do <c>Pos</c> à devolução do <c>Sales</c> (PDV-06): o <c>Sales</c> não lê as tabelas <c>pos.*</c>; pergunta e escreve por aqui, <b>na
/// transação de quem chama</b> (os métodos de escrita só rastreiam; quem chama grava), como o <see cref="IPosRegisters"/>.
/// </summary>
public interface IPosReturnSupport
{
    /// <summary>O caixa do aparelho do cookie <c>__Host-bq_pos</c>; null sem aparelho de caixa ativo.</summary>
    Task<PosReturnContext?> ContextAsync(string? posCookie, CancellationToken cancellationToken = default);

    /// <summary>
    /// Usa a aprovação de supervisor (ação <c>Return</c>) desta devolução: do mesmo caixa e operador, ligada à devolução, com o valor dela, ainda não usada
    /// e dentro do prazo. Marca o uso. Não grava.
    /// </summary>
    Task<Result<Guid>> UseApprovalAsync(Guid approvalId, Guid operatorId, Guid registerId, Guid returnId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devolução em dinheiro: o movimento <c>Refund</c> na sessão (sai da gaveta, entra no esperado do fechamento) e o <c>CashMovementRecorded</c> no outbox.
    /// O id do movimento é o da devolução (reenviar não duplica). Não grava.
    /// </summary>
    Task RecordCashRefundAsync(Guid sessionId, Guid returnId, string returnNumber, decimal amount, Guid operatorId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revisão de segurança (achado 1): toma a trava do caixa (a mesma da subida dos eventos, que grava vendas, sangrias e suprimentos) na transação de
    /// quem chama e devolve o dinheiro na gaveta da sessão recalculado depois da trava; null se a sessão não está mais aberta (ou já em "Fechar caixa").
    /// </summary>
    Task<decimal?> LockDrawerAsync(Guid registerId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revisão de segurança (achado 5): usa a aprovação de supervisor da reemissão do código do vale-troca (ação <c>CreditReissue</c>): do mesmo caixa e
    /// operador, ligada à devolução que emitiu o vale e com o saldo do vale, ainda não usada e dentro do prazo. Marca o uso. Não grava.
    /// </summary>
    Task<Result<Guid>> UseReissueApprovalAsync(Guid approvalId, Guid operatorId, Guid registerId, Guid returnId, decimal amount, CancellationToken cancellationToken = default);
}
