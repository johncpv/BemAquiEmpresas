using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Fiscal.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// "Baixar PDF" e "Imprimir" do recebimento (EXP-04 CA 9): o comprovante gerado na hora pelo mesmo <see cref="GoodsReceiptService.DetailAsync"/>
/// da tela (404 para recebimento de outro tenant), sem gravar arquivo e sem custo de entrada. A nota do fornecedor vem do Fiscal quando o
/// recebimento foi pela nota. Cada download fica na auditoria de exportação.
/// </summary>
public sealed class GoodsReceiptDocumentService(GoodsReceiptService receipts, IIncomingInvoiceReceipts invoices, PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit)
{
    public const string AuditEntity = nameof(GoodsReceipt);

    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await receipts.DetailAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var receipt = detail.Value;
        if (receipt.Lines.Count > PurchaseOrderRules.PdfMaxLines)
        {
            return PurchaseOrderService.Invalid("lines", $"O PDF aceita até {PurchaseOrderRules.PdfMaxLines} linhas.");
        }

        string? invoice = null;
        if (receipt.IncomingInvoiceId is { } invoiceId && await invoices.FindForReceiptAsync(invoiceId, cancellationToken) is { } nf)
        {
            invoice = $"NF-e {nf.Number} série {nf.Series}";
        }

        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new GoodsReceiptPdfDocument(frame, GoodsReceiptPdf.Build(receipt, invoice)), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, id.ToString(), "pdf", 1, cancellationToken);
        return (bytes, receipt.Number + ".pdf");
    }
}
