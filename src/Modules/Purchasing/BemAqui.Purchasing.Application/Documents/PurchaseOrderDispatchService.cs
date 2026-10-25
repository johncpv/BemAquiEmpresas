using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// PDF e envio do pedido ao fornecedor (CMP-03b): o PDF sai do <see cref="PurchaseOrderPdfBuilder"/> (sem custo, margem nem projeto)
/// pelo <see cref="IPdfRenderer"/>; o envio guarda o PDF como arquivo do sistema do pedido, põe o e-mail na fila com o anexo (só
/// depois do <c>Clean</c>, no <c>SendEmailJob</c>), registra o envio e passa o pedido a <c>Sent</c>, numa transação com a trava do
/// limite diário (<see cref="EmailDispatchLimiter"/>). O envio é sempre lido pelo pedido da URL (IDOR).
/// </summary>
public sealed partial class PurchaseOrderDispatchService(
    IPurchasingDb db, PurchaseOrderService orders, IItems items, IItemSupplierCodes supplierCodes, IParties parties, IPaymentTerms terms, ICompanyIdentity company,
    IFileStorage storage, IPdfRenderer renderer, AttachmentService attachments, IEmailSender email, EmailLogReader emailLogs, EmailDispatchLimiter limiter,
    ITenantParameters tenantParameters, IUserDirectory users, ICurrentUser currentUser, IPermissionChecker permissions, TimeProvider clock)
{
    public const string NotApprovedCode = "purchase_order_not_approved";
    public const string NotDispatchableCode = "purchase_order_not_dispatchable";

    private static Error NotApproved() => Error.Conflict(NotApprovedCode, "Aprove o pedido antes de enviar.");

    /// <summary>"Baixar PDF" (CA 1, 8): do aprovado em diante; 409 antes da aprovação; 422 acima de 300 linhas ou 2.000 caracteres, antes de gerar.</summary>
    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return PurchaseOrderService.NotFound();
        }

        if (!PurchaseOrderRules.PdfStatuses.Contains(order.Status))
        {
            return NotApproved();
        }

        var built = await BuildAsync(order, cancellationToken);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        return (await renderer.RenderAsync(new PurchaseOrderPdfDocument(built.Value), cancellationToken), FileName(order));
    }

    /// <summary>Diálogo "Enviar ao fornecedor" (CA 2): o destinatário padrão, o assunto e os e-mails dos contatos do fornecedor.</summary>
    public async Task<Result<PurchaseOrderDispatchDraft>> DraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return PurchaseOrderService.NotFound();
        }

        var contacts = await parties.ContactsAsync(order.PartyId, cancellationToken);
        var billing = await parties.BillingContactAsync(order.PartyId, cancellationToken);
        var to = billing?.Email is { Length: > 0 } b ? b : contacts.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Email))?.Email;
        var party = await parties.FindAsync(order.PartyId, cancellationToken);
        var info = await company.GetAsync(cancellationToken);
        return new PurchaseOrderDispatchDraft(to?.Trim(), Subject(order, info), party is null ? "o fornecedor" : party.TradeName ?? party.LegalName,
            contacts.Where(c => !string.IsNullOrWhiteSpace(c.Email)).Select(c => c.Email!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), IsResend(order), info.Complete);
    }

    /// <summary>
    /// "Enviar ao fornecedor" e "Reenviar" (CA 2, 4 a 7, 9): endereços puros, confirmação fora dos contatos, PDF novo guardado como arquivo
    /// do sistema, e-mail na fila com o anexo, o registro do envio e o pedido em <c>Sent</c> (a faixa "Alterado depois do envio" some),
    /// tudo numa transação com o limite diário conferido sob a trava do tenant.
    /// </summary>
    public async Task<Result<PurchaseOrderDetail>> SendAsync(Guid id, PurchaseOrderDispatchInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return PurchaseOrderService.NotFound();
        }

        var checkedInput = await SendErrorAsync(order, input, cancellationToken);
        if (checkedInput.IsFailure)
        {
            return checkedInput.Error!;
        }

        var (to, cc, message, confirmed) = checkedInput.Value;
        var built = await BuildAsync(order, cancellationToken);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        var bytes = await renderer.RenderAsync(new PurchaseOrderPdfDocument(built.Value), cancellationToken);
        var me = currentUser.UserId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await limiter.CheckAsync(me, await tenantParameters.EmailDispatchMaxPerUserDayAsync(cancellationToken), cancellationToken) is { } limit)
        {
            return limit;
        }

        var now = clock.GetUtcNow();
        var resend = IsResend(order);
        var pdf = await attachments.StoreGeneratedAsync(PurchaseOrderService.AttachmentOwner, order.Id, FileName(order), "application/pdf", bytes, cancellationToken);
        var logId = await EnqueueAsync(order, pdf, to, cc, message, cancellationToken);
        db.PurchaseOrderDispatches.Add(new PurchaseOrderDispatch
        {
            OrderId = order.Id, EmailLogId = logId, AttachmentId = pdf.Id, To = to, Cc = cc.Count > 0 ? string.Join(",", cc) : null, RecipientConfirmed = confirmed, IsResend = resend,
            SentBy = me, CreatedAt = now,
        });
        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        order.Status = PurchaseOrderStatus.Sent;
        order.ChangedAfterSent = false;
        order.SentAt = now;
        order.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency_conflict", "O pedido mudou desde que você abriu. Recarregue.");
        }

        await transaction.CommitAsync(cancellationToken);
        return await orders.DetailAsync(order.Id, cancellationToken);
    }

    private static bool IsResend(PurchaseOrder order) => order.SentAt is not null || order.Status == PurchaseOrderStatus.Sent || order.ChangedAfterSent;

    private static string FileName(PurchaseOrder order) => order.Number + ".pdf";

    private static string Subject(PurchaseOrder order, CompanyIdentity info) => $"Pedido de compra {order.Number} — {EmailAddresses.OneLine(info.TradeName ?? info.LegalName)}";
}
