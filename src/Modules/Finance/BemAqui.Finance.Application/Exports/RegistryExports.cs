using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;

namespace BemAqui.Finance.Application.Exports;

/// <summary>Conta do plano exportada (EXP-05 CA 18): o nível dá o recuo da coluna "Conta".</summary>
public sealed record AccountExportRow(string Code, string Name, string Type, string DreGroup, string Entries, string Situation, int Level);

/// <summary>Centro de custo exportado (EXP-05 CA 18), com o nível para o recuo.</summary>
public sealed record CostCenterExportRow(string Code, string Name, string Situation, int Level);

/// <summary>Desconto ou cancelamento do período (EXP-05 CA 18); as duas últimas linhas são os totais da tela.</summary>
public sealed record DiscountExportRow(DateOnly? Date, string? Title, string? Customer, string Kind, decimal Amount, string? Reason, string? By, bool IsTotal);

/// <summary>
/// Planilhas dos cadastros do financeiro (EXP-05 CA 18): plano de contas e centros de custo com a hierarquia (código e recuo por nível) e o
/// relatório de descontos e cancelamentos com os totais. Mesma consulta e mesmos rótulos da tela; sem coluna que a tela não mostra.
/// </summary>
public static class RegistryExports
{
    public static readonly ExportSpec<AccountExportRow> Accounts = new ExportSpec<AccountExportRow>("Account", "plano-de-contas", "Plano de contas")
        .Text("Código", r => r.Code)
        .Text("Conta", r => r.Name).Indented()
        .Text("Natureza", r => r.Type)
        .Text("Grupo do DRE", r => r.DreGroup)
        .Text("Lançamentos", r => r.Entries)
        .Text("Situação", r => r.Situation)
        .Style(r => r.Level > 1 ? new ExportRowStyle(Indent: r.Level - 1) : new ExportRowStyle(Bold: true));

    public static readonly ExportSpec<CostCenterExportRow> CostCenters = new ExportSpec<CostCenterExportRow>("CostCenter", "centros-de-custo", "Centros de custo")
        .Text("Código", r => r.Code)
        .Text("Centro", r => r.Name).Indented()
        .Text("Situação", r => r.Situation)
        .Style(r => r.Level > 1 ? new ExportRowStyle(Indent: r.Level - 1) : new ExportRowStyle(Bold: true));

    public static readonly ExportSpec<DiscountExportRow> Discounts = new ExportSpec<DiscountExportRow>("Receivable", "descontos-e-cancelamentos", "Descontos e cancelamentos")
        .Date("Data", r => r.Date)
        .Text("Título", r => r.Title)
        .Text("Cliente", r => r.Customer)
        .Text("Tipo", r => r.Kind)
        .Money("Valor", r => r.Amount)
        .Text("Motivo", r => r.Reason)
        .Text("Por", r => r.By)
        .Style(r => r.IsTotal ? new ExportRowStyle(Bold: true) : null);

    /// <summary>Nível pelo código ("1" = 1, "1.2" = 2, "1.2.3" = 3), como a árvore da tela.</summary>
    public static int Level(string code) => string.IsNullOrEmpty(code) ? 1 : code.Split('.').Length;

    public static string TypeLabel(AccountType type) => type switch
    {
        AccountType.Revenue => "Receita",
        AccountType.Expense => "Despesa",
        AccountType.Asset => "Ativo",
        AccountType.Liability => "Passivo",
        var other => other.ToString(),
    };

    public static string DreGroupLabel(DreGroup group) => group switch
    {
        DreGroup.GrossRevenue => "Receita bruta",
        DreGroup.Deductions => "Deduções",
        DreGroup.Costs => "Custos",
        DreGroup.OperatingExpenses => "Despesas operacionais",
        DreGroup.FinancialResult => "Resultado financeiro",
        DreGroup.OtherResults => "Outros resultados",
        DreGroup.OutsideDre => "Fora da DRE",
        var other => other.ToString(),
    };

    /// <summary>Contas na ordem da árvore (o serviço já ordena pelo código), sem as inativas quando a tela não as mostra.</summary>
    public static IReadOnlyList<AccountExportRow> AccountRows(IEnumerable<AccountDetail> accounts, bool inactive) =>
        accounts.Where(a => inactive || a.IsActive).Select(a => new AccountExportRow(a.Code, a.Name, TypeLabel(a.Type), DreGroupLabel(a.DreGroup),
            a.AcceptsEntries ? "Analítica" : "Sintética", a.IsActive ? "Ativa" : "Inativa", Level(a.Code))).ToList();

