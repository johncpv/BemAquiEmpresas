using System.Globalization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Pos.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>Uma linha do relatório: a loja e o operador, com a quantidade e o valor das vendas sem documento fiscal no período.</summary>
public sealed record NonFiscalSalesRow(Guid? StoreId, string StoreName, Guid? OperatorId, string OperatorName, int Count, decimal Total);

/// <summary>Relatório "Vendas sem documento fiscal" (IMP-01 CA 12): o período (datas de negócio), os totais e as linhas por loja e operador.</summary>
public sealed record NonFiscalSalesReport(DateOnly From, DateOnly To, int Count, decimal Total, IReadOnlyList<NonFiscalSalesRow> Rows);

public sealed record NonFiscalSalesFile(byte[] Content, string FileName);

/// <summary>
/// Vendas sem documento fiscal (IMP-01 CA 12, PP-05): vendas do balcão concluídas (não canceladas) sem NFC-e nem NF-e (o Fiscal ainda não reagiu
/// nem há nota ligada), por período da data de negócio, loja e operador, com exportação em CSV. Pela regra, entram tanto as vendas das lojas no
/// modo "só cupom não fiscal" quanto as vendidas com <c>pos.skip_fiscal</c> numa loja que emite NFC-e (Fase 13): o critério é a venda não ter
/// documento, não o modo da loja. O operador é quem vendeu (o vendedor da venda; sem ele, quem a registrou).
/// </summary>
public sealed class NonFiscalSalesReportService(ISalesDb db, IPosRegisters registers, IUserDirectory users)
{
    /// <summary>Período máximo de uma consulta (um ano), para o relatório não varrer a base inteira.</summary>
    public const int MaxDays = 366;

    public static readonly IReadOnlyList<string> CsvHeader = ["Loja", "Operador", "Vendas", "Valor (R$)"];

    public async Task<Result<NonFiscalSalesReport>> QueryAsync(DateOnly from, DateOnly to, Guid? storeId, Guid? operatorId, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return Field("to", "A data final tem de ser igual ou depois da inicial.");
        }

        if (to.DayNumber - from.DayNumber >= MaxDays)
        {
            return Field("from", "Escolha um período de até um ano.");
        }

        var query = db.Sales.AsNoTracking().Where(s =>
            s.Channel == SaleChannel.Pos && ReturnRules.Done.Contains(s.Status) && s.FiscalStatus == null && s.InvoiceId == null
            && s.BusinessDate >= from && s.BusinessDate <= to);
        if (storeId is { } store)
        {
            query = query.Where(s => s.StoreId == store);
        }

        if (operatorId is { } op)
        {
            query = query.Where(s => (s.SellerUserId ?? s.CreatedBy) == op);
        }

        var groups = await query
            .GroupBy(s => new { s.StoreId, OperatorId = s.SellerUserId ?? s.CreatedBy })
            .Select(g => new { g.Key.StoreId, g.Key.OperatorId, Count = g.Count(), Total = g.Sum(s => s.Total) })
            .ToListAsync(cancellationToken);

        var stores = (await registers.ListStoresAsync(cancellationToken)).ToDictionary(s => s.Id, s => s.Name);
        var names = new Dictionary<Guid, string>();
        foreach (var id in groups.Where(g => g.OperatorId is not null).Select(g => g.OperatorId!.Value).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, cancellationToken) ?? "Usuário removido";
        }

        var rows = groups
            .Select(g => new NonFiscalSalesRow(
                g.StoreId, g.StoreId is { } s ? stores.GetValueOrDefault(s, "Loja removida") : "Sem loja",
                g.OperatorId, g.OperatorId is { } o ? names[o] : "Sem operador", g.Count, g.Total))
            .OrderBy(r => r.StoreName, StringComparer.CurrentCulture).ThenBy(r => r.OperatorName, StringComparer.CurrentCulture).ToList();
        return new NonFiscalSalesReport(from, to, rows.Sum(r => r.Count), rows.Sum(r => r.Total), rows);
    }

    /// <summary>O mesmo relatório em CSV seguro (separador ;, UTF-8 com BOM, sem fórmula), com a linha de total.</summary>
    public async Task<Result<NonFiscalSalesFile>> ExportAsync(DateOnly from, DateOnly to, Guid? storeId, Guid? operatorId, CancellationToken cancellationToken = default)
    {
        var report = await QueryAsync(from, to, storeId, operatorId, cancellationToken);
        if (report.IsFailure)
        {
            return report.Error!;
        }

        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var rows = report.Value.Rows.Select(r => (IReadOnlyList<string?>)[r.StoreName, r.OperatorName, r.Count.ToString(pt), r.Total.ToString("N2", pt)])
            .Append(["Total", string.Empty, report.Value.Count.ToString(pt), report.Value.Total.ToString("N2", pt)]);
        var name = $"vendas-sem-documento-fiscal-{from:yyyy-MM-dd}-a-{to:yyyy-MM-dd}.csv";
        return new NonFiscalSalesFile(SafeCsv.Bytes(SafeCsv.Text(CsvHeader, rows)), name);
    }

    private static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
