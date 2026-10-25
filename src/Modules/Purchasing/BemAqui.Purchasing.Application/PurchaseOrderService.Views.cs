using BemAqui.BuildingBlocks.Paging;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

public sealed partial class PurchaseOrderService
{
    public const string AwaitingMe = "aguardando-voce";
    public const string OpenOnes = "abertos";
    public const string All = "todos";

    /// <summary>"A receber" (CMP-04 CA 15): aprovados, enviados ou parcialmente recebidos com saldo, pela previsão de entrega.</summary>
    public const string ToReceive = "a-receber";

    /// <summary>Detalhe do pedido (CA 3 a 23); 404 se não existe.</summary>
    public async Task<Result<PurchaseOrderDetail>> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var lines = await db.PurchaseOrderLines.AsNoTracking().Where(l => l.OrderId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var levels = await db.PurchaseOrderLevels.AsNoTracking().Where(l => l.OrderId == id && l.Attempt == order.Attempt).OrderBy(l => l.Level).ToListAsync(cancellationToken);
        var itemInfo = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var unitIds = lines.Where(l => l.EnteredUnitId is not null).Select(l => l.EnteredUnitId!.Value).Distinct().ToList();
        var unitCodes = unitIds.Count == 0 ? new Dictionary<Guid, string>() : await itemUnits.CodesAsync(unitIds, cancellationToken);
        var requestLineIds = lines.SelectMany(l => l.RequestLineIds).Distinct().ToList();
        var requestRefs = requestLineIds.Count == 0 ? [] : await (from l in db.PurchaseRequestLines.AsNoTracking()
                                                                  join r in db.PurchaseRequests.AsNoTracking() on l.RequestId equals r.Id
                                                                  where requestLineIds.Contains(l.Id)
                                                                  select new PurchaseOrderRequestRef(r.Id, r.Number, l.Id)).ToListAsync(cancellationToken);
        var party = await parties.FindAsync(order.PartyId, cancellationToken);
        var project = order.ProjectId is { } pid ? await projects.FindAsync(pid, cancellationToken) : null;
        var term = await terms.FindAsync(order.PaymentTermId, cancellationToken);
        var dues = await terms.SplitAsync(order.PaymentTermId, order.Total, order.ExpectedDate, cancellationToken) ?? [];
        var me = currentUser.UserId;
        var canEdit = await permissions.HasPermissionAsync("purchasing", "edit", cancellationToken);
        var canCreate = await permissions.HasPermissionAsync("purchasing", "create", cancellationToken);
        var canApprove = await permissions.HasPermissionAsync("purchasing", "approve", cancellationToken);
        var received = lines.Sum(l => l.QuantityReceived);
        var changeable = PurchaseOrderRules.IsChangeable(order.Status, received);
        var (canDecide, blocker) = await DecideStateAsync(order, canApprove, cancellationToken);
        // Recebimento (CMP-04): quem pode registrar agora (ou o motivo), quanto já virou título e se o usuário já recebeu (não corrige o preço).
        var canReceive = await permissions.HasPermissionAsync("purchasing", "receive", cancellationToken);
        var receiveBlocker = canReceive ? await ReceiveBlockerAsync(order, lines, cancellationToken) : null;
        var receipts = await db.GoodsReceipts.AsNoTracking().Where(r => r.PurchaseOrderId == id).Select(r => new { r.Status, r.Amount, r.ReceivedBy }).ToListAsync(cancellationToken);
        var receivedAmount = receipts.Where(r => r.Status == GoodsReceiptStatus.Completed).Sum(r => r.Amount);
        var receiptsCount = receipts.Count;
        var receivedByMe = me is { } current && receipts.Any(r => r.ReceivedBy == current);
        var views = lines.Select(l =>
        {
            var item = itemInfo.GetValueOrDefault(l.ItemId);
            return new PurchaseOrderLineView(
                l.Id, l.Sequence, l.ItemId, item?.Code ?? string.Empty, item?.Name ?? "Item removido", item?.UnitCode ?? string.Empty, l.Quantity, l.EnteredQuantity,
                l.EnteredUnitId is { } u ? unitCodes.GetValueOrDefault(u) : null, l.UnitPrice, l.LineTotal, l.CostType, l.QuantityReceived, l.RequestLineIds,
                requestRefs.Where(r => l.RequestLineIds.Contains(r.LineId)).ToList(), l.QuantityOverReceived, l.AmountReceived, Math.Max(0m, l.Quantity - l.QuantityReceived));
        }).ToList();
        return new PurchaseOrderDetail(
            order.Id, order.Number, order.Status, order.PartyId, party is null ? "Fornecedor removido" : party.TradeName ?? party.LegalName, order.ProjectId, project?.Number, project?.Name,
            order.PaymentTermId, term?.Name, order.ExpectedDate, PurchaseOrderRules.DaysLate(order.Status, order.ExpectedDate, Today), order.Subtotal, order.Freight, order.IpiPct,
            order.IpiAmount, order.Total, order.Notes, order.RfqId, order.QuotesCount, PurchaseOrderRules.SingleQuoteWarning(order.RfqId, order.QuotesCount), order.RequestedBy,
            order.RequestedBy is { } by ? await users.DisplayNameAsync(by, cancellationToken) : null, order.CreatedAt, order.SubmittedAt, order.ApprovedAt, order.Attempt,
            order.ReferenceAmount, levels.Select(l => l.SplitWarning).FirstOrDefault(w => w is not null), order.SelfApproved, order.ChangedAfterSent, order.RejectionComment,
            order.CancelReason, order.CancelledAt, order.CancelledBy is { } cb ? await users.DisplayNameAsync(cb, cancellationToken) : null, received, views,
            levels.Select(l => new PurchaseOrderLevelView(l.Attempt, l.Level, l.ApprovalRuleId, l.ApproverName, l.ReferenceAmount, l.SplitWarning, l.Status)).ToList(),
            dues.Select(d => new PurchaseOrderInstallmentPreview(d.Number, d.DueDate, d.Amount)).ToList(), me is { } user && user == order.RequestedBy, canEdit && changeable,
            order.Status == PurchaseOrderStatus.Draft && (canCreate || canEdit), canDecide, canEdit && changeable, blocker, order.Version, order.ApprovedTotal, order.ClosedBalanceAmount,
            order.CloseReason, order.ClosedAt, receivedAmount, receiptsCount, canReceive && receiveBlocker is null, canReceive ? receiveBlocker : null,
            canEdit && order.Status == PurchaseOrderStatus.PartiallyReceived, canApprove && PurchaseOrderRules.OpenForReceipt.Contains(order.Status) && !receivedByMe,
            canEdit && PurchaseOrderRules.Dispatchable.Contains(order.Status), PurchaseOrderRules.PdfStatuses.Contains(order.Status), order.SentAt);
    }

