using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>Filtros da lista de romaneios (CA 13): projeto, "Aguardando você" (<c>aguardando</c>) ou todos, e a situação.</summary>
public sealed record ShipmentListQuery(Guid? ProjectId, string? Filter, string? Situation);

/// <summary>
/// Romaneio de expedição (PRD-06b, R-PRD-8): montar (<c>production.create</c>), confirmar e cancelar (<c>production.edit</c>), confirmar o
/// produto do estoque geral (outra pessoa com <c>inventory.approve</c>) e estornar (<c>production.approve</c>). A Produção é dona da
/// transação da confirmação e chama o Inventory de forma síncrona, só pelos contratos (<see cref="IStockMovements"/> e
/// <see cref="IStockSerials"/>, ADR 0023 §1); o que está pronto para expedir vem do <see cref="IFinishedGoodsStock"/>. Projeto, cliente,
/// endereço e transportadora são validados pelos Contracts dos donos (ADR 0006).
/// </summary>
public sealed partial class ShipmentService(
    IProductionDb db, IProjects projects, IParties parties, IItems items, ILocations locations, IFinishedGoodsStock finished, IStockSerials serials, IStockMovements stock,
    IStockBalances balances, INumberSequence numbers, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, INotificationService notifications,
    IEventPublisher events, TimeProvider clock, IPdfRenderer renderer, AttachmentService attachments, ICompanyIdentity company, IFileStorage storage,
    BemAqui.Fiscal.Contracts.IOutgoingInvoices invoices)
{
    public const string Link = "/app/producao/expedicao/";
    public const string AwaitingType = "production.shipment_stock_pending";
    public const string DecisionType = "production.shipment_stock_decided";
    public const string SelfApprovedType = "production.shipment_self_approved";

    public static Error NotFound() => Error.NotFound("shipment_not_found", "Romaneio não encontrado.");

    public async Task<ShipmentPage> ListAsync(ShipmentListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var me = currentUser.UserId;
        var approver = await permissions.HasPermissionAsync("inventory", "approve", cancellationToken);
        var mine = AwaitingMine(db.Shipments.AsNoTracking(), me);
        var shipments = db.Shipments.AsNoTracking();
        if (query.ProjectId is { } projectId)
        {
            shipments = shipments.Where(s => s.ProjectId == projectId);
        }

        if (query.Filter == "aguardando")
        {
            shipments = approver ? AwaitingMine(shipments, me) : shipments.Where(_ => false);
        }

        shipments = query.Situation switch
        {
            "rascunho" => shipments.Where(s => s.Status == ShipmentState.Draft),
            "aguardando" => shipments.Where(s => s.Status == ShipmentState.AwaitingStockApproval),
            "expedido" => shipments.Where(s => s.Status == ShipmentState.Shipped),
            "cancelado" => shipments.Where(s => s.Status == ShipmentState.Cancelled),
            "estornado" => shipments.Where(s => s.Status == ShipmentState.Reversed),
            _ => shipments,
        };
        var total = await shipments.CountAsync(cancellationToken);
        var rows = await shipments.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var lines = await db.ShipmentLines.AsNoTracking().Where(l => ids.Contains(l.ShipmentId)).ToListAsync(cancellationToken);
        var projectNumbers = (await projects.FindManyAsync(rows.Select(r => r.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.Number);
        var partyNames = await PartyNamesAsync(rows.Select(r => r.PartyId), cancellationToken);
        var items = rows.Select(s =>
        {
            var own = lines.Where(l => l.ShipmentId == s.Id).ToList();
            return new ShipmentRow(s.Id, s.Number, s.Status, s.ProjectId, projectNumbers.GetValueOrDefault(s.ProjectId, string.Empty), partyNames.GetValueOrDefault(s.PartyId, string.Empty),
                own.Count, own.Sum(l => l.Quantity), s.PlannedDate, s.ShippedAt, s.CreatedAt, own.Any(l => l.FromGeneralStock));
        }).ToList();
        return new ShipmentPage(items, total, page.Page, page.PageSize, approver ? await mine.CountAsync(cancellationToken) : 0);
    }

    public async Task<Result<ShipmentDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shipment is null)
        {
            return NotFound();
        }

        var lines = await LineViewsAsync(id, cancellationToken);
        var project = await projects.FindAsync(shipment.ProjectId, cancellationToken);
        var party = await parties.FindAsync(shipment.PartyId, cancellationToken);
        var carrier = shipment.CarrierPartyId is { } c ? await parties.FindAsync(c, cancellationToken) : null;
        var installation = await db.ProductionOrders.AsNoTracking()
            .Where(o => o.ProjectId == shipment.ProjectId && o.Type == ProductionOrderType.Installation && o.Status != ProductionOrderStatus.Cancelled)
            .OrderBy(o => o.Number).Select(o => new { o.Id, o.Number }).FirstOrDefaultAsync(cancellationToken);
        var (canConfirm, confirmBlocker) = shipment.Status == ShipmentState.Draft ? (true, (string?)null) : (false, ShipmentRules.NotDraft);
        var (canStock, stockBlocker) = await StockDecisionAsync(shipment, cancellationToken);
        var status = project?.Status ?? string.Empty;
        var invoice = shipment.Status == ShipmentState.Shipped ? await invoices.ActiveForShipmentAsync(shipment.Id, cancellationToken) : null;
        var next = new ShipmentNext(canConfirm, confirmBlocker, canStock, stockBlocker, installation?.Id, installation?.Number, status is "Delivered" or "Closed", status,
            invoice?.Id, invoice?.Label, invoice?.Status);
        return new ShipmentDetail(
            shipment.Id, shipment.Number, shipment.Status, shipment.ProjectId, project?.Number ?? string.Empty, project?.Name ?? string.Empty, shipment.PartyId, Name(party),
            shipment.DeliveryAddress, shipment.CarrierPartyId, carrier is null ? null : Name(carrier), shipment.VehiclePlate, shipment.DriverName, shipment.Volumes, shipment.GrossWeight,
            shipment.PlannedDate, lines.Any(l => l.FromGeneralStock), shipment.CreatedAt, shipment.CreatedBy, await WhoAsync(shipment.CreatedBy, cancellationToken),
            shipment.StockApprovalRequestedAt, await WhoAsync(shipment.StockApprovedBy, cancellationToken), shipment.SelfApproved, shipment.ShippedAt,
            await WhoAsync(shipment.ShippedBy, cancellationToken), shipment.CancelledAt, await WhoAsync(shipment.CancelledBy, cancellationToken), shipment.CancelReason,
            shipment.ReversedAt, await WhoAsync(shipment.ReversedBy, cancellationToken), shipment.ReversalReason, shipment.PdfAttachmentId, lines, next, shipment.Version);
    }

    /// <summary>Linha do romaneio pelo id do romaneio (CA 15, IDOR): a linha de outro romaneio dá 404.</summary>
    public async Task<Result<ShipmentLineView>> LineAsync(Guid id, Guid lineId, CancellationToken cancellationToken = default)
    {
        if (!await db.ShipmentLines.AsNoTracking().AnyAsync(l => l.Id == lineId && l.ShipmentId == id, cancellationToken))
        {
            return Error.NotFound("shipment_line_not_found", "Linha do romaneio não encontrada.");
        }

        return (await LineViewsAsync(id, cancellationToken)).Single(l => l.Id == lineId);
    }

    /// <summary>"Prontos para expedir" (CA 13): projetos com produto acabado no estoque, com físico, reservado e disponível.</summary>
    public async Task<IReadOnlyList<ReadyToShipProject>> ReadyAsync(CancellationToken cancellationToken = default)
    {
        var stockLines = await finished.ReadyToShipAsync(500, cancellationToken);
        var views = await ReadyLinesAsync(stockLines, null, cancellationToken);
        var byProject = stockLines.Zip(views).GroupBy(x => x.First.ProjectId!.Value).ToList();
        var summaries = (await projects.FindManyAsync(byProject.Select(g => g.Key).ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var partyNames = await PartyNamesAsync(summaries.Values.Select(p => p.PartyId), cancellationToken);
        return byProject
            .Select(g =>
            {
                var p = summaries.GetValueOrDefault(g.Key);
                var lines = g.Select(x => x.Second).ToList();
                return new ReadyToShipProject(g.Key, p?.Number ?? string.Empty, p?.Name ?? string.Empty, p is null ? string.Empty : partyNames.GetValueOrDefault(p.PartyId, string.Empty),
                    lines.Sum(l => l.Shippable), lines);
            })
            .Where(r => r.Lines.Count > 0)
            .OrderBy(r => r.ProjectNumber, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Formulário do "Novo romaneio" (CA 1, padrão 6): o projeto, o endereço da obra (<see cref="IProjects"/> e
    /// <see cref="IParties.AddressesAsync"/>), as transportadoras com a última usada com o cliente como "Sugerido" e o produto acabado do
    /// projeto. Vindo da OS (CA 9), as séries da OS vêm marcadas na tela.
    /// </summary>
    public async Task<Result<ShipmentForm>> FormAsync(Guid projectId, Guid? productionOrderId, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return Error.NotFound("project_not_found", "Projeto não encontrado.");
        }

        var party = await parties.FindAsync(project.PartyId, cancellationToken);
        var address = await AddressAsync(project, cancellationToken);
        var lastCarrier = await db.Shipments.AsNoTracking().Where(s => s.PartyId == project.PartyId && s.CarrierPartyId != null && s.Status != ShipmentState.Cancelled)
            .OrderByDescending(s => s.CreatedAt).Select(s => s.CarrierPartyId).FirstOrDefaultAsync(cancellationToken);
        var carriers = await CarriersAsync(null, lastCarrier, cancellationToken);
        var available = await ReadyLinesAsync(await finished.ForProjectAsync(projectId, cancellationToken), null, cancellationToken);
        return new ShipmentForm(project.Id, project.Number, project.Name, project.PartyId, Name(party), address, lastCarrier, carriers, available, productionOrderId);
    }

    /// <summary>"Incluir produto do estoque geral" (CA 2): produto acabado sem projeto, pela busca.</summary>
    public async Task<IReadOnlyList<ReadyToShipLine>> GeneralStockAsync(string? term, CancellationToken cancellationToken = default) =>
        await ReadyLinesAsync(await finished.GeneralStockAsync(term, 50, cancellationToken), null, cancellationToken);

    /// <summary>Transportadoras (terceiros com o papel Transportadora) para o seletor, pela busca; a sugerida primeiro.</summary>
    public async Task<IReadOnlyList<CarrierOption>> CarriersAsync(string? term, Guid? suggested, CancellationToken cancellationToken = default)
    {
        var found = (await parties.SearchAsync(term, PartyRoleKind.Carrier, 50, cancellationToken)).ToList();
        if (suggested is { } id && found.All(p => p.Id != id) && await parties.FindAsync(id, cancellationToken) is { IsActive: true, IsDeleted: false } extra)
        {
            found.Insert(0, extra);
        }

        return found.Select(p => new CarrierOption(p.Id, Name(p), p.Id == suggested)).OrderByDescending(c => c.Suggested).ThenBy(c => c.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>
    /// Impacto da confirmação (CA 4): por linha, a saída do estoque, o local, as séries que passam a "Expedido" e o físico antes → depois;
    /// "Saldo insuficiente" marcado quando o disponível já não cobre a saída.
    /// </summary>
    public async Task<Result<ShipmentImpact>> ImpactAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var shipment = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shipment is null)
        {
            return NotFound();
        }

        var lines = await LineViewsAsync(id, cancellationToken);
        var positions = await balances.GetAsync(lines.Select(l => l.ItemId).Distinct().ToList(), null, cancellationToken);
        var impact = new List<ShipmentImpactLine>();
        var taken = new Dictionary<(Guid, Guid), decimal>();
        foreach (var l in lines)
        {
            var position = positions.FirstOrDefault(p => p.ItemId == l.ItemId && p.LocationId == l.LocationId);
            var before = (position?.OnHand ?? 0m) - taken.GetValueOrDefault((l.ItemId, l.LocationId));
            var available = (position?.Available ?? 0m) - taken.GetValueOrDefault((l.ItemId, l.LocationId));
            taken[(l.ItemId, l.LocationId)] = taken.GetValueOrDefault((l.ItemId, l.LocationId)) + l.Quantity;
            impact.Add(new ShipmentImpactLine(l.Id, l.ItemCode, l.ItemName, l.UnitCode, l.LocationName, l.Quantity, before, before - l.Quantity, l.Serials.Select(s => s.SerialNumber).ToList(),
                l.FromGeneralStock, available < l.Quantity));
        }

        return new ShipmentImpact(shipment.Id, shipment.Number, shipment.Status, ShipmentRules.RequiresStockApproval(lines.Select(l => l.FromGeneralStock)), impact, shipment.Version);
    }

    /// <summary>"Baixar PDF" (CA 3): gerado na hora, do romaneio como está (o expedido também fica guardado na aba Documentos).</summary>
    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var bytes = await renderer.RenderAsync(new ShipmentPdfDocument(await PdfModelAsync(detail.Value, cancellationToken)), cancellationToken);
        return (bytes, detail.Value.Number + ".pdf");
    }

    /// <summary>O romaneio existe (dono de anexo <c>Shipment</c>, CAD-06 CA 8).</summary>
    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => db.Shipments.AnyAsync(s => s.Id == id, cancellationToken);

    internal async Task<ShipmentPdfModel> PdfModelAsync(ShipmentDetail detail, CancellationToken ct)
    {
        var info = await company.GetAsync(ct);
        byte[]? logo = null;
        if (info.LogoStoragePath is { } path)
        {
            await using var content = await storage.OpenReadAsync(path, ct);
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            logo = buffer.ToArray();
        }

        var party = await parties.FindAsync(detail.PartyId, ct);
        return new ShipmentPdfModel(
            new ShipmentPdfCompany(info.TradeName ?? info.LegalName, info.LegalName, info.Document, info.Address, string.Join(" · ", new[] { info.Phone, info.Email }.Where(x => !string.IsNullOrWhiteSpace(x))), logo),
            detail.Number, detail.PlannedDate ?? BusinessTime.Today(clock), detail.ProjectNumber, detail.PartyName, party?.Document, detail.DeliveryAddress, detail.CarrierName,
            detail.VehiclePlate, detail.DriverName, detail.Volumes, detail.GrossWeight,
            detail.Lines.Select(l => new ShipmentPdfItem(l.Sequence, l.ItemCode, l.ItemName, l.Quantity, l.UnitCode, l.Serials.Select(s => s.SerialNumber).ToList(), l.LotNumber)).ToList());
    }

    private async Task<IReadOnlyList<ShipmentLineView>> LineViewsAsync(Guid shipmentId, CancellationToken ct)
    {
        var lines = await db.ShipmentLines.AsNoTracking().Where(l => l.ShipmentId == shipmentId).OrderBy(l => l.Sequence).ToListAsync(ct);
        var lineSerials = await db.ShipmentLineSerials.AsNoTracking().Where(s => s.ShipmentId == shipmentId).OrderBy(s => s.SerialNumber).ToListAsync(ct);
        var info = (await serials.FindAsync(lineSerials.Select(s => s.SerialId).ToList(), ct)).ToDictionary(s => s.Id);
        var names = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var places = (await locations.FindAsync(lines.Select(l => l.LocationId).Distinct().ToList(), ct)).ToDictionary(l => l.Id, l => l.Name);
        return lines.Select(l =>
        {
            var item = names.GetValueOrDefault(l.ItemId);
            var own = lineSerials.Where(s => s.LineId == l.Id)
                .Select(s => new ShipmentSerialView(s.SerialId, s.SerialNumber, info.GetValueOrDefault(s.SerialId)?.ProductionOrderId, null)).ToList();
            return new ShipmentLineView(l.Id, l.Sequence, l.ItemId, item?.Code ?? string.Empty, item?.Name ?? string.Empty, item?.UnitCode ?? string.Empty, l.Quantity, l.LocationId,
                places.GetValueOrDefault(l.LocationId, string.Empty), l.LotId, l.LotNumber, l.FromGeneralStock, own, l.MovementId);
        }).ToList();
    }

    /// <summary>Linhas do estoque com nomes, as séries com o romaneio não cancelado em que já estão e o que já está em outros romaneios abertos.</summary>
    private async Task<IReadOnlyList<ReadyToShipLine>> ReadyLinesAsync(IReadOnlyList<FinishedGoodsStockLine> stockLines, Guid? exceptShipment, CancellationToken ct)
    {
        if (stockLines.Count == 0)
        {
            return [];
        }

        var serialIds = stockLines.SelectMany(l => l.Serials).Select(s => s.Id).ToList();
        var held = await HeldSerialsAsync(serialIds, exceptShipment, ct);
        var open = await OpenQuantitiesAsync(stockLines.Select(l => l.ItemId).Distinct().ToList(), exceptShipment, ct);
        var names = (await items.FindAsync(stockLines.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var places = (await locations.FindAsync(stockLines.Select(l => l.LocationId).Distinct().ToList(), ct)).ToDictionary(l => l.Id, l => l.Name);
        return stockLines.Select(l =>
        {
            var item = names.GetValueOrDefault(l.ItemId);
            var general = l.ProjectId is null;
            var inOpen = l.TracksSerial ? l.Serials.Count(s => held.ContainsKey(s.Id))
                : open.Where(o => o.ItemId == l.ItemId && o.LocationId == l.LocationId && o.LotId == l.LotId && o.FromGeneralStock == general).Sum(o => o.Quantity);
            return new ReadyToShipLine(l.ItemId, item?.Code ?? string.Empty, item?.Name ?? string.Empty, item?.UnitCode ?? string.Empty, l.LocationId, places.GetValueOrDefault(l.LocationId, string.Empty),
                l.LotId, l.LotNumber, l.TracksSerial, l.Quantity, l.OnHand, l.Reserved, l.Available, l.Shippable, inOpen, general,
                l.Serials.Select(s => new ShipmentSerialView(s.Id, s.SerialNumber, s.ProductionOrderId, held.GetValueOrDefault(s.Id))).ToList());
        }).ToList();
    }

    /// <summary>Séries em romaneio não cancelado nem estornado (o índice único parcial), com o número do romaneio.</summary>
    private async Task<Dictionary<Guid, string>> HeldSerialsAsync(List<Guid> serialIds, Guid? exceptShipment, CancellationToken ct)
    {
        if (serialIds.Count == 0)
        {
            return [];
        }

        var rows = await (from ls in db.ShipmentLineSerials.AsNoTracking()
                          join s in db.Shipments.AsNoTracking() on ls.ShipmentId equals s.Id
                          where ls.ReleasedAt == null && serialIds.Contains(ls.SerialId) && (exceptShipment == null || s.Id != exceptShipment)
                          select new { ls.SerialId, s.Number }).ToListAsync(ct);
        return rows.GroupBy(r => r.SerialId).ToDictionary(g => g.Key, g => g.First().Number);
    }

    /// <summary>Quantidades sem série em romaneios abertos (rascunho ou aguardando), para o padrão da montagem não repetir o que já está montado.</summary>
    private async Task<List<ShipmentLine>> OpenQuantitiesAsync(IReadOnlyCollection<Guid> itemIds, Guid? exceptShipment, CancellationToken ct) =>
        await (from l in db.ShipmentLines.AsNoTracking()
               join s in db.Shipments.AsNoTracking() on l.ShipmentId equals s.Id
               where itemIds.Contains(l.ItemId) && (s.Status == ShipmentState.Draft || s.Status == ShipmentState.AwaitingStockApproval) && (exceptShipment == null || s.Id != exceptShipment)
               select l).ToListAsync(ct);

    /// <summary>Quem pode confirmar o estoque geral agora (CA 6): <c>inventory.approve</c> e não ter montado nem pedido, salvo o Proprietário único.</summary>
    private async Task<(bool Can, string? Blocker)> StockDecisionAsync(Shipment shipment, CancellationToken ct)
    {
        if (shipment.Status != ShipmentState.AwaitingStockApproval)
        {
            return (false, "Só romaneio aguardando a confirmação do estoque geral.");
        }

        if (!await permissions.HasPermissionAsync("inventory", "approve", ct))
        {
            return (false, "Quem confirma o estoque geral é quem aprova no Estoque.");
        }

        if (currentUser.UserId is { } me && Involved(shipment).Contains(me)
            && !await SoleOwnerRule.NoOtherEligibleAsync(users, "inventory.approve", me, Involved(shipment), ct))
        {
            return (false, ShipmentRules.OwnBlocked);
        }

        return (true, null);
    }

    private static Guid[] Involved(Shipment s) => new[] { s.CreatedBy, s.StockApprovalRequestedBy }.OfType<Guid>().Distinct().ToArray();

    private static IQueryable<Shipment> AwaitingMine(IQueryable<Shipment> shipments, Guid? me) =>
        shipments.Where(s => s.Status == ShipmentState.AwaitingStockApproval && s.CreatedBy != me && s.StockApprovalRequestedBy != me);

    /// <summary>Endereço da obra do projeto; sem obra, o de entrega e depois o de cobrança do cliente.</summary>
    private async Task<string?> AddressAsync(ProjectSummary project, CancellationToken ct)
    {
        var addresses = await parties.AddressesAsync(project.PartyId, ct);
        var site = project.SiteAddressId is { } siteId ? addresses.FirstOrDefault(a => a.Id == siteId) : null;
        site ??= addresses.FirstOrDefault(a => a.Kind == AddressKind.Delivery) ?? addresses.FirstOrDefault(a => a.Kind == AddressKind.Site) ?? (addresses.Count > 0 ? addresses[0] : null);
        return site is null ? null : ShipmentRules.FormatAddress(site);
    }

    private async Task<Dictionary<Guid, string>> PartyNamesAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        (await parties.FindManyAsync(ids.Distinct().ToList(), ct)).ToDictionary(p => p.Id, Name);

    private static string Name(PartySummary? p) => p is null ? string.Empty : p.TradeName ?? p.LegalName;

    private async Task<string?> WhoAsync(Guid? userId, CancellationToken ct) => userId is { } id ? await users.DisplayNameAsync(id, ct) : null;
}
