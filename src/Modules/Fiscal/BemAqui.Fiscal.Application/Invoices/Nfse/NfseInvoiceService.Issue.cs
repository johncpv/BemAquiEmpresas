using BemAqui.BuildingBlocks.Email;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices.Nfse;

public sealed partial class NfseInvoiceService
{
    public const string SentMessage = "NFS-e enviada. Aguardando a prefeitura…";
    public const string AuthorizedMessage = "NFS-e autorizada pela prefeitura.";
    public const string RejectedMessage = "A prefeitura recusou a NFS-e. Veja o motivo e corrija.";

    // ---------- salvar o rascunho ----------

    /// <summary>"Salvar rascunho": a discriminação (saneada, até 2000), o envio ao cliente e a cópia; o cálculo refaz pela regra (nada de imposto da tela).</summary>
    public async Task<Result<NfseView>> UpdateAsync(Guid id, UpdateNfseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loaded = await LoadDraftAsync(id, request.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (invoice, items) = loaded.Value;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var description = NfeTextRules.Clean(request.Description);
        if (description is null || description.Length > NfseRules.DescriptionMaxLength)
        {
            errors["description"] = [$"Informe a discriminação do serviço, com até {NfseRules.DescriptionMaxLength} caracteres."];
        }

        var cc = (request.EmailCc ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (cc.Length > 3 || cc.Any(c => !EmailAddresses.IsPlain(c)))
        {
            errors["emailCc"] = ["Informe até 3 e-mails válidos para a cópia, separados por vírgula."];
        }

        if (errors.Count > 0)
        {
            Clear();
            return Error.Validation(FiscalErrorCodes.Validation, errors.First().Value[0], errors);
        }

        invoice.SendToCustomer = request.SendToCustomer;
        invoice.EmailCc = cc.Length == 0 ? null : string.Join(", ", cc);
        await RecalculateAsync(invoice, items, null, description!, cancellationToken);
        invoice.UpdatedAt = clock.GetUtcNow();
        invoice.UpdatedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>"Descartar rascunho": exclusão lógica, sem número; a instalação volta para "A emitir". Nunca apaga.</summary>
    public async Task<Result<NfseView>> DiscardAsync(Guid id, OutgoingInvoiceVersionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loaded = await LoadDraftAsync(id, request.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        if (loaded.Value.Invoice.Discard(currentUser.UserId, clock.GetUtcNow()) is { } refused)
        {
            Clear();
            return refused;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(loaded.Value.Invoice, cancellationToken);
    }

    /// <summary>"Corrigir e emitir de novo": a rejeitada volta a rascunho com o cálculo refeito; a próxima emissão usa a tentativa seguinte (outra chave).</summary>
    public async Task<Result<NfseView>> ReopenAsync(Guid id, OutgoingInvoiceVersionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.Id == id && i.Type == InvoiceType.NFSe && i.InstallationOrderId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Version != request.Version)
        {
            Clear();
            return Error.Conflict("concurrency_conflict", "A NFS-e mudou desde que você abriu. Recarregue.");
        }

        db.Entry(invoice).Property(i => i.Version).OriginalValue = request.Version;
        if (await ActiveNfse().FirstOrDefaultAsync(i => i.InstallationOrderId == invoice.InstallationOrderId && i.Id != invoice.Id, cancellationToken) is { } other)
        {
            Clear();
            return Exists(other);
        }

        if (invoice.Reopen(currentUser.UserId, clock.GetUtcNow()) is { } refused)
        {
            Clear();
            return refused;
        }

        var items = await db.OutgoingInvoiceItems.Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        await RecalculateAsync(invoice, items, null, invoice.Service?.Description ?? string.Empty, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(invoice, cancellationToken);
    }

    // ---------- CA 3: conferir e emitir pelo Emissor ----------

    /// <summary>O que o diálogo mostra (CA 3 e 6): o modo, cada pré-condição com o link e o impacto, com o cálculo de agora (nada é gravado).</summary>
    public async Task<Result<NfseIssueCheckView>> IssueCheckAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.Type == InvoiceType.NFSe && i.InstallationOrderId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        var items = await db.OutgoingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        await RecalculateAsync(invoice, items, null, invoice.Service?.Description ?? string.Empty, cancellationToken);
        var (mode, _) = await ModeAsync(cancellationToken);
        var (preconditions, warnings, environment) = await PreconditionsAsync(invoice, items, mode, cancellationToken);
        return new NfseIssueCheckView(invoice.Id, mode, environment, preconditions, warnings, invoice.RecipientName, invoice.Service, invoice.TotalAmount, invoice.Service?.IssAmount ?? 0m,
            invoice.CostTaxAmount, invoice.ProjectId is { } p ? (await projects.FindAsync(p, cancellationToken))?.Number : null, environment == FiscalEnvironment.Production, invoice.Version);
    }

    private async Task<(IReadOnlyList<IssuePrecondition> Preconditions, IReadOnlyList<string> Warnings, FiscalEnvironment Environment)> PreconditionsAsync(
        OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, NfseMode mode, CancellationToken ct)
    {
        var list = new List<IssuePrecondition>();
        var warnings = new List<string>();
        var current = await settings.CurrentAsync(ct);
        list.Add(invoice.IsEditableDraft
            ? new IssuePrecondition("draft", true, "A NFS-e está em rascunho.", null)
            : new IssuePrecondition("draft", false, "Só o rascunho é emitido.", null));
        if (mode == NfseMode.Emitter)
        {
            list.Add(new IssuePrecondition("integration", true, "Emissor Fiscal conectado, com a NFS-e do município habilitada.", FiscalLinks.Integration));
        }

        // ADR 0042: em produção, os campos da NFS-e conferidos (CodSrv, CTN, NBS, ISS) e a regra de prestação de serviço conferida.
        var readiness = await settings.ReadinessAsync(FiscalDocumentType.Nfse, ct);
        if (readiness.IsSuccess)
        {
            list.Add(new IssuePrecondition("settings", true, "Configuração fiscal da NFS-e pronta.", FiscalSettingsService.ScreenLink));
            warnings.AddRange(readiness.Value.Warnings);
        }
        else
        {
            list.Add(new IssuePrecondition("settings", false, readiness.Error!.Message, readiness.Error.Data?.GetValueOrDefault("link") as string ?? FiscalSettingsService.ScreenLink));
        }

        var s = invoice.Service;
        list.Add(new IssuePrecondition("serviceCodes", s is { MunicipalCode.Length: > 0, Ctn.Length: > 0, Nbs.Length: > 0 },
            "Informe o código do serviço na prefeitura (CodSrv), o CTN e o NBS em Configurações → Fiscal → NFS-e.", ServiceCodesLink));
        list.Add(new IssuePrecondition("serviceCity", s?.ServiceCityCode is { Length: 7 },
            "Informe o endereço da obra com o código IBGE no projeto: o ISS da instalação é devido no município da obra.", invoice.ProjectId is { } p ? ProjectLink + p : null));
        list.AddRange(OutgoingInvoiceRules.InvoicePreconditions(invoice, items, invoice.PartyId is { } party ? OutgoingInvoiceService.PartyLink + party : OutgoingInvoiceService.PartyLink)
            .Where(c => c.Key != "difal"));
        if (s?.FederalWithholding == true)
        {
            warnings.Add("Retenções federais marcadas em Configurações → Fiscal: o BemAqui ainda não calcula PIS, COFINS, CSLL, IR e INSS retidos. Confira com a contadora.");
        }

        return (list, warnings, current.Environment);
    }

    /// <summary>
    /// "Emitir NFS-e" (CA 3): só com a NFS-e do município habilitada no Emissor; cálculo de agora igual ao confirmado, pré-condições, a nota a
    /// <c>Processing</c> com o <c>payload_hash</c> sobre a versão aberta (<c>xmin</c>) e o envio com a chave <c>nfse-{id}-{tentativa}</c>.
    /// </summary>
    public async Task<Result<NfseIssueResultView>> IssueAsync(Guid id, OutgoingInvoiceVersionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (mode, _) = await ModeAsync(cancellationToken);
        if (mode != NfseMode.Emitter)
        {
            return new Error(FiscalErrorCodes.NfseEmitterUnavailable, NfseRules.PortalNotice, ErrorType.Conflict)
            {
                Data = new Dictionary<string, object?> { ["link"] = NfseRules.PortalUrl },
            };
        }

        var loaded = await LoadDraftAsync(id, request.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (invoice, items) = loaded.Value;
        var confirmed = invoice.TotalAmount;
        var confirmedIss = invoice.Service?.IssAmount;
        await RecalculateAsync(invoice, items, null, invoice.Service?.Description ?? string.Empty, cancellationToken);
        if (invoice.TotalAmount != confirmed || invoice.Service?.IssAmount != confirmedIss)
        {
            Clear();
            return Error.Conflict(FiscalErrorCodes.TotalsChanged, "O ISS ou o valor mudaram desde que você abriu a NFS-e (regra fiscal ou configuração). Confira e emita de novo.");
        }

        var (preconditions, _, environment) = await PreconditionsAsync(invoice, items, mode, cancellationToken);
        if (preconditions.Where(p => !p.Ok).ToList() is { Count: > 0 } missing)
        {
            Clear();
            return new Error(FiscalErrorCodes.IssueBlocked, "Antes de emitir: " + missing[0].Text, ErrorType.Conflict)
            {
                Data = new Dictionary<string, object?> { ["preconditions"] = missing.Select(m => m.Key).ToList(), ["link"] = missing[0].Link },
            };
        }

        invoice.Environment = environment;
        invoice.IssuedBy = currentUser.UserId;
        invoice.UpdatedAt = clock.GetUtcNow();
        invoice.UpdatedBy = currentUser.UserId;
        var payload = await payloads.BuildAsync(invoice, items, cancellationToken);
        Result<NfeSubmission> submitted;
        try
        {
            submitted = await submissions.SubmitAsync(invoice.Id, payload, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            Clear();
            return Error.Conflict("concurrency_conflict", "A NFS-e mudou desde que você abriu (outra emissão já começou). Recarregue.");
        }

        if (submitted.IsFailure)
        {
            Clear();
            return submitted.Error!;
        }

        var outcome = submitted.Value;
        if (outcome.Outcome == SubmissionOutcome.NotCreated)
        {
            return outcome.EmitterError ?? Error.Validation(FiscalErrorCodes.Validation, "O Emissor Fiscal recusou o pedido. Confira a NFS-e e emita de novo.");
        }

        var view = await GetAsync(id, cancellationToken);
        var message = outcome.Status switch
        {
            OutgoingInvoiceStatus.Authorized => AuthorizedMessage,
            OutgoingInvoiceStatus.Rejected or OutgoingInvoiceStatus.Denied => RejectedMessage,
            _ => SentMessage,
        };
        return new NfseIssueResultView(id, outcome.Status, outcome.IdempotencyKey, message, view.Value);
    }

    /// <summary>"Reenviar XML e PDF" ao cliente.</summary>
    public async Task<Result<NfseView>> ResendAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.Id == id && i.Type == InvoiceType.NFSe && i.InstallationOrderId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Status != OutgoingInvoiceStatus.Authorized || (invoice.XmlAttachmentId is null && invoice.DanfeAttachmentId is null))
        {
            return Error.Conflict(FiscalErrorCodes.NotAuthorized, "Só a NFS-e autorizada, com o XML ou o PDF guardado, é reenviada.");
        }

        if (!EmailAddresses.IsPlain(invoice.RecipientEmail))
        {
            return Error.Conflict(FiscalErrorCodes.Validation, "O cliente não tem e-mail de cobrança válido. Informe no cadastro do cliente.");
        }

        await effects.SendAsync(invoice, resend: true, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(invoice, cancellationToken);
    }

    private async Task<Result<(OutgoingInvoice Invoice, List<OutgoingInvoiceItem> Items)>> LoadDraftAsync(Guid id, uint version, CancellationToken ct)
    {
        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.Id == id && i.Type == InvoiceType.NFSe && i.InstallationOrderId != null, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Version != version)
        {
            Clear();
            return Error.Conflict("concurrency_conflict", "A NFS-e mudou desde que você abriu. Recarregue.");
        }

        if (!invoice.IsEditableDraft)
        {
            Clear();
            return Error.Conflict(FiscalErrorCodes.InvoiceClosed, invoice.Status == OutgoingInvoiceStatus.Rejected
                ? "A NFS-e foi rejeitada. Use \"Corrigir e emitir de novo\"."
                : "A NFS-e não está mais em rascunho.");
        }

        db.Entry(invoice).Property(i => i.Version).OriginalValue = version;
        var items = await db.OutgoingInvoiceItems.Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(ct);
        return (invoice, items);
    }
}