    public static IReadOnlyList<CostCenterExportRow> CostCenterRows(IEnumerable<CostCenterDetail> centers, bool inactive) =>
        centers.Where(c => inactive || c.IsActive).Select(c => new CostCenterExportRow(c.Code, c.Name, c.IsActive ? "Ativo" : "Inativo", Level(c.Code))).ToList();

    /// <summary>Descontos (data da baixa; motivo "Acima do limite" quando passou) e cancelamentos (data do cancelamento e o motivo), por data, e os dois totais.</summary>
    public static IReadOnlyList<DiscountExportRow> DiscountRows(DiscountsAndCancellations report, IReadOnlyDictionary<Guid, string> users)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(users);
        string? By(Guid? id) => id is { } user ? users.GetValueOrDefault(user) : null;
        var rows = report.Discounts.Select(d => new DiscountExportRow(d.Date, d.Number, d.PartyName, "Desconto", d.Discount, d.AboveLimit ? "Acima do limite" : null, By(d.UserId), false))
            .Concat(report.Cancellations.Select(c => new DiscountExportRow(DateOnly.FromDateTime(BusinessTime.ToBusiness(c.At).DateTime), c.Number, c.PartyName,
                c.IsPayable ? "Cancelamento (a pagar)" : "Cancelamento", c.Balance, c.Reason, By(c.UserId), false)))
            .OrderBy(r => r.Date).ThenBy(r => r.Title, StringComparer.Ordinal).ToList();
        rows.Add(new DiscountExportRow(null, "Total de descontos", null, "Desconto", report.TotalDiscount, null, null, true));
        rows.Add(new DiscountExportRow(null, "Total cancelado", null, "Cancelamento", report.TotalCancelled, null, null, true));
        return rows;
    }
}

/// <summary>
/// Exportação dos cadastros do financeiro (EXP-05 CA 18): chama o mesmo serviço da tela (plano de contas, centros de custo, relatório de
/// descontos e cancelamentos) e devolve as linhas; teto, auditoria e download no <see cref="ListExportService"/>.
/// </summary>
public sealed class RegistryExportService(AccountService accounts, CostCenterService centers, ReceivableReports reports, IUserDirectory users)
{
    public async Task<ExportData<AccountExportRow>> AccountsAsync(bool inactive, CancellationToken ct)
    {
        var rows = RegistryExports.AccountRows(await accounts.ListAsync(ct), inactive);
        return new ExportData<AccountExportRow>(RegistryExports.Accounts, [new("Inativas", inactive ? "Mostrando as inativas" : "Só as ativas")], new ExportRows<AccountExportRow>(rows.Count, rows));
    }

    public async Task<ExportData<CostCenterExportRow>> CostCentersAsync(bool inactive, CancellationToken ct)
    {
        var rows = RegistryExports.CostCenterRows(await centers.ListAsync(ct), inactive);
        return new ExportData<CostCenterExportRow>(RegistryExports.CostCenters, [new("Inativos", inactive ? "Mostrando os inativos" : "Só os ativos")], new ExportRows<CostCenterExportRow>(rows.Count, rows));
    }

    public async Task<ExportData<DiscountExportRow>> DiscountsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var report = await reports.DiscountsAndCancellationsAsync(from, to, ct);
        var ids = report.Discounts.Select(d => d.UserId).Concat(report.Cancellations.Select(c => c.UserId)).OfType<Guid>().Distinct().ToList();
        var names = new Dictionary<Guid, string>();
        foreach (var id in ids)
        {
            if (await users.DisplayNameAsync(id, ct) is { } name)
            {
                names[id] = name;
            }
        }

        var rows = RegistryExports.DiscountRows(report, names);
        ExportFilter[] filters = [new("De", FinanceExports.Date(from)), new("Até", FinanceExports.Date(to))];
        return new ExportData<DiscountExportRow>(RegistryExports.Discounts, filters, new ExportRows<DiscountExportRow>(rows.Count, rows));
    }
}
