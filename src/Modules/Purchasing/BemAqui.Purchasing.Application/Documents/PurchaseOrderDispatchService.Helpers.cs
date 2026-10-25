using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application.Documents;

public sealed partial class PurchaseOrderDispatchService
{
    /// <summary>
    /// Validação do envio (CA 2, 6, 10): situação, versão, endereços puros (422 no campo), até 3 cópias, mensagem até 1.000 caracteres e,
    /// com endereço fora dos contatos do fornecedor, a confirmação. Devolve os endereços e a mensagem já limpos.
    /// </summary>
    private async Task<Result<(string To, List<string> Cc, string Message, bool Confirmed)>> SendErrorAsync(PurchaseOrder order, PurchaseOrderDispatchInput input, CancellationToken ct)
    {
        if (order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval)
        {
            return NotApproved();
        }

        if (!PurchaseOrderRules.Dispatchable.Contains(order.Status))
        {
            return Error.Conflict(NotDispatchableCode, order.Status == PurchaseOrderStatus.Cancelled
                ? "O pedido está cancelado: não pode ser enviado."
                : "O pedido já está em recebimento: baixe o PDF se o fornecedor precisar de outra via.");
        }

        if (order.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O pedido mudou desde que você abriu. Recarregue.");
        }

        if (!EmailAddresses.IsPlain(input.To))
        {
            return PurchaseOrderService.Invalid("to", "Informe um e-mail válido, só o endereço.");
        }

        var cc = (input.Cc ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
        if (cc.Count > PurchaseOrderRules.MaxDispatchCc)
        {
            return PurchaseOrderService.Invalid("cc", "Informe até 3 endereços em cópia.");
        }

        for (var i = 0; i < cc.Count; i++)
        {
            if (!EmailAddresses.IsPlain(cc[i]))
            {
                return PurchaseOrderService.Invalid($"cc[{i}]", "Informe um e-mail válido, só o endereço.");
            }
        }

        var message = (input.Message ?? string.Empty).Trim();
        if (message.Length > PurchaseOrderRules.MaxDispatchMessage)
        {
            return PurchaseOrderService.Invalid("message", "Escreva a mensagem com até 1.000 caracteres.");
        }

        var to = input.To.Trim();
        var known = (await parties.ContactsAsync(order.PartyId, ct)).Where(c => !string.IsNullOrWhiteSpace(c.Email)).Select(c => c.Email!.Trim().ToLowerInvariant()).ToHashSet();
        var strangers = new[] { to }.Concat(cc).Any(a => !known.Contains(a.ToLowerInvariant()));
        if (strangers && !input.ConfirmRecipient)
        {
            var party = await parties.FindAsync(order.PartyId, ct);
            return PurchaseOrderService.Invalid("confirmRecipient", $"Este e-mail não está nos contatos de {party?.TradeName ?? party?.LegalName ?? "o fornecedor"}. Confirme o destinatário.");
        }

        return (to, cc, message, strangers);
    }

    /// <summary>Modelo do PDF (CA 1, 8): limites da ADR 0022 antes de gerar; itens, códigos e unidade do fornecedor, empresa e logo.</summary>
    private async Task<Result<PurchaseOrderPdfModel>> BuildAsync(PurchaseOrder order, CancellationToken ct)
    {
        var lines = await db.PurchaseOrderLines.AsNoTracking().Where(l => l.OrderId == order.Id).OrderBy(l => l.Sequence).ToListAsync(ct);
        if (lines.Count > PurchaseOrderRules.PdfMaxLines)
        {
            return PurchaseOrderService.Invalid("lines", $"O PDF aceita até {PurchaseOrderRules.PdfMaxLines} linhas. Divida o pedido.");
        }

        if (order.Notes is { Length: > PurchaseOrderRules.PdfMaxNotes })
        {
            return PurchaseOrderService.Invalid("notes", "A observação do PDF tem até 2.000 caracteres.");
        }

        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var found = (await items.FindAsync(itemIds, ct)).ToDictionary(i => i.Id, i => new PurchaseOrderPdfItem(i.Code, i.Name, i.UnitCode));
        var codes = (await supplierCodes.ForItemsAsync(order.PartyId, itemIds, ct))
            .ToDictionary(c => c.Key, c => new PurchaseOrderPdfSupplierCode(c.Value.SupplierCode, c.Value.SupplierUnit, c.Value.ConversionFactor));
        var info = await company.GetAsync(ct);
        byte[]? logo = null;
        if (info.LogoStoragePath is { } path)
        {
            await using var stream = await storage.OpenReadAsync(path, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            logo = buffer.ToArray();
        }

        var party = await parties.FindAsync(order.PartyId, ct);
        var term = await terms.FindAsync(order.PaymentTermId, ct);
        var contact = string.Join(" · ", new[] { info.Phone, info.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return PurchaseOrderPdfBuilder.Build(
            order, lines, found, codes,
            new PurchaseOrderPdfCompany(info.TradeName ?? info.LegalName, info.LegalName, info.Document is { } d ? BrazilianDocuments.Format(d) : null, info.Address,
                contact.Length > 0 ? contact : null, logo),
            new PurchaseOrderPdfSupplier(party?.TradeName ?? party?.LegalName ?? "Fornecedor removido", party?.LegalName ?? "Fornecedor removido", party?.Document), term?.Name,
            BusinessTime.Today(clock));
    }

    /// <summary>
    /// E-mail na fila (CA 7): template <c>purchase-order-sent</c>, a mensagem como valor do modelo (escapada, nunca template), o nome da
    /// empresa sem CR/LF, remetente "{nome fantasia} via BemAqui", "Responder para" de quem enviou e só o PDF do pedido anexo; nenhum link.
    /// </summary>
    private async Task<Guid> EnqueueAsync(PurchaseOrder order, Attachment pdf, string to, List<string> cc, string message, CancellationToken ct)
    {
        var info = await company.GetAsync(ct);
        var name = EmailAddresses.OneLine(info.TradeName ?? info.LegalName);
        var me = currentUser.UserId;
        var sender = me is { } u ? await users.DisplayNameAsync(u, ct) : null;
        var replyTo = me is { } r ? await users.EmailOfAsync(r, ct) : null;
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tenant_name"] = name, ["order_number"] = order.Number, ["expected_date"] = order.ExpectedDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            ["message"] = message, ["sender_name"] = EmailAddresses.OneLine(sender ?? name),
        };
        return email.Enqueue(new EmailRequest(EmailDispatchLimiter.PurchaseOrderTemplate, to, model, PurchaseOrderService.AttachmentOwner, order.Id, [pdf.Id], cc, replyTo,
            $"{name} via BemAqui", me));
    }

    /// <summary>Aba Documentos (CA 3): PDFs gerados, envios com a situação do e-mail e anexos do usuário.</summary>
    public async Task<Result<PurchaseOrderDocuments>> DocumentsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.PurchaseOrders.AnyAsync(o => o.Id == id, cancellationToken))
        {
            return PurchaseOrderService.NotFound();
        }

        var listed = await attachments.ListAsync(PurchaseOrderService.AttachmentOwner, id, cancellationToken);
        if (listed.IsFailure)
        {
            return listed.Error!;
        }

        var rows = await db.PurchaseOrderDispatches.AsNoTracking().Where(d => d.OrderId == id).OrderByDescending(d => d.CreatedAt).ToListAsync(cancellationToken);
        var views = await ViewsAsync(rows, listed.Value, cancellationToken);
        var info = await company.GetAsync(cancellationToken);
        return new PurchaseOrderDocuments(listed.Value.Where(a => a.Origin == AttachmentOrigin.System).ToList(), listed.Value.Where(a => a.Origin == AttachmentOrigin.User).ToList(),
            views, info.Complete);
    }

    /// <summary>Um envio do pedido (CA 3, 10): lido pelo pedido da URL; envio de outro pedido é 404 (IDOR).</summary>
    public async Task<Result<PurchaseOrderDispatchView>> DispatchAsync(Guid id, Guid dispatchId, CancellationToken cancellationToken = default)
    {
        var row = await db.PurchaseOrderDispatches.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dispatchId && d.OrderId == id, cancellationToken);
        if (row is null)
        {
            return Error.NotFound("purchase_order_dispatch_not_found", "Envio não encontrado neste pedido.");
        }

        var listed = await attachments.ListAsync(PurchaseOrderService.AttachmentOwner, id, cancellationToken);
        return (await ViewsAsync([row], listed.IsSuccess ? listed.Value : [], cancellationToken))[0];
    }

