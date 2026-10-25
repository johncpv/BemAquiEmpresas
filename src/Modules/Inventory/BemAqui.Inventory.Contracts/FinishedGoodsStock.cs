namespace BemAqui.Inventory.Contracts;

/// <summary>
/// Produto acabado no estoque por item, local e lote (PRD-06b CA 1, 2 e 13), sem custo. <c>ProjectId</c> nulo é o estoque geral (produto
/// acabado sem projeto). <c>Quantity</c> é o que é do projeto (séries <c>InStock</c> do projeto, ou, sem série, as entradas
/// <c>Production</c> menos as saídas <c>Shipment</c> com o projeto); <c>OnHand</c>, <c>Reserved</c> e <c>Available</c> são da posição
/// (item, local); <c>Shippable</c> é o menor entre o do projeto e o disponível (do lote, quando há lote).
/// </summary>
public sealed record FinishedGoodsStockLine(
    Guid? ProjectId, Guid ItemId, Guid LocationId, Guid? LotId, string? LotNumber, bool TracksSerial, decimal Quantity, decimal OnHand, decimal Reserved, decimal Available,
    decimal Shippable, IReadOnlyList<SerialInfo> Serials);

/// <summary>
/// Produto acabado pronto para expedir (PRD-06b, R-PRD-8), lido pelo romaneio da Produção: só leitura, pelas séries e pelos movimentos do
/// próprio estoque. A saída é sempre pelo <see cref="IStockMovements"/> (tipo <c>Shipment</c>) e pelo <see cref="IStockSerials"/>.
/// </summary>
public interface IFinishedGoodsStock
{
    /// <summary>O que é do projeto e está no estoque (CA 1): séries no estoque do projeto e o saldo sem série vindo das OS do projeto.</summary>
    Task<IReadOnlyList<FinishedGoodsStockLine>> ForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>"Prontos para expedir" (CA 13): o mesmo de <see cref="ForProjectAsync"/> para todos os projetos com produto no estoque.</summary>
    Task<IReadOnlyList<FinishedGoodsStockLine>> ReadyToShipAsync(int limit = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Incluir produto do estoque geral" (CA 2 e 6): produto acabado sem projeto, isto é, o físico do item (tipo produto acabado) menos o
    /// que é de algum projeto, e as séries sem projeto; busca por código ou nome.
    /// </summary>
    Task<IReadOnlyList<FinishedGoodsStockLine>> GeneralStockAsync(string? term, int limit = 50, CancellationToken cancellationToken = default);
}
