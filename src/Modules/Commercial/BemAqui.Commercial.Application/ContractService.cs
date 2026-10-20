using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>Contratos (COM-04a CA 12 e 16): só leitura; o contrato muda por aditivo (COM-07) e o cancelamento é da COM-04b.</summary>
public sealed partial class ContractService(
    ICommercialDb db, IParties parties, IChartOfAccounts chart, IReceivables receivables, IPermissionChecker permissions, IEventPublisher events, ICurrentUser currentUser, TimeProvider clock)
{
    public const string AttachmentOwner = "Contract";

    public async Task<ContractPage> ListAsync(string? q, string? situation, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var contracts = db.Contracts.AsNoTracking();
        contracts = situation switch
        {
            "ativo" => contracts.Where(c => c.Status == ContractStatus.Active),
            "concluido" => contracts.Where(c => c.Status == ContractStatus.Completed),
            "cancelado" => contracts.Where(c => c.Status == ContractStatus.Cancelled),
            _ => contracts,
        };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var matches = (await parties.SearchAsync(term, PartyRoleKind.Customer, 50, cancellationToken)).Select(p => p.Id).ToList();
            contracts = contracts.Where(c => c.Number.Contains(term) || matches.Contains(c.PartyId));
        }

        var total = await contracts.CountAsync(cancellationToken);
        var rows = await contracts.OrderByDescending(c => c.SignedAt).ThenByDescending(c => c.CreatedAt).Skip(page.Skip).Take(page.PageSize)
            .Select(c => new { Contract = c, Project = db.Projects.Where(p => p.ContractId == c.Id).Select(p => new { p.Id, p.Number }).FirstOrDefault() })
            .ToListAsync(cancellationToken);
        var names = (await parties.FindManyAsync(rows.Select(r => r.Contract.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        return new ContractPage(
            rows.Select(r => new ContractSummary(r.Contract.Id, r.Contract.Number, r.Contract.PartyId, names.GetValueOrDefault(r.Contract.PartyId, string.Empty), r.Contract.TotalValue,
                r.Contract.SignedAt, r.Contract.Status, r.Project?.Id, r.Project?.Number)).ToList(),
            page.Page, page.PageSize, total);
    }

    public async Task<Result<ContractDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contract is null)
        {
            return NotFound();
        }

        var quote = await db.Quotes.IgnoreQueryFilters().AsNoTracking().Where(q => q.Id == contract.QuoteId).Select(q => new { q.Number, q.Revision }).FirstAsync(cancellationToken);
        var project = await db.Projects.AsNoTracking().Where(p => p.ContractId == id).Select(p => new { p.Id, p.Number, p.Name, p.Status }).FirstOrDefaultAsync(cancellationToken);
        var milestones = await db.ContractMilestones.AsNoTracking().Where(m => m.ContractId == id).OrderBy(m => m.Sequence)
            .Select(m => new ContractMilestoneView(m.Id, m.Sequence, m.Description, m.Percent, m.Amount, m.Trigger, m.TriggerEvent, m.DueDate, m.DaysAfterEvent)).ToListAsync(cancellationToken);
        var party = await parties.FindAsync(contract.PartyId, cancellationToken);
        var account = await chart.FindAsync([contract.RevenueAccountId], cancellationToken) is [var found, ..] ? found : null;
        var withCost = await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken);
        var parcels = await receivables.ForContractAsync(id, cancellationToken);
        var withFinance = await permissions.HasPermissionAsync("finance", "view", cancellationToken);
        var byMilestone = parcels.GroupBy(p => p.MilestoneId).ToDictionary(g => g.Key, g => g.First());
        var milestoneViews = milestones.Select(m => byMilestone.TryGetValue(m.Id, out var p)
            ? m with { Receivable = withFinance ? new MilestoneReceivable(p.Id, p.Number, p.Status.ToString(), p.DueDate, p.Amount, p.Balance) : null, HasReceivable = true }
            : m).ToList();
        return new ContractDetail(
            contract.Id, contract.Number, contract.Status, contract.PartyId, party is null ? string.Empty : party.TradeName ?? party.LegalName, party?.Document, contract.TotalValue,
            contract.SignedAt, contract.RevenueAccountId, account is null ? null : $"{account.Code} {account.Name}", contract.QuoteId, quote.Number, quote.Revision, project?.Id,
            project?.Number, project?.Name, contract.CreatedAt, milestoneViews, ContractSnapshots.Read(contract.Snapshot, withCost),
            parcels.Count, contract.Version, contract.CancelReason, contract.CancelledAt, contract.CompletedAt, project?.Status);
    }

    /// <summary>Contrato do orçamento aprovado, para o link do 409 "já foi aprovado" e do orçamento (CA 9).</summary>
    public async Task<Guid?> ByQuoteAsync(Guid quoteId, CancellationToken cancellationToken = default) =>
        await db.Contracts.AsNoTracking().Where(c => c.QuoteId == quoteId).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);

    internal static Error NotFound() => Error.NotFound("contract_not_found", "Contrato não encontrado.");
}
