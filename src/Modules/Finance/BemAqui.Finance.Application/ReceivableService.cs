using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Títulos a receber (FIN-01): manuais e parcelados pela condição, edição, vencimento, cancelamento, baixa, quitação e
/// estorno. Nada é apagado; cada movimento de dinheiro grava uma transação na conta, na mesma transação do banco.
/// </summary>
public sealed partial class ReceivableService(
    IFinanceDb db,
    IParties parties,
    IChartOfAccounts chart,
    IPaymentTerms terms,
    INumberSequence numbers,
    IPermissionChecker permissions,
    IFinanceParameters parameters,
    INotificationService notifications,
    IUserDirectory users,
    ICurrentUser currentUser,
    IEventPublisher events,
    IEnumerable<IContracts> contracts,
    IProjects projects,
    BillingSlipReactions billing,
    TimeProvider clock)
{
    public const string NumberIndex = "ix_receivables_number";
    public const string ContractMilestoneIndex = "ux_receivables_contract_milestone";
    public const string TriggerEventIndex = "ux_project_trigger_events_project_event";
    public const string DocType = "CR";
    public const string NotificationType = "finance.receivable";

    private DateOnly Today => BusinessTime.Today(clock);

    public async Task<ReceivablePage> ListAsync(ReceivableListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var today = Today;
        var receivables = db.Receivables.AsNoTracking();
        if (query.PartyId is { } party)
        {
            receivables = receivables.Where(r => r.PartyId == party);
        }

        if (query.ContractId is { } contract)
        {
            receivables = receivables.Where(r => r.ContractId == contract);
        }

        if (query.WithBillingSlip)
        {
            receivables = receivables.Where(r => r.BillingSlipId != null);
        }

        if (query.DueFrom is { } from)
        {
            receivables = receivables.Where(r => r.DueDate >= from);
        }

        if (query.DueTo is { } to)
        {
            receivables = receivables.Where(r => r.DueDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var like = "%" + query.Q.Trim() + "%";
            receivables = receivables.Where(r => EF.Functions.ILike(r.Number, like) || EF.Functions.ILike(r.Description, like));
        }

        receivables = query.Situation switch
        {
            "aberto" => receivables.Where(r => r.Status == ReceivableStatus.Open || r.Status == ReceivableStatus.PartiallyPaid),
            "vencido" => receivables.Where(r => (r.Status == ReceivableStatus.Open || r.Status == ReceivableStatus.PartiallyPaid) && r.DueDate < today),
            "previsto" => receivables.Where(r => r.Status == ReceivableStatus.Planned),
            "pago" => receivables.Where(r => r.Status == ReceivableStatus.Paid),
            "cancelado" => receivables.Where(r => r.Status == ReceivableStatus.Cancelled),
            _ => receivables.Where(r => r.Status != ReceivableStatus.Cancelled),
        };

        var open = receivables.Where(r => r.Status == ReceivableStatus.Open || r.Status == ReceivableStatus.PartiallyPaid);
        var totals = new ReceivableTotals(
            await open.SumAsync(r => r.Amount - r.PaidAmount, cancellationToken),
            await open.Where(r => r.DueDate < today).SumAsync(r => r.Amount - r.PaidAmount, cancellationToken));
        receivables = page.ParseSort() switch
        {
            ("number", true) => receivables.OrderByDescending(r => r.Number),
            ("number", false) => receivables.OrderBy(r => r.Number),
            ("amount", true) => receivables.OrderByDescending(r => r.Amount),
            ("amount", false) => receivables.OrderBy(r => r.Amount),
            ("dueDate", true) => receivables.OrderByDescending(r => r.DueDate).ThenByDescending(r => r.Number),
            _ => receivables.OrderBy(r => r.DueDate).ThenBy(r => r.Number),
        };
        var paged = await receivables.ToPagedListAsync(page, cancellationToken);
        return new ReceivablePage(new PagedList<ReceivableDetail>(await DetailsAsync(paged.Items, cancellationToken), paged.Page, paged.PageSize, paged.Total), totals);
    }

    /// <summary>Clientes ativos para o seletor (CA 2), com a condição padrão de cada um (CA 3).</summary>
    public async Task<IReadOnlyList<CustomerOption>> CustomersAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, PartyRoleKind.Customer, 20, cancellationToken))
            .Select(p => new CustomerOption(p.Id, p.TradeName ?? p.LegalName, p.Kind == PartyKind.PJ ? p.Document : null, p.DefaultPaymentTermId)).ToList();

    public async Task<Result<ReceivableDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var receivable = await db.Receivables.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return receivable is null ? NotFound() : (await DetailsAsync([receivable], cancellationToken))[0];
    }

    public async Task<Result<IReadOnlyList<SettlementView>>> SettlementsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Receivables.AnyAsync(r => r.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var items = await db.ReceivableSettlements.AsNoTracking().Where(s => s.ReceivableId == id).OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken);
        var accountIds = items.Select(s => s.BankAccountId).OfType<Guid>().Distinct().ToList();
        var names = await db.BankAccounts.IgnoreQueryFilters().AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        return items.Select(s => new SettlementView(s.Id, s.Kind, s.BankAccountId, s.BankAccountId is { } b ? names.GetValueOrDefault(b) : null, s.Date, s.Principal, s.Interest, s.Fine, s.Discount, s.Total,
            s.BackdateReason, s.CreatedBy, s.CreatedAt, s.ReversedAt, s.ReversalReason)).ToList();
    }

    private async Task<List<ReceivableDetail>> DetailsAsync(IReadOnlyList<Receivable> receivables, CancellationToken ct)
    {
        var today = Today;
        var partyIds = receivables.Select(r => r.PartyId).Distinct().ToList();
        var names = (await parties.FindManyAsync(partyIds, ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var accountIds = receivables.Select(r => r.AccountId).Distinct().ToList();
        var accounts = (await chart.FindAsync(accountIds, ct)).ToDictionary(a => a.Id, a => a.Code + " " + a.Name);
        var projectIds = receivables.Where(r => r.ProjectId is not null).Select(r => r.ProjectId!.Value).Distinct().ToList();
        var projectNumbers = (await projects.FindManyAsync(projectIds, ct)).ToDictionary(p => p.Id, p => p.Number);
        var slipIds = receivables.Where(r => r.BillingSlipId is not null).Select(r => r.BillingSlipId!.Value).ToList();
        var slipStatus = slipIds.Count == 0 ? new Dictionary<Guid, BillingSlipStatus>() : await db.BillingSlips.AsNoTracking().Where(s => slipIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Status, ct);
        return receivables.Select(r => new ReceivableDetail(
            r.Id, r.Number, r.PartyId, names.GetValueOrDefault(r.PartyId, string.Empty), r.Description, r.AccountId, accounts.GetValueOrDefault(r.AccountId, string.Empty),
            r.CostCenterId, r.ProjectId, r.Amount, r.PaidAmount, r.Balance, r.Interest, r.Fine, r.Discount, r.IssueDate, r.DueDate, r.Status,
            r.IsOverdue(today), r.IsDueToday(today), r.Origin, r.OriginId, r.OpeningReference, r.InstallmentGroupId, r.CancelReason, r.CancelledAt, r.Version,
            r.ProjectId is { } project ? projectNumbers.GetValueOrDefault(project) : null, r.TriggerEvent, r.ContractId,
            r.BillingSlipId, r.BillingSlipId is { } slip && slipStatus.TryGetValue(slip, out var st) ? (BillingSlipStatus?)st : null)).ToList();
    }

    private async Task NotifyOwnersAsync(string title, Guid receivableId, CancellationToken ct)
    {
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, NotificationType, title, "/app/financeiro/receber/" + receivableId);
        }
    }

    private async Task<string> WhoAsync(CancellationToken ct) =>
        currentUser.UserId is { } me ? await users.DisplayNameAsync(me, ct) ?? "um usuário" : "o sistema";

    /// <summary>Versão aberta pelo cliente já superada: 409 antes de qualquer validação, que leria o título novo (ADR 0019 §2).</summary>
    private static Error? Stale(Receivable receivable, uint version) =>
        receivable.Version == version ? null : Error.Conflict(BemAqui.BuildingBlocks.Web.ConcurrencyExceptionHandler.Code, "O título mudou desde que você abriu. Recarregue.");

    /// <summary>Confere a versão (xmin) que o cliente abriu: outra gravação no meio vira 409 na gravação (ADR 0019 §2).</summary>
    private void Expect(Receivable receivable, uint version) => db.Entry(receivable).Property(r => r.Version).OriginalValue = version;

    internal static string Money(decimal value) => value.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    internal static Error NotFound() => Error.NotFound("receivable_not_found", "Título a receber não encontrado.");

    public static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });
}
