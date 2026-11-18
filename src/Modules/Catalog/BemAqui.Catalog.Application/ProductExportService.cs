using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.SharedKernel;

namespace BemAqui.Catalog.Application;

/// <summary>
/// Exportação da lista de produtos (CAT-01, ADR 0045 §3): a <b>mesma consulta da tela</b>, com a busca e os filtros. Custo e margem só para
/// quem tem <c>catalog.view_cost</c>: sem a ação, as colunas nem entram no arquivo (CA 10).
/// </summary>
public sealed class ProductExportService(ProductService products)
{
    public static readonly ExportSpec<ProductListItem> Spec = new ExportSpec<ProductListItem>("Product", "produtos", "Produtos")
        .Text("Produto", p => p.SaleName)
        .Text("Categoria", p => p.CategoryName)
        .Text("Item do estoque", p => p.ItemCode)
        .Text("Unidade", p => p.UnitCode)
        .Text("Código de barras", p => p.FirstGtin)
        .Text("PLU", p => p.Plu).NotInPdf()
        .Quantity("Variações", p => p.VariantCount).NotInPdf()
        .Money("Preço", p => p.Price)
        .Money("Custo médio", p => p.Cost?.AverageCost, ExportSensitivity.Omitted("catalog.view_cost"))
        .Percent("Margem", p => p.Cost?.MarginPercent, ExportSensitivity.Omitted("catalog.view_cost"))
        .Text("Situação", p => p.IsActive ? "Ativo" : "Inativo");

    public async Task<Result<ExportData<ProductListItem>>> ExportAsync(ProductListQuery query, string? sort, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await products.ListAsync(query, new PageRequest(1, Math.Max(1, max), sort), ct);
        ExportFilter[] filters =
        [
            new("Busca", string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim()),
            new("Categoria", query.CategoryId is null ? null : page.Items.Select(p => p.CategoryName).FirstOrDefault(n => n is not null)),
            new("Sem preço", query.WithoutPrice ? "Só os sem preço" : null),
            new("Inativos", query.IncludeInactive ? "Incluídos" : null),
        ];
        return new ExportData<ProductListItem>(Spec, filters, new ExportRows<ProductListItem>(page.Total, page.Items));
    }
}
