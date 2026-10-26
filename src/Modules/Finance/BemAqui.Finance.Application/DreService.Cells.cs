using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Drill-down de uma célula (CA 13 e 14): os documentos que somam o valor, com o mesmo sinal da célula. Só conta do plano do tenant e
/// mês do período (CA 18, IDOR). Título de folha (origem <c>Payroll</c>; com a RH-02c, favorecido <c>Employee</c>, ADR 0036) sem
/// <c>hr.view_sensitive</c> vira uma linha agregada por mês, sem número, favorecido nem valor por funcionário.
/// </summary>
public sealed partial class DreService
{
    public const int MaxCellItems = 1000;

    /// <summary>Ordenação do drill-down só por esta lista (CA 18): fora dela, 422.</summary>
    public static readonly string[] CellSorts = ["date", "-date", "amount", "-amount", "number"];

    public async Task<Result<DreCell>> CellAsync(DreCellQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var period = Period(query.From, query.To);
        if (period.Error is { } error)
        {
            return error;
        }

        if (query.Sort is { } sort && !CellSorts.Contains(sort, StringComparer.Ordinal))
        {
            return ReceivableService.Invalid("sort", "Ordenação inválida. Use date, -date, amount, -amount ou number.");
        }

        if ((query.AccountId is null) == (query.Group is null))
        {
            return ReceivableService.Invalid("accountId", "Informe a conta ou o grupo da célula.");
        }

        var (accounts, _) = await AccountsAsync(cancellationToken);
        DreGroup group;
        HashSet<Guid> ids;
        string label;
        if (query.AccountId is { } accountId)
        {
            var account = accounts.FirstOrDefault(a => a.Id == accountId);
            if (account is null)
            {
                return Error.NotFound("dre_account_not_found", "Conta não encontrada no plano de contas.");
            }

            if (account.Group == DreGroup.OutsideDre)
            {
                return ReceivableService.Invalid("accountId", "Esta conta está fora da DRE.");
            }

            group = account.Group;
            ids = Subtree(account, accounts);
            label = $"{account.Code} {account.Name}";
        }
        else
        {
            if (!Enum.TryParse(query.Group, false, out group) || !DreBuilder.Groups.Contains(group) || int.TryParse(query.Group, out _))
            {
                return ReceivableService.Invalid("group", "Grupo da DRE inválido.");
            }

            ids = accounts.Where(a => a.Group == group).Select(a => a.Id).ToHashSet();
            label = DreBuilder.GroupLabel(group);
        }

        var (start, end) = (period.From, period.To);
        if (query.Month is { } month)
        {
            var m = DreBuilder.MonthOf(month);
            if (m < period.From || m > period.To)
            {
                return ReceivableService.Invalid("month", "Escolha um mês do período.");
            }

            (start, end) = (m, m.AddMonths(1).AddDays(-1));
            label += " · " + Month(m);
        }

        var factor = DreBuilder.DisplayFactor(group);
        var items = new List<(DreCellItem Item, Guid? PartyId)>();
        var list = ids.ToList();

        foreach (var doc in await revenue.DocumentsAsync(list, start, end, cancellationToken))
        {
            items.Add((new DreCellItem(doc.Kind, doc.DocumentId, doc.Number, null, null, doc.Date, doc.Amount * factor, doc.Link, doc.Note, null, false), doc.PartyId));
        }

        await AddPayablesAsync(items, list, start, end, factor, cancellationToken);
        var bank = await db.BankEntries.AsNoTracking().Where(e => e.ReversedAt == null && list.Contains(e.AccountId) && e.Date >= start && e.Date <= end)
            .Select(e => new { e.Id, e.BankAccountId, e.Date, e.Amount, e.Reason }).ToListAsync(cancellationToken);
        items.AddRange(bank.Select(e => (new DreCellItem("BankEntry", e.Id, null, null, e.Reason, e.Date, e.Amount * factor, $"/app/financeiro/contas/{e.BankAccountId}/extrato", "Lançamento bancário", null, false), (Guid?)null)));
        await AddSettlementsAsync(items, ids, start, end, factor, cancellationToken);

        var names = await PartyNamesAsync(items.Select(i => i.PartyId).OfType<Guid>().Distinct().ToList(), cancellationToken);
        var all = items.Select(i => i.PartyId is { } p ? i.Item with { Counterparty = names.GetValueOrDefault(p) } : i.Item).ToList();
        IEnumerable<DreCellItem> ordered = (query.Sort ?? "date") switch
        {
            "-date" => all.OrderByDescending(i => i.Date).ThenBy(i => i.Number, StringComparer.Ordinal),
            "amount" => all.OrderBy(i => i.Amount),
            "-amount" => all.OrderByDescending(i => i.Amount),
            "number" => all.OrderBy(i => i.Number ?? "~", StringComparer.Ordinal),
            _ => all.OrderBy(i => i.Date).ThenBy(i => i.Number, StringComparer.Ordinal),
        };
        return new DreCell(label, start, end, all.Sum(i => i.Amount), ordered.Take(MaxCellItems).ToList(), all.Count > MaxCellItems, all.Any(i => i.Protected));
    }

