using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// "Baixar PDF" da NC (EXP-04 CA 14): o detalhe é o mesmo <see cref="NonConformityService.GetAsync"/> da tela, que já zera o custo sem
/// <c>projects.view_cost</c>; o checklist é o da tentativa reprovada; os anexos (dono <c>NonConformity</c>) são só listados.
/// </summary>
public sealed class NonConformityDocumentService(
    NonConformityService service, IProductionDb db, AttachmentService attachments, PdfFrameFactory frames, IPdfRenderer renderer, IAuditWriter audit, TimeProvider clock)
{
    public const string AuditEntity = "NonConformity";

    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await service.GetAsync(id, null, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var nc = detail.Value;
        var attempt = nc.Attempt;
        var checklist = await db.ProductionOrderChecklist.AsNoTracking().Where(c => c.StepId == nc.StepId && c.Attempt == attempt).OrderBy(c => c.Sequence)
            .Select(c => new { c.Text, c.Result }).ToListAsync(cancellationToken);
        var files = await attachments.ListAsync(NonConformityService.AttachmentOwner, id, cancellationToken);
        List<NonConformityPdfFile> listed = files.IsSuccess ? files.Value.Where(a => a.ScanStatus is ScanStatus.Clean or ScanStatus.Pending).Select(a => new NonConformityPdfFile(a.FileName, a.CreatedAt)).ToList() : [];
        if (checklist.Count + listed.Count > NonConformityPdf.MaxLines)
        {
            var message = $"O PDF da não conformidade aceita até {NonConformityPdf.MaxLines} linhas (checklist e anexos).";
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["lines"] = [message] });
        }

        var model = new NonConformityPdfModel(
            nc.Number, nc.Status, NonConformityPdf.StatusLabel(nc.Status), nc.OrderNumber, nc.ProjectNumber, nc.ProjectName, nc.PartyName,
            nc.StepCode is { Length: > 0 } code ? $"{code} · {nc.StepName}" : nc.StepName, nc.Attempt, nc.AgeDays, nc.ResponsibleName, nc.ReturnToStepName, nc.Description, nc.Cause,
            nc.Action, nc.Result, nc.OpenedByName, nc.OpenedAt, nc.ClosedByName, nc.ClosedAt,
            checklist.Select(c => new NonConformityPdfCheck(c.Text, InstallationPdf.ResultLabel(c.Result))).ToList(), listed, NonConformityPdf.Cost(nc.Cost, nc.CostVisible), nc.CostVisible,
            BusinessTime.Today(clock));
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new NonConformityPdfDocument(frame, model), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, id.ToString(), "pdf", 1, cancellationToken);
        return (bytes, nc.Number + ".pdf");
    }
}
