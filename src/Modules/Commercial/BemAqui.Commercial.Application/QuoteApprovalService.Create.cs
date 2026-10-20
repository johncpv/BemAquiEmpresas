using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteApprovalService
{
    /// <summary>Contrato, marcos, projeto, itens, orçado e eventos (CA 5 a 8), no contexto da transação aberta.</summary>
    private async Task<(Contract Contract, Project Project)> CreateAsync(Quote quote, ApproveInput input, string projectName, DateTimeOffset now, CancellationToken ct)
    {
        var items = await db.QuoteItems.AsNoTracking().Where(i => i.QuoteId == quote.Id).OrderBy(i => i.Sequence).ToListAsync(ct);
        var itemIds = items.Select(i => i.Id).ToList();
        var costs = await db.QuoteItemCosts.AsNoTracking().Where(c => itemIds.Contains(c.QuoteItemId)).ToListAsync(ct);
        var milestones = await db.QuoteMilestones.AsNoTracking().Where(m => m.QuoteId == quote.Id).OrderBy(m => m.Sequence).ToListAsync(ct);
        var party = await parties.FindAsync(quote.PartyId, ct);
        var partyName = party is null ? string.Empty : party.TradeName ?? party.LegalName;

        var contract = new Contract
        {
            Number = await numbers.NextAsync(ContractDocType, ct), QuoteId = quote.Id, PartyId = quote.PartyId, SignedAt = input.SignedAt, TotalValue = quote.TotalPrice,
            RevenueAccountId = input.RevenueAccountId, Snapshot = ContractSnapshots.Serialize(quote, partyName, items, costs, milestones), CreatedAt = now, CreatedBy = currentUser.UserId,
        };
        db.Contracts.Add(contract);
        var contractMilestones = milestones.Select(m => new ContractMilestone
        {
            ContractId = contract.Id, Sequence = m.Sequence, Description = m.Description, Percent = m.Percent, Amount = m.Amount, Trigger = m.Trigger, TriggerEvent = m.TriggerEvent,
            DueDate = m.DueDate, DaysAfterEvent = m.DaysAfterEvent,
        }).ToList();
        db.ContractMilestones.AddRange(contractMilestones);

        var project = new Project
        {
            Number = await numbers.NextAsync(ProjectDocType, ct), ContractId = contract.Id, PartyId = quote.PartyId, Name = projectName, SiteAddressId = quote.SiteAddressId,
            PromisedDate = quote.DeliveryDays is { } days ? input.SignedAt.AddDays(days) : null, ManagerUserId = quote.CreatedBy, CreatedAt = now,
        };
        db.Projects.Add(project);
        var projectItems = items.Select(i => new ProjectItem
        {
            ProjectId = project.Id, QuoteItemId = i.Id, Sequence = i.Sequence, Description = i.Description, Quantity = i.Quantity, ItemId = i.ItemId,
        }).ToList();
        db.ProjectItems.AddRange(projectItems);
        db.ProjectBudgets.AddRange(Budget(quote.TotalCost, items, costs).Select(b => new ProjectBudget { ProjectId = project.Id, CostType = b.Type, BudgetedAmount = b.Amount }));

        events.Publish(new QuoteApproved(quote.Id, contract.Id));
        events.Publish(new ContractSigned(contract.Id, project.Id, contract.PartyId, contract.SignedAt, contract.TotalValue, contract.RevenueAccountId,
            contractMilestones.Select(m => new ContractMilestoneInfo(m.Id, m.Sequence, m.Description, m.Amount, m.Trigger, m.TriggerEvent, m.DueDate, m.DaysAfterEvent)).ToList()));
        foreach (var item in projectItems)
        {
            events.Publish(new ProjectItemCreated(project.Id, item.Id, item.QuoteItemId, item.ItemId, item.Quantity));
        }

        return (contract, project);
    }

    /// <summary>
    /// Orçado por natureza (CA 8): Σ (total da linha × quantidade do item); a parte do custo do item sem composição vai para
    /// Outros; os centavos fecham no custo total do orçamento pelo maior resto (ADR 0019 §9).
    /// </summary>
    public static IReadOnlyList<(CostType Type, decimal Amount)> Budget(decimal totalCost, IReadOnlyList<QuoteItem> items, IReadOnlyList<QuoteItemCost> costs)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(costs);
        var raw = Enum.GetValues<CostType>().ToDictionary(t => t, _ => 0m);
        foreach (var item in items)
        {
            var lines = costs.Where(c => c.QuoteItemId == item.Id).ToList();
            foreach (var line in lines)
            {
                raw[line.CostType] += line.Total * item.Quantity;
            }

            raw[CostType.Other] += (item.UnitCost - lines.Sum(l => l.Total)) * item.Quantity;
        }

        var types = raw.Keys.ToList();
        var amounts = QuotePricing.LargestRemainder(totalCost, types.Select(t => Math.Max(0m, raw[t])).ToList());
        return types.Select((t, i) => (Type: t, Amount: amounts[i])).Where(x => x.Amount != 0m).ToList();
    }
}
