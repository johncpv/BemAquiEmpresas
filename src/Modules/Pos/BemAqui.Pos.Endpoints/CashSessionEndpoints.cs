using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Pos.Endpoints;

/// <summary>CX-02 CA 7 e 10: aprovar ou reabrir com o motivo; <c>Version</c> é a da sessão lida.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CashSessionDecisionRequest(string Reason, uint Version);

/// <summary>CX-02 CA 13: "Conferido" (<c>Found</c>) ou "Não localizado" (<c>NotFound</c>) num Pix atestado pelo operador.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CashPixCheckRequest(Guid PaymentId, PixCheckStatus Status);

/// <summary>
/// Loja → Caixas (CX-02): as sessões de caixa são do <c>Pos</c>; as rotas de retaguarda são autorizadas com <c>sales.*</c> (02, seção 4.1). Ver:
/// <c>sales.view</c> (o esperado e a diferença só saem para <c>sales.approve</c>, conferido no serviço); aprovar, reabrir e conferir Pix:
/// <c>sales.approve</c>. A contagem vem do aparelho pela subida de eventos (<c>/pos/sync/events</c>), nunca por aqui.
/// </summary>
internal static class CashSessionEndpoints
{
    public static IEndpointRouteBuilder MapCashSessionEndpoints(this IEndpointRouteBuilder api)
    {
        var sessions = api.MapGroup("/pos/cash-sessions").WithTags("Loja: caixas").RequireModule("sales");
        sessions.MapGet(string.Empty, async (Guid? storeId, Guid? registerId, Guid? operatorId, CashSessionStatus? status, DateOnly? from, DateOnly? to,
                CashSessionQueries service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new CashSessionFilter(storeId, registerId, operatorId, status, from, to), ct)))
            .RequirePermission("sales.view").WithName("ListCashSessions").Produces<CashSessionList>();
        sessions.MapGet("/export", async (Guid? storeId, Guid? registerId, Guid? operatorId, CashSessionStatus? status, DateOnly? from, DateOnly? to,
                CashSessionQueries service, CancellationToken ct) =>
            {
                var file = await service.ExportAsync(new CashSessionFilter(storeId, registerId, operatorId, status, from, to), ct);
                return Results.File(file.Content, SafeCsv.ContentType, file.FileName);
            })
            .RequirePermission("sales.view").WithName("ExportCashSessions");
        sessions.MapGet("/{id:guid}", async (Guid id, CashSessionQueries service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("sales.view").WithName("GetCashSession").Produces<CashSessionDetail>();
        sessions.MapPost("/{id:guid}/approve", async (Guid id, CashSessionDecisionRequest input, CashSessionQueries service, CancellationToken ct) =>
                (await service.ApproveAsync(id, new CashSessionDecisionInput(input.Reason, input.Version), ct)).ToHttpResult())
            .RequirePermission("sales.approve").WithName("ApproveCashSession").Produces<CashSessionDetail>();
        sessions.MapPost("/{id:guid}/reopen", async (Guid id, CashSessionDecisionRequest input, CashSessionQueries service, CancellationToken ct) =>
                (await service.ReopenAsync(id, new CashSessionDecisionInput(input.Reason, input.Version), ct)).ToHttpResult())
            .RequirePermission("sales.approve").WithName("ReopenCashSession").Produces<CashSessionDetail>();
        sessions.MapPost("/{id:guid}/pix-check", async (Guid id, CashPixCheckRequest input, CashSessionQueries service, CancellationToken ct) =>
                (await service.CheckPixAsync(id, new CashPixCheckInput(input.PaymentId, input.Status), ct)).ToHttpResult())
            .RequirePermission("sales.approve").WithName("CheckCashSessionPix").Produces<CashSessionDetail>();
        // CA 9: a via do operador (sem esperado nem diferença) para quem vê; a do gerente (copy=manager) só para sales.approve.
        sessions.MapGet("/{id:guid}/report.pdf", async (Guid id, string? copy, CashSessionReportService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.PdfAsync(id, string.Equals(copy, "manager", StringComparison.Ordinal), ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, "application/pdf", result.Value.FileName);
            })
            .RequirePermission("sales.view").WithName("DownloadCashSessionReport");
        return api;
    }
}
