using System.Globalization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application.Documents;

/// <summary>
/// Exportação das listas do estoque (EXP-04 CA 15): a mesma consulta da tela (os serviços das listas, página a página, sem pular nada), os
/// mesmos filtros com os rótulos da tela e o teto da <see cref="ListExportService"/>, que confere o formato, audita e devolve o arquivo.
/// </summary>
public sealed class InventoryExportService(
    IInventoryDb db, StockQueryService stock, StockAdjustmentService adjustments, MaterialRequisitionService requisitions, InventoryCountService counts, IUserDirectory users)
{
    public async Task<ExportData<BalanceExportRow>> BalancesAsync(BalanceListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await AllAsync(p => stock.BalancesAsync(query, p, ct), max);
        ExportFilter[] filters =
        [
            new("Busca", Text(query.Term)),
            new("Local", await LocationAsync(query.LocationId, ct)),
            new("Categoria", query.CategoryId is { } c ? await db.ItemCategories.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == c).Select(x => x.Name).FirstOrDefaultAsync(ct) : null),
            new("Tipo", query.Type is { } t ? InventoryExports.ItemTypeLabel(t) : null),
            new("Situação", query.Situation is { } s ? InventoryExports.SituationLabel(s) : null),
        ];
        IReadOnlyList<BalanceExportRow> items = rows.Total > max ? [] : InventoryExports.BalanceRows(rows.Items);
        return new ExportData<BalanceExportRow>(InventoryExports.Balances, filters, new ExportRows<BalanceExportRow>(rows.Total + 1, items));
    }

    public async Task<ExportData<MovementRow>> MovementsAsync(MovementListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await AllAsync(p => stock.MovementsAsync(query, p, ct), max);
        ExportFilter[] filters =
        [
            new("Busca", Text(query.Number)),
            new("Item", query.ItemId is { } i ? await db.Items.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == i).Select(x => x.Code + " " + x.Name).FirstOrDefaultAsync(ct) : null),
            new("Local", await LocationAsync(query.LocationId, ct)),
            new("Tipo", query.Type is { } t ? InventoryExports.MovementTypeLabel(t) : null),
            new("De", Day(query.From)),
            new("Até", Day(query.To)),
            new("Origem", query.Origin switch { "Manual" => "Lançamento manual", "Document" => "Documento", _ => Text(query.Origin) }),
            new("Projeto", query.ProjectId is null ? null : "Um projeto"),
            new("OS", query.ProductionOrderId is null ? null : "Uma OS"),
        ];
        return new ExportData<MovementRow>(InventoryExports.Movements, filters, rows);
    }

    public async Task<ExportData<AdjustmentExportRow>> AdjustmentsAsync(StockAdjustmentListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await AllAsync(p => adjustments.ListAsync(query, p, ct), max);
        var ids = rows.Items.Select(r => r.Id).ToList();
        var decisions = await db.StockAdjustments.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.DecidedBy, a.DecidedAt }).ToDictionaryAsync(a => a.Id, ct);
        var names = new Dictionary<Guid, string?>();
        foreach (var user in decisions.Values.Where(d => d.DecidedBy is not null).Select(d => d.DecidedBy!.Value).Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, ct);
        }

        var items = rows.Items.Select(r =>
        {
            var d = decisions.GetValueOrDefault(r.Id);
            return new AdjustmentExportRow(r, d?.DecidedBy is { } by ? names.GetValueOrDefault(by) : null, d?.DecidedAt);
        }).ToList();
        ExportFilter[] filters =
        [
            new("Situação", query.Status is { } s ? InventoryPdf.AdjustmentStatus(s, false) : null),
            new("Mostrar", query.AwaitingMe ? "Aguardando você" : null),
            new("Modo", query.Mode is { } m ? StockAdjustmentRules.ModeLabel(m) : null),
            new("Local", await LocationAsync(query.LocationId, ct)),
        ];
        return new ExportData<AdjustmentExportRow>(InventoryExports.Adjustments, filters, new ExportRows<AdjustmentExportRow>(rows.Total, items));
    }

    public async Task<ExportData<RequisitionSummary>> RequisitionsAsync(RequisitionListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await AllAsync(p => requisitions.ListAsync(query, p, ct), max);
        ExportFilter[] filters =
        [
            new("Situação", query.Status is { } s ? InventoryPdf.RequisitionStatus(s) : null),
            new("Mostrar", query.AwaitingMe ? "Aguardando você" : null),
            new("Tipo", query.Kind is { } k ? InventoryPdf.RequisitionKind(k) : null),
            new("OS", query.ProductionOrderId is null ? null : "Uma OS"),
        ];
        return new ExportData<RequisitionSummary>(InventoryExports.Requisitions, filters, rows);
    }

    public async Task<ExportData<InventoryCountSummary>> CountsAsync(InventoryCountListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await AllAsync(p => counts.ListAsync(query, p, ct), max);
        ExportFilter[] filters =
        [
            new("Situação", query.Status is { } s ? InventoryCountRules.StatusLabel(s) : null),
            new("Local", await LocationAsync(query.LocationId, ct)),
        ];
        return new ExportData<InventoryCountSummary>(InventoryExports.Counts, filters, rows);
    }

    /// <summary>Lê todas as páginas da lista (na ordem da tela) até o teto; acima dele, só o total, para a recusa sem montar nada.</summary>
    public static async Task<ExportRows<T>> AllAsync<T>(Func<PageRequest, Task<PagedList<T>>> page, int max)
    {
        ArgumentNullException.ThrowIfNull(page);
        var first = await page(new PageRequest(1, PageRequest.MaxPageSize));
        if (first.Total > max)
        {
            return new ExportRows<T>(first.Total, []);
        }

        var items = new List<T>(first.Items);
        for (var number = 2; items.Count < first.Total; number++)
        {
            var next = await page(new PageRequest(number, PageRequest.MaxPageSize));
            if (next.Items.Count == 0)
            {
                break;
            }

            items.AddRange(next.Items);
        }

        return new ExportRows<T>(Math.Max(first.Total, items.Count), items);
    }

    private async Task<string?> LocationAsync(Guid? id, CancellationToken ct) => id is { } value
        ? await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => l.Id == value).Select(l => l.Code + " — " + l.Name).FirstOrDefaultAsync(ct)
        : null;

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Day(DateOnly? day) => day?.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
}
