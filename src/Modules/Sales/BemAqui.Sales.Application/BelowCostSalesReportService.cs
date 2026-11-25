using BemAqui.Pos.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>Uma linha vendida abaixo do custo: o preço praticado líquido do desconto (por unidade) abaixo do custo da baixa.</summary>
public sealed record BelowCostSaleRow(
    Guid SaleId, string SaleNumber, DateOnly BusinessDate, Guid? StoreId, string StoreName, string Description, decimal Quantity, decimal? ListUnitPrice, decimal UnitPrice,
    decimal Discount, decimal NetUnitPrice, decimal UnitCost, decimal Loss, string? DiscountReason, string? PriceReason);

public sealed record BelowCostSalesReport(DateOnly From, DateOnly To, int Count, decimal Loss, IReadOnlyList<BelowCostSaleRow> Rows, bool Truncated);

/// <summary>
/// "Vendas abaixo do custo" (PDV-04 CA 4): linhas de vendas concluídas cujo valor líquido do desconto, por unidade, ficou abaixo do custo gravado na
/// baixa do estoque (PDV-05). O custo nunca desce para o aparelho; o relatório é da retaguarda, com <c>sales.view_cost</c>.
/// </summary>
public sealed class BelowCostSalesReportService(ISalesDb db, IPosRegisters registers)
{
    public const int MaxDays = 366;
    public const int MaxRows = 1000;

    public async Task<Result<BelowCostSalesReport>> QueryAsync(DateOnly from, DateOnly to, Guid? storeId, CancellationToken cancellationToken = default)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
        {
            return Error.Validation(Error.Codes.Validation, "Escolha um período de até um ano, com a data final igual ou depois da inicial.");
        }

        var (first, last) = (from, to);
        var query =
            from l in db.SaleLines.AsNoTracking()
            join s in db.Sales.AsNoTracking() on l.SaleId equals s.Id
            where ReturnRules.Done.Contains(s.Status) && s.BusinessDate >= first && s.BusinessDate <= last && l.UnitCost != null && l.Quantity > 0
                && l.Total < l.UnitCost * l.Quantity
            select new { Sale = s, Line = l };
        if (storeId is { } store)
        {
            query = query.Where(x => x.Sale.StoreId == store);
        }

        var found = await query.OrderByDescending(x => x.Sale.BusinessDate).ThenBy(x => x.Sale.Number).ThenBy(x => x.Line.LineNumber).Take(MaxRows + 1)
            .ToListAsync(cancellationToken);
        var names = (await registers.ListStoresAsync(cancellationToken)).ToDictionary(s => s.Id, s => s.Name);

        var rows = found.Take(MaxRows).Select(x =>
        {
            var net = SaleRules.Money(x.Line.Total / x.Line.Quantity);
            var cost = x.Line.UnitCost!.Value;
            return new BelowCostSaleRow(
                x.Sale.Id, x.Sale.Number, x.Sale.BusinessDate, x.Sale.StoreId, x.Sale.StoreId is { } s ? names.GetValueOrDefault(s, "Loja removida") : "Sem loja", x.Line.Description, x.Line.Quantity,
                x.Line.ListUnitPrice, x.Line.UnitPrice, x.Line.Discount, net, cost, SaleRules.Money((cost * x.Line.Quantity) - x.Line.Total), x.Line.DiscountReason, x.Line.PriceReason);
        }).ToList();
        return new BelowCostSalesReport(from, to, rows.Count, rows.Sum(r => r.Loss), rows, found.Count > MaxRows);
    }
}
