using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// PDFs da cotação (EXP-03 CA 1 a 3, 16): o pedido de cotação de um fornecedor e o mapa comparativo, gerados na hora no modelo único, sem
/// guardar arquivo. O fornecedor é sempre lido pela cotação da URL (IDOR: de outra cotação é 404). Cada download fica na auditoria.
/// </summary>
public sealed class RfqDocumentService(
    IPurchasingDb db, RfqService rfqs, IItems items, IItemSupplierCodes supplierCodes, IParties parties, IUserDirectory users, ICurrentUser currentUser,
    PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit, TimeProvider clock)
{
    public const string AuditEntity = "Rfq";

    /// <summary>
    /// Pedido de cotação de um fornecedor (CA 1): só da cotação aberta (409 "A cotação não está aberta." na concluída ou cancelada); itens na
    /// unidade do fornecedor, o endereço da empresa como local de entrega e "Resposta por" de quem baixa. Nunca traz preço nem resposta.
    /// </summary>
    public async Task<Result<(byte[] Content, string FileName)>> RequestPdfAsync(Guid id, Guid supplierId, CancellationToken cancellationToken = default)
    {
        var rfq = await db.Rfqs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rfq is null)
        {
            return RfqService.NotFound();
        }

        var supplier = await db.RfqSuppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId && s.RfqId == id, cancellationToken);
        if (supplier is null)
        {
            return Error.NotFound("rfq_supplier_not_found", "Fornecedor não encontrado nesta cotação.");
        }

        if (rfq.Status != RfqStatus.Open)
        {
            return Error.Conflict(RfqPdf.NotOpenCode, "A cotação não está aberta.");
        }

        var lines = await db.RfqLines.AsNoTracking().Where(l => l.RfqId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        if (lines.Count > PurchaseOrderRules.PdfMaxLines)
        {
            return TooMany();
        }

        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var found = (await items.FindAsync(itemIds, cancellationToken)).ToDictionary(i => i.Id, i => new PurchaseOrderPdfItem(i.Code, i.Name, i.UnitCode));
        var codes = (await supplierCodes.ForItemsAsync(supplier.PartyId, itemIds, cancellationToken))
            .ToDictionary(c => c.Key, c => new PurchaseOrderPdfSupplierCode(c.Value.SupplierCode, c.Value.SupplierUnit, c.Value.ConversionFactor));
        var party = await parties.FindAsync(supplier.PartyId, cancellationToken);
        var contact = await parties.BillingContactAsync(supplier.PartyId, cancellationToken)
            ?? (await parties.ContactsAsync(supplier.PartyId, cancellationToken)).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Email));
        var name = party is null ? "Fornecedor removido" : party.TradeName ?? party.LegalName;
        var buyer = currentUser.UserId ?? rfq.CreatedBy;
        var buyerName = buyer is { } b ? await users.DisplayNameAsync(b, cancellationToken) : null;
        var buyerEmail = buyer is { } e ? await users.EmailOfAsync(e, cancellationToken) : null;
        var frame = await frames.CreateAsync(cancellationToken);
        var model = RfqPdf.Request(
            rfq, lines, found, codes,
            new RfqRequestPdfSupplier(name, party?.LegalName ?? name, party?.Document,
                contact is null ? null : string.Join(" · ", new[] { contact.Name, contact.Email, contact.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)))),
            frame.Company.Address, buyerName ?? "Compras", buyerEmail, BusinessTime.Today(clock));
        var bytes = await renderer.RenderAsync(new RfqRequestPdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, id.ToString(), "pdf", 1, cancellationToken);
        return (bytes, RfqPdf.RequestFileName(rfq.Number, name));
    }

    /// <summary>
    /// Mapa comparativo (CA 2 e 3): da cotação aberta com ao menos uma resposta ("Prévia"), da concluída (sem marca) ou da cancelada
    /// ("Cancelado"); o mesmo cálculo da aba Comparativo (<see cref="RfqService.DetailAsync"/>).
    /// </summary>
    public async Task<Result<(byte[] Content, string FileName)>> ComparisonPdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await rfqs.DetailAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var d = detail.Value;
        if (d.Status == RfqStatus.Open && !d.Suppliers.Any(s => s.Responded))
        {
            return Error.Conflict(RfqPdf.NoResponseCode, "O mapa comparativo sai depois da primeira resposta de fornecedor.");
        }

        if (d.Lines.Count > PurchaseOrderRules.PdfMaxLines)
        {
            return TooMany();
        }

        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new RfqComparisonPdfDocument(frame, RfqPdf.Comparison(d, BusinessTime.Today(clock))), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, id.ToString(), "pdf", 1, cancellationToken);
        return (bytes, RfqPdf.ComparisonFileName(d.Number));
    }

    private static Error TooMany() => PurchaseOrderService.Invalid("lines", $"O PDF aceita até {PurchaseOrderRules.PdfMaxLines} linhas. Divida a cotação.");
}