    /// <summary>Pode decidir agora? E, quando tem <c>purchasing.approve</c> e não pode, o motivo (CA 9).</summary>
    private async Task<(bool CanDecide, string? Blocker)> DecideStateAsync(PurchaseOrder order, bool canApprove, CancellationToken ct)
    {
        if (order.Status != PurchaseOrderStatus.PendingApproval || !canApprove || currentUser.UserId is not { } me)
        {
            return (false, null);
        }

        var level = await db.PurchaseOrderLevels.AsNoTracking().Where(l => l.OrderId == order.Id && l.Attempt == order.Attempt && l.Status == PurchaseLevelStatus.Pending)
            .OrderBy(l => l.Level).FirstOrDefaultAsync(ct);
        if (level is null)
        {
            return (false, null);
        }

        if ((await EligibleNowAsync(order, level, ct)).Contains(me))
        {
            return (true, null);
        }

        var decided = await db.PurchaseOrderApprovals.AsNoTracking()
            .AnyAsync(a => a.OrderId == order.Id && a.Attempt == order.Attempt && a.DecidedBy == me && a.Decision == PurchaseDecision.Approved, ct);
        return (false, decided ? "Você já aprovou um nível deste pedido. O próximo nível é de outro aprovador."
            : me == order.RequestedBy ? "Quem pediu não aprova o próprio pedido." : "Este pedido não está na sua alçada.");
    }

