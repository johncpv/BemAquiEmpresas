using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

/// <summary>
/// Dados do Painel da TV (PNL-01b, ADR 0030 §5) no módulo <c>kiosk</c>: só GET e só o principal de quiosque (policy <c>kiosk</c>). A
/// linha e as visões vêm sempre do escopo da sessão (do banco do tenant), nunca do cliente; visão fora do escopo e OS que não existe, é
/// de outro tenant ou não é da linha respondem o mesmo 404, sem dado nenhum (CA 13 e 14).
/// </summary>
internal static class KioskPanelEndpoints
{
    public const string ViewBoard = "board";
    public const string ViewQueue = "queue";
    public const string ViewLate = "late";

    public static IEndpointRouteBuilder MapKioskPanelEndpoints(this IEndpointRouteBuilder api)
    {
        var kiosk = api.MapGroup("/kiosk").WithTags("Painel da TV").RequireModule("kiosk");
        kiosk.MapGet("/board", async (HttpContext http, IKioskPanel panel, CancellationToken ct) =>
                Scope(http, ViewBoard) is KioskSessionInfo s ? Results.Ok(await panel.BoardAsync(s.Line, ct)) : NotFound())
            .RequireKiosk().WithName("GetKioskBoard").Produces<KioskBoard>();
        kiosk.MapGet("/board/orders/{id:guid}", async (Guid id, HttpContext http, IKioskPanel panel, CancellationToken ct) =>
                Scope(http, ViewBoard) is KioskSessionInfo s && await panel.CardAsync(id, s.Line, ct) is KioskCard card ? Results.Ok(card) : NotFound())
            .RequireKiosk().WithName("GetKioskBoardOrder").Produces<KioskCard>();
        kiosk.MapGet("/queue", async (HttpContext http, IKioskPanel panel, CancellationToken ct) =>
                Scope(http, ViewQueue) is KioskSessionInfo s ? Results.Ok(await panel.QueueAsync(s.Line, ct)) : NotFound())
            .RequireKiosk().WithName("GetKioskQueue").Produces<KioskQueueView>();
        kiosk.MapGet("/delays", async (HttpContext http, IKioskPanel panel, CancellationToken ct) =>
                Scope(http, ViewLate) is KioskSessionInfo s ? Results.Ok(await panel.DelaysAsync(s.Line, ct)) : NotFound())
            .RequireKiosk().WithName("GetKioskDelays").Produces<KioskDelays>();
        return api;
    }

    /// <summary>Escopo da sessão de quiosque, se a visão pedida está entre as da tela.</summary>
    private static KioskSessionInfo? Scope(HttpContext http, string view)
    {
        var session = KioskPrincipal.From(http.User);
        http.Response.Headers.CacheControl = "no-store";
        return session.Views.Contains(view, StringComparer.Ordinal) ? session : null;
    }

    /// <summary>404 único (CA 14): não diz se a OS não existe, é de outro tenant ou de outra linha.</summary>
    public static IResult NotFound() =>
        Results.Problem(detail: "Não encontrado neste painel.", statusCode: StatusCodes.Status404NotFound, title: "Não encontrado",
            extensions: new Dictionary<string, object?> { ["code"] = "not_found" });
}
