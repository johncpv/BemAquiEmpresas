namespace BemAqui.Inventory.Application;

/// <summary>Linha da BOM liberada vigente: a necessidade da OS já na unidade de estoque (4 casas).</summary>
public sealed record ReleasedBomLine(Guid BomLineId, Guid ItemId, decimal RequiredQty);

/// <summary>Revisão liberada vigente da BOM de uma OS: número, quando foi liberada e as linhas.</summary>
public sealed record ReleasedBom(Guid BomId, int Revision, DateTimeOffset ReleasedAt, IReadOnlyList<ReleasedBomLine> Lines);

/// <summary>
/// Fonte da reconciliação das reservas (EST-02 CA 1, ADR 0023 §6): a BOM liberada vigente da OS. É a porta do Estoque para o
/// <c>IBoms.ReleasedForOrderAsync</c> da ENG-02, que ainda não está na <c>main</c>: até ela, vale <see cref="NoReleasedBomSource"/>
/// (nenhuma OS tem BOM liberada, o alvo é 0). Na integração com a ENG-02 entra um adaptador sobre o <c>IBoms</c> (ver o Change Log da
/// EST-02), sem mudar o reconciliador.
/// </summary>
public interface IReleasedBomSource
{
    /// <summary>A revisão liberada vigente (uma por OS) ou null, se a OS ainda não tem BOM liberada.</summary>
    Task<ReleasedBom?> ReleasedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>OS em aberto com BOM liberada, para a recomposição da implantação (D3, CA 12).</summary>
    Task<IReadOnlyList<Guid>> OpenOrdersWithReleasedBomAsync(CancellationToken cancellationToken = default);
}

/// <summary>Sem a ENG-02: nenhuma OS tem BOM liberada.</summary>
public sealed class NoReleasedBomSource : IReleasedBomSource
{
    public Task<ReleasedBom?> ReleasedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) => Task.FromResult<ReleasedBom?>(null);

    public Task<IReadOnlyList<Guid>> OpenOrdersWithReleasedBomAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
}
