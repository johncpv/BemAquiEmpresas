using BemAqui.Inventory.Contracts;

namespace BemAqui.Engineering.Application;

/// <summary>Posição do item no estoque somada nos locais (ADR 0023 §2): físico, reservado e disponível (físico − reservado).</summary>
public sealed record BomStockPosition(decimal OnHand, decimal Reserved, decimal Available);

/// <summary>
/// O que a BOM lê do estoque (ENG-02 CA 3, 8, 14; custo previsto). <see cref="StockBalancesBomStock"/> liga o saldo ao motor de estoque
/// (EST-01a: <c>IStockBalances</c> e o custo médio do item) e às reservas (EST-02: "vai reservar" e o já baixado por OS); o
/// <see cref="DisconnectedBomStock"/> deixa tudo desligado.
/// </summary>
public interface IBomStock
{
    /// <summary>Saldos ligados: com false, a tela diz que o saldo ainda não aparece e a prévia não calcula falta.</summary>
    bool Connected { get; }

    /// <summary>Reservas ligadas (EST-02): com false, a prévia não diz quanto vai reservar.</summary>
    bool ReservationsConnected { get; }

    /// <summary>Posição por item (todos os locais); item sem posição fica fora (vale zero).</summary>
    Task<IReadOnlyDictionary<Guid, BomStockPosition>> PositionsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);

    /// <summary>Custo médio por item (<c>numeric(18,6)</c>), para o custo previsto da BOM; nunca gravado nem no PWA.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> AverageCostsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);

    /// <summary>Quanto já foi baixado para a OS por item (EST-03, lido pela EST-02); null enquanto as reservas não existem.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>?> IssuedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Saldo da BOM pelo motor de estoque (EST-01a): <see cref="IStockBalances"/> somado nos locais e o custo médio pelo <see cref="IItemCosts"/>.
/// Reservas ligadas (EST-02): "vai reservar" na prévia e o baixado por OS pelo <see cref="IStockReservations.ForOrderAsync"/>.
/// </summary>
public sealed class StockBalancesBomStock(IStockBalances balances, IItemCosts costs, IStockReservations reservations) : IBomStock
{
    public bool Connected => true;

    public bool ReservationsConnected => true;

    public async Task<IReadOnlyDictionary<Guid, BomStockPosition>> PositionsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count == 0)
        {
            return new Dictionary<Guid, BomStockPosition>();
        }

        return Sum(await balances.GetAsync(itemIds, null, cancellationToken));
    }

    public Task<IReadOnlyDictionary<Guid, decimal>> AverageCostsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default) =>
        costs.AverageCostsAsync(itemIds, cancellationToken);

    /// <summary>Baixado para a OS por item (EST-03 grava, a EST-02 expõe), somado nos locais; item sem reserva fica fora (vale zero).</summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>?> IssuedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
        (await reservations.ForOrderAsync(productionOrderId, cancellationToken)).GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => r.Issued));

    /// <summary>Soma das posições do item em todos os locais.</summary>
    public static IReadOnlyDictionary<Guid, BomStockPosition> Sum(IEnumerable<StockAvailability> rows) =>
        rows.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => new BomStockPosition(g.Sum(r => r.OnHand), g.Sum(r => r.Reserved), g.Sum(r => r.Available)));
}

/// <summary>Estoque desligado (testes e tenants sem o motor): sem saldo, sem custo e sem reservas.</summary>
public sealed class DisconnectedBomStock : IBomStock
{
    private static readonly IReadOnlyDictionary<Guid, BomStockPosition> NoPositions = new Dictionary<Guid, BomStockPosition>();
    private static readonly IReadOnlyDictionary<Guid, decimal> NoCosts = new Dictionary<Guid, decimal>();

    public bool Connected => false;

    public bool ReservationsConnected => false;

    public Task<IReadOnlyDictionary<Guid, BomStockPosition>> PositionsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default) => Task.FromResult(NoPositions);

    public Task<IReadOnlyDictionary<Guid, decimal>> AverageCostsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default) => Task.FromResult(NoCosts);

    public Task<IReadOnlyDictionary<Guid, decimal>?> IssuedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, decimal>?>(null);
}
