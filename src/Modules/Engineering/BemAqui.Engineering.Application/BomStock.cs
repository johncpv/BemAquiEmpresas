namespace BemAqui.Engineering.Application;

/// <summary>Posição do item no estoque somada nos locais (ADR 0023 §2): físico, reservado e disponível (físico − reservado).</summary>
public sealed record BomStockPosition(decimal OnHand, decimal Reserved, decimal Available);

/// <summary>
/// O que a BOM lê do estoque (ENG-02 CA 3, 8, 14; custo previsto): ponto de ligação pequeno, porque o motor de estoque (EST-01a:
/// <c>IStockBalances</c>) e as reservas (EST-02: <c>IStockReservations</c>) ainda não estão na main. Até lá vale
/// <see cref="DisconnectedBomStock"/>: a tela mostra a necessidade sem saldo, sem falta e sem o aviso de baixado. A EST-01a liga
/// <see cref="PositionsAsync"/> e <see cref="AverageCostsAsync"/>; a EST-02, <see cref="IssuedForOrderAsync"/>.
/// </summary>
public interface IBomStock
{
    /// <summary>Saldos ligados: com false, a tela diz que o saldo ainda não aparece e a prévia não calcula falta.</summary>
    bool Connected { get; }

    /// <summary>Posição por item (todos os locais); item sem posição fica fora (vale zero).</summary>
    Task<IReadOnlyDictionary<Guid, BomStockPosition>> PositionsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);

    /// <summary>Custo médio por item (<c>numeric(18,6)</c>), para o custo previsto da BOM; nunca gravado nem no PWA.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> AverageCostsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);

    /// <summary>Quanto já foi baixado para a OS por item (EST-03, lido pela EST-02); null enquanto as reservas não existem.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>?> IssuedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);
}

/// <summary>Estoque ainda não ligado (antes da EST-01a e da EST-02).</summary>
public sealed class DisconnectedBomStock : IBomStock
{
    private static readonly IReadOnlyDictionary<Guid, BomStockPosition> NoPositions = new Dictionary<Guid, BomStockPosition>();
    private static readonly IReadOnlyDictionary<Guid, decimal> NoCosts = new Dictionary<Guid, decimal>();

    public bool Connected => false;

    public Task<IReadOnlyDictionary<Guid, BomStockPosition>> PositionsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default) => Task.FromResult(NoPositions);

    public Task<IReadOnlyDictionary<Guid, decimal>> AverageCostsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default) => Task.FromResult(NoCosts);

    public Task<IReadOnlyDictionary<Guid, decimal>?> IssuedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, decimal>?>(null);
}
