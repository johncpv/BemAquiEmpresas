using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>Arquivo da nota para download (XML ou DANFE), lido do storage do tenant.</summary>
public sealed record InvoiceFileContent(Stream Content, string ContentType, string FileName);

public sealed partial class OutgoingInvoiceService
{
    public const string SentMessage = "Nota enviada à SEFAZ. Aguardando a autorização…";
    public const string AuthorizedMessage = "Nota autorizada pela SEFAZ.";
    public const decimal MaxIpiRate = 100m;

    // ---------- CA 3 e 4: salvar o rascunho ----------

    public async Task<Result<OutgoingInvoiceView>> UpdateAsync(Guid id, UpdateOutgoingInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loaded = await LoadDraftAsync(id, request.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (invoice, items) = loaded.Value;
        var inputs = request.Items ?? [];
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (inputs.Count != items.Count || inputs.Select(i => i.Id).Distinct().Count() != inputs.Count || inputs.Any(i => items.All(x => x.Id != i.Id)))
        {
            Clear();
            return Error.Validation(Error.Codes.Validation, "Os itens não são os desta nota. Recarregue a nota.", new Dictionary<string, string[]> { ["items"] = ["Recarregue a nota."] });
        }

        if (request.Freight < 0 || request.OtherExpenses < 0)
        {
            errors["freight"] = ["Frete e outras despesas sem sinal negativo."];
        }

        var candidates = await calculator.CandidatesAsync(cancellationToken);
        var recipient = new TaxRecipient((invoice.RecipientState ?? string.Empty).ToUpperInvariant(), invoice.RecipientType, invoice.RecipientType != RecipientType.Contributor);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var input = inputs.Single(i => i.Id == item.Id);
            var n = item.Sequence.ToString(CultureInfo.InvariantCulture);
            var prefix = $"items[{index.ToString(CultureInfo.InvariantCulture)}].";
            if (input.Quantity <= 0 || input.Quantity > item.ShippedQuantity)
            {
                errors[prefix + "quantity"] = [$"A quantidade do item {n} vai de mais de zero até o expedido ({item.ShippedQuantity.ToString("0.####", CultureInfo.GetCultureInfo("pt-BR"))})."];
            }

            if (input.UnitPrice < 0 || input.Discount < 0 || input.Discount > Math.Round(input.Quantity * input.UnitPrice, 2, MidpointRounding.AwayFromZero))
            {
                errors[prefix + "discount"] = [$"Valor e desconto do item {n} sem sinal negativo, e o desconto não passa do valor do item."];
            }

            if (input.IpiRate is { } rate && (rate < 0 || rate > MaxIpiRate))
            {
                errors[prefix + "ipiRate"] = ["A alíquota do IPI vai de 0 a 100%."];
            }

            item.Quantity = Math.Round(input.Quantity, 4, MidpointRounding.AwayFromZero);
            item.UnitPrice = Math.Round(input.UnitPrice, 6, MidpointRounding.AwayFromZero);
            item.Discount = Math.Round(input.Discount, 2, MidpointRounding.AwayFromZero);
            item.Description = NfeTextRules.Clean(input.Description) ?? string.Empty;
            item.AdditionalInfo = NfeTextRules.Clean(input.AdditionalInfo);
            item.IpiRate = input.IpiRate;
            item.ChosenRuleId = input.TaxRuleId;
            if (input.TaxRuleId is { } chosen && OutgoingInvoiceCalculator.Applicable(item, recipient, candidates).All(r => r.RuleId != chosen))
            {
                errors[prefix + "taxRuleId"] = [$"Escolha uma regra ativa que vale para este cliente e para o item {n}."];
            }
        }

        invoice.Freight = Math.Round(request.Freight, 2, MidpointRounding.AwayFromZero);
        invoice.OtherExpenses = Math.Round(request.OtherExpenses, 2, MidpointRounding.AwayFromZero);
        invoice.AdditionalInformation = NfeTextRules.Clean(request.AdditionalInformation);
        invoice.SendToCustomer = request.SendToCustomer;
        var cc = (request.EmailCc ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (cc.Length > 3 || cc.Any(c => !EmailAddresses.IsPlain(c)))
        {
            errors["emailCc"] = ["Informe até 3 e-mails válidos para a cópia, separados por vírgula."];
        }

        invoice.EmailCc = cc.Length == 0 ? null : string.Join(", ", cc);
        foreach (var (field, messages) in OutgoingInvoiceRules.TextErrors(invoice, items, await ComplementaryAsync(invoice, items, cancellationToken)))
        {
            errors.TryAdd(field, messages);
        }

        if (errors.Count > 0)
        {
            Clear();
            return Error.Validation(FiscalErrorCodes.Validation, errors.First().Value[0], errors);
        }

        OutgoingInvoiceCalculator.Recalculate(invoice, items, candidates);
        invoice.UpdatedAt = clock.GetUtcNow();
        invoice.UpdatedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>"Descartar rascunho" (CA 20): exclusão lógica, sem número; nunca apaga.</summary>
    public async Task<Result<OutgoingInvoiceView>> DiscardAsync(Guid id, OutgoingInvoiceVersionRequest request, CancellationToken cancellationToken = default)
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

    /// <summary>"Corrigir e emitir de novo" (CA 8): a rejeitada volta a rascunho com os dados e o cálculo refeito; a próxima emissão usa a tentativa seguinte.</summary>
    public async Task<Result<OutgoingInvoiceView>> ReopenAsync(Guid id, OutgoingInvoiceVersionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Version != request.Version)
        {
            return Error.Conflict("concurrency_conflict", "A nota fiscal mudou desde que você abriu. Recarregue.");
        }

        db.Entry(invoice).Property(i => i.Version).OriginalValue = request.Version;
        if (invoice.ShipmentId is { } shipment && await ActiveForShipmentAsync(shipment, cancellationToken) is { } other && other.Id != invoice.Id)
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
        await calculator.RecalculateAsync(invoice, items, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(invoice, cancellationToken);
    }

    // ---------- CA 5: pré-visualizar ----------

    public async Task<Result<byte[]>> PreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (!invoice.IsEditableDraft)
        {
            return Error.Conflict(FiscalErrorCodes.InvoiceClosed, "A prévia é do rascunho. A nota emitida tem o DANFE em Documentos.");
        }

        var items = await db.OutgoingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        var request = await payloads.BuildAsync(invoice, items, cancellationToken);
        if (OutgoingInvoiceRules.TextErrors(invoice, items, request.AdditionalInformation) is { Count: > 0 } errors)
        {
            return Error.Validation(FiscalErrorCodes.TextTooLong, errors.First().Value[0], errors);
        }

        var emitter = await access.CurrentAsync(requireActive: true, cancellationToken);
        if (emitter.IsFailure)
        {
            return emitter.Error!;
        }

        var preview = await emitter.Value.PreviewAsync(NfePayload.Normalize(request), cancellationToken);
        if (preview.IsFailure)
        {
            return preview.Error!;
        }

        var profile = await company.GetFiscalProfileAsync(cancellationToken);
        return await renderer.RenderAsync(new DanfePreviewDocument(new DanfePreviewModel(profile.LegalName, profile.Document, preview.Value.AccessKey, preview.Value.Number,
            preview.Value.Series, preview.Value.Warnings, invoice, items, request.AdditionalInformation)), cancellationToken);
    }

    // ---------- CA 6: conferir e emitir ----------

    /// <summary>O que o diálogo da emissão mostra (CA 6): cada pré-condição com o link e o impacto, com o cálculo de agora (nada é gravado).</summary>
    public async Task<Result<IssueCheckView>> IssueCheckAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        var items = await db.OutgoingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        await calculator.RecalculateAsync(invoice, items, cancellationToken);
        var (preconditions, warnings, environment) = await PreconditionsAsync(invoice, items, cancellationToken);
        return new IssueCheckView(invoice.Id, environment, preconditions, warnings, invoice.RecipientName, items.Count, invoice.Totals, invoice.TotalAmount, invoice.CostTaxAmount,
            invoice.ProjectId is { } p ? (await projects.FindAsync(p, cancellationToken))?.Number : null, environment == FiscalEnvironment.Production, invoice.Version);
    }

    private async Task<(IReadOnlyList<IssuePrecondition> Preconditions, IReadOnlyList<string> Warnings, FiscalEnvironment Environment)> PreconditionsAsync(
        OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, CancellationToken ct)
    {
        var list = new List<IssuePrecondition>();
        var warnings = new List<string>();
        var current = await settings.CurrentAsync(ct);
        list.Add(invoice.IsEditableDraft
            ? new IssuePrecondition("draft", true, "A nota está em rascunho.", null)
            : new IssuePrecondition("draft", false, "Só o rascunho é emitido.", null));
        var emitter = await access.CurrentAsync(requireActive: true, ct);
        list.Add(new IssuePrecondition("integration", emitter.IsSuccess, emitter.IsSuccess ? "Emissor Fiscal conectado e testado." : emitter.Error!.Message, FiscalLinks.Integration));
        var readiness = await settings.ReadinessAsync(FiscalDocumentType.Nfe, ct);
        if (readiness.IsSuccess)
        {
            list.Add(new IssuePrecondition("settings", true, "Configuração fiscal pronta para emitir.", FiscalSettingsService.ScreenLink));
            warnings.AddRange(readiness.Value.Warnings);
        }
        else
        {
            list.Add(new IssuePrecondition("settings", false, readiness.Error!.Message, readiness.Error.Data?.GetValueOrDefault("link") as string ?? FiscalSettingsService.ScreenLink));
        }

        var certificateOk = current.CertificateNotAfter is { } notAfter && notAfter > clock.GetUtcNow();
        list.Add(new IssuePrecondition("certificate", certificateOk,
            certificateOk ? "Certificado digital válido." : "Envie o certificado digital A1 válido em Configurações → Fiscal → Certificado digital.", FiscalSettingsService.CertificateLink));
        list.AddRange(OutgoingInvoiceRules.InvoicePreconditions(invoice, items, invoice.PartyId is { } party ? PartyLink + party : PartyLink));
        return (list, warnings, current.Environment);
    }

    /// <summary>
    /// "Emitir NF-e" (CA 6, 10 e 19): saneamento (422 sem chamar o Emissor), cálculo de agora igual ao confirmado, pré-condições, a nota a
    /// <c>Processing</c> com o <c>payload_hash</c> sobre a versão aberta (<c>xmin</c>: duas emissões juntas, só uma vai à SEFAZ) e o envio pelo
    /// <see cref="NfeSubmissionService"/> com a chave <c>nfe-{id}-{tentativa}</c>.
    /// </summary>
    public async Task<Result<IssueResultView>> IssueAsync(Guid id, OutgoingInvoiceVersionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loaded = await LoadDraftAsync(id, request.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (invoice, items) = loaded.Value;
        var confirmedTotal = invoice.TotalAmount;
        var payload = await payloads.BuildAsync(invoice, items, cancellationToken);
        if (OutgoingInvoiceRules.TextErrors(invoice, items, payload.AdditionalInformation) is { Count: > 0 } textErrors)
        {
            Clear();
            return Error.Validation(FiscalErrorCodes.TextTooLong, textErrors.First().Value[0], textErrors);
        }

        await calculator.RecalculateAsync(invoice, items, cancellationToken);
        if (invoice.TotalAmount != confirmedTotal)
        {
            Clear();
            return Error.Conflict(FiscalErrorCodes.TotalsChanged, "Os tributos mudaram desde que você abriu a nota (regra fiscal ou configuração). Confira os totais e emita de novo.");
        }

        var (preconditions, _, environment) = await PreconditionsAsync(invoice, items, cancellationToken);
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
        payload = await payloads.BuildAsync(invoice, items, cancellationToken);
        Result<NfeSubmission> submitted;
        try
        {
            // O BeginSubmission grava Processing com o hash sobre a versão aberta (xmin): a segunda emissão simultânea para aqui, com 409.
            submitted = await submissions.SubmitAsync(invoice.Id, payload, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            Clear();
            return Error.Conflict("concurrency_conflict", "A nota fiscal mudou desde que você abriu (outra emissão já começou). Recarregue.");
        }

        if (submitted.IsFailure)
        {
            Clear();
            return submitted.Error!;
        }

        var view = await GetAsync(id, cancellationToken);
        var outcome = submitted.Value;
        if (outcome.Outcome == SubmissionOutcome.NotCreated)
        {
            return outcome.EmitterError ?? Error.Validation(FiscalErrorCodes.Validation, "O Emissor Fiscal recusou o pedido. Confira a nota e emita de novo.");
        }

        var message = outcome.Status switch
        {
            OutgoingInvoiceStatus.Authorized => AuthorizedMessage,
            OutgoingInvoiceStatus.Rejected => "A SEFAZ rejeitou a nota. Veja o motivo e a ação sugerida.",
            OutgoingInvoiceStatus.Denied => OutgoingInvoice.DeniedGuidance,
            OutgoingInvoiceStatus.ContingencyPending => "A SEFAZ está demorando. Avisaremos quando a nota for autorizada.",
            _ => SentMessage,
        };
        return new IssueResultView(id, outcome.Status, outcome.IdempotencyKey, message, view.Value);
    }

    // ---------- CA 14: reenviar XML e DANFE ----------

    public async Task<Result<OutgoingInvoiceView>> ResendAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Status != OutgoingInvoiceStatus.Authorized || invoice.XmlAttachmentId is null || invoice.DanfeAttachmentId is null)
        {
            return Error.Conflict(FiscalErrorCodes.NotAuthorized, "Só a nota autorizada, com o XML e o DANFE guardados, é reenviada.");
        }

        if (!EmailAddresses.IsPlain(invoice.RecipientEmail))
        {
            return Error.Conflict(FiscalErrorCodes.Validation, "O cliente não tem e-mail de cobrança válido. Informe no cadastro do cliente.");
        }

        await effects.SendAsync(invoice, resend: true, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(invoice, cancellationToken);
    }

    // ---------- CA 18: XML e DANFE só por endpoint autorizado ----------

    /// <summary>O XML ou o DANFE da nota (CA 18): o anexo tem de ser desta nota (dono <c>OutgoingInvoice</c>, id da nota) e estar <c>Clean</c>.</summary>
    public async Task<Result<InvoiceFileContent>> FileAsync(Guid id, bool xml, CancellationToken cancellationToken = default)
    {
        // FIS-06: a NFS-e usa as mesmas rotas (XML e o PDF da prefeitura no lugar do DANFE).
        var invoice = await db.OutgoingInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id && (i.ShipmentId != null || i.Type == BemAqui.Fiscal.Contracts.InvoiceType.NFSe), cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        var attachmentId = xml ? invoice.XmlAttachmentId : invoice.DanfeAttachmentId;
        var attachment = attachmentId is { } a ? await attachments.FindAsync(a, cancellationToken) : null;
        if (attachment is null || attachment.OwnerType != OutgoingInvoiceReconciler.AttachmentOwner || attachment.OwnerId != invoice.Id)
        {
            return Error.NotFound(FiscalErrorCodes.NotFound, xml ? "O XML da nota ainda não foi guardado." : invoice.Type == BemAqui.Fiscal.Contracts.InvoiceType.NFSe
                ? "O PDF da NFS-e ainda não foi guardado." : "O DANFE da nota ainda não foi guardado.");
        }

        if (attachment.ScanStatus != ScanStatus.Clean)
        {
            return Error.Conflict("attachment.not_scanned", "O arquivo ainda está sendo verificado pelo antivírus. Tente de novo em alguns minutos.");
        }

        return new InvoiceFileContent(await storage.OpenReadAsync(attachment.StoragePath, cancellationToken), attachment.ContentType, attachment.FileName);
    }

    // ---------- apoio ----------

    private async Task<Result<(OutgoingInvoice Invoice, List<OutgoingInvoiceItem> Items)>> LoadDraftAsync(Guid id, uint version, CancellationToken ct)
    {
        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, ct);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Version != version)
        {
            Clear();
            return Error.Conflict("concurrency_conflict", "A nota fiscal mudou desde que você abriu. Recarregue.");
        }

        if (!invoice.IsEditableDraft)
        {
            Clear();
            return Error.Conflict(FiscalErrorCodes.InvoiceClosed, invoice.Status == OutgoingInvoiceStatus.Rejected
                ? "A nota foi rejeitada. Use \"Corrigir e emitir de novo\"."
                : "A nota não está mais em rascunho.");
        }

        db.Entry(invoice).Property(i => i.Version).OriginalValue = version;
        var items = await db.OutgoingInvoiceItems.Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(ct);
        return (invoice, items);
    }

    private async Task<string?> ComplementaryAsync(OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, CancellationToken ct)
    {
        var carrier = invoice.CarrierPartyId is { } c ? (await parties.FindAsync(c, ct))?.LegalName : null;
        return OutgoingInvoiceRules.ComplementaryForEmitter(invoice.AdditionalInformation, items.Select(i => (i.Sequence, i.AdditionalInfo)), invoice.Volumes, invoice.GrossWeight,
            invoice.VolumeSpecies, carrier);
    }

    /// <summary>Nada do que foi recusado fica rastreado no escopo.</summary>
    private void Clear() => db.ChangeTracker.Clear();
}
