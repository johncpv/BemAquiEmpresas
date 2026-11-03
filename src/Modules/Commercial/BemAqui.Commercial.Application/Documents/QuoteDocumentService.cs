using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// PDF e envio do orçamento (COM-03): o PDF sai do DTO sem custo, é gravado como anexo do sistema da revisão e vai por e-mail
/// pela fila, com cópia e "Responder para" de quem enviou.
/// </summary>
public sealed partial class QuoteDocumentService(
    ICommercialDb db, QuoteService quotes, IParties parties, IItems items, ICompanyIdentity company, PdfFrameFactory frames, IPdfRenderer renderer, AttachmentService attachments,
    IEmailSender email, EmailLogReader emailLogs, EmailDispatchLimiter limiter, ITenantParameters tenantParameters, IUserDirectory users, ICurrentUser currentUser,
    IEventPublisher events, IAuditWriter audit, TimeProvider clock)
{
    public const string OwnerType = "Quote";
    public const int MaxItems = 300;

    /// <summary>Limite do tenant, somado com os pedidos de compra (CMP-03b CA 5, <see cref="EmailDispatchLimiter"/>).</summary>
    public const int DailyLimit = EmailDispatchLimiter.TenantDailyLimit;

    /// <summary>Gerar PDF (CA 1 a 5): 422 acima de 300 itens ou observações acima de 2.000 caracteres, antes de gerar.</summary>
    public async Task<Result<AttachmentDto>> GeneratePdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var built = await BuildAsync(id, cancellationToken);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        var stored = await StoreAsync(built.Value, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new AttachmentDto(stored.Id, stored.FileName, stored.ContentType, stored.Size, stored.ScanStatus, stored.CreatedAt, Origin: stored.Origin);
    }

    /// <summary>
    /// "Baixar PDF" do cabeçalho (EXP-03 CA 7 e 8): o mesmo montador do "Gerar PDF", da revisão corrente, gerado na hora e <b>sem</b> gravar
    /// anexo; nome <c>{número}-rev{n}.pdf</c>. O download fica na auditoria de exportação.
    /// </summary>
    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var built = await BuildAsync(id, cancellationToken);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        var (quote, model) = built.Value;
        var bytes = await renderer.RenderAsync(new QuotePdfDocument(await frames.CreateAsync(cancellationToken), model), cancellationToken);
        await audit.RecordExportAsync(OwnerType, quote.Id.ToString(), "pdf", model.Items.Count, cancellationToken);
        return (bytes, $"{quote.Number}-rev{quote.Revision}.pdf");
    }

    private async Task<Attachment> StoreAsync((Quote Quote, QuotePdfModel Model) built, CancellationToken ct)
    {
        var bytes = await renderer.RenderAsync(new QuotePdfDocument(await frames.CreateAsync(ct), built.Model), ct);
        return await attachments.StoreGeneratedAsync(OwnerType, built.Quote.Id, $"{built.Quote.Number}-rev{built.Quote.Revision}.pdf", "application/pdf", bytes, ct);
    }

    private async Task<Result<(Quote Quote, QuotePdfModel Model)>> BuildAsync(Guid id, CancellationToken ct)
    {
        var quote = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quote is null)
        {
            return QuoteService.NotFound();
        }

        var detail = (await quotes.GetAsync(id, ct)).Value;
        if (detail.Items.Count > MaxItems)
        {
            return QuoteService.Invalid("items", $"O PDF aceita até {MaxItems} itens.");
        }

        if (detail.Notes is { Length: > 2000 })
        {
            return QuoteService.Invalid("notes", "As observações do PDF têm até 2.000 caracteres.");
        }

        var conditions = (await quotes.ConditionsAsync(id, ct)).Value;
        var units = (await items.FindAsync(detail.Items.Where(i => i.ItemId != null).Select(i => i.ItemId!.Value).Distinct().ToList(), ct)).ToDictionary(i => i.Id, i => i.UnitCode);
        var party = await parties.FindAsync(quote.PartyId, ct);
        var contact = quote.ContactId is { } c ? (await parties.ContactsAsync(quote.PartyId, ct)).FirstOrDefault(x => x.Id == c) : null;
        var site = quote.SiteAddressId is { } s ? (await parties.AddressesAsync(quote.PartyId, ct)).FirstOrDefault(x => x.Id == s) : null;
        var model = new QuotePdfModel(
            quote.Number, quote.Revision, BusinessTime.Today(clock),
            new QuotePdfCustomer(detail.PartyName, party?.Document, site is null ? null : $"{site.Street}, {site.Number} — {site.District} — {site.City}/{site.State}",
                contact is null ? null : string.Join(" · ", new[] { contact.Name, contact.Email, contact.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)))),
            detail.Items.Select(i => new QuotePdfItem(i.Sequence, i.Description, i.Quantity, i.ItemId is { } it ? units.GetValueOrDefault(it) : null, i.UnitPrice, i.Total)).ToList(),
            detail.TotalPrice,
            conditions.Milestones.Select(m => new QuotePdfMilestone(m.Description, m.Percent, m.Amount, When(m))).ToList(),
            detail.ValidUntil, detail.DeliveryDays, detail.Notes);
        var (mark, notice) = QuotePdf.Mark(quote.Status, quote.ValidUntil, quote.ExpiredAt);
        return (quote, model with { Mark = mark, Notice = notice });
    }

    private static string When(QuoteMilestoneView m) => m.Trigger == MilestoneTrigger.Date
        ? $"em {m.DueDate:dd/MM/yyyy}"
        : $"{(m.DaysAfterEvent is > 0 ? $"{m.DaysAfterEvent} dias após " : "na ")}{m.TriggerEvent switch
        {
            TriggerEvent.Signature => "assinatura do contrato",
            TriggerEvent.FabricationDone => "fabricação concluída",
            TriggerEvent.Delivered => "entrega",
            _ => "aceite técnico",
        }}";
}
