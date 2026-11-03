using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;

namespace BemAqui.Assets.Application;

/// <summary>
/// "Baixar PDF" e "Imprimir" da OS de manutenção (EXP-05 CA 8): gerado na hora pelo mesmo <see cref="MaintenanceService.GetOrderAsync"/> da tela
/// (404 para OS de outro tenant), com o local do bem, a última leitura do horímetro (plano por horas) e, só com <c>assets.edit</c> (a mesma
/// regra da aba Custo), o valor combinado e o custo por natureza. Sem <c>hr.view_sensitive</c>, o PDF não traz nome nem custo-hora de
/// funcionário: só os totais por natureza. Cada download entra na auditoria.
/// </summary>
public sealed class MaintenanceOrderDocumentService(
    MaintenanceService maintenance, MaintenanceCostService costs, AssetService assets, IPermissionChecker permissions, PdfFrameFactory frames, IPdfRenderer renderer,
    IAuditWriter audit, TimeProvider clock)
{
    public const string AuditEntity = "MaintenanceOrder";

    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await maintenance.GetOrderAsync(id, cancellationToken);
        if (detail.IsFailure)
        {
            return detail.Error!;
        }

        var o = detail.Value;
        if (o.Checklist.Count > MaintenanceOrderPdf.MaxLines)
        {
            var message = $"O PDF da OS de manutenção aceita até {MaintenanceOrderPdf.MaxLines} itens de checklist.";
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { ["lines"] = [message] });
        }

        var asset = await assets.GetAsync(o.AssetId, cancellationToken);
        string? meter = null;
        if (o.MeterPlan && await maintenance.ReadingsAsync(o.AssetId, cancellationToken) is { IsSuccess: true, Value: [var last, ..] })
        {
            meter = $"{PdfTheme.Quantity(last.Value)} h em {PdfTheme.Date(last.ReadOn)}";
        }

        IReadOnlyList<MaintenancePdfCost>? costLines = null;
        decimal? costTotal = null;
        var canEdit = await permissions.HasPermissionAsync("assets", "edit", cancellationToken);
        if (canEdit)
        {
            costLines = [];
            if (o.Status == MaintenanceOrderStatus.Done && await costs.ListAsync(id, cancellationToken) is { IsSuccess: true } view)
            {
                costLines = view.Value.Totals.Where(t => t.Amount != 0).Select(t => new MaintenancePdfCost(MaintenanceOrderPdf.CostLabel(t.CostType), t.Amount)).ToList();
                costTotal = view.Value.Total;
            }
        }

        var model = new MaintenanceOrderPdfModel(
            o.Number, o.Status, o.Type, o.AssetTag, o.AssetName, asset.IsSuccess ? asset.Value.SiteName : null, o.WorkCenterName, o.PlanDescription, o.ScheduledOn,
            o.SupplierName ?? MaintenanceOrderPdf.InternalTeam, o.Problem, o.MachineStopped, meter,
            o.StartedAt is { } started ? Who(started, o.StartedByName) : null, o.DoneAt is { } done ? Who(done, o.DoneByName) : null, o.WorkDone,
            o.CancelledAt is { } cancelled ? PdfTheme.DateTime(cancelled) : null, o.CancelReason,
            o.Checklist.OrderBy(i => i.Sequence).Select(i => new MaintenancePdfCheck(i.Sequence, i.Text, i.Result, i.MarkedByName)).ToList(),
            canEdit ? o.ContractValue : null, costLines, costTotal);
        var frame = await frames.CreateAsync(cancellationToken);
        var bytes = await renderer.RenderAsync(new MaintenanceOrderPdfDocument(frame, model, BusinessTime.Today(clock)), cancellationToken);
        await audit.RecordExportAsync(AuditEntity, id.ToString(), "pdf", 1, cancellationToken);
        return (bytes, o.Number + ".pdf");
    }

    private static string Who(DateTimeOffset at, string? name) => PdfTheme.DateTime(at) + (name is { Length: > 0 } n ? " por " + n : string.Empty);
}
