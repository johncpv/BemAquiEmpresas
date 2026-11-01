using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// NF-e de saída (FIS-03a): rascunho a partir do romaneio expedido (CA 1), "Faturar" no projeto (CA 2), edição do rascunho com o cálculo
/// refeito (CA 3), saneamento pelos limites do esquema (CA 4), prévia (CA 5), emissão com as pré-condições (CA 6), correção da rejeitada
/// (CA 8), reenvio do XML e do DANFE (CA 14), fila "A emitir" (CA 16) e a lista (CA 17). Romaneio, projeto, cliente, item e títulos vêm só
/// pelos contratos dos donos (ADR 0006). Permissões no endpoint: ver <c>fiscal.view</c>; criar, emitir e reenviar <c>fiscal.create</c>;
/// editar, corrigir e descartar <c>fiscal.edit</c>.
/// </summary>
public sealed partial class OutgoingInvoiceService(
    IFiscalDb db, IShipments shipments, IProjects projects, IParties parties, IItems catalog, IReceivables receivables, IFiscalSettings settings, ICompanyFiscalProfile company,
    OutgoingInvoiceCalculator calculator, OutgoingNfePayloads payloads, NfeSubmissionService submissions, FiscalEmitterAccess access, OutgoingInvoiceEffects effects,
    AttachmentService attachments, IFileStorage storage, IPdfRenderer renderer, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users,
    IBusinessCalendar calendar, TimeProvider clock)
{
    public const string ShipmentLink = "/app/producao/expedicao/";
    public const string PartyLink = "/app/terceiros/";
    public const int QueueLimit = 500;
    public const string QueueLink = "/app/fiscal/saida";

    // ---------- CA 1: rascunho a partir do romaneio ----------

    public async Task<Result<OutgoingInvoiceView>> CreateAsync(CreateOutgoingInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var shipment = await shipments.ByIdAsync(request.ShipmentId, cancellationToken);
        if (shipment is null)
        {
            return Error.NotFound(FiscalErrorCodes.NotFound, "Romaneio não encontrado.");
        }

        if (shipment.Status != ShipmentState.Shipped)
        {
            return Error.Conflict(FiscalErrorCodes.ShipmentNotShipped, "Só o romaneio expedido vira nota. Confirme a expedição antes.");
        }

        if (await ActiveForShipmentAsync(shipment.Id, cancellationToken) is { } existing)
        {
            return Exists(existing);
        }

        var project = await projects.FindAsync(shipment.ProjectId, cancellationToken);
        if (project is null)
        {
            return Error.NotFound(FiscalErrorCodes.NotFound, "Projeto do romaneio não encontrado.");
        }

        var current = await settings.CurrentAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var invoice = OutgoingInvoice.NewDraft(shipment.Id, shipment.Number, project.Id, project.ContractId, project.PartyId, currentUser.UserId, now);
        await FillRecipientAsync(parties, invoice, project.PartyId, cancellationToken);
        invoice.Environment = current.Environment;
        invoice.FreightMode = current.FreightMode;
        invoice.VolumeSpecies = current.VolumeSpecies;
        invoice.CarrierPartyId = shipment.CarrierPartyId;
        invoice.Volumes = shipment.Volumes;
        invoice.GrossWeight = shipment.GrossWeight;
        invoice.AdditionalInformation = OutgoingInvoiceRules.SiteInfo(shipment.DeliveryAddress);
        var items = await ItemsFromShipmentAsync(invoice, shipment, cancellationToken);
        if (items.Count == 0)
        {
            return Error.Conflict(FiscalErrorCodes.ShipmentNotShipped, "O romaneio não tem item de mercadoria para a NF-e.");
        }

        await calculator.RecalculateAsync(invoice, items, cancellationToken);
        db.OutgoingInvoices.Add(invoice);
        db.OutgoingInvoiceItems.AddRange(items);
        foreach (var r in await receivables.ForContractAsync(project.ContractId, cancellationToken))
        {
            db.OutgoingInvoiceReceivables.Add(new OutgoingInvoiceReceivable { InvoiceId = invoice.Id, ReceivableId = r.Id });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_outgoing_invoices_active_shipment", StringComparison.Ordinal) == true)
        {
            // CA 19: dois rascunhos do mesmo romaneio ao mesmo tempo; o índice único parcial deixa só um.
            db.ChangeTracker.Clear();
            return await ActiveForShipmentAsync(shipment.Id, cancellationToken) is { } winner ? Exists(winner) : Error.Conflict(FiscalErrorCodes.InvoiceExists, "O romaneio já tem nota.");
        }

        return await GetAsync(invoice.Id, cancellationToken);
    }

    private static Error Exists(OutgoingInvoice existing) =>
        new(FiscalErrorCodes.InvoiceExists, $"O romaneio já tem a nota {(existing.Number is null ? "em rascunho" : OutgoingInvoiceEffects.Label(existing))}.", ErrorType.Conflict)
        {
            Data = new Dictionary<string, object?> { ["invoiceId"] = existing.Id, ["link"] = OutgoingInvoiceEffects.Link(existing.Id) },
        };

    private Task<OutgoingInvoice?> ActiveForShipmentAsync(Guid shipmentId, CancellationToken ct) =>
        db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.ShipmentId == shipmentId && i.DiscardedAt == null && !OutgoingInvoice.InactiveStatuses.Contains(i.Status), ct);

    /// <summary>
    /// Destinatário do cadastro (CA 1): documento, IE, indicador, endereço de cobrança (ou o primeiro com IBGE) e o e-mail de cobrança. O tomador
    /// da NFS-e da instalação usa o mesmo (FIS-06 CA 2).
    /// </summary>
    internal static async Task FillRecipientAsync(IParties parties, OutgoingInvoice invoice, Guid partyId, CancellationToken ct)
    {
        var party = await parties.FiscalInfoAsync(partyId, ct);
        var addresses = await parties.AddressesAsync(partyId, ct);
        var address = addresses.Where(a => a.Kind == AddressKind.Billing).OrderByDescending(a => a.IbgeCode != null).FirstOrDefault()
            ?? addresses.Where(a => a.Kind != AddressKind.Site).OrderByDescending(a => a.IbgeCode != null).FirstOrDefault()
            ?? (addresses.Count > 0 ? addresses[0] : null);
        var contact = await parties.BillingContactAsync(partyId, ct);
        invoice.RecipientName = NfeTextRules.Clean(party?.LegalName);
        invoice.RecipientDocument = party?.Document is { } d ? new string(d.Where(char.IsAsciiDigit).ToArray()) : null;
        invoice.RecipientStateRegistration = NfeTextRules.Clean(party?.StateRegistration);
        invoice.RecipientType = OutgoingInvoiceRules.RecipientTypeOf(party?.Kind == PartyKind.PJ, party?.StateRegistration);
        invoice.RecipientEmail = contact is { ReceivesBilling: true, Email: { Length: > 0 } billing } ? billing.Trim() : party?.Email?.Trim() ?? contact?.Email?.Trim();
        invoice.RecipientPhone = party?.Phone is { } phone ? new string(phone.Where(char.IsAsciiDigit).ToArray()) : null;
        invoice.RecipientStreet = NfeTextRules.Clean(address?.Street);
        invoice.RecipientNumber = NfeTextRules.Clean(address?.Number);
        invoice.RecipientComplement = NfeTextRules.Clean(address?.Complement);
        invoice.RecipientDistrict = NfeTextRules.Clean(address?.District);
        invoice.RecipientCityName = NfeTextRules.Clean(address?.City);
        invoice.RecipientCityCode = address?.IbgeCode;
        invoice.RecipientState = address?.State?.Trim().ToUpperInvariant();
        invoice.RecipientPostalCode = address?.ZipCode is { } zip ? new string(zip.Where(char.IsAsciiDigit).ToArray()) : null;
        invoice.FinalConsumer = invoice.RecipientType != RecipientType.Contributor;
    }

    /// <summary>
    /// Itens a partir das linhas do romaneio (CA 1 e 13): só o que não é <c>Service</c>; valor unitário do item do projeto (contrato); a
    /// operação pelo tipo do item; as séries nas informações do item; a alíquota do IPI da TIPI da última nota com o mesmo NCM.
    /// </summary>
    private async Task<List<OutgoingInvoiceItem>> ItemsFromShipmentAsync(OutgoingInvoice invoice, ShipmentInfo shipment, CancellationToken ct)
    {
        var projectItems = (await projects.ItemsAsync(shipment.ProjectId, ct)).Where(i => i.Kind != ProjectItemKind.Service).OrderBy(i => i.Sequence).ToList();
        var summaries = (await catalog.FindAsync(shipment.Lines.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var used = new Dictionary<Guid, decimal>();
        var result = new List<OutgoingInvoiceItem>();
        var sequence = 0;
        foreach (var line in shipment.Lines.OrderBy(l => l.Sequence))
        {
            if (!summaries.TryGetValue(line.ItemId, out var summary) || summary.Type == ItemType.Service)
            {
                continue;
            }

            var match = projectItems.FirstOrDefault(p => p.ItemId == line.ItemId && used.GetValueOrDefault(p.Id) < p.Quantity) ?? projectItems.FirstOrDefault(p => p.ItemId == line.ItemId);
            if (match is not null)
            {
                used[match.Id] = used.GetValueOrDefault(match.Id) + line.Quantity;
            }

            var kind = match?.Kind.ToString() ?? nameof(ProjectItemKind.Product);
            var ncm = summary.Ncm is { } n ? new string(n.Where(char.IsAsciiDigit).ToArray()) : null;
            var ipi = ncm is null ? null : await db.OutgoingInvoiceItems.AsNoTracking().Where(i => i.Ncm == ncm && i.IpiRate != null).OrderByDescending(i => i.Id)
                .Select(i => i.IpiRate).FirstOrDefaultAsync(ct);
            result.Add(new OutgoingInvoiceItem
            {
                InvoiceId = invoice.Id, Sequence = ++sequence, ShipmentLineId = line.LineId, ProjectItemId = match?.Id, ItemId = line.ItemId, Code = summary.Code,
                Description = NfeTextRules.Clean(summary.Name) ?? summary.Code, Ncm = ncm, Cest = summary.Cest, Origin = summary.Origin, Unit = string.IsNullOrEmpty(summary.UnitCode) ? "UN" : summary.UnitCode,
                QuoteItemKind = kind, Operation = OutgoingInvoiceRules.OperationFor(kind, summary.Type is ItemType.Finished or ItemType.Component), ShippedQuantity = line.Quantity,
                Quantity = line.Quantity, UnitPrice = match?.UnitPrice ?? 0m, IpiRate = ipi, AdditionalInfo = OutgoingInvoiceRules.SerialsInfo(line.SerialNumbers),
                SerialIds = [.. line.SerialIds], SerialNumbers = [.. line.SerialNumbers],
            });
        }

        return result;
    }

    // ---------- Consulta ----------

    public async Task<Result<OutgoingInvoiceView>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken);
        return invoice is null ? NotFound() : await ViewAsync(invoice, cancellationToken);
    }

    /// <summary>Item pelo id da nota (CA 18, IDOR): o item de outra nota dá 404.</summary>
    public async Task<Result<OutgoingInvoiceItemView>> ItemAsync(Guid id, Guid itemId, CancellationToken cancellationToken = default)
    {
        var view = await GetAsync(id, cancellationToken);
        if (view.IsFailure)
        {
            return view.Error!;
        }

        return view.Value.Items.FirstOrDefault(i => i.Id == itemId) is { } item ? item : Error.NotFound(FiscalErrorCodes.NotFound, "Item da nota não encontrado.");
    }

    private static Error NotFound() => Error.NotFound(FiscalErrorCodes.InvoiceNotFound, "Nota não encontrada.");

    private async Task<OutgoingInvoiceView> ViewAsync(OutgoingInvoice invoice, CancellationToken ct)
    {
        var items = await db.OutgoingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == invoice.Id).OrderBy(i => i.Sequence).ToListAsync(ct);
        var project = invoice.ProjectId is { } p ? await projects.FindAsync(p, ct) : null;
        var carrier = invoice.CarrierPartyId is { } c ? await parties.FindAsync(c, ct) : null;
        var editable = invoice.IsEditableDraft;
        var candidates = editable ? await calculator.CandidatesAsync(ct) : null;
        var recipient = new TaxRecipient((invoice.RecipientState ?? string.Empty).ToUpperInvariant(), invoice.RecipientType, invoice.FinalConsumer);
        var itemViews = items.Select(i => new OutgoingInvoiceItemView(
            i.Id, i.Sequence, i.ItemId, i.Code, i.Description, i.Ncm, i.Cest, i.Origin, i.Unit, i.QuoteItemKind, i.Operation, i.ShippedQuantity, i.Quantity, i.UnitPrice, i.Discount,
            i.Freight, i.OtherExpenses, i.IpiRate, NeedsIpiRate(i, candidates, recipient), i.AdditionalInfo, i.SerialNumbers, i.ChosenRuleId, i.TaxRuleId, i.TaxRuleVersion, i.TaxRuleName, i.Cfop,
            i.Taxes, i.GrossAmount, i.Total, i.CostTaxAmount,
            candidates is null ? [] : OutgoingInvoiceCalculator.Applicable(i, recipient, candidates).OrderBy(r => r.Name, StringComparer.CurrentCulture)
                .Select(r => new TaxRuleOption(r.RuleId, r.Name, r.RuleVersion, r.IsConfirmed)).ToList())).ToList();
        var installments = invoice.ContractId is { } contract
            ? (await receivables.ForContractAsync(contract, ct)).Select(r => new ContractInstallmentView(r.Id, r.Number, r.Description, r.DueDate, r.Amount, r.Status.ToString())).ToList()
            : [];
        var canEdit = await permissions.HasPermissionAsync(FiscalModuleInfo.Code, "edit", ct);
        var canCreate = await permissions.HasPermissionAsync(FiscalModuleInfo.Code, "create", ct);
        var actions = new OutgoingInvoiceActions(
            canEdit && editable, canCreate && editable, canCreate && editable, canEdit && invoice.Status == OutgoingInvoiceStatus.Rejected, canEdit && editable && invoice.Number is null,
            canCreate && invoice.Status == OutgoingInvoiceStatus.Authorized && invoice.XmlAttachmentId is not null && invoice.DanfeAttachmentId is not null,
            invoice.XmlAttachmentId is not null || invoice.DanfeAttachmentId is not null);
        var createdBy = invoice.CreatedBy is { } u ? await users.DisplayNameAsync(u, ct) : null;
        return new OutgoingInvoiceView(
            invoice.Id, invoice.Type, invoice.Status, invoice.Environment, invoice.Operation, invoice.NatureOfOperation, invoice.Series, invoice.Number, invoice.AccessKey, invoice.Protocol,
            invoice.AuthorizedAt, invoice.IssuedOn, invoice.Attempt, invoice.ProjectId, project?.Number, project?.Name, invoice.ShipmentId, invoice.ShipmentNumber, invoice.PartyId,
            new OutgoingInvoiceRecipientView(invoice.RecipientName, invoice.RecipientDocument, invoice.RecipientType, invoice.FinalConsumer, invoice.RecipientStateRegistration,
                invoice.RecipientEmail, invoice.RecipientStreet, invoice.RecipientNumber, invoice.RecipientComplement, invoice.RecipientDistrict, invoice.RecipientCityName,
                invoice.RecipientCityCode, invoice.RecipientState, invoice.RecipientPostalCode),
            invoice.FreightMode, invoice.CarrierPartyId, carrier?.LegalName, invoice.Volumes, invoice.GrossWeight, invoice.VolumeSpecies, invoice.Freight, invoice.OtherExpenses,
            invoice.AdditionalInformation, itemViews, invoice.Totals, invoice.TotalAmount, invoice.CostTaxAmount, invoice.CalculationError, invoice.RejectionCode, invoice.RejectionReason,
            invoice.SuggestedAction, invoice.Status == OutgoingInvoiceStatus.Denied ? OutgoingInvoice.DeniedGuidance : null, invoice.ProcessingSince, invoice.ContingencySince,
            invoice.XmlAttachmentId, invoice.DanfeAttachmentId, invoice.SendToCustomer, invoice.EmailCc, invoice.EmailedAt, installments, actions, invoice.CreatedAt, createdBy,
            invoice.DiscardedAt, invoice.Version);
    }

    private static bool NeedsIpiRate(OutgoingInvoiceItem item, TaxRuleCandidates? candidates, TaxRecipient recipient)
    {
        if (candidates is null)
        {
            return item.IpiRate is not null;
        }

        var rule = OutgoingInvoiceCalculator.RuleFor(item, recipient, candidates);
        return rule?.Ipi is { FromNcm: true } ipi && ipi.Cst is "50" or "99";
    }

    // ---------- CA 17: Emitidas ----------

    public async Task<OutgoingInvoicePage> ListAsync(OutgoingInvoiceQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        // FIS-06: a NFS-e da instalação aparece junto, com o filtro "NFS-e".
        var invoices = db.OutgoingInvoices.AsNoTracking().Where(i => (i.ShipmentId != null || i.InstallationOrderId != null) && i.DiscardedAt == null);
        if (query.Type is { } type)
        {
            invoices = invoices.Where(i => i.Type == type);
        }

        if (query.From is { } from)
        {
            var since = BusinessTime.StartOfDayUtc(from);
            invoices = invoices.Where(i => (i.IssuedOn != null && i.IssuedOn >= from) || (i.IssuedOn == null && i.CreatedAt >= since));
        }

        if (query.To is { } to)
        {
            var until = BusinessTime.StartOfDayUtc(to.AddDays(1));
            invoices = invoices.Where(i => (i.IssuedOn != null && i.IssuedOn <= to) || (i.IssuedOn == null && i.CreatedAt < until));
        }

        if (query.Status is { } status)
        {
            invoices = invoices.Where(i => i.Status == status);
        }

        if (query.PartyId is { } party)
        {
            invoices = invoices.Where(i => i.PartyId == party);
        }

        if (query.ProjectId is { } project)
        {
            invoices = invoices.Where(i => i.ProjectId == project);
        }

        var totals = await invoices.GroupBy(_ => 1).Select(g => new { Count = g.Count(), Amount = g.Sum(i => i.TotalAmount), Taxes = g.Sum(i => i.TaxAmount) }).FirstOrDefaultAsync(cancellationToken);
        var rows = await invoices.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).Skip(page.Skip).Take(page.PageSize)
            .Select(i => new { i.Id, i.Series, i.Number, i.IssuedOn, i.CreatedAt, i.PartyId, i.ProjectId, i.ShipmentNumber, i.TotalAmount, i.TaxAmount, i.Environment, i.Status, i.Type, i.InstallationOrderNumber })
            .ToListAsync(cancellationToken);
        var partyNames = (await parties.FindManyAsync(rows.Where(r => r.PartyId != null).Select(r => r.PartyId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var projectNumbers = (await projects.FindManyAsync(rows.Where(r => r.ProjectId != null).Select(r => r.ProjectId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.Id, p => p.Number);
        var items = rows.Select(r => new OutgoingInvoiceRow(r.Id, r.Series, r.Number, r.IssuedOn, r.CreatedAt, r.PartyId, r.PartyId is { } pid ? partyNames.GetValueOrDefault(pid, string.Empty) : string.Empty,
            r.ProjectId, r.ProjectId is { } prid ? projectNumbers.GetValueOrDefault(prid) : null, r.ShipmentNumber, r.TotalAmount, r.TaxAmount, r.Environment, r.Status, r.Type,
            r.InstallationOrderNumber)).ToList();
        return new OutgoingInvoicePage(items, totals?.Count ?? 0, page.Page, page.PageSize, totals?.Amount ?? 0m, totals?.Taxes ?? 0m);
    }

    // ---------- CA 16: A emitir ----------

    /// <summary>Romaneios <c>Shipped</c> sem nota ativa, com o valor estimado pelo contrato e o atraso acima de 1 dia útil.</summary>
    public async Task<IReadOnlyList<InvoiceQueueRow>> ToIssueAsync(CancellationToken cancellationToken = default)
    {
        var withInvoice = await db.OutgoingInvoices.AsNoTracking()
            .Where(i => i.ShipmentId != null && i.DiscardedAt == null && !OutgoingInvoice.InactiveStatuses.Contains(i.Status)).Select(i => i.ShipmentId!.Value).ToListAsync(cancellationToken);
        var shipped = await shipments.ShippedAsync(withInvoice, QueueLimit, cancellationToken);
        if (shipped.Count == 0)
        {
            return [];
        }

        var today = BusinessTime.Today(clock);
        var projectList = (await projects.FindManyAsync(shipped.Select(s => s.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var partyNames = (await parties.FindManyAsync(shipped.Select(s => s.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var prices = new Dictionary<Guid, IReadOnlyList<ProjectItemInfo>>();
        foreach (var projectId in projectList.Keys)
        {
            prices[projectId] = await projects.ItemsAsync(projectId, cancellationToken);
        }

        var shippedOn = shipped.ToDictionary(s => s.Id, s => DateOnly.FromDateTime(BusinessTime.ToBusiness(s.ShippedAt ?? s.CreatedAt).DateTime));
        var first = shippedOn.Values.Min();
        var working = first < today ? await calendar.WorkingDatesAsync(first.AddDays(1), today, cancellationToken) : [];
        return shipped.Select(s =>
        {
            var on = shippedOn[s.Id];
            var project = projectList.GetValueOrDefault(s.ProjectId);
            var items = prices.GetValueOrDefault(s.ProjectId) ?? [];
            var estimate = s.Lines.Sum(l => TaxCalculator.Round(l.Quantity * (items.FirstOrDefault(i => i.ItemId == l.ItemId && i.Kind != ProjectItemKind.Service)?.UnitPrice ?? 0m)));
            var late = working.Count(d => d > on) > 1;
            return new InvoiceQueueRow(s.Id, s.Number, s.ProjectId, project?.Number ?? string.Empty, project?.Name ?? string.Empty, s.PartyId, partyNames.GetValueOrDefault(s.PartyId, string.Empty),
                on, today.DayNumber - on.DayNumber, late, estimate);
        }).ToList();
    }

    // ---------- CA 2: Faturar ----------

    public async Task<Result<ProjectBillingView>> BillingAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projects.FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return Error.NotFound(FiscalErrorCodes.NotFound, "Projeto não encontrado.");
        }

        var list = (await shipments.ByProjectAsync(projectId, cancellationToken)).Where(s => s.Status is not (ShipmentState.Cancelled or ShipmentState.Reversed)).ToList();
        var ids = list.Select(s => s.Id).ToList();
        var active = (await db.OutgoingInvoices.AsNoTracking()
                .Where(i => i.ShipmentId != null && ids.Contains(i.ShipmentId.Value) && i.DiscardedAt == null && !OutgoingInvoice.InactiveStatuses.Contains(i.Status)).ToListAsync(cancellationToken))
            .ToDictionary(i => i.ShipmentId!.Value);
        var views = list.Select(s =>
        {
            var invoice = active.GetValueOrDefault(s.Id);
            return new BillingShipmentView(s.Id, s.Number, s.Status.ToString(), s.ShippedAt, invoice?.Id,
                invoice is null ? null : invoice.Number is null ? "Rascunho de nota" : "NF-e " + OutgoingInvoiceEffects.Label(invoice), invoice?.Status.ToString(),
                s.Status == ShipmentState.Shipped && invoice is null);
        }).ToList();
        var projectItems = await projects.ItemsAsync(projectId, cancellationToken);
        var shippedLines = list.Where(s => s.Status == ShipmentState.Shipped).SelectMany(s => s.Lines).ToList();
        var pending = projectItems.Where(i => i.Kind != ProjectItemKind.Service)
            .Select(i => new BillingItemView(i.Id, i.Description, i.Kind.ToString(), i.Quantity, i.ItemId is { } item ? shippedLines.Where(l => l.ItemId == item).Sum(l => l.Quantity) : 0m))
            .Where(i => i.Shipped < i.Quantity).ToList();
        return new ProjectBillingView(project.Id, project.Number, project.Name, views, pending, projectItems.Count(i => i.Kind == ProjectItemKind.Service));
    }
}
