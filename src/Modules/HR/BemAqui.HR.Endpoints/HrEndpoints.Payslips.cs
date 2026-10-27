using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application.Documents;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.HR.Endpoints;

/// <summary>
/// Marca do filtro de vínculo (ADR 0035 §1): o endpoint de autoatendimento do RH resolve o funcionário pela sessão
/// (<see cref="IUserEmployeeLink"/>) e confere a situação (<see cref="IEmployees"/>). <c>AllowOnLeave</c>: o afastado também entra
/// ("Meus holerites", porque continua recebendo holerite).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class ActiveEmployeeLinkAttribute(bool allowOnLeave) : Attribute
{
    public bool AllowOnLeave { get; } = allowOnLeave;
}

/// <summary>
/// Filtro <c>RequireActiveEmployeeLink</c> do HR (RH-02b CA 6, ADR 0035), reusado pela RH-01a ("Meu ponto") e pela RH-01c ("Meu espelho"):
/// o funcionário vem sempre da sessão; a query não aceita <c>employeeId</c> (400); sem vínculo, desligado ou (sem <c>allowOnLeave</c>)
/// afastado, 403 <c>employee_not_active</c>.
/// </summary>
public static class ActiveEmployeeLink
{
    private const string ItemKey = "bq.hr.self-employee";

    public static RouteHandlerBuilder RequireActiveEmployeeLink(this RouteHandlerBuilder builder, bool allowOnLeave = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RequireSelfService("hr").WithMetadata(new ActiveEmployeeLinkAttribute(allowOnLeave)).AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.Request.Query.Keys.Any(k => string.Equals(k, "employeeId", StringComparison.OrdinalIgnoreCase)))
            {
                return Error.Validation(Error.Codes.Validation, "O funcionário vem do seu login: não informe employeeId.",
                    new Dictionary<string, string[]> { ["employeeId"] = ["O funcionário vem do seu login: não informe employeeId."] }).ToProblem();
            }

            var services = http.RequestServices;
            var userId = services.GetRequiredService<ICurrentUser>().UserId;
            var employeeId = userId is { } u ? await services.GetRequiredService<IUserEmployeeLink>().EmployeeOfUserAsync(u, http.RequestAborted) : null;
            var found = employeeId is { } e ? await services.GetRequiredService<IEmployees>().FindAsync([e], http.RequestAborted) : [];
            var employee = found.Count > 0 ? found[0] : null;
            if (employee is null || employee.Status == EmployeeSituation.Terminated || (employee.Status == EmployeeSituation.OnLeave && !allowOnLeave))
            {
                return Error.Forbidden(PayslipDocumentRules.EmployeeNotActiveCode, PayslipDocumentRules.EmployeeNotActiveMessage).ToProblem();
            }

            http.Items[ItemKey] = employee;
            return await next(context);
        });
    }

    /// <summary>Funcionário da sessão, já conferido pelo filtro.</summary>
    public static EmployeeSummary Employee(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items[ItemKey] as EmployeeSummary ?? throw new InvalidOperationException("Endpoint de autoatendimento sem RequireActiveEmployeeLink.");
    }
}

internal static partial class HrEndpoints
{
    /// <summary>
    /// Holerite em PDF e aviso pelo RH (RH-02b CA 1 a 4 e 7 a 11): baixar com <c>hr.view_sensitive</c>; avisar com <c>hr.edit</c> na rota e
    /// <c>hr.view_sensitive</c> no serviço, com <c>Idempotency-Key</c>. Holerite precisa ser da folha da rota (IDOR, 404).
    /// </summary>
    private static void MapPayslipDocuments(RouteGroupBuilder runs)
    {
        runs.MapGet("/{id:guid}/payslips/{payslipId:guid}/pdf", async (Guid id, Guid payslipId, PayslipDocumentService service, HttpContext http, CancellationToken ct) =>
                File(http, await service.PdfAsync(id, payslipId, ct)))
            .RequirePermission("hr.view_sensitive").WithName("DownloadPayslipPdf").Produces(StatusCodes.Status200OK, contentType: PayslipDocumentService.PdfContentType);
        runs.MapPost("/{id:guid}/pdfs", async (Guid id, PayslipDocumentService service, CancellationToken ct) => (await service.StartBatchAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GeneratePayslipBatch").Produces<PayslipBatchResult>();
        runs.MapGet("/{id:guid}/pdfs/{attachmentId:guid}", async (Guid id, Guid attachmentId, PayslipDocumentService service, HttpContext http, CancellationToken ct) =>
                File(http, await service.BatchFileAsync(id, attachmentId, ct)))
            .RequirePermission("hr.view_sensitive").WithName("DownloadPayslipBatch").Produces(StatusCodes.Status200OK, contentType: PayslipDocumentService.ZipContentType);
        runs.MapGet("/{id:guid}/notices/sensitive", async (Guid id, PayslipNoticeService service, CancellationToken ct) => (await service.NoticesAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPayslipNoticesSensitive").Produces<PayslipNotices>();
        runs.MapPost("/{id:guid}/notify", async (Guid id, PayslipNotifyInput input, PayslipNoticeService service, CancellationToken ct) => (await service.NotifyAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").RequireIdempotencyKey().WithName("NotifyPayslips").Produces<PayslipNotifyResult>();
        runs.MapPost("/{id:guid}/payslips/{payslipId:guid}/notify", async (Guid id, Guid payslipId, PayslipNoticeService service, CancellationToken ct) => (await service.NotifyOneAsync(id, payslipId, ct)).ToHttpResult())
            .RequirePermission("hr.edit").RequireIdempotencyKey().WithName("NotifyPayslipAgain").Produces<PayslipNotifyResult>();
    }

    /// <summary>
    /// "Meus holerites" (RH-02b CA 5 e 6, ADR 0035): <c>/api/v1/me/hr/*</c> pela camada 1 (<c>RequireModule("hr")</c>) e pelo vínculo com
    /// funcionário ativo ou afastado, sem ação no grupo; fora da sessão por PIN (sem <c>[PinSessionAllowed]</c>) e sempre <c>no-store</c>.
    /// </summary>
    private static void MapMyPayslips(RouteGroupBuilder me)
    {
        me.MapGet("/link", (HttpContext http) =>
            {
                var employee = ActiveEmployeeLink.Employee(http);
                return Results.Ok(new MyEmployeeLink(employee.Name, employee.Status.ToString()));
            })
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("GetMyEmployeeLink").Produces<MyEmployeeLink>();
        me.MapGet("/payslips", async (HttpContext http, PayslipDocumentService service, CancellationToken ct) => Results.Ok(await service.MineAsync(ActiveEmployeeLink.Employee(http).Id, ct)))
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("ListMyPayslips").Produces<IReadOnlyList<MyPayslip>>();
        me.MapGet("/payslips/{id:guid}/pdf", async (Guid id, HttpContext http, PayslipDocumentService service, CancellationToken ct) =>
                File(http, await service.MinePdfAsync(ActiveEmployeeLink.Employee(http).Id, id, ct)))
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("DownloadMyPayslipPdf").Produces(StatusCodes.Status200OK, contentType: PayslipDocumentService.PdfContentType);
    }

    private static IResult File(HttpContext http, Result<PayslipFile> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
