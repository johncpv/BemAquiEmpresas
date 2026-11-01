using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>O que a leitura assistida usa além da FIS-04a: a fila de jobs, o e-mail, o sino, o worker, o XML pela chave e a empresa.</summary>
public sealed record IncomingReadingDependencies(
    IBackgroundJobClient Jobs, IEmailSender Email, INotificationService Notifications, IPdfInvoiceReader Reader, IIncomingXmlDownloader XmlDownloads, ICompanyIdentity Company);

/// <summary>
/// FIS-05: NF-e de entrada pelo PDF da DANFE (leitura assistida no worker isolado) ou digitada, com a conferência campo a campo antes de
/// virar nota; depois, a nota segue pela FIS-04a (de-para, pedido, conferência) e pela FIS-04b (recebimento). O que o worker lê é só
/// sugestão: nada entra no estoque nem no financeiro sem a conferência (R-PRP-7). Log só com o resultado, a duração e a chave mascarada.
/// </summary>
public sealed partial class IncomingInvoiceService
{
    /// <summary>Dono de anexo do PDF da DANFE (anexo de sistema do rascunho de leitura, debaixo do prefixo do tenant).</summary>
    public const string PdfAttachmentOwner = "IncomingInvoicePdf";
    public const string ReadingNotFoundCode = "incoming_reading_not_found";
    public const string ReadingLink = "/app/fiscal/entrada/leitura/";
    public const string XmlRequestTemplate = "incoming-invoice-xml-request";
    public const string XmlDifferenceNotification = "fiscal.incoming_xml_difference";

