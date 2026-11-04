using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;

namespace BemAqui.Engineering.Application.Documents;

/// <summary>
/// "Baixar PDF" da BOM e do roteiro (EXP-05 CA 3 e 4): gerados na hora no modelo único, pelos mesmos <see cref="BomService"/> e
/// <see cref="RoutingTemplateService"/> da tela (404 para id de outro tenant), sem custo, com o limite de linhas e a auditoria do download.
/// </summary>
public sealed class EngineeringDocumentService(BomService boms, RoutingTemplateService routings, PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit, TimeProvider clock)
{
    public async Task<Result<(byte[] Content, string FileName)>> BomPdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await boms.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var bom = detail.Value;
        if (bom.Lines.Count > BomPdf.MaxLines)
        {
            return TooMany($"A BOM aceita até {BomPdf.MaxLines} linhas no PDF.");
        }

        var view = await boms.GetOrderAsync(bom.ProductionOrderId, cancellationToken);
        if (view.IsFailure)
        {
            return view.Error!;
        }

        var model = BomPdf.Build(bom, view.Value.Order, view.Value.Revisions, BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new BomPdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(nameof(Domain.Bom), id.ToString(), "pdf", model.Lines.Count, cancellationToken);
        return (bytes, BomPdf.FileName(model.OrderNumber, model.Revision));
    }

    public async Task<Result<(byte[] Content, string FileName)>> RoutingPdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await routings.GetAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var template = detail.Value;
        if (RoutingTemplatePdf.Lines(template) > RoutingTemplatePdf.MaxLines)
        {
            return TooMany($"O roteiro aceita até {RoutingTemplatePdf.MaxLines} linhas no PDF (etapas e itens de checklist).");
        }

        var products = await routings.ListItemsAsync(id, cancellationToken);
        var model = RoutingTemplatePdf.Build(template, products.IsSuccess ? products.Value : [], BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new RoutingTemplatePdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(nameof(Domain.RoutingTemplate), id.ToString(), "pdf", model.Steps.Count, cancellationToken);
        return (bytes, RoutingTemplatePdf.FileName(template.Name));
    }

    private static Error TooMany(string message) => Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["lines"] = [message] });
}
