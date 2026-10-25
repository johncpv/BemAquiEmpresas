using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Inventory.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

public sealed partial class PurchaseRequestService
{
    public const string AwaitingMe = "aguardando-voce";
    public const string OpenOnes = "abertas";
    public const string All = "todas";

    /// <summary>Detalhe da SC (CA 12 a 14); 404 se não existe.</summary>
    public async Task<Result<PurchaseRequestDetail>> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await db.PurchaseRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        var lines = await db.PurchaseRequestLines.AsNoTracking().Where(l => l.RequestId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var itemInfo = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var unitCodes = await itemUnits.CodesAsync(lines.Where(l => l.EnteredUnitId is not null).Select(l => l.EnteredUnitId!.Value).Distinct().ToList(), cancellationToken);
        var orderIds = lines.Select(l => l.ProductionOrderId).Append(request.ProductionOrderId).OfType<Guid>().Distinct().ToList();
        Dictionary<Guid, string> orderNumbers = orderIds.Count == 0 ? [] : (await orders.FindManyAsync(orderIds, cancellationToken)).ToDictionary(o => o.Id, o => o.Number);
        var project = request.ProjectId is { } pid ? await projects.FindAsync(pid, cancellationToken) : null;
        var canEdit = await permissions.HasPermissionAsync("purchasing", "edit", cancellationToken);
        var hasOpen = lines.Any(l => l.Status == PurchaseRequestStatus.Open);
        var views = lines.Select(l =>
        {
            var item = itemInfo.GetValueOrDefault(l.ItemId);
            return new PurchaseRequestLineView(
                l.Id, l.Sequence, l.ItemId, item?.Code ?? string.Empty, item?.Name ?? "Item removido", item?.UnitCode ?? string.Empty, l.Quantity, l.EnteredQuantity,
                l.EnteredUnitId is { } u ? unitCodes.GetValueOrDefault(u) : null, l.NeededBy, l.Status, l.ProductionOrderId,
                l.ProductionOrderId is { } o ? orderNumbers.GetValueOrDefault(o) : null, l.BomLineId, l.ManuallyEdited, l.CalculatedOnOnboarding, l.CancelReason, l.Version);
        }).ToList();
        return new PurchaseRequestDetail(
            request.Id, request.Number, request.Origin, request.Status, request.ProjectId, project?.Number, project?.Name, request.ProductionOrderId,
            request.ProductionOrderId is { } ro ? orderNumbers.GetValueOrDefault(ro) : null, request.Notes, request.RequestedBy,
            request.RequestedBy is { } by ? await users.DisplayNameAsync(by, cancellationToken) : null, request.CreatedAt, request.UpdatedAt, request.CancelReason, request.CancelledAt,
            request.CancelledBy is { } cb ? await users.DisplayNameAsync(cb, cancellationToken) : null,
            lines.Where(l => l.Status == PurchaseRequestStatus.Open).Select(l => (DateOnly?)l.NeededBy).Min(), canEdit && hasOpen,
            canEdit && hasOpen && lines.All(l => l.Status is PurchaseRequestStatus.Open or PurchaseRequestStatus.Cancelled), views, request.Version);
    }

    /// <summary>
    /// Lista "Solicitações" (CA 12): filtros na URL; "Aguardando você" (SC aberta, sem cotação nem pedido) é o padrão de quem tem
    /// <c>purchasing.edit</c>; sem ela, as abertas e em cotação. Ordem: a necessidade aberta mais próxima primeiro, depois o número.
    /// </summary>
    public async Task<PagedList<PurchaseRequestSummary>> ListAsync(PurchaseRequestListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var show = query.Show ?? (await permissions.HasPermissionAsync("purchasing", "edit", cancellationToken) ? AwaitingMe : OpenOnes);
        var requests = db.PurchaseRequests.AsNoTracking();
        requests = show switch
        {
            AwaitingMe => requests.Where(r => r.Status == PurchaseRequestStatus.Open),
            OpenOnes => requests.Where(r => r.Status == PurchaseRequestStatus.Open || r.Status == PurchaseRequestStatus.Quoting),
            _ => requests,
        };
        if (query.Status is { } status)
        {
            requests = requests.Where(r => r.Status == status);
        }

        if (query.Origin is { } origin)
        {
            requests = requests.Where(r => r.Origin == origin);
        }

        if (query.ProjectId is { } project)
        {
            requests = requests.Where(r => r.ProjectId == project);
        }

        if (query.ProductionOrderId is { } order)
        {
            requests = requests.Where(r => r.ProductionOrderId == order || db.PurchaseRequestLines.Any(l => l.RequestId == r.Id && l.ProductionOrderId == order));
        }

        if (query.ItemId is { } item)
        {
            requests = requests.Where(r => db.PurchaseRequestLines.Any(l => l.RequestId == r.Id && l.ItemId == item && l.Status != PurchaseRequestStatus.Cancelled));
        }

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim().ToUpperInvariant();
            requests = requests.Where(r => r.Number.Contains(term));
        }