    /// <summary>Um pedido do XML por leitura a cada 24 horas (o fornecedor recebe um e-mail por vez).</summary>
    public static readonly TimeSpan XmlRequestInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// "Importar PDF" (CA 1): até 5 MB, a triagem do dono (allowlist só PDF, magic bytes e antivírus, falha fechada), a recusa de conteúdo
    /// ativo e de criptografia pelos bytes (sem abrir o PDF), o rascunho "Lendo o PDF…" com o PDF como anexo de sistema e o job da leitura.
    /// </summary>
    public async Task<Result<IncomingReadingView>> UploadPdfAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            return Invalid("file", "Escolha o PDF da nota.");
        }

        if (content.Length > IncomingReadingRules.MaxPdfBytes)
        {
            return Refused(null, Invalid("file", "O PDF precisa ter até 5 MB."));
        }

        var screened = await attachments.ScreenAsync(PdfAttachmentOwner, fileName, content, cancellationToken);
        if (screened.IsFailure)
        {
            return Refused(null, screened.Error!);
        }

        if (!PdfActiveContentScan.IsPdf(content) || PdfActiveContentScan.HasActiveContent(content))
        {
            return Refused(null, Error.Validation("incoming_pdf_unsafe", IncomingReadingRules.UnsafeMessage, new Dictionary<string, string[]> { ["file"] = [IncomingReadingRules.UnsafeMessage] }));
        }

        var now = clock.GetUtcNow();
        var draft = IncomingInvoiceReading.ForPdf(currentUser.UserId, now, screened.Value.FileName);
        var stored = await attachments.StoreScreenedAsync(PdfAttachmentOwner, draft.Id, screened.Value with { FileName = "danfe-" + draft.Id.ToString("N")[..8] + ".pdf" }, TenantId().ToString("N"),
            AttachmentOrigin.System, cancellationToken);
        draft.AttachPdf(stored.Id);
        db.IncomingInvoiceReadings.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        PdfInvoiceReadingJob.Enqueue(reading.Jobs, TenantId(), draft.Id);
        LogPdfQueued(logger, draft.Id, content.Length);
        return await GetReadingAsync(draft.Id, cancellationToken);
    }

    /// <summary>"Digitar a nota" sem PDF (CA 9): o mesmo formulário da leitura assistida, vazio.</summary>
    public async Task<Result<IncomingReadingView>> StartManualAsync(CancellationToken cancellationToken = default)
    {
        var draft = IncomingInvoiceReading.ForManual(currentUser.UserId, clock.GetUtcNow());
        db.IncomingInvoiceReadings.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        return await GetReadingAsync(draft.Id, cancellationToken);
    }

    /// <summary>"Digitar a nota" depois da leitura que não serviu (CA 9): o formulário vazio, com o PDF ainda à mão para consulta.</summary>
    public async Task<Result<IncomingReadingView>> SwitchToManualAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var draft = await OpenReadingAsync(id, cancellationToken);
        if (draft.IsFailure)
        {
            return draft.Error!;
        }

        draft.Value.SwitchToManual(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return await GetReadingAsync(id, cancellationToken);
    }

    /// <summary>
    /// Job da leitura (CA 2 a 4 e 12): o PDF guardado vai ao worker; a resposta (dado de fora) é conferida campo a campo; a chave com DV
    /// errado é descartada com aviso; o CNPJ do texto que contradiz a chave é ignorado. Só a chave mascarada, o resultado e a duração no log.
    /// </summary>
    public async Task CompleteReadingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var draft = await db.IncomingInvoiceReadings.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (draft is not { Status: IncomingReadingStatus.Reading, Source: IncomingInvoiceSource.Pdf })
        {
            return;
        }

        PdfReaderReply reply;
        if (draft.PdfAttachmentId is not { } attachmentId || (await attachments.DownloadCheckedAsync(PdfAttachmentOwner, draft.Id, attachmentId, cancellationToken)) is not { IsSuccess: true } file)
        {
            reply = PdfReaderReply.Of(PdfReadingOutcome.Failed);
        }
        else
        {
            byte[] bytes;
            await using (var stream = file.Value.Content)
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                bytes = buffer.ToArray();
            }

            reply = await reading.Reader.ReadAsync(bytes, cancellationToken);
        }

        var tenantDocument = await catalog.DocumentAsync(TenantId(), cancellationToken);
        var (fields, key, warnings) = IncomingReadingSanitizer.Sanitize(reply, tenantDocument);
        draft.Complete(reply.Outcome, reply.Pages, reply.DurationMs, key, fields, warnings, clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // O usuário trocou para "Digitar a nota" enquanto o worker lia: vale a escolha dele, a leitura é descartada.
            return;
        }

        var readKey = IncomingInvoiceRules.MaskKey(key);
        var outcome = reply.Outcome.ToString();
        LogPdfRead(logger, draft.Id, outcome, reply.DurationMs, readKey);
    }

    /// <summary>A leitura assistida (CA 3 a 7): campos, avisos, chave desmontada, fornecedor pela chave, duplicidade e o pedido do XML.</summary>
    public async Task<Result<IncomingReadingView>> GetReadingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var draft = await db.IncomingInvoiceReadings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (draft is null)
        {
            return ReadingNotFound();
        }

        var keyValue = draft.AccessKey ?? draft.Fields.FirstOrDefault(f => f.Key == "accessKey")?.Value;
        var key = IncomingReadingRules.Parse(keyValue);
        IncomingSupplierView? supplier = null;
        Guid? duplicateId = null;
        string? duplicateMessage = null;
        if (key is not null)
        {
            var party = await parties.FindByDocumentAsync(key.IssuerDocument, cancellationToken);
            supplier = party is null || party.IsDeleted
                ? new IncomingSupplierView(IncomingSupplierState.NotFound, null, null, false)
                : new IncomingSupplierView(party.Roles.Contains(PartyRoleKind.Supplier) ? IncomingSupplierState.Found : IncomingSupplierState.NotSupplier, party.Id,
                    party.TradeName is { Length: > 0 } t ? t : party.LegalName, party.IsActive);
            if (draft.IsOpen && await db.IncomingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.AccessKey == key.Key && i.Status != IncomingInvoiceStatus.Rejected, cancellationToken) is { } previous)
            {
                duplicateId = previous.Id;
                duplicateMessage = (await DuplicateAsync(previous, cancellationToken)).Message;
            }
        }

        var can = new IncomingReadingPermissions(
            await permissions.HasPermissionAsync("fiscal", "import_incoming", cancellationToken),
            await permissions.HasPermissionAsync("parties", StandardActions.Create, cancellationToken));
        return new IncomingReadingView(
            draft.Id, draft.Source, draft.Status, draft.Result, draft.Result is { } r && r != PdfReadingOutcome.Read ? IncomingReadingRules.MessageOf(r) : null,
            draft.PdfAttachmentId is not null && IncomingReadingRules.PassedSafety(draft.Result), draft.FileName, draft.Pages, draft.DurationMs, draft.Fields, draft.Warnings,
            key is null ? null : new AccessKeyView(key.Key, key.StateCode, key.Year, key.Month, key.IssuerDocument, key.Model, key.Series, key.Number),
            supplier, await catalog.DocumentAsync(TenantId(), cancellationToken), duplicateId, duplicateMessage, reading.XmlDownloads.IsAvailable,
            reading.XmlDownloads.IsAvailable ? null : IncomingReadingRules.NoDfeMessage, draft.XmlRequestedAt, draft.InvoiceId, draft.CreatedAt, can);
    }

    /// <summary>
    /// O PDF da leitura (CA 11): só pelo endpoint autorizado, do rascunho do tenant (anexo por registro); de outro, 404. O PDF que não passou
    /// pela conferência de segurança do worker (recusado, inválido, que falhou ou estourou o tempo) não sai (409).
    /// </summary>
    public async Task<Result<AttachmentContent>> ReadingPdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var draft = await db.IncomingInvoiceReadings.AsNoTracking().Where(r => r.Id == id).Select(r => new { r.PdfAttachmentId, r.Result }).FirstOrDefaultAsync(cancellationToken);
        if (draft is not { PdfAttachmentId: { } attachmentId })
        {
            return ReadingNotFound();
        }

        return IncomingReadingRules.PassedSafety(draft.Result)
            ? await attachments.DownloadCheckedAsync(PdfAttachmentOwner, id, attachmentId, cancellationToken)
            : Error.Conflict("incoming_pdf_not_shown", IncomingReadingRules.PdfNotShownMessage);
    }

    /// <summary>O PDF da nota lida do PDF (CA 8 e 11), pela leitura que a originou.</summary>
    public async Task<Result<AttachmentContent>> InvoicePdfAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var pdf = await db.IncomingInvoices.AsNoTracking().Where(i => i.Id == invoiceId).Select(i => i.PdfAttachmentId).FirstOrDefaultAsync(cancellationToken);
        var source = pdf is null ? null : await db.IncomingInvoiceReadings.AsNoTracking().Where(r => r.InvoiceId == invoiceId && r.PdfAttachmentId == pdf).Select(r => new { r.Id, r.PdfAttachmentId }).FirstOrDefaultAsync(cancellationToken);
        return source is { PdfAttachmentId: { } attachmentId }
            ? await attachments.DownloadCheckedAsync(PdfAttachmentOwner, source.Id, attachmentId, cancellationToken)
            : NotFound();
    }

    /// <summary>
    /// "Pedir o XML ao fornecedor" (CA 6, R-PRP-3): e-mail <c>incoming-invoice-xml-request</c> pela fila ao contato do fornecedor da chave (o
    /// de cobrança, senão o primeiro com e-mail), com o número e a chave; "Responder para" de quem pediu. Um pedido a cada 24 horas.
    /// </summary>
    public async Task<Result<XmlRequestResult>> RequestXmlAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var draft = await db.IncomingInvoiceReadings.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (draft is null)
        {
            return ReadingNotFound();
        }

        if (draft.InvoiceId is { } invoiceId && await db.IncomingInvoices.AsNoTracking().AnyAsync(i => i.Id == invoiceId && i.Source == IncomingInvoiceSource.Xml, cancellationToken))
        {
            return Error.Conflict("incoming_xml_already_received", "Esta nota já tem o XML.");
        }

        var key = IncomingReadingRules.Parse(draft.AccessKey ?? draft.Fields.FirstOrDefault(f => f.Key == "accessKey")?.Value);
        if (key is null)
        {
            return Invalid("accessKey", "Confira a chave de acesso antes de pedir o XML: o pedido leva o número e a chave da nota.");
        }

        var now = clock.GetUtcNow();
        if (draft.XmlRequestedAt is { } last && now - last < XmlRequestInterval)
        {
            return Error.Conflict("incoming_xml_already_requested", "O XML desta nota já foi pedido nas últimas 24 horas. Aguarde a resposta do fornecedor.");
        }

        var party = await parties.FindByDocumentAsync(key.IssuerDocument, cancellationToken);
        if (party is null || party.IsDeleted)
        {
            return Invalid("supplier", "Cadastre o fornecedor da nota, com um contato de e-mail, para pedir o XML.");
        }

        var billing = await parties.BillingContactAsync(party.Id, cancellationToken);
        var to = EmailAddresses.IsPlain(billing?.Email) ? billing!.Email!.Trim()
            : (await parties.ContactsAsync(party.Id, cancellationToken)).Select(c => c.Email?.Trim()).FirstOrDefault(EmailAddresses.IsPlain);
        if (to is null)
        {
            return Invalid("contact", "O fornecedor não tem contato com e-mail. Cadastre um contato no fornecedor para pedir o XML.");
        }

        var info = await reading.Company.GetAsync(cancellationToken);
        var name = EmailAddresses.OneLine(info.TradeName ?? info.LegalName);
        var me = currentUser.UserId;
        var sender = me is { } u ? await users.DisplayNameAsync(u, cancellationToken) : null;
        var replyTo = me is { } r ? await users.EmailOfAsync(r, cancellationToken) : null;
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tenant_name"] = name, ["invoice_number"] = key.Number.ToString(CultureInfo.InvariantCulture), ["invoice_series"] = key.Series.ToString(CultureInfo.InvariantCulture),
            ["access_key"] = string.Join(' ', Enumerable.Range(0, 11).Select(i => key.Key.Substring(i * 4, 4))), ["sender_name"] = EmailAddresses.OneLine(sender ?? name),
        };
        reading.Email.Enqueue(new EmailRequest(XmlRequestTemplate, to, model, nameof(IncomingInvoiceReading), draft.Id, null, null, replyTo, $"{name} via BemAqui", me));
        draft.MarkXmlRequested(me, now);
        await db.SaveChangesAsync(cancellationToken);
        var requestedKey = IncomingInvoiceRules.MaskKey(key.Key);
        LogXmlRequested(logger, draft.Id, requestedKey);
        var visible = await permissions.HasPermissionAsync("parties", StandardActions.View, cancellationToken);
        return new XmlRequestResult(visible ? to : EmailAddresses.Mask(to), now);
    }

    /// <summary>
    /// "Conferir nota" da leitura assistida ou da nota digitada (CA 4, 5 e 7 a 9): todo campo conferido; a chave de 44 dígitos com DV,
    /// modelo 55 e emitente pessoa jurídica; a emissão no mês da chave; o destinatário igual ao CNPJ do tenant; os itens válidos e a soma
    /// igual ao total dos produtos; a chave não importada (409 com o link). A nota nasce <c>Imported</c> com a origem, o PDF de anexo, o
    /// fornecedor pelo CNPJ da chave e o de-para lembrado, e segue pela FIS-04a.
    /// </summary>
    public async Task<Result<CheckReadingResult>> CheckReadingAsync(Guid id, CheckReadingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var opened = await OpenReadingAsync(id, cancellationToken);
        if (opened.IsFailure)
        {
            return opened.Error!;
        }

        var draft = opened.Value;
        if (draft.Status == IncomingReadingStatus.Reading)
        {
            return Error.Conflict("incoming_reading_in_progress", "Aguarde a leitura do PDF terminar.");
        }

        if (draft.Status == IncomingReadingStatus.Failed)
        {
            return Error.Conflict("incoming_reading_failed", "Esta leitura não serviu. Peça o XML ao fornecedor ou digite a nota.");
        }

        var tenantDocument = await catalog.DocumentAsync(TenantId(), cancellationToken);
        var built = IncomingReadingForm.Build(request, tenantDocument, BusinessTime.Today(clock));
        if (built.IsFailure)
        {
            return built.Error!;
        }

        var form = built.Value;
        if (await db.IncomingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.AccessKey == form.Key.Key && i.Status != IncomingInvoiceStatus.Rejected, cancellationToken) is { } previous)
        {
            return Refused(form.Key.Key, await DuplicateAsync(previous, cancellationToken));
        }

        var party = await parties.FindByDocumentAsync(form.Key.IssuerDocument, cancellationToken);
        var now = clock.GetUtcNow();
        var invoice = new IncomingInvoice
        {
            AccessKey = form.Key.Key, Number = form.Key.Number, Series = form.Key.Series, IssuedAt = form.IssuedAt, IssuerDocument = form.Key.IssuerDocument, IssuerName = form.IssuerName,
            Totals = form.Totals, Total = form.Totals.Total, Duplicates = [], SefazStatusChecked = false, OrderReferences = [], ImportedBy = currentUser.UserId, ImportedAt = now,
        };
        invoice.FromReading(draft.Source, IncomingReadingRules.PassedSafety(draft.Result) ? draft.PdfAttachmentId : null);
        invoice.SetParty(party?.Id, now);
        var lines = form.Items.Select((i, n) => new IncomingInvoiceItem
        {
            InvoiceId = invoice.Id, Sequence = n + 1, SupplierCode = i.Code, Description = i.Description, Ncm = i.Ncm, Unit = i.Unit, Quantity = i.Quantity, UnitPrice = i.UnitPrice,
            Amount = i.Amount,
        }).ToList();
        var remembered = party is null ? 0 : await ApplyRememberedAsync(party.Id, lines, cancellationToken);
        var corrected = IncomingReadingForm.Corrected(draft.Fields, form.Values);
        draft.MarkChecked(invoice.Id, form.Key.Key, corrected, currentUser.UserId, now);
        db.IncomingInvoices.Add(invoice);
        db.IncomingInvoiceItems.AddRange(lines);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, AccessKeyIndex))
        {
            db.ChangeTracker.Clear();
            var existing = await db.IncomingInvoices.AsNoTracking().FirstAsync(i => i.AccessKey == form.Key.Key && i.Status != IncomingInvoiceStatus.Rejected, cancellationToken);
            return Refused(form.Key.Key, await DuplicateAsync(existing, cancellationToken));
        }

        var checkedKey = IncomingInvoiceRules.MaskKey(invoice.AccessKey);
        var origin = draft.Source.ToString();
        LogReadingChecked(logger, checkedKey, invoice.Id, origin, lines.Count, corrected.Count);
        return new CheckReadingResult(invoice.Id, invoice.AccessKey, invoice.Number, invoice.Series, invoice.IssuerName, invoice.Total, lines.Count, remembered, draft.Source);
    }

    /// <summary>
    /// "Substituir pelo XML" (CA 10): o XML passa por toda a validação da FIS-04a (triagem, leiaute, protocolo, assinatura, destinatário),
    /// tem de ser da mesma chave e troca a origem para <c>Xml</c>, mantendo os valores conferidos e o recebimento já feito; as diferenças com
    /// o XML ficam na nota e quem tem <c>fiscal.import_incoming</c> e <c>finance.edit</c> é avisado no sino.
    /// </summary>
    public async Task<Result<IncomingInvoiceView>> ReplaceWithXmlAsync(Guid id, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var invoice = await db.IncomingInvoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (!invoice.WithoutXml)
        {
            return Error.Conflict("incoming_xml_already_received", "Esta nota já tem o XML.");
        }

        if (invoice.Status == IncomingInvoiceStatus.Rejected)
        {
            return Error.Conflict(NotEditableCode, "Esta nota foi recusada. Importe o XML como uma nota nova.");
        }

        if (content.Length == 0 || content.Length > IncomingInvoiceRules.MaxBytes)
        {
            return Invalid("file", content.Length == 0 ? "Escolha o arquivo XML da nota." : "O arquivo precisa ter até 2 MB.");
        }

        var screened = await attachments.ScreenAsync(AttachmentOwner, fileName, content, cancellationToken);
        if (screened.IsFailure)
        {
            return Refused(invoice.AccessKey, screened.Error!);
        }

        var validated = NfeXmlValidator.Validate(content, anchors);
        if (validated.IsFailure)
        {
            return Refused(invoice.AccessKey, validated.Error!);
        }

        var nfe = validated.Value.Nfe;
        var document = await catalog.DocumentAsync(TenantId(), cancellationToken);
        if (document is null || !string.Equals(nfe.RecipientCnpj, document, StringComparison.Ordinal))
        {
            return Refused(nfe.AccessKey, NfeXmlReader.Invalid("incoming_not_recipient", IncomingInvoiceRules.NotRecipientMessage));
        }

        if (!string.Equals(nfe.AccessKey, invoice.AccessKey, StringComparison.Ordinal))
        {
            return Refused(nfe.AccessKey, Invalid("file", "Este XML é de outra nota: a chave de acesso não é a desta nota."));
        }

        var lines = await db.IncomingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        var differences = IncomingReadingForm.Differences(invoice, lines, nfe);
        var stored = await attachments.StoreScreenedAsync(AttachmentOwner, invoice.Id, screened.Value with { FileName = nfe.AccessKey + "-nfe.xml" }, TenantId().ToString("N"), AttachmentOrigin.System,
            cancellationToken);
        var now = clock.GetUtcNow();
        invoice.ReplaceWithXml(stored.Id, validated.Value.Signer.Document, nfe.Protocol!, nfe.AuthorizedAt?.ToUniversalTime(), differences, currentUser.UserId, now);
        if (differences.Count > 0)
        {
            var importers = (await users.UsersWithPermissionAsync("fiscal.import_incoming", cancellationToken)).ToHashSet();
            var finance = await users.UsersWithPermissionAsync("finance.edit", cancellationToken);
            var title = differences.Count == 1
                ? $"O XML da NF-e {invoice.Number} chegou com 1 diferença do que foi conferido. Confira a nota e os títulos."
                : $"O XML da NF-e {invoice.Number} chegou com {differences.Count} diferenças do que foi conferido. Confira a nota e os títulos.";
            foreach (var user in finance.Where(importers.Contains).Distinct())
            {
                reading.Notifications.Notify(user, XmlDifferenceNotification, title, Link + invoice.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var replacedKey = IncomingInvoiceRules.MaskKey(invoice.AccessKey);
        LogReplaced(logger, replacedKey, invoice.Id, differences.Count);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<Result<IncomingInvoiceReading>> OpenReadingAsync(Guid id, CancellationToken ct)
    {
        var draft = await db.IncomingInvoiceReadings.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (draft is null)
        {
            return ReadingNotFound();
        }

        return draft.IsOpen
            ? draft
            : Error.Conflict("incoming_reading_checked", "Esta leitura já foi conferida e virou nota.") with
            {
                Data = new Dictionary<string, object?> { ["invoiceId"] = draft.InvoiceId, ["link"] = Link + draft.InvoiceId },
            };
    }

    private static Error ReadingNotFound() => Error.NotFound(ReadingNotFoundCode, "Leitura da nota não encontrada.");

    [LoggerMessage(Level = LogLevel.Information, Message = "PDF de nota de entrada enviado para leitura ({ReadingId}, {Bytes} bytes).")]
    private static partial void LogPdfQueued(ILogger logger, Guid readingId, int bytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Leitura do PDF {ReadingId}: {Result} em {DurationMs} ms, chave {AccessKey}.")]
    private static partial void LogPdfRead(ILogger logger, Guid readingId, string result, int durationMs, string accessKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pedido do XML da nota {AccessKey} enviado ao fornecedor (leitura {ReadingId}).")]
    private static partial void LogXmlRequested(ILogger logger, Guid readingId, string accessKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nota de entrada {AccessKey} conferida da leitura ({InvoiceId}, origem {Source}): {Items} itens, {Corrected} campos corrigidos.")]
    private static partial void LogReadingChecked(ILogger logger, string accessKey, Guid invoiceId, string source, int items, int corrected);

    [LoggerMessage(Level = LogLevel.Information, Message = "Nota de entrada {AccessKey} ({InvoiceId}) substituída pelo XML: {Differences} diferenças.")]
    private static partial void LogReplaced(ILogger logger, string accessKey, Guid invoiceId, int differences);
}