    /// <summary>Situação de cada envio pelo e-mail da fila; endereços mascarados para quem não tem <c>parties.view</c> (CA 11).</summary>
    private async Task<List<PurchaseOrderDispatchView>> ViewsAsync(IReadOnlyList<PurchaseOrderDispatch> rows, IReadOnlyList<AttachmentDto> files, CancellationToken ct)
    {
        var status = (await emailLogs.StatusAsync(rows.Select(d => d.EmailLogId).ToList(), ct)).ToDictionary(s => s.Id);
        var byId = files.ToDictionary(f => f.Id);
        var personal = await permissions.HasPermissionAsync("parties", "view", ct);
        string Show(string address) => personal ? address : EmailAddresses.Mask(address);
        var views = new List<PurchaseOrderDispatchView>();
        foreach (var d in rows)
        {
            var s = status.GetValueOrDefault(d.EmailLogId);
            var file = byId.GetValueOrDefault(d.AttachmentId);
            var state = s?.Status ?? EmailStatus.Queued;
            views.Add(new PurchaseOrderDispatchView(
                d.Id, Show(d.To), d.Cc is null ? null : string.Join(",", d.Cc.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Show)), d.RecipientConfirmed, d.IsResend,
                d.SentBy is { } u ? await users.DisplayNameAsync(u, ct) : null, d.CreatedAt, d.AttachmentId, file?.FileName, state.ToString(), s?.Error,
                state == EmailStatus.Queued && file is { ScanStatus: ScanStatus.Pending }, s?.SentAt));
        }

        return views;
    }
}