        var ordered = requests
            .Select(r => new { Request = r, Nearest = db.PurchaseRequestLines.Where(l => l.RequestId == r.Id && l.Status == PurchaseRequestStatus.Open).Min(l => (DateOnly?)l.NeededBy) })
            .OrderBy(x => x.Nearest == null).ThenBy(x => x.Nearest).ThenByDescending(x => x.Request.Number);
        var result = await ordered.ToPagedListAsync(page, cancellationToken);
        var ids = result.Items.Select(x => x.Request.Id).ToList();
        var lines = await db.PurchaseRequestLines.AsNoTracking().Where(l => ids.Contains(l.RequestId)).OrderBy(l => l.Sequence)
            .Select(l => new { l.RequestId, l.ItemId, l.Status }).ToListAsync(cancellationToken);
        var itemInfo = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var orderIds = result.Items.Select(x => x.Request.ProductionOrderId).OfType<Guid>().Distinct().ToList();
        Dictionary<Guid, string> orderNumbers = orderIds.Count == 0 ? [] : (await orders.FindManyAsync(orderIds, cancellationToken)).ToDictionary(o => o.Id, o => o.Number);
        var projectIds = result.Items.Select(x => x.Request.ProjectId).OfType<Guid>().Distinct().ToList();
        Dictionary<Guid, BemAqui.Commercial.Contracts.ProjectSummary> projectInfo = projectIds.Count == 0 ? [] : (await projects.FindManyAsync(projectIds, cancellationToken)).ToDictionary(p => p.Id);
        var rows = result.Items.Select(x =>
        {
            var r = x.Request;
            var mine = lines.Where(l => l.RequestId == r.Id).ToList();
            var active = mine.Where(l => l.Status != PurchaseRequestStatus.Cancelled).ToList();
            var shown = active.Count > 0 ? active : mine;
            var first = shown.Count > 0 ? itemInfo.GetValueOrDefault(shown[0].ItemId) : null;
            var summary = first is null ? "—" : $"{first.Code} {first.Name}{(shown.Count > 1 ? $" e mais {shown.Count - 1}" : string.Empty)}";
            var p = r.ProjectId is { } pid ? projectInfo.GetValueOrDefault(pid) : null;
            return new PurchaseRequestSummary(
                r.Id, r.Number, r.Origin, r.Status, r.ProjectId, p?.Number, p?.Name, r.ProductionOrderId, r.ProductionOrderId is { } o ? orderNumbers.GetValueOrDefault(o) : null,
                summary, mine.Count, mine.Count(l => l.Status == PurchaseRequestStatus.Open), x.Nearest, r.CreatedAt);
        }).ToList();
        return new PagedList<PurchaseRequestSummary>(rows, result.Page, result.PageSize, result.Total);
    }

    /// <summary>Visão "Por item" (CA 12): as linhas abertas de todas as SC somadas por item, da necessidade mais próxima para a mais distante.</summary>
    public async Task<PagedList<PurchaseItemRow>> ByItemAsync(string? term, Guid? projectId, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var open = db.PurchaseRequestLines.AsNoTracking().Where(l => l.Status == PurchaseRequestStatus.Open);
        if (projectId is { } project)
        {
            open = open.Where(l => l.ProjectId == project);
        }

        var rows = await (from l in open
                          join r in db.PurchaseRequests.AsNoTracking() on l.RequestId equals r.Id
                          select new { LineId = l.Id, l.ItemId, l.Quantity, l.NeededBy, l.ProjectId, l.ProductionOrderId, r.Id, r.Number, r.Origin }).ToListAsync(cancellationToken);
        var itemInfo = (await items.FindAsync(rows.Select(r => r.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var projectIds = rows.Select(r => r.ProjectId).OfType<Guid>().Distinct().ToList();
        Dictionary<Guid, string> projectNumbers = projectIds.Count == 0 ? [] : (await projects.FindManyAsync(projectIds, cancellationToken)).ToDictionary(p => p.Id, p => p.Number);
        var orderIds = rows.Select(r => r.ProductionOrderId).OfType<Guid>().Distinct().ToList();
        Dictionary<Guid, string> orderNumbers = orderIds.Count == 0 ? [] : (await orders.FindManyAsync(orderIds, cancellationToken)).ToDictionary(o => o.Id, o => o.Number);
        var text = string.IsNullOrWhiteSpace(term) ? null : term.Trim();
        var grouped = rows.GroupBy(r => r.ItemId)
            .Select(g =>
            {
                var item = itemInfo.GetValueOrDefault(g.Key);
                var requests = g.OrderBy(r => r.NeededBy).ThenBy(r => r.Number, StringComparer.Ordinal)
                    .Select(r => new PurchaseItemRequest(r.Id, r.Number, r.Origin, r.Quantity, r.NeededBy, r.ProjectId is { } p ? projectNumbers.GetValueOrDefault(p) : null,
                        r.ProductionOrderId is { } o ? orderNumbers.GetValueOrDefault(o) : null, r.LineId)).ToList();
                return new PurchaseItemRow(g.Key, item?.Code ?? string.Empty, item?.Name ?? "Item removido", item?.UnitCode ?? string.Empty, g.Sum(r => r.Quantity), g.Min(r => r.NeededBy),
                    requests.Select(r => r.RequestId).Distinct().Count(), requests);
            })
            .Where(r => text is null || r.ItemCode.Contains(text, StringComparison.OrdinalIgnoreCase) || r.ItemName.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.NearestNeededBy).ThenBy(r => r.ItemCode, StringComparer.Ordinal).ToList();
        var normalized = page.Normalize();
        return new PagedList<PurchaseItemRow>(grouped.Skip(normalized.Skip).Take(normalized.PageSize).ToList(), normalized.Page, normalized.PageSize, grouped.Count);
    }

    /// <summary>
    /// "Nova solicitação" já preenchida (propositivo, CA 3): necessidade = hoje + prazo padrão, tag "Sugerido" na tela; vinda da falta de uma
    /// OS, o destino é o projeto dela, a necessidade é a data prometida − prazo (nunca antes de hoje) e a quantidade é o que falta sem SC.
    /// </summary>
    public async Task<Result<PurchaseRequestDefaults>> DefaultsAsync(Guid? productionOrderId, Guid? itemId, CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var lead = await parameters.LeadDaysAsync(cancellationToken);
        var neededBy = PurchaseRequestRules.SuggestedNeededBy(today, lead);
        Guid? projectId = null;
        string? projectNumber = null, projectName = null, orderNumber = null;
        if (productionOrderId is { } oid)
        {
            var order = await orders.FindAsync(oid, cancellationToken);
            if (order is null)
            {
                return Error.NotFound("production_order_not_found", "OS de fabricação não encontrada.");
            }

            orderNumber = order.Number;
            var project = await projects.FindAsync(order.ProjectId, cancellationToken);
            projectId = project?.Id;
            projectNumber = project?.Number;
            projectName = project?.Name;
            var fromPromise = PurchaseRequestRules.ShortageNeededBy(project?.PromisedDate, today, lead);
            neededBy = fromPromise < today ? today : fromPromise;
        }

        PurchaseItemOption? option = null;
        decimal? quantity = null;
        if (itemId is { } iid)
        {
            option = (await OptionsAsync((await items.FindAsync([iid], cancellationToken)).Where(i => i.IsActive && !i.IsDeleted && i.Type != ItemType.Service).ToList(), cancellationToken))
                .FirstOrDefault();
            if (option is not null && productionOrderId is { } order)
            {
                var shortage = (await reservations.OpenShortagesAsync(cancellationToken)).Where(s => s.ProductionOrderId == order && s.ItemId == iid).Sum(s => s.ShortQty);
                var covered = await db.PurchaseRequestLines.AsNoTracking()
                    .Where(l => l.ProductionOrderId == order && l.ItemId == iid && l.Status != PurchaseRequestStatus.Cancelled).SumAsync(l => l.Quantity, cancellationToken);
                quantity = shortage - covered > 0m ? shortage - covered : null;
            }
        }

        return new PurchaseRequestDefaults(neededBy, lead, projectId, projectNumber, projectName, productionOrderId, orderNumber, option, quantity);
    }

    /// <summary>Itens para a linha (CA 3, 4): só material ativo (Serviço fica fora; o servidor também recusa), com as unidades.</summary>
    public async Task<IReadOnlyList<PurchaseItemOption>> ItemOptionsAsync(string? term, CancellationToken cancellationToken = default) =>
        await OptionsAsync((await items.SearchAsync(null, term, 30, cancellationToken)).Where(i => i.Type != ItemType.Service).Take(20).ToList(), cancellationToken);

    /// <summary>Projetos em andamento para o destino (R-CMP-6), sem exigir permissão do Commercial.</summary>
    public async Task<IReadOnlyList<PurchaseProjectOption>> ProjectOptionsAsync(string? term, CancellationToken cancellationToken = default) =>
        (await projects.SearchAsync(term, 20, cancellationToken)).Where(p => p.Status is not ("Closed" or "Cancelled")).Select(p => new PurchaseProjectOption(p.Id, p.Number, p.Name)).ToList();

    private async Task<List<PurchaseItemOption>> OptionsAsync(List<ItemSummary> found, CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, IReadOnlyList<ItemUnitOption>> units = found.Count == 0 ? new Dictionary<Guid, IReadOnlyList<ItemUnitOption>>() : await itemUnits.ForItemsAsync(found.Select(i => i.Id).ToList(), ct);
        return found.Select(i => new PurchaseItemOption(i.Id, i.Code, i.Name, i.Type, i.UnitId, i.UnitCode,
            units.GetValueOrDefault(i.Id) ?? [new ItemUnitOption(i.UnitId, i.UnitCode, true, 1m)])).ToList();
    }
}
