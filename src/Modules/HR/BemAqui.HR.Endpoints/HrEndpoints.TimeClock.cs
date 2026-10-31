using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application.TimeClock;
using BemAqui.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>
    /// "Meu ponto" (RH-01a CA 2 a 10 e 13, ADR 0035): <c>/api/v1/me/hr/time-entries</c> pela camada 1 e pelo vínculo com funcionário ativo
    /// (o afastado não marca), sem ação no grupo; o funcionário vem sempre da sessão. Só "hoje" e "marcar" entram na sessão por PIN
    /// (adendo de 21/10/2026 à ADR 0026); marcar no tablet encerra a sessão por PIN no servidor, na mesma transação.
    /// </summary>
    private static void MapMyTimeClock(RouteGroupBuilder me)
    {
        var entries = me.MapGroup("/time-entries");
        entries.MapGet("/today", async (HttpContext http, TimeClockService service, IShopfloorDevices devices, CancellationToken ct) =>
                Results.Ok(await service.TodayAsync(ActiveEmployeeLink.Employee(http), await OriginAsync(http, devices, ct), ct)))
            .RequireActiveEmployeeLink().AllowPinSession().WithName("GetMyTimeClock").Produces<MyTimeClock>();
        entries.MapPost(string.Empty, async (TimeEntryInput input, HttpContext http, TimeClockService service, IShopfloorDevices devices, ICurrentUser user, CancellationToken ct) =>
            {
                var origin = await OriginAsync(http, devices, ct);
                Func<CancellationToken, Task<bool>>? endPin = origin is { Pin: true, DeviceId: { } deviceId } && user.UserId is { } userId
                    ? async token => await devices.EndPinSessionAsync(userId, deviceId, "time_entry", token) > 0
                    : null;
                var result = await service.RecordAsync(ActiveEmployeeLink.Employee(http), input, origin, endPin, ct);
                if (result.IsSuccess && origin.Pin)
                {
                    // A sessão por PIN acabou no servidor: o tablet volta à lista de nomes para o próximo funcionário.
                    http.Response.Cookies.Delete(SessionCookie.Refresh, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
                }

                return result.ToHttpResult(receipt => Results.Created("/api/v1/me/hr/time-entries/" + receipt.Id, receipt));
            })
            .RequireActiveEmployeeLink().RequireIdempotencyKey().AllowPinSession().WithName("RecordMyTimeEntry").Produces<TimeEntryReceipt>(StatusCodes.Status201Created);
        entries.MapPost("/geo-consent", async (HttpContext http, TimeClockService service, CancellationToken ct) =>
                Results.Ok(await service.AcceptGeoConsentAsync(ActiveEmployeeLink.Employee(http), ct)))
            .RequireActiveEmployeeLink().WithName("AcceptMyGeoConsent").Produces<GeoConsentResult>();
        entries.MapGet(string.Empty, async (HttpContext http, TimeClockService service, CancellationToken ct) =>
                Results.Ok(await service.MineAsync(ActiveEmployeeLink.Employee(http), ct)))
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("ListMyTimeEntries").Produces<IReadOnlyList<MyTimeEntry>>();
    }

    /// <summary>
    /// Consulta do RH (RH-01a CA 11 e 12): marcações com <c>hr.view</c> (sem localização); a localização só pela rota <c>/sensitive</c>, com
    /// <c>hr.view_sensitive</c> e <c>SensitiveRead</c>. Não existe rota para editar nem apagar marcação (CA 14).
    /// </summary>
    private static void MapTimeEntries(RouteGroupBuilder hr, RouteGroupBuilder employees)
    {
        hr.MapGet("/time-entries", async (DateOnly? from, DateOnly? to, Guid? employeeId, Guid? workCenterId, TimeClockService service, CancellationToken ct) =>
                (await service.ListAsync(from, to, employeeId, workCenterId, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("ListTimeEntries").Produces<TimeEntryList>();
        employees.MapGet("/{id:guid}/time-entries", async (Guid id, DateOnly? from, DateOnly? to, TimeClockService service, CancellationToken ct) =>
                (await service.EmployeeAsync(id, from, to, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("ListEmployeeTimeEntries").Produces<TimeEntryList>();
        employees.MapGet("/{id:guid}/time-entries/sensitive", async (Guid id, DateOnly? from, DateOnly? to, TimeClockService service, CancellationToken ct) =>
                (await service.LocationsAsync(id, from, to, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ListEmployeeTimeEntryLocations").Produces<IReadOnlyList<TimeEntryLocationRow>>();
    }

    /// <summary>Aparelho registrado do cookie e se a sessão é por PIN (claim <c>amr</c>); a sessão por PIN sempre tem o aparelho.</summary>
    private static async Task<TimeClockOrigin> OriginAsync(HttpContext http, IShopfloorDevices devices, CancellationToken ct)
    {
        var pin = http.User.FindAll(AuthMethods.Claim).Any(c => c.Value == AuthMethods.Pin);
        var device = await devices.ResolveAsync(http.Request.Cookies[ShopfloorDeviceCookie.Name], ct);
        return new TimeClockOrigin(device?.Id, pin && device is not null);
    }
}
