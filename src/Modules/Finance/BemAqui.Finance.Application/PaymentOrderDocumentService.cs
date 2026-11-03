using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// "Baixar PDF" e "Imprimir" da ordem de pagamento (EXP-01): gerado na hora, do jeito que a ordem está, no modelo único. Lê pelo mesmo
/// <see cref="PaymentOrderService.GetAsync"/> da tela (mesma permissão e mesmo retrato mascarado) e registra a exportação na auditoria.
/// </summary>
public sealed class PaymentOrderDocumentService(PaymentOrderService orders, IFinanceDb db, PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit)
{
    public const string AuditEntity = "PaymentOrder";

    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await orders.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var order = detail.Value;
        var bank = order.BankAccountId is { } bankId
            ? await db.BankAccounts.AsNoTracking().Where(b => b.Id == bankId).Select(b => b.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new PaymentOrderPdfDocument(frame, new PaymentOrderPdfModel(order, bank)), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, id.ToString(), "pdf", order.Items.Count, cancellationToken);
        return (bytes, order.Summary.Number + ".pdf");
    }
}
