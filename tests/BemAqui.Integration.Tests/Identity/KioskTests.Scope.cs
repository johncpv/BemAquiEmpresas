using System.Net;
using BemAqui.BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>PNL-01a CA 9, 14, 15 e 20: o principal de quiosque só alcança o GET do painel; revogação, suspensão e módulo derrubam a TV.</summary>
public partial class KioskTests
{
    private List<RouteEndpoint> Endpoints() =>
        Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static string UrlOf(RouteEndpoint endpoint) =>
        string.Concat(endpoint.RoutePattern.PathSegments.Select(s => "/" + string.Concat(s.Parts.Select(p => p switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternParameterPart parameter => parameter.ParameterPolicies.Any(x => x.Content == "int") ? "2026" : Guid.Empty.ToString(),
            _ => string.Empty,
        }))));

    /// <summary>
    /// CA 9 (teste parametrizado sobre o app inteiro): a sessão de quiosque chama cada endpoint que exige usuário (todos os que não são
    /// anônimos nem do painel, inclusive a negociação dos outros hubs) e recebe 403 <c>kiosk_scope</c>, antes de qualquer regra.
    /// </summary>
    [Fact]
    public async Task Sessao_de_quiosque_nao_alcanca_nenhum_endpoint_fora_do_painel()
    {
        var (_, cookie, _) = await PairedAsync();
        var outside = Endpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && e.Metadata.GetMetadata<KioskEndpointAttribute>() is null)
            .Where(e => (e.RoutePattern.RawText!.StartsWith("/api/v1/", StringComparison.Ordinal) && !e.RoutePattern.RawText.StartsWith("/api/v1/platform", StringComparison.Ordinal))
                || e.RoutePattern.RawText.StartsWith("/hubs/", StringComparison.Ordinal))
            .Where(e => !e.RoutePattern.RawText!.StartsWith("/api/v1/kiosk/", StringComparison.Ordinal))
            .ToList();
        outside.Should().HaveCountGreaterThan(100);
        var failures = new List<string>();
        foreach (var endpoint in outside)
        {
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods is { Count: > 0 } methods ? methods[0] : "GET";
            var response = await Client().SendAsync(KioskRequest(new HttpMethod(method), UrlOf(endpoint), cookie, body: method is "GET" or "DELETE" ? null : new { }));
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.Forbidden || !body.Contains("\"kiosk_scope\"", StringComparison.Ordinal))
            {
                failures.Add($"{method} {endpoint.RoutePattern.RawText}: {(int)response.StatusCode}");
            }
        }

        failures.Should().BeEmpty("o principal de quiosque só faz GET do painel (ADR 0030 §2)");
        (await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("POST", "/api/v1/kiosk/screen")]
    [InlineData("PUT", "/api/v1/kiosk/screen")]
    [InlineData("DELETE", "/api/v1/kiosk/screen")]
    [InlineData("PATCH", "/api/v1/kiosk/session")]
    [InlineData("DELETE", "/api/v1/kiosk/session")]
    [InlineData("POST", "/api/v1/kiosk/board/orders")]
    public async Task Metodo_diferente_de_GET_no_painel_responde_405(string method, string url)
    {
        var (_, cookie, _) = await PairedAsync();

        var response = await Client().SendAsync(KioskRequest(new HttpMethod(method), url, cookie, body: method == "DELETE" ? null : new { }));

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.Should().Contain("GET");
    }

    [Fact]
    public async Task Endpoint_do_painel_nao_aceita_usuario_e_endpoint_de_usuario_nao_aceita_o_esquema_Kiosk()
    {
        var (_, owner) = await OwnerOnDutyAsync();

        var asUser = await Client().SendAsync(Authorized(HttpMethod.Get, Screen, owner));
        asUser.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o JWT do usuário não vale no painel");

        var kioskSchemes = Endpoints().Where(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == BemAqui.BuildingBlocks.Security.KioskClaims.Policy
                || (a.AuthenticationSchemes ?? string.Empty).Contains(BemAqui.BuildingBlocks.Security.KioskClaims.Scheme, StringComparison.Ordinal)))
            .ToList();
        kioskSchemes.Should().NotBeEmpty();
        kioskSchemes.Should().OnlyContain(e => e.Metadata.GetMetadata<KioskEndpointAttribute>() != null
            && (e.RoutePattern.RawText!.StartsWith("/hubs/kiosk", StringComparison.Ordinal)
                || (e.RoutePattern.RawText.StartsWith("/api/v1/kiosk/", StringComparison.Ordinal) && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.SequenceEqual(new[] { "GET" }))));
    }

    [Fact]
    public async Task Revogar_com_a_conexao_caida_derruba_a_TV_no_proximo_request_e_avisa_o_Proprietario()
    {
        var (row, cookie, manager) = await PairedAsync("TV Revogada " + Guid.NewGuid().ToString("N")[..6]);
        (await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie))).StatusCode.Should().Be(HttpStatusCode.OK, "a conferência fica em cache 30 s");
        var (_, revoker) = await UserAsync("kiosk:view,delete", "Paula Lima");
        var id = Id(row);
        var current = await JsonAsync(await Client().SendAsync(Authorized(HttpMethod.Get, Screens, revoker)));
        var version = Version(current.GetProperty("screens").EnumerateArray().Single(s => Id(s) == id));

        var revoked = await Client().SendAsync(Authorized(HttpMethod.Post, $"{Screens}/{id}/revoke", revoker, new { version }));

        revoked.StatusCode.Should().Be(HttpStatusCode.OK, await revoked.Content.ReadAsStringAsync());
        var after = await JsonAsync(revoked);
        after.GetProperty("status").GetString().Should().Be("Revoked");
        after.GetProperty("revokedByName").GetString().Should().Be("Paula Lima");
        var next = await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie));
        next.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o cache é invalidado na revogação: cai no próximo request, bem antes de 1 min");
        var problem = await JsonAsync(next);
        Code(problem).Should().Be("kiosk_revoked");
        problem.GetProperty("detail").GetString().Should().StartWith("Esta tela foi desligada por ");
        (await NotificationsAsync($"Tela de TV {row.GetProperty("name").GetString()} revogada por Paula Lima.")).Should().NotBeEmpty();
        (await WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.KioskSessions, s => s.KioskId == id && s.RevokedAt == null))).Should().Be(0);
        (await Client().SendAsync(Authorized(HttpMethod.Post, $"{Screens}/{id}/revoke", revoker, new { version = Version(after) }))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Client().SendAsync(Authorized(HttpMethod.Post, $"{Screens}/{id}/new-link", manager, new { version = Version(after) }))).StatusCode.Should().Be(HttpStatusCode.Conflict, "tela revogada não ganha link novo");
    }

    [Fact]
    public async Task Tenant_suspenso_ou_modulo_nao_liberado_recusa_a_sessao()
    {
        var (tenant, host, _, cookie, _) = await PairedInOwnTenantAsync();
        (await Client(host).SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie))).StatusCode.Should().Be(HttpStatusCode.OK);

        await SetKioskReleasedAsync(tenant.Id, released: false);
        var notReleased = await Client(host).SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie));
        notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        Code(await JsonAsync(await Client(host).SendAsync(OpenRequest(BemAqui.BuildingBlocks.Security.OpaqueToken.New(), origin: "https://" + host)))).Should().Be("module_not_released");

        await SetKioskReleasedAsync(tenant.Id, released: true);
        (await Client(host).SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie))).StatusCode.Should().Be(HttpStatusCode.OK);
        await Tenancy.SetStatusAsync(tenant.Id, suspended: true);
        try
        {
            (await EventuallyAsync(async () =>
            {
                var response = await Client(host).SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie));
                return response.StatusCode == HttpStatusCode.Forbidden && (await response.Content.ReadAsStringAsync()).Contains("tenant_suspended", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(10))).Should().BeTrue("tenant suspenso recusa a TV no próximo request");
        }
        finally
        {
            await Tenancy.SetStatusAsync(tenant.Id, suspended: false);
        }
    }
}
