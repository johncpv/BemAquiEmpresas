using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class RecurringExpenseService
{
    private async Task<Error?> ApplyAsync(RecurringExpense expense, RecurringExpenseInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length > 150)
        {
            return ReceivableService.Invalid("description", "Informe a descrição, com até 150 caracteres.");
        }

        if (input.Amount <= 0)
        {
            return ReceivableService.Invalid("amount", "Informe um valor maior que zero.");
        }

        if (input.DueDay is < 1 or > 31)
        {
            return ReceivableService.Invalid("dueDay", "Informe um dia de 1 a 31.");
        }

        if (input.LeadDays is < 0 or > 60)
        {
            return ReceivableService.Invalid("leadDays", "Informe de 0 a 60 dias de antecedência.");
        }

        var start = new DateOnly(input.Start.Year, input.Start.Month, 1);
        DateOnly? end = input.End is { } e ? new DateOnly(e.Year, e.Month, 1) : null;
        if (end < start)
        {
            return ReceivableService.Invalid("end", "O fim não pode ser antes do início.");
        }

        var party = await parties.FindAsync(input.PartyId, ct);
        if (party is null || !party.IsActive || !(party.Roles.Contains(PartyRoleKind.Supplier) || party.Roles.Contains(PartyRoleKind.Installer)))
        {
            return ReceivableService.Invalid("partyId", "Escolha um fornecedor ou instalador.");
        }

        var found = await chart.FindAsync([input.AccountId], ct);
        if (found.Count == 0 || !found[0].IsActive || !found[0].AcceptsEntries || found[0].Type == AccountType.Revenue)
        {
            return ReceivableService.Invalid("accountId", "Escolha uma conta de despesa analítica ativa.");
        }

        if (input.CostCenterId is { } center && !await db.CostCenters.AnyAsync(c => c.Id == center && c.IsActive, ct))
        {
            return ReceivableService.Invalid("costCenterId", "Escolha um centro de custo ativo.");
        }

        PaymentDetailsInput? details = null;
        if (input.PaymentDetails is { } raw)
        {
            var (value, error) = PaymentDetails.Validate(raw, BusinessTime.Today(clock));
            if (error is not null)
            {
                return error;
            }

            details = value;
        }

        var today = BusinessTime.Today(clock);
        expense.Description = input.Description.Trim();
        expense.PartyId = input.PartyId;
        expense.AccountId = input.AccountId;
        expense.CostCenterId = input.CostCenterId;
        expense.Amount = input.Amount;
        expense.DueDay = input.DueDay;
        expense.Start = start;
        expense.End = end;
        expense.LeadDays = input.LeadDays ?? 10;
        expense.IsActive = end is null || end >= new DateOnly(today.Year, today.Month, 1);
        expense.PaymentMethod = details?.Kind;
        expense.PaymentDetails = details is null ? null : PaymentDetails.Serialize(details);
        return null;
    }

    private async Task<List<RecurringExpenseDetail>> DetailsAsync(IReadOnlyList<RecurringExpense> items, CancellationToken ct)
    {
        var names = (await parties.FindManyAsync(items.Select(i => i.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var ids = items.Select(i => i.Id).ToList();
        var used = (await db.Payables.Where(p => p.RecurringExpenseId != null && ids.Contains(p.RecurringExpenseId.Value)).Select(p => p.RecurringExpenseId!.Value).Distinct().ToListAsync(ct)).ToHashSet();
        var today = BusinessTime.Today(clock);
        return items.Select(r => new RecurringExpenseDetail(r.Id, r.Description, r.PartyId, names.GetValueOrDefault(r.PartyId, string.Empty), r.AccountId, r.CostCenterId, r.Amount, r.DueDay, r.Start, r.End, r.LeadDays, r.IsActive,
            r.PaymentMethod, Next(r, today), used.Contains(r.Id))).ToList();
    }

    /// <summary>As próximas 3 ocorrências a partir de hoje (CA 16).</summary>
    private static List<DateOnly> Next(RecurringExpense r, DateOnly today)
    {
        var list = new List<DateOnly>();
        for (var c = r.Start; list.Count < 3 && (r.End is not { } end || c <= end); c = c.AddMonths(1))
        {
            if (r.DueDateOf(c) >= today)
            {
                list.Add(r.DueDateOf(c));
            }
        }

        return list;
    }
}
