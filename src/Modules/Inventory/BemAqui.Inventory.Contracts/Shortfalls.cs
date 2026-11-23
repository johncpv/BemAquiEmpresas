using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Contracts;

/// <summary>
/// Falta de saldo de uma saída que não pode ser recusada (PDV-05, opção B da PP-02): a venda do caixa. <c>Quantity</c> é o que não saiu, na
/// unidade de estoque (<c>decimal(18,4)</c>, positivo); <c>SourceNumber</c>, o número da venda; <c>OccurredAt</c>, quando ela aconteceu.
/// </summary>
public sealed record StockShortfallInput(Guid ItemId, Guid LocationId, decimal Quantity, string SourceType, Guid SourceId, string SourceNumber, DateTimeOffset OccurredAt);

/// <summary>
/// Pendências de falta de saldo no estoque (PDV-05): o motor (<see cref="IStockMovements"/>) e o <c>CHECK</c> do saldo não mudam; a falta fica
/// estacionada aqui até alguém regularizar o estoque e aplicar a baixa (Estoque → Divergências). Tudo na transação de quem chama.
/// </summary>
public interface IStockShortfalls
{
    /// <summary>Abre a falta (idempotente por origem, item e local: a segunda entrega não abre outra).</summary>
    Task<Result> OpenAsync(StockShortfallInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// A origem foi cancelada (CA 9): a falta aberta fecha sem movimento (o estorno reduz a falta antes de dar entrada) e a baixa já aplicada é
    /// estornada pelo próprio movimento. Idempotente. Devolve quanto de falta aberta foi fechado, por item.
    /// </summary>
    Task<Result<IReadOnlyDictionary<Guid, decimal>>> CancelSourceAsync(string sourceType, Guid sourceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Baixa de uma falta aplicada depois (outbox): só ids e valores (ADR 0037 §2). O dono da origem (a venda) marca a linha como baixada; o custo
/// unitário (<c>decimal(18,6)</c>) é o custo médio da saída.
/// </summary>
public sealed record StockShortfallApplied(
    Guid ShortfallId, string SourceType, Guid SourceId, Guid ItemId, Guid LocationId, decimal Quantity, Guid MovementId, decimal UnitCost, DateTimeOffset At)
    : IIntegrationEvent;

/// <summary>
/// Saldo disponível de um local para o caixa da loja (PDV-05, PP-02: sem rede, o caixa usa o último saldo recebido). <c>ChangedSince</c> nulo
/// devolve todas as posições do local; com valor, só as dos itens com movimento lançado depois dele (o caixa aplica por cima do que tem).
/// </summary>
public interface IStockLocationFeed
{
    Task<IReadOnlyList<StockAvailability>> LocationBalancesAsync(Guid locationId, DateTimeOffset? changedSince, CancellationToken cancellationToken = default);
}
