using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Contracts;

/// <summary>
/// Item novo pedido por outro módulo (CAT-01 CA 3): o Catálogo cria a mercadoria no mesmo formulário do produto. <c>Code</c> nulo = o Estoque
/// gera o próximo <c>MER-000001</c>. Passa pelas mesmas regras da tela de itens (CAD-02a).
/// </summary>
public sealed record NewItemData(ItemType Type, string? Code, string Name, Guid UnitId, string? Ncm);

/// <summary>Unidade de estoque ativa para o seletor de quem cria item (CAT-01).</summary>
public sealed record UnitChoice(Guid Id, string Code, string Name);

/// <summary>
/// Cadastro de item por outro módulo (CAT-01, ADR 0006): o Estoque segue dono de unidade, NCM, custo e saldo. Roda no DbContext e na
/// transação de quem chama (como o <see cref="IStockMovements"/>): se o produto não for salvo, o item volta.
/// </summary>
public interface IItemRegistration
{
    Task<IReadOnlyList<UnitChoice>> UnitsAsync(CancellationToken cancellationToken = default);

    /// <summary>Cria o item e devolve o id; erro de validação (422) ou código repetido (409) como a tela de itens.</summary>
    Task<Result<Guid>> CreateAsync(NewItemData data, CancellationToken cancellationToken = default);
}
