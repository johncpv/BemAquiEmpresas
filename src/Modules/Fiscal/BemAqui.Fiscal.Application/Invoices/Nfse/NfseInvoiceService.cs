using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices.Nfse;

/// <summary>
/// NFS-e da instalação (FIS-06): a fila "A emitir" das instalações aceitas (CA 1), o rascunho a partir da OS aceita com os itens <c>Service</c>
/// ainda não faturados e o ISS, as retenções e o IBS/CBS pela regra de prestação de serviço (CA 2), a emissão pelo Emissor quando a NFS-e do
/// município está habilitada (CA 3 e 4) e, até lá, o processo provisório: os valores para copiar no portal da prefeitura e o registro da nota
/// emitida lá (CA 5 e 6), o cancelamento registrado com 2FA (CA 9). Reaproveita o fluxo da NF-e de saída (FIS-03a): a mesma tabela, o mesmo
/// cálculo (<see cref="OutgoingInvoiceCalculator"/>), o mesmo envio idempotente e os mesmos efeitos (<see cref="OutgoingInvoiceEffects"/>). OS,
/// projeto, itens e cliente só pelos contratos dos donos (ADR 0006). Permissões no endpoint: ver <c>fiscal.view</c>; criar, emitir e registrar
/// <c>fiscal.create</c>; salvar, corrigir e descartar <c>fiscal.edit</c>; registrar cancelamento <c>fiscal.approve</c>.
/// </summary>
public sealed partial class NfseInvoiceService(
    IFiscalDb db, IProjects projects, IParties parties, IInstallationOrders installations, IFiscalSettings settings, ICompanyFiscalProfile company,
    OutgoingInvoiceCalculator calculator, NfsePayloads payloads, NfseSubmissionService submissions, FiscalEmitterAccess access, OutgoingInvoiceEffects effects,
    AttachmentService attachments, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, IStepUpTwoFactor stepUp, IEventPublisher events,
    IBusinessCalendar calendar, ITenantContext tenant, TimeProvider clock)
{
    public const int QueueLimit = 500;
    public const string ServiceCodesLink = FiscalSettingsService.ScreenLink + "#campo-nfseMunicipalCode";
    public const string ProjectLink = "/app/projetos/";

    // ---------- CA 1: fila "A emitir" ----------

    /// <summary>OS de instalação aceitas sem NFS-e ativa cujo projeto ainda tem serviço a faturar, com o valor e o atraso acima de 1 dia útil.</summary>
    public async Task<IReadOnlyList<InstallationQueueRow>> ToIssueAsync(CancellationToken cancellationToken = default)
    {
        var active = ActiveNfse().Select(i => i.InstallationOrderId!.Value);
        var billings = await db.InstallationBillings.AsNoTracking().Where(b => !active.Contains(b.InstallationOrderId)).OrderBy(b => b.AcceptedOn).Take(QueueLimit)
            .ToListAsync(cancellationToken);
        if (billings.Count == 0)
        {
            return [];
        }

        var projectList = (await projects.FindManyAsync(billings.Select(b => b.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var partyNames = (await parties.FindManyAsync(projectList.Values.Select(p => p.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var today = BusinessTime.Today(clock);
        var first = billings.Min(b => b.AcceptedOn);
        var working = first < today ? await calendar.WorkingDatesAsync(first.AddDays(1), today, cancellationToken) : [];
        var rows = new List<InstallationQueueRow>();
        foreach (var group in billings.GroupBy(b => b.ProjectId))
        {
            if (!projectList.TryGetValue(group.Key, out var project))
            {
                continue;
            }

            var pending = await PendingServiceItemsAsync(project.Id, cancellationToken);
            if (pending.Count == 0)
            {
                continue; // o serviço do contrato já saiu em NFS-e (ou o contrato não tem serviço)
            }

            var orders = (await installations.ByProjectAsync(project.Id, cancellationToken)).ToDictionary(o => o.Id);
            foreach (var billing in group)
            {
                if (!orders.TryGetValue(billing.InstallationOrderId, out var order) || order.Status != InstallationState.Completed)
                {
                    continue;
                }

                var on = billing.AcceptedOn;
                rows.Add(new InstallationQueueRow(order.Id, order.Number, project.Id, project.Number, project.Name, project.PartyId, partyNames.GetValueOrDefault(project.PartyId, string.Empty), on,
                    today.DayNumber - on.DayNumber, working.Count(d => d > on) > 1, pending.Sum(i => TaxCalculator.Round(i.Quantity * i.UnitPrice))));
            }
        }

        return rows;
    }

    private IQueryable<OutgoingInvoice> ActiveNfse() =>
        db.OutgoingInvoices.AsNoTracking().Where(i => i.InstallationOrderId != null && i.DiscardedAt == null && !OutgoingInvoice.InactiveStatuses.Contains(i.Status));

    /// <summary>Itens <c>Service</c> do contrato ainda fora de uma NFS-e ativa do projeto (CA 2: o valor do serviço não faturado).</summary>
    private async Task<List<ProjectItemInfo>> PendingServiceItemsAsync(Guid projectId, CancellationToken ct)
    {
        var services = (await projects.ItemsAsync(projectId, ct)).Where(i => i.Kind == ProjectItemKind.Service).OrderBy(i => i.Sequence).ToList();
        if (services.Count == 0)
        {
            return services;
        }

        var invoiced = await db.OutgoingInvoiceItems.AsNoTracking()
            .Where(i => i.ProjectItemId != null && ActiveNfse().Where(n => n.ProjectId == projectId).Select(n => n.Id).Contains(i.InvoiceId))
            .Select(i => i.ProjectItemId!.Value).ToListAsync(ct);
        return services.Where(s => !invoiced.Contains(s.Id)).ToList();
    }

    // ---------- CA 2: rascunho ----------

    public async Task<Result<NfseView>> CreateAsync(CreateNfseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var billing = await db.InstallationBillings.AsNoTracking().FirstOrDefaultAsync(b => b.InstallationOrderId == request.InstallationOrderId, cancellationToken)
            ?? await BillingFromOrderAsync(request, cancellationToken);
        var order = billing is null ? null : (await installations.ByProjectAsync(billing.ProjectId, cancellationToken)).FirstOrDefault(o => o.Id == request.InstallationOrderId);
        if (billing is null || order is null)
        {
            return Error.NotFound(FiscalErrorCodes.InstallationNotAccepted, "OS de instalação com aceite técnico não encontrada.");
        }

        if (order.Status != InstallationState.Completed)
        {
            return Error.Conflict(FiscalErrorCodes.InstallationNotAccepted, "Só a OS de instalação com o aceite técnico vira NFS-e.");
        }

        if (await ActiveNfse().FirstOrDefaultAsync(i => i.InstallationOrderId == order.Id, cancellationToken) is { } existing)
        {
            return Exists(existing);
        }

        var project = await projects.FindAsync(billing.ProjectId, cancellationToken);
        if (project is null)
        {
            return Error.NotFound(FiscalErrorCodes.NotFound, "Projeto da instalação não encontrado.");
        }

        var pending = await PendingServiceItemsAsync(project.Id, cancellationToken);
        if (pending.Count == 0)
        {
            return Error.Conflict(FiscalErrorCodes.NothingToInvoice, "O contrato não tem serviço a faturar: a instalação já saiu em NFS-e ou não foi vendida.");
        }

        var current = await settings.CurrentAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var invoice = OutgoingInvoice.NewNfseDraft(order.Id, order.Number, project.Id, project.ContractId, project.PartyId, currentUser.UserId, now);
        await OutgoingInvoiceService.FillRecipientAsync(parties, invoice, project.PartyId, cancellationToken);
        invoice.Environment = current.Environment;
        invoice.LinkedInvoiceId = await db.OutgoingInvoices.AsNoTracking()
            .Where(i => i.ProjectId == project.Id && i.Type == InvoiceType.NFe && i.Status == OutgoingInvoiceStatus.Authorized)
            .OrderByDescending(i => i.AuthorizedAt).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(cancellationToken);
        var sequence = 0;
        var items = pending.Select(p => new OutgoingInvoiceItem
        {
            InvoiceId = invoice.Id, Sequence = ++sequence, ProjectItemId = p.Id, ItemId = p.ItemId ?? Guid.Empty, Code = "SERVICO", Description = NfeTextRules.Clean(p.Description) ?? "Instalação",
            Unit = "UN", QuoteItemKind = nameof(ProjectItemKind.Service), Operation = TaxOperation.ServiceProvision, ShippedQuantity = p.Quantity, Quantity = p.Quantity, UnitPrice = p.UnitPrice,
        }).ToList();
        var equipment = (await projects.ItemsAsync(project.Id, cancellationToken))
            .Where(i => i.Kind == ProjectItemKind.Equipment).OrderByDescending(i => order.ProjectItemIds.Contains(i.Id)).ThenBy(i => i.Sequence).FirstOrDefault();
        var description = NfseRules.Discrimination(equipment?.Description, project.Number, order.Number);
        await RecalculateAsync(invoice, items, project, description, cancellationToken);
        db.OutgoingInvoices.Add(invoice);
        db.OutgoingInvoiceItems.AddRange(items);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_outgoing_invoices_active_installation", StringComparison.Ordinal) == true)
        {
            // CA 12: dois rascunhos da mesma OS ao mesmo tempo; o índice único parcial deixa só um.
            db.ChangeTracker.Clear();
            return await ActiveNfse().FirstOrDefaultAsync(i => i.InstallationOrderId == order.Id, cancellationToken) is { } winner
                ? Exists(winner)
                : Error.Conflict(FiscalErrorCodes.InvoiceExists, "A instalação já tem NFS-e.");
        }

        return await GetAsync(invoice.Id, cancellationToken);
    }

    /// <summary>
    /// "Emitir NFS-e" logo depois do aceite, antes de a inbox entregar o <c>InstallationAccepted</c>: com o projeto informado pela tela, a OS é
    /// conferida pelo contrato da Produção (concluída) e a linha da fila nasce aqui; o consumidor, depois, vê que já existe.
    /// </summary>
    private async Task<InstallationBilling?> BillingFromOrderAsync(CreateNfseRequest request, CancellationToken ct)
    {
        if (request.ProjectId is not { } projectId)
        {
            return null;
        }

        var order = (await installations.ByProjectAsync(projectId, ct)).FirstOrDefault(o => o.Id == request.InstallationOrderId);
        if (order is not { Status: InstallationState.Completed })
        {
            return null;
        }

        var billing = new InstallationBilling { InstallationOrderId = order.Id, ProjectId = projectId, AcceptedOn = BusinessTime.Today(clock), CreatedAt = clock.GetUtcNow() };
        db.InstallationBillings.Add(billing);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear(); // a inbox gravou ao mesmo tempo: vale a dela
            return await db.InstallationBillings.AsNoTracking().FirstOrDefaultAsync(b => b.InstallationOrderId == order.Id, ct);
        }

        return billing;
    }

    private static Error Exists(OutgoingInvoice existing) =>
        new(FiscalErrorCodes.InvoiceExists, $"A instalação já tem a NFS-e {(existing.Number is null ? "em rascunho" : OutgoingInvoiceEffects.Label(existing))}.", ErrorType.Conflict)
        {
            Data = new Dictionary<string, object?> { ["invoiceId"] = existing.Id, ["link"] = OutgoingInvoiceEffects.NfseLink(existing.Id) },
        };

    /// <summary>
    /// O cálculo pela regra de prestação de serviço (CA 2) e o serviço da nota: item da LC 116, alíquota e retenção do ISS da regra; <c>CodSrv</c>,
    /// CTN, NBS e retenções federais de Configurações → Fiscal; o município da prestação (o da obra no ISS da obra, o da empresa no outro); o
    /// IBS/CBS da regra. Nada é digitado.
    /// </summary>
    private async Task RecalculateAsync(OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, ProjectSummary? project, string description, CancellationToken ct)
    {
        var candidates = await calculator.CandidatesAsync(ct);
        OutgoingInvoiceCalculator.Recalculate(invoice, items, candidates);
        var current = await settings.CurrentAsync(ct);
        var rule = items.Select(i => candidates.Rules.FirstOrDefault(r => r.Id == i.TaxRuleVersionId)).FirstOrDefault(r => r is not null);
        var location = rule?.Iss?.Location ?? current.NfseIssLocation;
        var (cityCode, cityName) = await ServiceCityAsync(invoice, project, location, ct);
        var iss = items.SelectMany(i => i.Taxes).Where(t => t.Code == TaxCodes.Iss).Sum(t => t.Amount);
        var withheld = rule?.Iss?.Withheld ?? current.NfseIssWithheld;
        invoice.Service = new NfseServiceInfo(rule?.Iss?.ServiceItem ?? current.NfseServiceItem, current.NfseMunicipalCode, current.NfseCtn, current.NfseNbs, cityCode, cityName, location,
            rule?.Iss?.Rate ?? current.NfseIssRate, iss, withheld, current.NfseFederalWithholding, description, rule?.IbsCbs?.Cst, rule?.IbsCbs?.Classification,
            NfseRules.NetAmount(invoice.TotalAmount, iss, withheld));
    }

    /// <summary>Município da prestação: o da obra (endereço do projeto) quando o ISS é da obra (item 7.02); senão, o da empresa.</summary>
    private async Task<(string? Code, string? Name)> ServiceCityAsync(OutgoingInvoice invoice, ProjectSummary? project, IssLocation location, CancellationToken ct)
    {
        if (location == IssLocation.Company)
        {
            var profile = await company.GetFiscalProfileAsync(ct);
            return (profile.CityIbge, profile.City);
        }

        project ??= invoice.ProjectId is { } p ? await projects.FindAsync(p, ct) : null;
        if (project?.SiteAddressId is not { } site || invoice.PartyId is not { } party)
        {
            return (null, null);
        }

        var address = (await parties.AddressesAsync(party, ct)).FirstOrDefault(a => a.Id == site);
        return (address?.IbgeCode, NfeTextRules.Clean(address?.City));
    }

    // ---------- Consulta ----------

    public async Task<Result<NfseView>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.Type == InvoiceType.NFSe && i.InstallationOrderId != null, cancellationToken);
        return invoice is null ? NotFound() : await ViewAsync(invoice, cancellationToken);
    }

    private static Error NotFound() => Error.NotFound(FiscalErrorCodes.InvoiceNotFound, "NFS-e não encontrada.");

    private async Task<NfseView> ViewAsync(OutgoingInvoice invoice, CancellationToken ct)
    {
        var items = await db.OutgoingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == invoice.Id).OrderBy(i => i.Sequence).ToListAsync(ct);
        var project = invoice.ProjectId is { } p ? await projects.FindAsync(p, ct) : null;
        var draft = invoice.IsEditableDraft;
        var mode = draft ? (await ModeAsync(ct)).Mode : invoice.Origin == InvoiceOrigin.PortalRegistered ? NfseMode.Portal : NfseMode.Emitter;
        var canEdit = await permissions.HasPermissionAsync(FiscalModuleInfo.Code, "edit", ct);
        var canCreate = await permissions.HasPermissionAsync(FiscalModuleInfo.Code, "create", ct);
        var canApprove = await permissions.HasPermissionAsync(FiscalModuleInfo.Code, "approve", ct);
        var hasFile = invoice.XmlAttachmentId is not null || invoice.DanfeAttachmentId is not null;
        var actions = new NfseActions(canEdit && draft, canCreate && draft && mode == NfseMode.Emitter, canCreate && draft && mode == NfseMode.Portal,
            canEdit && invoice.Status == OutgoingInvoiceStatus.Rejected, canEdit && draft && invoice.Number is null, canApprove && invoice.Status == OutgoingInvoiceStatus.Authorized, hasFile,
            canCreate && invoice.Status == OutgoingInvoiceStatus.Authorized && hasFile);
        var calculated = CalculatedIss(items, invoice);
        var linked = invoice.LinkedInvoiceId is { } l ? await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == l, ct) : null;
        var cancellation = invoice.PortalCancellation is { } c
            ? new NfseCancellationView(c.Reason, c.Protocol, c.By is { } by ? await users.DisplayNameAsync(by, ct) : null, c.At, c.On)
            : null;
        return new NfseView(
            invoice.Id, invoice.Status, invoice.Origin, invoice.Environment, mode, draft && mode == NfseMode.Portal ? NfseRules.PortalNotice : null, NfseRules.PortalUrl, invoice.Number,
            invoice.VerificationCode, invoice.AuthorizedAt, invoice.IssuedOn, invoice.Attempt, invoice.ProjectId, project?.Number, project?.Name, invoice.InstallationOrderId,
            invoice.InstallationOrderNumber, invoice.PartyId,
            new OutgoingInvoiceRecipientView(invoice.RecipientName, invoice.RecipientDocument, invoice.RecipientType, invoice.FinalConsumer, invoice.RecipientStateRegistration,
                invoice.RecipientEmail, invoice.RecipientStreet, invoice.RecipientNumber, invoice.RecipientComplement, invoice.RecipientDistrict, invoice.RecipientCityName,
                invoice.RecipientCityCode, invoice.RecipientState, invoice.RecipientPostalCode),
            invoice.Service,
            items.Select(i => new NfseItemView(i.Id, i.Sequence, i.ProjectItemId, i.Description, i.Quantity, i.UnitPrice, i.Discount, i.GrossAmount, i.TaxRuleName, i.TaxRuleVersion, i.Taxes,
                i.CostTaxAmount)).ToList(),
            invoice.Totals, invoice.TotalAmount, invoice.CostTaxAmount, calculated, invoice.IssTyped,
            invoice.IssTyped is { } typed ? NfseRules.IssDifference(typed, calculated) : null, invoice.CalculationError, invoice.RejectionCode, invoice.RejectionReason, invoice.SuggestedAction,
            invoice.ProcessingSince, invoice.XmlAttachmentId, invoice.DanfeAttachmentId, invoice.SendToCustomer, invoice.EmailCc, invoice.EmailedAt, invoice.LinkedInvoiceId,
            linked is null ? null : linked.Number is null ? "NF-e em rascunho" : "NF-e " + OutgoingInvoiceEffects.Label(linked), cancellation,
            draft && mode == NfseMode.Portal ? PortalCopy(invoice) : [], actions, invoice.CreatedAt,
            invoice.CreatedBy is { } u ? await users.DisplayNameAsync(u, ct) : null, invoice.IssuedBy is { } ib ? await users.DisplayNameAsync(ib, ct) : null, invoice.DiscardedAt,
            invoice.Version);
    }

    /// <summary>ISS calculado pela regra (o da nota da prefeitura pode ser outro: fica no <c>IssTyped</c>).</summary>
    private static decimal CalculatedIss(IReadOnlyList<OutgoingInvoiceItem> items, OutgoingInvoice invoice) =>
        invoice.Origin == InvoiceOrigin.PortalRegistered && invoice.IssTyped is not null
            ? items.SelectMany(i => i.Taxes).Where(t => t.Code == TaxCodes.Iss).Sum(t => TaxCalculator.Round(t.Base * t.Rate / 100m))
            : items.SelectMany(i => i.Taxes).Where(t => t.Code == TaxCodes.Iss).Sum(t => t.Amount);

    /// <summary>Os valores do rascunho para copiar no portal da prefeitura (CA 5).</summary>
    public static IReadOnlyList<NfseCopyField> PortalCopy(OutgoingInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var s = invoice.Service;
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var list = new List<NfseCopyField>
        {
            new("taker", "Tomador", invoice.RecipientName ?? string.Empty),
            new("takerDocument", "CPF ou CNPJ do tomador", invoice.RecipientDocument is { Length: > 0 } d ? BrazilianDocuments.Format(d) : string.Empty),
            new("description", "Discriminação", s?.Description ?? string.Empty),
            new("municipalCode", "Código do serviço (CodSrv)", s?.MunicipalCode ?? "A informar em Configurações → Fiscal"),
            new("serviceItem", "Item da LC 116", s?.ServiceItem ?? string.Empty),
            new("serviceCity", "Município da prestação", s?.ServiceCityName is { } city ? $"{city} ({s.ServiceCityCode})" : "Informe o endereço da obra no projeto"),
            new("amount", "Valor do serviço", invoice.TotalAmount.ToString("N2", pt)),
            new("issRate", "Alíquota do ISS (%)", (s?.IssRate ?? 0m).ToString("0.##", pt)),
            new("issAmount", "ISS", (s?.IssAmount ?? 0m).ToString("N2", pt)),
            new("issWithheld", "ISS retido pelo tomador", s?.IssWithheld == true ? "Sim" : "Não"),
        };
        return list;
    }

    /// <summary>
    /// A NFS-e sai pelo Emissor? (CA 3 e 5, lacuna L-m): só com a integração ativa, a chave com <c>nfse:emitir</c> e o município da empresa entre os
    /// da NFS-e habilitada no <c>GET /me</c>; senão, o processo provisório pelo portal da prefeitura.
    /// </summary>
    private async Task<(NfseMode Mode, IFiscalEmitter? Emitter)> ModeAsync(CancellationToken ct)
    {
        var emitter = await access.CurrentAsync(requireActive: true, ct);
        if (emitter.IsFailure)
        {
            return (NfseMode.Portal, null);
        }

        var account = await emitter.Value.GetAccountAsync(ct);
        if (account.IsFailure)
        {
            return (NfseMode.Portal, null);
        }

        var profile = await company.GetFiscalProfileAsync(ct);
        return NfseRules.EmitterEnabled(account.Value.Scopes, account.Value.NfseCities, profile.CityIbge) ? (NfseMode.Emitter, emitter.Value) : (NfseMode.Portal, null);
    }

    private Guid TenantId() => tenant.Tenant?.Id ?? Guid.Empty;

    /// <summary>Nada do que foi recusado fica rastreado no escopo.</summary>
    private void Clear() => db.ChangeTracker.Clear();
}