    /// <summary>A conta e as descendentes no mesmo grupo, pela mesma regra da árvore do <see cref="DreBuilder"/> (pai = ancestral mais próximo no grupo).</summary>
    private static HashSet<Guid> Subtree(DreAccount root, IReadOnlyList<DreAccount> accounts)
    {
        var byId = accounts.ToDictionary(a => a.Id);
        Guid? ParentInGroup(DreAccount account)
        {
            var parent = account.ParentId;
            for (var guard = 0; parent is { } p && byId.TryGetValue(p, out var candidate) && guard < 16; guard++)
            {
                if (candidate.Group == root.Group)
                {
                    return candidate.Id;
                }

                parent = candidate.ParentId;
            }

            return null;
        }

        var ids = new HashSet<Guid> { root.Id };
        foreach (var account in accounts.Where(a => a.Group == root.Group && a.Id != root.Id))
        {
            var current = account;
            for (var guard = 0; ParentInGroup(current) is { } up && guard < 16; guard++)
            {
                if (up == root.Id)
                {
                    ids.Add(account.Id);
                    break;
                }

                current = byId[up];
            }
        }

        return ids;
    }

    private async Task AddPayablesAsync(List<(DreCellItem Item, Guid? PartyId)> items, List<Guid> ids, DateOnly start, DateOnly end, int factor, CancellationToken ct)
    {
        var payables = await db.Payables.AsNoTracking()
            .Where(p => !DreRules.ExcludedPayableStatuses.Contains(p.Status) && p.Origin != DocumentOrigin.Opening && ids.Contains(p.AccountId)
                && ((p.Competence != null && p.Competence >= start && p.Competence <= end) || (p.Competence == null && p.IssueDate >= start && p.IssueDate <= end)))
            .Select(p => new { p.Id, p.Number, p.PartyId, p.Description, Date = p.Competence ?? p.IssueDate, p.Amount, p.Origin }).ToListAsync(ct);
        var sensitive = await permissions.HasPermissionAsync("hr", "view_sensitive", ct);
        foreach (var p in payables.Where(p => sensitive || p.Origin != DocumentOrigin.Payroll))
        {
            items.Add((new DreCellItem("Payable", p.Id, p.Number, null, p.Description, p.Date, -p.Amount * factor, "/app/financeiro/pagar/" + p.Id, null, null, false), p.PartyId));
        }

        if (!sensitive)
        {
            // CA 14 (§4.3): sem hr.view_sensitive, a folha do mês é uma linha só, sem nomes nem valores individuais.
            foreach (var month in payables.Where(p => p.Origin == DocumentOrigin.Payroll).GroupBy(p => DreBuilder.MonthOf(p.Date)))
            {
                var total = month.Sum(p => -p.Amount * factor);
                var count = month.Count();
                items.Add((new DreCellItem("Payroll", null, null, null, $"Folha de {Month(month.Key)}: {count} {(count == 1 ? "funcionário" : "funcionários")}", month.Key, total, null,
                    "Dados protegidos: os nomes e os valores por funcionário aparecem só para quem vê dados sensíveis do RH.", count, true), null));
            }
        }
    }