    /// <summary>
    /// Lista "Pedidos" (CA 20): para quem aprova, o padrão é "Aguardando você" (só o que o usuário pode decidir agora); para os demais, os
    /// abertos. Ordem: a previsão de entrega mais próxima primeiro, depois o número.
    /// </summary>
    public async Task<PagedList<PurchaseOrderSummary>> ListAsync(PurchaseOrderListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var canApprove = await permissions.HasPermissionAsync("purchasing", "approve", cancellationToken);
        var show = query.Show ?? (canApprove ? AwaitingMe : OpenOnes);
        var orders = db.PurchaseOrders.AsNoTracking();
        if (show == AwaitingMe)
        {
            var mine = canApprove && currentUser.UserId is { } me ? await AwaitingAsync(me, cancellationToken) : [];
            orders = orders.Where(o => mine.Contains(o.Id));
        }
        else if (show == OpenOnes)
        {
            orders = orders.Where(o => o.Status != PurchaseOrderStatus.Received && o.Status != PurchaseOrderStatus.Cancelled);
        }        else if (show == ToReceive)
        {
            var receivable = PurchaseOrderRules.OpenForReceipt.ToList();
            orders = orders.Where(o => receivable.Contains(o.Status) && db.PurchaseOrderLines.Any(l => l.OrderId == o.Id && l.Quantity > l.QuantityReceived));
        }

        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        if (query.PartyId is { } party)
        {
            orders = orders.Where(o => o.PartyId == party);
        }

        if (query.ProjectId is { } project)
        {
            orders = orders.Where(o => o.ProjectId == project);
        }

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim().ToUpperInvariant();
            orders = orders.Where(o => o.Number.Contains(term));
        }

