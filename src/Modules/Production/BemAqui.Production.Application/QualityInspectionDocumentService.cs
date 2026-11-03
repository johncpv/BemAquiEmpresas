using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// "Baixar PDF" da inspeção de qualidade (EXP-05 CA 9), com <c>production.view</c>: a etapa de qualidade é lida pela OS de fabricação (404 para
/// etapa que não é de qualidade, de instalação ou de outro tenant), a tentativa pela mesma aba Qualidade da OS (<see cref="QualityService.OrderAsync"/>)
/// e as fotos pela NC da tentativa reprovada. Gerado na hora, auditado.
/// </summary>
public sealed class QualityInspectionDocumentService(
    IProductionDb db, FabricationOrderService orders, QualityService quality, NonConformityService nonConformities, AttachmentService attachments, PdfFrameFactory frames,
    IPdfRenderer renderer, IAuditWriter audit, TimeProvider clock)
{
    public const string AuditEntity = "QualityInspection";

    public static Error NotStarted() => Error.Conflict("quality_not_started", "A inspeção desta etapa ainda não começou: a etapa espera as anteriores.");

    /// <param name="attempt">A tentativa (aba Qualidade da OS); sem ela, a mais recente da etapa.</param>
    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid stepId, int? attempt, CancellationToken cancellationToken = default)
    {
        var orderId = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.Id == stepId && s.IsQualityStep).Select(s => (Guid?)s.OrderId).FirstOrDefaultAsync(cancellationToken);
        if (orderId is not { } id)
        {
            return QualityService.StepNotFound();
        }

        var order = await orders.GetAsync(id, cancellationToken);
        if (order.IsFailure)
        {
            return QualityService.StepNotFound();
        }

        var view = await quality.OrderAsync(id, nonConformities, cancellationToken);
        if (view.IsFailure)
        {
            return view.Error!;
        }

        var picked = QualityInspectionPdf.Pick(view.Value.Attempts, stepId, attempt);
        if (picked is null)
        {
            return attempt is null ? NotStarted() : Error.NotFound("quality_attempt_not_found", "Tentativa não encontrada nesta etapa de qualidade.");
        }

        if (picked.Items.Count > QualityInspectionPdf.MaxLines)
        {
            var message = $"A inspeção aceita até {QualityInspectionPdf.MaxLines} itens de checklist no PDF.";
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["lines"] = [message] });
        }

        IReadOnlyList<AttachmentDto> photos = [];
        if (picked.NonConformityId is { } nc)
        {
            var files = await attachments.ListAsync(NonConformityService.AttachmentOwner, nc, cancellationToken);
            photos = files.IsSuccess ? files.Value : [];
        }

        var model = QualityInspectionPdf.Build(order.Value, picked, photos, BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new QualityInspectionPdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, stepId.ToString(), "pdf", model.Items.Count, cancellationToken);
        return (bytes, QualityInspectionPdf.FileName(model.OrderNumber, picked.StepCode, picked.Attempt));
    }
}
