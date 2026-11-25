using System.Globalization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Pos.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Loja → Devoluções (PDV-06 CA 12, <c>sales.view</c>): as devoluções concluídas no período (data de negócio), por operador, por motivo e por forma de
/// reembolso, com exportação em CSV seguro. Sem dado pessoal do cliente.
/// </summary>
public sealed class SaleReturnReportService(ISalesDb db, IPosRegisters registers, IUserDirectory users)
{
    public const int MaxRows = 2000;
    public const int MaxDays = 366;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] CsvHeader = ["Número", "Data", "Loja", "Operador", "Venda", "Reembolso", "Motivos", "Troca", "Valor (R$)"];

    public static string RefundLabel(RefundKind kind) => kind switch
    {
        RefundKind.StoreCredit => "Vale-troca",
        RefundKind.Cash => "Dinheiro",
        RefundKind.CardReversal => "Estorno no cartão",
        _ => kind.ToString(),
    };

    public async Task<Result<SaleReturnReport>> QueryAsync(SaleReturnFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.To < filter.From || filter.To.DayNumber - filter.From.DayNumber >= MaxDays)
        {
            return SaleReturnService.Field("from", "Escolha um período de até um ano, com a data final igual ou depois da inicial.");
        }

        var query = db.SaleReturns.AsNoTracking()
            .Where(r => r.Status == SaleReturnStatus.Completed && r.BusinessDate >= filter.From && r.BusinessDate <= filter.To);
        if (filter.StoreId is { } store)
        {
            query = query.Where(r => r.StoreId == store);
        }

        var all = await query.OrderByDescending(r => r.CompletedAt).Take(MaxRows + 1)
            .Join(db.Sales.AsNoTracking(), r => r.SaleId, s => s.Id, (r, s) => new { Return = r, SaleNumber = s.Number })
            .ToListAsync(cancellationToken);
        var page = all.Take(MaxRows).ToList();
        var ids = page.Select(p => p.Return.Id).ToList();
        var lines = await db.SaleReturnLines.AsNoTracking().Where(l => ids.Contains(l.ReturnId)).Select(l => new { l.ReturnId, l.Reason, l.Amount }).ToListAsync(cancellationToken);
        var stores = (await registers.ListStoresAsync(cancellationToken)).ToDictionary(s => s.Id, s => s.Name);
        var names = new Dictionary<Guid, string>();
        foreach (var id in page.Select(p => p.Return.OperatorId).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, cancellationToken) ?? "Usuário removido";
        }

        var rows = page.Select(p => new SaleReturnReportRow(
            p.Return.Id, p.Return.Number, p.Return.CompletedAt!.Value, p.Return.StoreId, stores.GetValueOrDefault(p.Return.StoreId, "Loja removida"), p.Return.OperatorId,
            names[p.Return.OperatorId], p.SaleNumber, p.Return.RefundKind, RefundLabel(p.Return.RefundKind),
            string.Join("; ", lines.Where(l => l.ReturnId == p.Return.Id).Select(l => l.Reason).Distinct()), p.Return.Total, p.Return.IsExchange)).ToList();
        var byReason = lines.GroupBy(l => l.Reason.Trim().ToLowerInvariant())
            .Select(g => new SaleReturnReportGroup(g.Key, g.First().Reason.Trim(), g.Select(l => l.ReturnId).Distinct().Count(), g.Sum(l => l.Amount)))
            .OrderByDescending(g => g.Count).ThenBy(g => g.Label, StringComparer.CurrentCulture).ToList();
        return new SaleReturnReport(
            rows,
            Group(rows, r => r.OperatorId.ToString(), r => r.OperatorName),
            byReason,
            Group(rows, r => r.RefundKind.ToString(), r => r.RefundLabel),
            rows.Sum(r => r.Total),
            all.Count > MaxRows);
    }

    public async Task<Result<SaleReturnFile>> ExportAsync(SaleReturnFilter filter, CancellationToken cancellationToken = default)
    {
        var report = await QueryAsync(filter, cancellationToken);
        if (report.IsFailure)
        {
            return report.Error!;
        }

        var rows = report.Value.Rows.Select(r => (IReadOnlyList<string?>)[
            r.Number, Local(r.CompletedAt), r.StoreName, r.OperatorName, r.SaleNumber, r.RefundLabel, r.Reasons, r.IsExchange ? "Sim" : "Não", r.Total.ToString("N2", PtBr)]);
        return new SaleReturnFile(SafeCsv.Bytes(SafeCsv.Text(CsvHeader, rows)), $"devolucoes-{filter.From:yyyy-MM-dd}-a-{filter.To:yyyy-MM-dd}.csv");
    }

    private static string Local(DateTimeOffset at) => BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(at).ToString("dd/MM/yyyy HH:mm", PtBr);

    private static List<SaleReturnReportGroup> Group(IEnumerable<SaleReturnReportRow> rows, Func<SaleReturnReportRow, string> key, Func<SaleReturnReportRow, string> label) =>
        rows.GroupBy(key).Select(g => new SaleReturnReportGroup(g.Key, label(g.First()), g.Count(), g.Sum(r => r.Total)))
            .OrderByDescending(g => g.Count).ThenBy(g => g.Label, StringComparer.CurrentCulture).ToList();
}
