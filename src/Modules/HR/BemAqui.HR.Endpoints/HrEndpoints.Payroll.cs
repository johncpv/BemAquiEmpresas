using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>Rubricas (RH-02a CA 1 e 2): ler com <c>hr.view</c>; criar, editar, inativar, excluir e conferir com <c>hr.edit</c>.</summary>
    private static void MapPayrollItems(RouteGroupBuilder items)
    {
        items.MapGet(string.Empty, async (PayrollItemService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("hr.view").WithName("ListPayrollItems").Produces<IReadOnlyList<PayrollItemDetail>>();
        items.MapPost(string.Empty, async (PayrollItemInput input, PayrollItemService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/payroll-items/" + saved.Id, saved)))
            .RequirePermission("hr.edit").WithName("CreatePayrollItem").Produces<PayrollItemDetail>(StatusCodes.Status201Created);
        items.MapPut("/{id:guid}", async (Guid id, PayrollItemInput input, PayrollItemService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdatePayrollItem").Produces<PayrollItemDetail>();
        items.MapPost("/{id:guid}/inactivate", async (Guid id, PayrollItemService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("InactivatePayrollItem").Produces<PayrollItemDetail>();
        items.MapPost("/{id:guid}/reactivate", async (Guid id, PayrollItemService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ReactivatePayrollItem").Produces<PayrollItemDetail>();
        items.MapPost("/{id:guid}/confirm", async (Guid id, PayrollItemService service, CancellationToken ct) => (await service.ConfirmAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ConfirmPayrollItem").Produces<PayrollItemDetail>();
        items.MapDelete("/{id:guid}", async (Guid id, PayrollItemService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("DeletePayrollItem");
    }

    /// <summary>Parâmetros do RH (CA 3, ADR 0016 M6): ler com <c>hr.view</c>; editar e "Marcar como conferido" com <c>settings.edit</c>, conferido no serviço (a rota fica no grupo do RH com <c>hr.view</c>, como Compras e o DRE).</summary>
    private static void MapHrParameters(RouteGroupBuilder parameters)
    {
        parameters.MapGet(string.Empty, async (HrParametersService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequirePermission("hr.view").WithName("GetHrParameters").Produces<HrParametersView>();
        parameters.MapPut(string.Empty, async (HrParametersInput input, HrParametersService service, PayrollClosingService closing, CancellationToken ct) =>
            {
                var before = await service.ForecastMonthsAsync(ct);
                var saved = await service.UpdateAsync(input, ct);
                // Mudou os meses da folha prevista (RH-02c CA 14): a previsão é refeita.
                if (saved.IsSuccess && await service.ForecastMonthsAsync(ct) != before)
                {
                    var refreshed = await closing.RecalculateForecastAsync(ct);
                    if (refreshed.IsFailure)
                    {
                        return refreshed.Error!.ToProblem();
                    }
                }

                return saved.ToHttpResult();
            })
            .RequirePermission("hr.view").WithName("UpdateHrParameters").Produces<HrParametersView>();
        parameters.MapPost("/confirm", async (HrParameterConfirmInput input, HrParametersService service, CancellationToken ct) => (await service.ConfirmAsync(input.Key, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("ConfirmHrParameter").Produces<HrParametersView>();
        parameters.MapGet("/payees", async (string? q, HrParametersService service, CancellationToken ct) => Results.Ok(await service.PayeesAsync(q, ct)))
            .RequirePermission("hr.view").WithName("SearchPayrollPayees").Produces<IReadOnlyList<PayrollPayeeOption>>();
    }

    /// <summary>
    /// Folha lançada (CA 4 a 16): o resumo com <c>hr.view</c>; valores só pelas rotas <c>/sensitive</c> (<c>hr.view_sensitive</c>, com
    /// <c>SensitiveRead</c>); criar com <c>hr.create</c> e editar, importar e as guias com <c>hr.edit</c>, os dois com
    /// <c>hr.view_sensitive</c> conferido no serviço. Holerite e guia precisam ser da folha da rota (IDOR, 404).
    /// </summary>
    private static void MapPayrollRuns(RouteGroupBuilder runs)
    {
        runs.MapGet(string.Empty, async (PayrollService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("hr.view").WithName("ListPayrollRuns").Produces<IReadOnlyList<PayrollRunSummary>>();
        runs.MapGet("/sensitive", async (PayrollService service, CancellationToken ct) => Results.Ok(await service.TotalsAsync(ct)))
            .RequirePermission("hr.view_sensitive").WithName("ListPayrollRunTotalsSensitive").Produces<IReadOnlyList<PayrollRunTotals>>();
        runs.MapGet("/eligible", async (string? period, PayrollService service, CancellationToken ct) => (await service.EligibleAsync(period, ct)).ToHttpResult())
            .RequirePermission("hr.create").WithName("ListPayrollEligibleEmployees").Produces<IReadOnlyList<PayrollEligibleEmployee>>();
        runs.MapPost(string.Empty, async (PayrollRunCreateInput input, PayrollService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/payroll-runs/" + saved.Id, saved)))
            .RequirePermission("hr.create").RequireIdempotencyKey().WithName("CreatePayrollRun").Produces<PayrollRunSummary>(StatusCodes.Status201Created);
        runs.MapGet("/{id:guid}", async (Guid id, PayrollService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetPayrollRun").Produces<PayrollRunSummary>();

        runs.MapGet("/{id:guid}/payslips/sensitive", async (Guid id, PayrollService service, CancellationToken ct) => (await service.PayslipsAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ListPayslipsSensitive").Produces<IReadOnlyList<PayslipRow>>();
        runs.MapGet("/{id:guid}/payslips/{payslipId:guid}/sensitive", async (Guid id, Guid payslipId, PayrollService service, CancellationToken ct) => (await service.PayslipAsync(id, payslipId, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPayslipSensitive").Produces<PayslipDetail>();
        runs.MapGet("/{id:guid}/payslips/{payslipId:guid}/previous/sensitive", async (Guid id, Guid payslipId, PayrollService service, CancellationToken ct) => (await service.PreviousAsync(id, payslipId, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPreviousPayslipSensitive").Produces<PayslipPrevious>();
        runs.MapPut("/{id:guid}/payslips/{payslipId:guid}", async (Guid id, Guid payslipId, PayslipInput input, PayrollService service, CancellationToken ct) => (await service.SavePayslipAsync(id, payslipId, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("SavePayslip").Produces<PayslipDetail>();

        runs.MapGet("/{id:guid}/charges/sensitive", async (Guid id, PayrollService service, CancellationToken ct) => (await service.ChargesAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ListPayrollChargesSensitive").Produces<PayrollChargesView>();
        runs.MapPost("/{id:guid}/charges", async (Guid id, PayrollChargeInput input, PayrollService service, CancellationToken ct) => (await service.AddChargeAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("AddPayrollCharge").Produces<PayrollChargesView>();
        runs.MapPut("/{id:guid}/charges/{chargeId:guid}", async (Guid id, Guid chargeId, PayrollChargeInput input, PayrollService service, CancellationToken ct) => (await service.UpdateChargeAsync(id, chargeId, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdatePayrollCharge").Produces<PayrollChargesView>();
        runs.MapDelete("/{id:guid}/charges/{chargeId:guid}", async (Guid id, Guid chargeId, PayrollService service, CancellationToken ct) => (await service.DeleteChargeAsync(id, chargeId, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("DeletePayrollCharge").Produces<PayrollChargesView>();

        runs.MapPost("/{id:guid}/import/preview", async (Guid id, IFormFile file, PayrollService service, CancellationToken ct) =>
                await ReadCsvAsync(file, ct) is { } content ? (await service.PreviewImportAsync(id, content, ct)).ToHttpResult() : TooLarge())
            .RequirePermission("hr.edit").DisableAntiforgery().WithName("PreviewPayrollImport").Produces<PayrollImportPreview>();
        runs.MapPost("/{id:guid}/import", async (Guid id, IFormFile file, PayrollService service, CancellationToken ct) =>
                await ReadCsvAsync(file, ct) is { } content ? (await service.ImportAsync(id, content, ct)).ToHttpResult() : TooLarge())
            .RequirePermission("hr.edit").DisableAntiforgery().WithName("ImportPayrollEntries").Produces<PayrollImportResult>();

        runs.MapGet("/{id:guid}/review/sensitive", async (Guid id, PayrollService service, CancellationToken ct) => (await service.ReviewAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPayrollReviewSensitive").Produces<PayrollReview>();

        // RH-02c: fechar e reabrir com hr.approve (hr.view_sensitive conferido no serviço), fechar com 2FA de novo e Idempotency-Key; a
        // prévia e a aba Pagamentos têm valores e saem só por /sensitive; "Recalcular previsão" com hr.edit.
        runs.MapGet("/{id:guid}/close/sensitive", async (Guid id, PayrollClosingService service, CancellationToken ct) => (await service.PreviewAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPayrollClosePreviewSensitive").Produces<PayrollClosePreview>();
        runs.MapPost("/{id:guid}/close", async (Guid id, PayrollCloseInput input, PayrollClosingService service, CancellationToken ct) => (await service.CloseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.approve").RequireIdempotencyKey().WithName("ClosePayrollRun").Produces<PayrollCloseResult>();
        runs.MapPost("/{id:guid}/reopen", async (Guid id, PayrollReopenInput input, PayrollClosingService service, CancellationToken ct) => (await service.ReopenAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.approve").WithName("ReopenPayrollRun").Produces<PayrollRunSummary>();
        runs.MapGet("/{id:guid}/payments/sensitive", async (Guid id, PayrollClosingService service, CancellationToken ct) => (await service.PaymentsAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPayrollPaymentsSensitive").Produces<PayrollPayments>();
        runs.MapPost("/forecast/recalculate", async (PayrollClosingService service, CancellationToken ct) => (await service.RecalculateForecastAsync(ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("RecalculatePayrollForecast").Produces<PayrollForecastResult>();

        // FLH-02a: folha calculada. Calcular com hr.create e recalcular com hr.edit (os dois com hr.view_sensitive no serviço); a situação e o
        // histórico sem valor com hr.view; a diferença e os totais do histórico só por /sensitive; a retificação e os avulsos com hr.edit.
        runs.MapGet("/suggested-period", async (PayrollCalculationService service, CancellationToken ct) => Results.Ok(await service.SuggestedPeriodAsync(ct)))
            .RequirePermission("hr.create").WithName("GetPayrollSuggestedPeriod").Produces<PayrollSuggestedPeriod>();
        runs.MapPost("/{id:guid}/calculate", async (Guid id, PayrollCalculationService service, CancellationToken ct) => (await service.CalculateAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.create").WithName("CalculatePayrollRun").Produces<PayrollCalculationStart>();
        runs.MapPost("/{id:guid}/recalculate", async (Guid id, PayrollCalculationService service, CancellationToken ct) => (await service.CalculateAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("RecalculatePayrollRun").Produces<PayrollCalculationStart>();
        runs.MapGet("/{id:guid}/calculation", async (Guid id, PayrollCalculationService service, CancellationToken ct) => (await service.StatusAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetPayrollCalculationStatus").Produces<PayrollCalculationStatus>();
        runs.MapGet("/{id:guid}/calculations", async (Guid id, PayrollCalculationService service, CancellationToken ct) => (await service.HistoryAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("ListPayrollCalculations").Produces<IReadOnlyList<PayrollCalculationEntry>>();
        runs.MapGet("/{id:guid}/calculations/sensitive", async (Guid id, PayrollCalculationService service, CancellationToken ct) => (await service.HistoryTotalsAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ListPayrollCalculationTotalsSensitive").Produces<IReadOnlyList<PayrollCalculationTotals>>();
        runs.MapGet("/{id:guid}/difference/sensitive", async (Guid id, PayrollCalculationService service, CancellationToken ct) => (await service.DifferenceAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPayrollDifferenceSensitive").Produces<PayrollRunDifference>();
        runs.MapPost("/{id:guid}/rectify", async (Guid id, PayrollCalculationService service, CancellationToken ct) =>
                (await service.RectifyAsync(id, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/payroll-runs/" + saved.Id, saved)))
            .RequirePermission("hr.edit").RequireIdempotencyKey().WithName("RectifyPayrollRun").Produces<PayrollRunSummary>(StatusCodes.Status201Created);
        runs.MapPost("/{id:guid}/payslips/{payslipId:guid}/manual-lines", async (Guid id, Guid payslipId, PayrollManualLineInput input, PayrollCalculationService service, CancellationToken ct) =>
                (await service.AddManualLineAsync(id, payslipId, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("AddPayslipManualLine").Produces<PayslipDetail>();
        runs.MapDelete("/{id:guid}/payslips/{payslipId:guid}/manual-lines/{lineId:guid}", async (Guid id, Guid payslipId, Guid lineId, PayrollCalculationService service, CancellationToken ct) =>
                (await service.RemoveManualLineAsync(id, payslipId, lineId, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("RemovePayslipManualLine").Produces<PayslipDetail>();

        // FLH-02b: banco de horas na folha calculada. A seção só por /sensitive; "Pagar" e "Descontar" com hr.edit (e hr.view_sensitive no serviço).
        runs.MapGet("/{id:guid}/hour-bank/sensitive", async (Guid id, PayrollCalculationService service, CancellationToken ct) => (await service.HourBankAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPayrollHourBankSensitive").Produces<PayrollHourBank>();
        runs.MapPut("/{id:guid}/payslips/{payslipId:guid}/hour-bank", async (Guid id, Guid payslipId, PayrollHourBankInput input, PayrollCalculationService service, CancellationToken ct) =>
                (await service.SetHourBankAsync(id, payslipId, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("SetPayslipHourBank").Produces<PayslipDetail>();
        runs.MapDelete("/{id:guid}/payslips/{payslipId:guid}/hour-bank", async (Guid id, Guid payslipId, PayrollCalculationService service, CancellationToken ct) =>
                (await service.ClearHourBankAsync(id, payslipId, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ClearPayslipHourBank").Produces<PayslipDetail>();
    }

    /// <summary>
    /// Lê o arquivo em memória até 2 MB (CA 10): passou, null (o arquivo nunca vai para disco nem fica guardado). O conteúdo nunca é
    /// executado; só o leitor <see cref="PayrollCsv"/> o interpreta como texto.
    /// </summary>
    private static async Task<byte[]?> ReadCsvAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length > PayrollCsv.MaxBytes)
        {
            return null;
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > PayrollCsv.MaxBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }

    private static IResult TooLarge() =>
        Error.Validation("payroll_import_too_large", "O arquivo passa de 2 MB. Divida em partes menores.").ToProblem();
}
