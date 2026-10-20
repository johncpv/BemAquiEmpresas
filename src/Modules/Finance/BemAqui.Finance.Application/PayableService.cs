using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Títulos a pagar (FIN-02): manuais e parcelados, retenções, forma de pagamento e cancelamento. Não há baixa aqui: só a
/// OP aprovada paga (FIN-03a). Título em OP fica travado nos campos financeiros (§2.2-C1).
/// </summary>
public sealed partial class PayableService(
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
    TimeProvider clock)
{
    public const string NumberIndex = "ix_payables_number";
    public const string OriginIndex = "ix_payables_origin";
    public const string CompetenceIndex = "ix_payables_recurring_competence";
    public const string DocType = "CP";
    public const string NotificationType = "finance.payable";

    private static readonly PayableStatus[] Unpaid = [PayableStatus.Open, PayableStatus.PartiallyPaid, PayableStatus.AwaitingApproval, PayableStatus.Approved];

    private DateOnly Today => BusinessTime.Today(clock);

    public async Task<PayablePage> ListAsync(PayableListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var today = Today;
        var payables = db.Payables.AsNoTracking();
        if (query.PartyId is { } party)
        {
            payables = payables.Where(p => p.PartyId == party);
        }

        if (query.DueFrom is { } from)
        {
            payables = payables.Where(p => p.DueDate >= from);
        }

        if (query.DueTo is { } to)
        {
            payables = payables.Where(p => p.DueDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var like = "%" + query.Q.Trim() + "%";
            payables = payables.Where(p => EF.Functions.ILike(p.Number, like) || EF.Functions.ILike(p.Description, like));
        }

        payables = query.Situation switch
        {
            "aberto" => payables.Where(p => Unpaid.Contains(p.Status)),
            "vencido" => payables.Where(p => Unpaid.Contains(p.Status) && p.DueDate < today),
            "previsto" => payables.Where(p => p.Status == PayableStatus.Planned),
            "em-op" => payables.Where(p => p.Status == PayableStatus.AwaitingApproval || p.Status == PayableStatus.Approved),
            "pago" => payables.Where(p => p.Status == PayableStatus.Paid),
            "cancelado" => payables.Where(p => p.Status == PayableStatus.Cancelled),
            _ => payables.Where(p => p.Status != PayableStatus.Cancelled),
        };

        var open = payables.Where(p => Unpaid.Contains(p.Status));
        var totals = new PayableTotals(
            await open.SumAsync(p => p.Amount - p.RetentionIss - p.RetentionInss - p.RetentionIr - p.RetentionPcc - p.PaidAmount, cancellationToken),
            await open.Where(p => p.DueDate < today).SumAsync(p => p.Amount - p.RetentionIss - p.RetentionInss - p.RetentionIr - p.RetentionPcc - p.PaidAmount, cancellationToken));
        payables = page.ParseSort() switch
        {
            ("number", true) => payables.OrderByDescending(p => p.Number),
            ("number", false) => payables.OrderBy(p => p.Number),
            ("amount", true) => payables.OrderByDescending(p => p.Amount),
            ("amount", false) => payables.OrderBy(p => p.Amount),
            ("dueDate", true) => payables.OrderByDescending(p => p.DueDate).ThenByDescending(p => p.Number),
            _ => payables.OrderBy(p => p.DueDate).ThenBy(p => p.Number),
        };
        var paged = await payables.ToPagedListAsync(page, cancellationToken);
        return new PayablePage(new PagedList<PayableDetail>(await DetailsAsync(paged.Items, cancellationToken), paged.Page, paged.PageSize, paged.Total), totals);
    }

    public async Task<Result<PayableDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payable = await db.Payables.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return payable is null ? NotFound() : (await DetailsAsync([payable], cancellationToken))[0];
    }

    /// <summary>Forma de pagamento mascarada e os avisos (CA 6 a 8); null quando o título não tem forma.</summary>
    public async Task<Result<PaymentDetailsView?>> PaymentMethodAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payable = await db.Payables.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (payable is null)
        {
            return NotFound();
        }

        return Result<PaymentDetailsView?>.Success(await ViewAsync(payable, cancellationToken));
    }

    /// <summary>Fornecedores e instaladores ativos (CA 2), com a condição padrão de cada um.</summary>
    public async Task<IReadOnlyList<FinanceSupplierOption>> SuppliersAsync(string? q, CancellationToken cancellationToken = default)
    {
        var found = (await parties.SearchAsync(q, PartyRoleKind.Supplier, 20, cancellationToken)).Concat(await parties.SearchAsync(q, PartyRoleKind.Installer, 20, cancellationToken));
        return found.DistinctBy(p => p.Id).OrderBy(p => p.TradeName ?? p.LegalName, StringComparer.CurrentCulture).Take(20)
            .Select(p => new FinanceSupplierOption(p.Id, p.TradeName ?? p.LegalName, p.Kind == PartyKind.PJ ? p.Document : null, p.DefaultPaymentTermId)).ToList();
    }

    private async Task<PaymentDetailsView?> ViewAsync(Payable payable, CancellationToken ct)
    {
        if (PaymentDetails.Deserialize(payable.PaymentDetails) is not { } details)
        {
            return null;
        }

        var supplier = await parties.FindAsync(payable.PartyId, ct);
        return PaymentDetails.View(details, supplier?.Document, payable.NetAmount, payable.DueDate, Today);
    }

    private async Task<List<PayableDetail>> DetailsAsync(IReadOnlyList<Payable> payables, CancellationToken ct)
    {
        var today = Today;
        var names = (await parties.FindManyAsync(payables.Select(p => p.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var accounts = (await chart.FindAsync(payables.Select(p => p.AccountId).Distinct().ToList(), ct)).ToDictionary(a => a.Id, a => a.Code + " " + a.Name);
        return payables.Select(p => new PayableDetail(
            p.Id, p.Number, p.PartyId, names.GetValueOrDefault(p.PartyId, string.Empty), p.Description, p.AccountId, accounts.GetValueOrDefault(p.AccountId, string.Empty), p.CostCenterId,
            p.Amount, p.RetentionIss, p.RetentionInss, p.RetentionIr, p.RetentionPcc, p.NetAmount, p.PaidAmount, p.Balance, p.IssueDate, p.DueDate, p.IsEstimate, p.Status,
            p.IsOverdue(today), p.IsDueToday(today), p.Origin, p.OriginId, p.OpeningReference, p.InstallmentGroupId, p.Competence, p.PaymentMethod, p.PaymentMethodChangedAt, p.CancelReason, p.CancelledAt, p.Version)).ToList();
    }

    private static Error? Stale(Payable payable, uint version) =>
        payable.Version == version ? null : Error.Conflict(BemAqui.BuildingBlocks.Web.ConcurrencyExceptionHandler.Code, "O título mudou desde que você abriu. Recarregue.");

    private void Expect(Payable payable, uint version) => db.Entry(payable).Property(p => p.Version).OriginalValue = version;

    internal static Error NotFound() => Error.NotFound("payable_not_found", "Título a pagar não encontrado.");

    internal static Error InOrder(Payable payable) =>
        Error.Conflict("payable_in_payment_order", $"O título {payable.Number} está numa ordem de pagamento. Retire-o da ordem antes.");
}
