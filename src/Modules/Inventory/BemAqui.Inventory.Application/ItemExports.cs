using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Application;

/// <summary>Exportação de Estoque → Itens (EXP-05 CA 18, ADR 0045 §3): as colunas da tela, na mesma consulta (<see cref="ItemService.ListAsync"/>).</summary>
public static class ItemExports
{
    public static readonly IReadOnlyDictionary<ItemType, string> TypeLabels = new Dictionary<ItemType, string>
    {
        [ItemType.RawMaterial] = "Matéria-prima",
        [ItemType.Component] = "Componente",
        [ItemType.Finished] = "Produto acabado",
        [ItemType.Consumable] = "Material de consumo",
        [ItemType.Service] = "Serviço",
    };

    public static readonly ExportSpec<ItemListItem> Spec = new ExportSpec<ItemListItem>("Item", "itens", "Itens")
        .Text("Código", i => i.Code)
        .Text("Descrição", i => i.Name)
        .Text("Tipo", i => TypeLabels.GetValueOrDefault(i.Type, i.Type.ToString()))
        .Text("Categoria", i => i.CategoryName)
        .Text("Unidade", i => i.UnitCode)
        .Text("NCM", i => Ncm(i.Ncm)).NotInPdf()
        .Text("Controle", i => Tracking(i))
        .Quantity("Mínimo", i => i.MinStock)
        .Text("Situação", i => i.IsActive ? "Ativo" : "Inativo");

    /// <summary>"Lote", "Série", "Lote e série" ou vazio, como a coluna "Lote/série" da tela.</summary>
    public static string? Tracking(ItemListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return (item.TracksLot, item.TracksSerial) switch
        {
            (true, true) => "Lote e série",
            (true, false) => "Lote",
            (false, true) => "Série",
            _ => null,
        };
    }

    /// <summary>NCM com os pontos da tela (0000.00.00).</summary>
    public static string? Ncm(string? ncm) => ncm is { Length: 8 } n ? $"{n[..4]}.{n[4..6]}.{n[6..]}" : ncm;

    /// <summary>A lista sem paginação até o teto; os filtros com os rótulos da tela (a categoria pelo nome das linhas).</summary>
    public static async Task<Result<ExportData<ItemListItem>>> LoadAsync(ItemService service, ItemListQuery query, string? sort, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(query);
        var page = await service.ListAsync(query, new PageRequest(1, Math.Max(1, max), sort), ct);
        ExportFilter[] filters =
        [
            new("Busca", string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim()),
            new("Tipo", query.Type is { } type ? TypeLabels.GetValueOrDefault(type) : null),
            new("Categoria", query.CategoryId is null ? null : page.Items.Select(i => i.CategoryName).FirstOrDefault(n => n is not null) ?? "Uma categoria"),
            new("Inativos", query.IncludeInactive ? "Incluídos" : null),
        ];
        return new ExportData<ItemListItem>(Spec, filters, new ExportRows<ItemListItem>(page.Total, page.Items));
    }
}
