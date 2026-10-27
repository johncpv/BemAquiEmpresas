using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
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
/// Ordens de pagamento (FIN-03a, R-FIN-5): criar e enviar, aprovar ou reprovar, pagar, reenviar, cancelar e estornar. Só
/// estes casos de uso mudam o título para AwaitingApproval, Approved, PartiallyPaid e Paid (ADR 0019 §2 e §8).
/// </summary>
public sealed partial class PaymentOrderService(
    IFinanceDb db, IParties parties, IUserDirectory users, IPermissionChecker permissions, ICurrentUser currentUser, INumberSequence numbers,
    INotificationService notifications, IEventPublisher events, IFinanceParameters parameters, BemAqui.Commercial.Contracts.IProjects projects, ApprovalResolver resolver,
    ApprovalRuleService ruleNames, BemAqui.BuildingBlocks.Email.IEmailSender email, BemAqui.BuildingBlocks.Email.TenantLinks links,
    IEnumerable<BemAqui.HR.Contracts.IEmployeeBankAccounts> employeeAccounts, TimeProvider clock)
{
    public const string DocType = "OP";
    public const string NotificationType = "finance.payment_order";

    /// <summary>Dono do anexo do comprovante (CA 22).</summary>
    public const string AttachmentOwner = "PaymentOrder";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private DateOnly Today => BusinessTime.Today(clock);

    private static string Link(PaymentOrder order) => "/app/financeiro/ordens-de-pagamento/" + order.Id;

    public async Task<PaymentOrderPage> ListAsync(PaymentOrderListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var orders = db.PaymentOrders.AsNoTracking();
        orders = query.Situation switch
        {
            "aguardando" => orders.Where(o => o.Status == PaymentOrderStatus.Pending),
            "aprovada" => orders.Where(o => o.Status == PaymentOrderStatus.Approved),
            "reprovada" => orders.Where(o => o.Status == PaymentOrderStatus.Rejected),
            "paga" => orders.Where(o => o.Status == PaymentOrderStatus.Paid),
            "cancelada" => orders.Where(o => o.Status == PaymentOrderStatus.Cancelled || o.Status == PaymentOrderStatus.Reversed),
            _ => orders,
        };
        if (query.SelfApproved)
        {
            orders = orders.Where(o => o.SelfApproved);
        }

        if (query.AwaitingMe)
        {
            var me = currentUser.UserId;
            var mine = me is { } user && await permissions.HasPermissionAsync("finance", "approve", cancellationToken) ? await AwaitingMeAsync(user, cancellationToken) : [];
            orders = mine.Count > 0
                ? orders.Where(o => mine.Contains(o.Id))
                : orders.Where(_ => false);
        }

        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders.OrderByDescending(o => o.SubmittedAt).Skip(page.Skip).Take(page.PageSize)
            .Select(o => new { Order = o, Items = db.PaymentOrderItems.Count(i => i.PaymentOrderId == o.Id) }).ToListAsync(cancellationToken);
        var names = await NamesAsync(rows.Select(r => r.Order.RequestedBy), cancellationToken);
        return new PaymentOrderPage(rows.Select(r => Summary(r.Order, r.Items, names)).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<Result<PaymentOrderDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await db.PaymentOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        return order is null ? NotFound() : await DetailAsync(order, cancellationToken);
    }

    private async Task<PaymentOrderDetail> DetailAsync(PaymentOrder order, CancellationToken ct)
    {
        var items = await db.PaymentOrderItems.AsNoTracking().Where(i => i.PaymentOrderId == order.Id).OrderBy(i => i.Id).ToListAsync(ct);
        var paid = await db.PayableSettlements.AsNoTracking().Where(s => s.PaymentOrderId == order.Id && s.ReversedAt == null)
            .GroupBy(s => s.PayableId).Select(g => new { g.Key, Total = g.Sum(s => s.Total) }).ToDictionaryAsync(g => g.Key, g => g.Total, ct);
        var decisions = await db.PaymentOrderApprovals.AsNoTracking().Where(a => a.PaymentOrderId == order.Id).OrderBy(a => a.DecidedAt).ToListAsync(ct);
        var names = await NamesAsync(decisions.Select(d => d.DecidedBy).Append(order.RequestedBy), ct);
        var isRequester = order.RequestedBy is { } r && r == currentUser.UserId;
        var canDecide = order.Status == PaymentOrderStatus.Pending && await permissions.HasPermissionAsync("finance", "approve", ct) && (await DecisionRuleAsync(order, ct)).Allowed
            && (await CurrentLevelAsync(order.Id, ct) is not { } level || (currentUser.UserId is { } me && (await resolver.EligibleAsync(level, order.RequestedBy, ct)).Contains(me)));
        var views = items.Select(i => ItemView(i, paid.GetValueOrDefault(i.PayableId))).ToList();
        views = await WithInstallationBudgetAsync(views, ct);
        return new PaymentOrderDetail(
            Summary(order, items.Count, names), views,
            decisions.Select(d => new PaymentOrderDecisionView(d.Kind, d.Decision, d.Comment, d.DecidedBy is { } u ? names.GetValueOrDefault(u) : null, d.DecidedAt, d.SelfApproved)).ToList(),
            isRequester, canDecide, order.CancelReason, order.ReversalReason, order.PaidOn, order.BankAccountId, order.Version, await LevelViewsAsync(order.Id, ct), order.SelfPaid);
    }

    /// <summary>
    /// "Combinado · Orçado" no título do instalador (COM-06 CA 15): orçado de Instalação do projeto, só com <c>projects.view_cost</c>.
    /// </summary>
    private async Task<List<PaymentOrderItemView>> WithInstallationBudgetAsync(List<PaymentOrderItemView> views, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync("projects", "view_cost", ct))
        {
            return views;
        }

        var ids = views.Select(v => v.PayableId).ToList();
        var installers = await db.Payables.AsNoTracking().Where(p => ids.Contains(p.Id) && p.Origin == DocumentOrigin.Installer && p.ProjectId != null)
            .ToDictionaryAsync(p => p.Id, p => p.ProjectId!.Value, ct);
        var result = new List<PaymentOrderItemView>();
        foreach (var view in views)
        {
            result.Add(installers.TryGetValue(view.PayableId, out var project)
                ? view with { InstallationBudget = await projects.BudgetedAsync(project, CostType.Installation, ct) }
                : view);
        }

        return result;
    }

    private PaymentOrderItemView ItemView(PaymentOrderItem item, decimal paid)
    {
        var s = Snapshot(item);
        var view = PaymentDetails.View(s.Details, s.PartyDocument, s.NetAmount, s.DueDate, Today);
        var warnings = new List<string>();
        if (s.PayeeType == PayeeType.Employee)
        {
            warnings.AddRange(EmployeeWarnings(s));
            return new PaymentOrderItemView(item.Id, item.PayableId, s.PayableNumber, s.Description, "Funcionário " + s.PartyName, s.NetAmount, s.Retentions, item.Amount, item.ExpectedCharges,
                item.ApprovedTotal, s.DueDate, view.Details, warnings, paid, null, PayeeType.Employee);
        }

        if (view.HolderWarning is not null)
        {
            warnings.Add("O titular não é o fornecedor.");
        }

        if (s.PartyCreatedAt is { } created && (clock.GetUtcNow() - created).TotalDays < 30)
        {
            warnings.Add($"Fornecedor cadastrado há {Days(created)}.");
        }

        if (s.PaymentMethodChangedAt is { } changed && (clock.GetUtcNow() - changed).TotalDays < 30)
        {
            warnings.Add($"Dados de pagamento alterados há {Days(changed)}.");
        }

        return new PaymentOrderItemView(item.Id, item.PayableId, s.PayableNumber, s.Description, s.PartyName, s.NetAmount, s.Retentions, item.Amount, item.ExpectedCharges, item.ApprovedTotal,
            s.DueDate, view.Details, warnings, paid);
    }

    private string Days(DateTimeOffset since)
    {
        var days = (int)(clock.GetUtcNow() - since).TotalDays;
        return days == 1 ? "1 dia" : $"{days} dias";
    }

    internal static PayeeSnapshot Snapshot(PaymentOrderItem item) => JsonSerializer.Deserialize<PayeeSnapshot>(item.PayeeSnapshot, Json)!;

    internal static string Serialize(PayeeSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json);

    private static PaymentOrderSummary Summary(PaymentOrder o, int items, IReadOnlyDictionary<Guid, string> names) =>
        new(o.Id, o.Number, o.Status, o.Total, o.ExpectedDate, o.RequestedBy, o.RequestedBy is { } r ? names.GetValueOrDefault(r) : null, items, o.SelfApproved, o.SubmittedAt);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var id in ids.OfType<Guid>().Distinct())
        {
            result[id] = await users.DisplayNameAsync(id, ct) ?? "usuário removido";
        }

        return result;
    }

    internal static Error NotFound() => Error.NotFound("payment_order_not_found", "Ordem de pagamento não encontrada.");
}