        var result = await orders.OrderBy(o => o.ExpectedDate).ThenByDescending(o => o.Number).ToPagedListAsync(page, cancellationToken);
        var ids = result.Items.Select(o => o.Id).ToList();
        var lines = await db.PurchaseOrderLines.AsNoTracking().Where(l => ids.Contains(l.OrderId)).OrderBy(l => l.Sequence).Select(l => new { l.OrderId, l.ItemId }).ToListAsync(cancellationToken);
        var itemInfo = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var partyIds = result.Items.Select(o => o.PartyId).Distinct().ToList();
        Dictionary<Guid, PartySummary> partyInfo = partyIds.Count == 0 ? [] : (await parties.FindManyAsync(partyIds, cancellationToken)).ToDictionary(p => p.Id);
        var projectIds = result.Items.Select(o => o.ProjectId).OfType<Guid>().Distinct().ToList();
        Dictionary<Guid, ProjectSummary> projectInfo = projectIds.Count == 0 ? [] : (await projects.FindManyAsync(projectIds, cancellationToken)).ToDictionary(p => p.Id);
        var today = Today;
        var rows = result.Items.Select(o =>
        {
            var mine = lines.Where(l => l.OrderId == o.Id).ToList();
            var first = mine.Count > 0 ? itemInfo.GetValueOrDefault(mine[0].ItemId) : null;
            var summary = first is null ? "—" : $"{first.Code} {first.Name}{(mine.Count > 1 ? $" e mais {mine.Count - 1}" : string.Empty)}";
            var p = partyInfo.GetValueOrDefault(o.PartyId);
            var pr = o.ProjectId is { } pid ? projectInfo.GetValueOrDefault(pid) : null;
            return new PurchaseOrderSummary(o.Id, o.Number, o.Status, o.PartyId, p is null ? "Fornecedor removido" : p.TradeName ?? p.LegalName, o.ProjectId, pr?.Number, pr?.Name, o.Total,
                o.ExpectedDate, PurchaseOrderRules.DaysLate(o.Status, o.ExpectedDate, today), summary, o.CreatedAt);
        }).ToList();
        return new PagedList<PurchaseOrderSummary>(rows, result.Page, result.PageSize, result.Total);
    }

    /// <summary>Pedidos aguardando aprovação cujo nível atual o usuário pode decidir agora ("Aguardando você", CA 20; também a Minha pauta, DSH-04).</summary>
    internal async Task<List<Guid>> AwaitingAsync(Guid me, CancellationToken ct)
    {
        var pending = await db.PurchaseOrders.AsNoTracking().Where(o => o.Status == PurchaseOrderStatus.PendingApproval).ToListAsync(ct);
        var result = new List<Guid>();
        foreach (var order in pending)
        {
            if (await CurrentLevelAsync(order, ct) is { } level && (await EligibleNowAsync(order, level, ct)).Contains(me))
            {
                result.Add(order.Id);
            }
        }

        return result;
    }

    /// <summary>Aba Decisões (CA 23): os níveis e a linha do tempo de todas as tentativas, a mais recente primeiro.</summary>
    public async Task<Result<PurchaseOrderDecisions>> DecisionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.PurchaseOrders.AnyAsync(o => o.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var levels = await db.PurchaseOrderLevels.AsNoTracking().Where(l => l.OrderId == id).OrderByDescending(l => l.Attempt).ThenBy(l => l.Level).ToListAsync(cancellationToken);
        var approvals = await db.PurchaseOrderApprovals.AsNoTracking().Where(a => a.OrderId == id).OrderByDescending(a => a.DecidedAt).ToListAsync(cancellationToken);
        var names = new Dictionary<Guid, string?>();
        foreach (var user in approvals.Select(a => a.DecidedBy).OfType<Guid>().Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, cancellationToken);
        }

        return new PurchaseOrderDecisions(
            levels.Select(l => new PurchaseOrderLevelView(l.Attempt, l.Level, l.ApprovalRuleId, l.ApproverName, l.ReferenceAmount, l.SplitWarning, l.Status)).ToList(),
            approvals.Select(a => new PurchaseOrderDecisionView(
                a.Attempt, a.Level, a.Decision, a.Comment, a.DecidedBy, a.DecidedBy is { } u ? names.GetValueOrDefault(u) : null, a.DecidedAt, a.SelfApproved, a.ReferenceAmount,
                levels.FirstOrDefault(l => l.Attempt == a.Attempt && l.Level == a.Level)?.ApproverName)).ToList());
    }

    /// <summary>Aba Financeiro (com <c>finance.view</c>): os títulos previstos do pedido por parcela, com a OP quando há.</summary>
    public async Task<Result<IReadOnlyList<PurchaseOrderPayableView>>> PayablesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("finance", "view", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Os títulos do pedido ficam no Financeiro. Fale com o administrador.");
        }

        if (!await db.PurchaseOrders.AnyAsync(o => o.Id == id, cancellationToken))
        {
            return NotFound();
        }

        return (await payables.ListByOriginAsync(DocumentOrigin.Purchase, id, cancellationToken))
            .Select(p => new PurchaseOrderPayableView(p.Id, p.Number, p.Installment, p.Status, p.Amount, p.DueDate, p.IsEstimate, p.PaymentOrderId)).ToList();
    }

    /// <summary>
    /// "Novo pedido" preenchido (CA 1, R-PRP-1): pelas linhas de SC em aberto (ou um item), o fornecedor do último pedido do item, a
    /// condição padrão dele, o último preço pago (ou o custo médio) com a marca "Sugerido", a data prevista e o destino das SC.
    /// </summary>
    public async Task<Result<PurchaseOrderDefaults>> DefaultsAsync(IReadOnlyCollection<Guid>? requestLineIds, Guid? itemId, CancellationToken cancellationToken = default)
    {
        var lead = await parameters.LeadDaysAsync(cancellationToken);
        var expected = PurchaseOrderRules.DefaultExpectedDate(Today, lead);
        var ids = requestLineIds ?? [];
        var requestLines = ids.Count == 0 ? [] : await (from l in db.PurchaseRequestLines.AsNoTracking()
                                                        join r in db.PurchaseRequests.AsNoTracking() on l.RequestId equals r.Id
                                                        where ids.Contains(l.Id) && l.Status == PurchaseRequestStatus.Open
                                                        orderby r.Number, l.Sequence
                                                        select new { Line = l, r.Number }).ToListAsync(cancellationToken);
        if (ids.Count > 0 && requestLines.Count == 0)
        {
            return Error.Conflict(PurchaseRequestService.LineNotOpenCode, "As linhas escolhidas já viraram pedido, estão em cotação ou foram canceladas.");
        }

        var itemIds = requestLines.Select(r => r.Line.ItemId).Distinct().ToList();
        if (itemIds.Count == 0 && itemId is { } single)
        {
            itemIds.Add(single);
        }

        var found = (await items.FindAsync(itemIds, cancellationToken)).Where(i => i.IsActive && !i.IsDeleted && i.Type != ItemType.Service).ToList();
        var options = (await OptionsAsync(found, cancellationToken)).ToDictionary(o => o.Id);
        var last = await LastPurchasesAsync(itemIds, cancellationToken);
        var average = itemIds.Count == 0 ? new Dictionary<Guid, decimal>() : await costs.AverageCostsAsync(itemIds, cancellationToken);
        var lines = new List<PurchaseOrderDefaultLine>();
        foreach (var id in itemIds.Where(options.ContainsKey))
        {
            var mine = requestLines.Where(r => r.Line.ItemId == id).ToList();
            var (price, source) = last.TryGetValue(id, out var purchase) ? ((decimal?)purchase.UnitPrice, "Último preço pago")
                : average.TryGetValue(id, out var avg) && avg > 0 ? (decimal.Round(avg, 6), "Custo médio") : ((decimal?)null, (string?)null);
            lines.Add(new PurchaseOrderDefaultLine(options[id], mine.Sum(r => r.Line.Quantity), price, price is not null, source, mine.Select(r => r.Line.Id).ToList(),
                mine.Select(r => r.Number).Distinct().ToList()));
        }

        var supplierId = itemIds.Select(i => last.TryGetValue(i, out var p) ? (Guid?)p.PartyId : null).FirstOrDefault(p => p is not null);
        var supplier = supplierId is { } sid ? await parties.FindAsync(sid, cancellationToken) : null;
        if (supplier is { } s && (!s.IsActive || s.IsDeleted || !s.Roles.Contains(PartyRoleKind.Supplier)))
        {
            supplier = null;
        }

        var term = supplier?.DefaultPaymentTermId is { } tid ? await terms.FindAsync(tid, cancellationToken) : null;
        var projectId = requestLines.Count > 0 ? PurchaseOrderRules.DestinationOf(requestLines.Select(r => r.Line.ProjectId).ToList()) : null;
        var project = projectId is { } pid ? await projects.FindAsync(pid, cancellationToken) : null;
        return new PurchaseOrderDefaults(supplier?.Id, supplier is null ? null : supplier.TradeName ?? supplier.LegalName, supplier is not null, term is { IsActive: true } ? term.Id : null,
            term is { IsActive: true } ? term.Name : null, expected, lead, project?.Id, project?.Number, project?.Name, lines);
    }

    /// <summary>Último pedido aprovado (ou além) de cada item: fornecedor e preço (R-PRP-1).</summary>
    private async Task<Dictionary<Guid, (Guid PartyId, decimal UnitPrice)>> LastPurchasesAsync(List<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var open = PurchaseOrderRules.CountsForReference.Where(s => s != PurchaseOrderStatus.PendingApproval).ToList();
        var rows = await (from l in db.PurchaseOrderLines.AsNoTracking()
                          join o in db.PurchaseOrders.AsNoTracking() on l.OrderId equals o.Id
                          where itemIds.Contains(l.ItemId) && open.Contains(o.Status) && o.ApprovedAt != null
                          select new { l.ItemId, o.PartyId, l.UnitPrice, o.ApprovedAt }).ToListAsync(ct);
        return rows.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.ApprovedAt).Select(r => (r.PartyId, r.UnitPrice)).First());
    }

    /// <summary>Fornecedores para o pedido (só com o papel Fornecedor e ativos), sem exigir permissão de Terceiros.</summary>
    public async Task<IReadOnlyList<PurchaseSupplierOption>> SupplierOptionsAsync(string? term, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(term, PartyRoleKind.Supplier, 20, cancellationToken)).Where(p => p.IsActive && !p.IsDeleted)
        .Select(p => new PurchaseSupplierOption(p.Id, p.TradeName ?? p.LegalName, p.Document, p.DefaultPaymentTermId)).ToList();

    /// <summary>Condições de pagamento ativas para o pedido.</summary>
    public async Task<IReadOnlyList<PurchasePaymentTermOption>> PaymentTermOptionsAsync(CancellationToken cancellationToken = default) =>
        (await terms.ActiveAsync(cancellationToken)).Select(t => new PurchasePaymentTermOption(t.Id, t.Name, t.Installments)).ToList();

    private async Task<List<PurchaseItemOption>> OptionsAsync(List<ItemSummary> found, CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, IReadOnlyList<ItemUnitOption>> units = found.Count == 0 ? new Dictionary<Guid, IReadOnlyList<ItemUnitOption>>() : await itemUnits.ForItemsAsync(found.Select(i => i.Id).ToList(), ct);
        return found.Select(i => new PurchaseItemOption(i.Id, i.Code, i.Name, i.Type, i.UnitId, i.UnitCode,
            units.GetValueOrDefault(i.Id) ?? [new ItemUnitOption(i.UnitId, i.UnitCode, true, 1m)])).ToList();
    }
}