    private async Task AddSettlementsAsync(List<(DreCellItem Item, Guid? PartyId)> items, HashSet<Guid> ids, DateOnly start, DateOnly end, int factor, CancellationToken ct)
    {
        var accounts = await parameters.SettlementAccountsAsync(ct);
        await AddReceivedAsync(items, ids, accounts, start, end, factor, ct);
        await AddPaidAsync(items, ids, accounts, start, end, factor, ct);
    }

    private async Task AddReceivedAsync(List<(DreCellItem Item, Guid? PartyId)> items, HashSet<Guid> ids, DreSettlementAccounts accounts, DateOnly start, DateOnly end, int factor, CancellationToken ct)
    {
        if (!ids.Contains(accounts.InterestIncome) && !ids.Contains(accounts.DiscountGranted))
        {
            return;
        }

        var rows = await db.ReceivableSettlements.AsNoTracking()
            .Where(s => s.ReversedAt == null && s.Date >= start && s.Date <= end && (s.Interest != 0 || s.Fine != 0 || s.Discount != 0))
            .Join(db.Receivables.AsNoTracking(), s => s.ReceivableId, r => r.Id, (s, r) => new { s.Date, Charges = s.Interest + s.Fine, s.Discount, ReceivableId = r.Id, r.Number, r.PartyId })
            .ToListAsync(ct);
        foreach (var s in rows)
        {
            foreach (var (_, signed) in DreRules.ReceivableSettlement(s.Charges, s.Discount, accounts).Where(x => ids.Contains(x.AccountId)))
            {
                var note = signed > 0 ? "Juros e multa recebidos na baixa" : "Desconto concedido na baixa";
                items.Add((new DreCellItem("ReceivableSettlement", s.ReceivableId, s.Number, null, null, s.Date, signed * factor, "/app/financeiro/receber/" + s.ReceivableId, note, null, false), s.PartyId));
            }
        }
    }

    private async Task AddPaidAsync(List<(DreCellItem Item, Guid? PartyId)> items, HashSet<Guid> ids, DreSettlementAccounts accounts, DateOnly start, DateOnly end, int factor, CancellationToken ct)
    {
        if (!ids.Contains(accounts.InterestExpense) && !ids.Contains(accounts.DiscountReceived))
        {
            return;
        }

        var sensitive = await permissions.HasPermissionAsync("hr", "view_sensitive", ct);
        var rows = await db.PayableSettlements.AsNoTracking()
            .Where(s => s.ReversedAt == null && s.Date >= start && s.Date <= end && (s.Interest != 0 || s.Fine != 0 || s.Discount != 0))
            .Join(db.Payables.AsNoTracking(), s => s.PayableId, p => p.Id, (s, p) => new { s.Date, Charges = s.Interest + s.Fine, s.Discount, PayableId = p.Id, p.Number, p.PartyId, p.Origin })
            .ToListAsync(ct);
        var hidden = new List<(DateOnly Date, decimal Amount)>();
        foreach (var s in rows)
        {
            foreach (var (_, signed) in DreRules.PayableSettlement(s.Charges, s.Discount, accounts).Where(x => ids.Contains(x.AccountId)))
            {
                if (!sensitive && s.Origin == DocumentOrigin.Payroll)
                {
                    hidden.Add((s.Date, signed * factor));
                    continue;
                }

                var note = signed < 0 ? "Juros e multa pagos" : "Desconto obtido no pagamento";
                items.Add((new DreCellItem("PayableSettlement", s.PayableId, s.Number, null, null, s.Date, signed * factor, "/app/financeiro/pagar/" + s.PayableId, note, null, false), s.PartyId));
            }
        }

        foreach (var month in hidden.GroupBy(h => DreBuilder.MonthOf(h.Date)))
        {
            items.Add((new DreCellItem("Payroll", null, null, null, $"Folha de {Month(month.Key)}: juros e descontos dos pagamentos", month.Key, month.Sum(h => h.Amount), null,
                "Dados protegidos: os valores por funcionário aparecem só para quem vê dados sensíveis do RH.", month.Count(), true), null));
        }
    }

    private async Task<Dictionary<Guid, string>> PartyNamesAsync(List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : (await parties.FindManyAsync(ids, ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
}
