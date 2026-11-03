using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Engineering.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Application;

/// <summary>
/// "Baixar PDF" e "Imprimir" da OS de fabricação (EXP-03 CA 10 a 17): a ficha gerada na hora, pelo mesmo <see cref="FabricationOrderService.GetAsync"/>
/// da tela (404 para OS de outro tenant ou de instalação), a previsão do plano, o material da aba Material (<see cref="IOrderMaterial"/>), a BOM
/// pelo <see cref="IBoms"/> e os anexos do dono <c>ProductionOrder</c>. Nenhum campo de custo é lido. QR pela URL do host do tenant.
/// </summary>
public sealed class ProductionOrderDocumentService(
    FabricationOrderService orders, IProductionSchedule schedule, IOrderMaterial material, IBoms boms, AttachmentService attachments, TenantLinks links,
    PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit, TimeProvider clock)
{
    public const string AuditEntity = "ProductionOrder";
    public const string AttachmentOwner = "ProductionOrder";

    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await orders.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var order = detail.Value;
        var stock = await material.ForOrderAsync(id, cancellationToken);
        if (ProductionOrderPdf.Lines(order, stock) > ProductionOrderPdf.MaxLines)
        {
            return Error.Validation(Error.Codes.Validation, $"A ficha da OS aceita até {ProductionOrderPdf.MaxLines} linhas (etapas, itens de checklist e materiais).",
                new Dictionary<string, string[]> { ["lines"] = [$"A ficha da OS aceita até {ProductionOrderPdf.MaxLines} linhas (etapas, itens de checklist e materiais)."] });
        }

        var forecasts = await schedule.ForOrdersAsync([id], cancellationToken);
        var forecast = forecasts.Count > 0 ? forecasts[0] : null;
        var draft = stock is not { BomReleased: true } && await boms.HasDraftAsync(id, cancellationToken);
        var files = await attachments.ListAsync(AttachmentOwner, id, cancellationToken);
        var model = ProductionOrderPdf.Build(order, forecast, stock, draft, files.IsSuccess ? files.Value : [], links.Absolute, BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new ProductionOrderPdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, id.ToString(), "pdf", 1, cancellationToken);
        return (bytes, order.Number + ".pdf");
    }
}
