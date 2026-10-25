using System.Diagnostics;
using System.Net;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Integration.Tests.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>PNL-01a CA 5 a 8, 10, 11, 16 e 19: troca do token pelo cookie, uso único, vencimento, rate limit, canário, dois tenants e rede.</summary>
public partial class KioskTests
{
    [Fact]
    public async Task Token_no_corpo_vira_cookie_Host_opaco_com_so_o_hash_no_banco_e_o_corpo_so_com_o_nome()
    {
        var (_, manager) = await UserAsync(Manager);
        var name = "TV Expedição " + Guid.NewGuid().ToString("N")[..6];
        var (row, token) = await CreateScreenAsync(manager, name);

        var opened = await Client().SendAsync(OpenRequest(token));

        opened.StatusCode.Should().Be(HttpStatusCode.OK);
        opened.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await JsonAsync(opened);
        body.EnumerateObject().Select(p => p.Name).Should().Equal(["screenName"], "nenhum dado do tenant além do nome da tela");
        body.GetProperty("screenName").GetString().Should().Be(name);
        var setCookie = CookieOf(opened, KioskCookie)!;
        setCookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/").And.NotContain("domain=");
        var cookie = ValueOf(setCookie);
        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(cookie).Should().HaveCount(32);
        cookie.Should().NotBe(token);
        var id = Id(row);
        var stored = await WithDbAsync(db => db.KioskSessions.AsNoTracking().SingleAsync(s => s.KioskId == id));
        stored.TokenHash.Should().Be(OpaqueToken.Hash(cookie));
        stored.Ip.Should().Be(_ip, "IP real pelo X-Forwarded-For do proxy confiável");

        var screen = await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie));
        screen.StatusCode.Should().Be(HttpStatusCode.OK);
        screen.Headers.CacheControl!.NoStore.Should().BeTrue("CA 11");
        var context = await JsonAsync(screen);
        context.GetProperty("name").GetString().Should().Be(name);
        context.GetProperty("companyName").GetString().Should().NotBeNullOrEmpty();
        (await WithDbAsync(db => db.KioskScreens.AsNoTracking().SingleAsync(s => s.Id == id))).Status.Should().Be(BemAqui.Identity.Domain.KioskScreenStatus.Active);
    }

    [Fact]
    public async Task Pareamento_de_uso_unico_segunda_troca_e_401_e_avisa_o_Proprietario()
    {
        await OwnerOnDutyAsync();
        var (_, manager) = await UserAsync(Manager);
        var name = "TV Reuso " + Guid.NewGuid().ToString("N")[..6];
        var (row, token) = await CreateScreenAsync(manager, name);
        (await Client().SendAsync(OpenRequest(token))).StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await Client().SendAsync(OpenRequest(token, ip: "10.250.250.1"));

        again.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await JsonAsync(again);
        problem.GetProperty("detail").GetString().Should().Be("Link inválido ou desligado.");
        Code(problem).Should().Be("kiosk_link_invalid");
        CookieOf(again, KioskCookie).Should().BeNull();
        (await NotificationsAsync($"Tentaram usar de novo o link da tela {name}.")).Should().HaveCount(1);
        var id = Id(row);
        (await WithDbAsync(db => db.KioskSessions.CountAsync(s => s.KioskId == id))).Should().Be(1, "no máximo 1 sessão por tela");
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityId == id.ToString() && a.Action == "KioskLinkReused"))).Should().Be(1);
    }

    [Fact]
    public async Task Gerar_novo_link_troca_o_token_derruba_a_sessao_anterior_e_o_link_antigo_nao_vale()
    {
        var (row, cookie, manager) = await PairedAsync();
        var id = Id(row);
        var current = await JsonAsync(await Client().SendAsync(Authorized(HttpMethod.Get, Screens, manager)));
        var version = Version(current.GetProperty("screens").EnumerateArray().Single(s => Id(s) == id));

        var renewed = await Client().SendAsync(Authorized(HttpMethod.Post, $"{Screens}/{id}/new-link", manager, new { version }));

        renewed.StatusCode.Should().Be(HttpStatusCode.OK, await renewed.Content.ReadAsStringAsync());
        renewed.Headers.CacheControl!.NoStore.Should().BeTrue();
        var link = (await JsonAsync(renewed)).GetProperty("link").GetString()!;
        var token = link[(link.IndexOf('#', StringComparison.Ordinal) + 1)..];
        var old = await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie));
        old.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a sessão anterior cai na hora");
        Code(await JsonAsync(old)).Should().Be("kiosk_session_invalid");
        (await Client().SendAsync(OpenRequest(token))).StatusCode.Should().Be(HttpStatusCode.OK, "o link novo pareia a TV");
        (await WithDbAsync(db => db.KioskSessions.CountAsync(s => s.KioskId == id && s.RevokedAt == null))).Should().Be(1);
    }

    [Fact]
    public async Task Sessao_vence_com_30_dias_sem_uso_ou_180_absolutos()
    {
        var (unusedRow, unusedCookie, _) = await PairedAsync();
        var (oldRow, oldCookie, _) = await PairedAsync();
        var unusedId = Id(unusedRow);
        var oldId = Id(oldRow);
        // Nenhuma das duas foi conferida ainda neste host (sem cache): as datas mudam direto no banco.
        await WithDbAsync(db => db.KioskSessions.Where(s => s.KioskId == unusedId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await WithDbAsync(db => db.KioskSessions.Where(s => s.KioskId == oldId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AbsoluteExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)).SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddDays(10))));

        foreach (var cookie in new[] { unusedCookie, oldCookie })
        {
            var response = await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie));
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var problem = await JsonAsync(response);
            Code(problem).Should().Be("kiosk_session_invalid");
            problem.GetProperty("detail").GetString().Should().Be("Esta tela precisa ser conectada de novo. Peça um novo link a quem cuida do painel.");
        }

        var list = await JsonAsync(await Client().SendAsync(Authorized(HttpMethod.Get, Screens, (await UserAsync("kiosk:view")).Token)));
        list.GetProperty("screens").EnumerateArray().Single(s => Id(s) == unusedId).GetProperty("needsNewLink").GetBoolean().Should().BeTrue("o propositivo oferece Gerar novo link");
    }

    [Fact]
    public async Task Tokens_errados_respondem_401_unico_com_tempo_uniforme_e_rate_limit_de_10_por_minuto()
    {
        var ip = "10.251." + Random.Shared.Next(1, 250) + "." + Random.Shared.Next(1, 250);
        var times = new List<TimeSpan>();
        for (var i = 0; i < 10; i++)
        {
            var watch = Stopwatch.StartNew();
            var response = await Client().SendAsync(OpenRequest(i % 2 == 0 ? OpaqueToken.New() : "curto", ip));
            times.Add(watch.Elapsed);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await JsonAsync(response)).GetProperty("detail").GetString().Should().Be("Link inválido ou desligado.");
        }

        times.Should().OnlyContain(t => t >= TimeSpan.FromMilliseconds(240), "tempo uniforme: o formato do token não muda o tempo");
        var limited = await Client().SendAsync(OpenRequest(OpaqueToken.New(), ip));
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "política kiosk-session: 10 por minuto por IP e tenant");
        limited.Headers.RetryAfter.Should().NotBeNull();
        (await Client().SendAsync(OpenRequest(OpaqueToken.New(), "10.252.0." + Random.Shared.Next(1, 250)))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "outro IP tem a própria janela");
    }

    [Fact]
    public async Task Troca_sem_Origin_do_host_ou_sem_X_CSRF_e_recusada()
    {
        var (_, manager) = await UserAsync(Manager);
        var (_, token) = await CreateScreenAsync(manager);

        (await Client().SendAsync(OpenRequest(token, csrf: false))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client().SendAsync(OpenRequest(token, origin: "https://evil.example"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client().SendAsync(OpenRequest(token))).StatusCode.Should().Be(HttpStatusCode.OK, "o token não foi consumido pelas tentativas recusadas");
    }

    [Fact]
    public async Task Nenhum_log_nem_auditoria_grava_o_token_o_cookie_ou_o_hash()
    {
        var sink = new MemorySink();
        await using var logged = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)));
        var client = logged.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        var (_, manager) = await UserAsync(Manager);
        var created = await client.SendAsync(Authorized(HttpMethod.Post, Screens, manager, new { name = "TV Canário " + Guid.NewGuid().ToString("N")[..6], views = Array.Empty<string>(), allowedNetworks = Array.Empty<string>() }));
        var link = (await JsonAsync(created)).GetProperty("link").GetString()!;
        var token = link[(link.IndexOf('#', StringComparison.Ordinal) + 1)..];
        var opened = await client.SendAsync(OpenRequest(token));
        var cookie = ValueOf(CookieOf(opened, KioskCookie)!);
        await client.SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie));
        await client.SendAsync(OpenRequest(token)); // reuso: auditado e logado sem o token
        await client.SendAsync(KioskRequest(HttpMethod.Get, "/api/v1/me", cookie));

        var canaries = new[] { token, cookie, OpaqueToken.Hash(token), OpaqueToken.Hash(cookie) };
        List<string> logs;
        lock (sink.Events)
        {
            logs = sink.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(' ', e.Properties.Select(p => p.Key + "=" + p.Value))).ToList();
        }

        logs.Should().NotBeEmpty();
        logs.Should().NotContain(l => canaries.Any(c => l.Contains(c, StringComparison.Ordinal)), "nem o token, nem o cookie, nem os hashes vão para o log");
        var audit = await WithDbAsync(db => db.AuditEntries.Where(a => a.Entity == "KioskScreen").Select(a => a.Changes).ToListAsync());
        audit.Should().NotContain(c => canaries.Any(x => c.Contains(x, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Token_de_outro_tenant_nao_abre_sessao_e_o_escopo_do_cookie_e_do_host()
    {
        await OwnerOnDutyAsync(Tenancy.TenantB, "b.localhost");
        var (_, managerB) = await UserAsync(Manager, owner: Tenancy.TenantB, host: "b.localhost");
        var (_, tokenB) = await CreateScreenAsync(managerB, host: "b.localhost");

        var wrongHost = await Client().SendAsync(OpenRequest(tokenB));
        wrongHost.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o hash do tenant B não existe no banco do tenant A");

        var openedB = await Client("b.localhost").SendAsync(OpenRequest(tokenB, origin: "https://b.localhost"));
        openedB.StatusCode.Should().Be(HttpStatusCode.OK, "o token não foi consumido pela tentativa no host errado");
        var cookieB = ValueOf(CookieOf(openedB, KioskCookie)!);
        CookieOf(openedB, KioskCookie)!.Should().NotContainEquivalentOf("domain=", "__Host-: o navegador só manda o cookie ao host que o criou");

        var crossed = await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookieB));
        crossed.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o cookie de B levado ao host de A não vale");
        (await Client("b.localhost").SendAsync(KioskRequest(HttpMethod.Get, Screen, cookieB))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rede_fora_da_permitida_recusa_a_troca_e_o_uso_da_sessao()
    {
        var (_, manager) = await UserAsync(Manager);
        var (_, token) = await CreateScreenAsync(manager, networks: ["192.168.77.0/24"]);

        var outside = await Client().SendAsync(OpenRequest(token, ip: "203.0.113.50"));
        outside.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(outside)).GetProperty("detail").GetString().Should().Be("Esta tela só funciona na rede da fábrica.");
        CookieOf(outside, KioskCookie).Should().BeNull("a sessão não é criada");

        var inside = await Client().SendAsync(OpenRequest(token, ip: "192.168.77.20"));
        inside.StatusCode.Should().Be(HttpStatusCode.OK, "fora da rede o token não é consumido");
        var cookie = ValueOf(CookieOf(inside, KioskCookie)!);
        (await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie, ip: "192.168.77.21"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var moved = await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie, ip: "203.0.113.50"));
        moved.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(moved)).Should().Be("kiosk_network");
    }

    [Fact]
    public async Task Ultimo_acesso_e_gravado_no_maximo_1_vez_por_minuto()
    {
        var (row, cookie, _) = await PairedAsync();
        var id = Id(row);
        var before = DateTimeOffset.UtcNow.AddMinutes(-5);
        await WithDbAsync(db => db.KioskSessions.Where(s => s.KioskId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, before)));

        (await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie))).StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await WithDbAsync(db => db.KioskSessions.AsNoTracking().SingleAsync(s => s.KioskId == id));
        first.LastSeenAt.Should().BeAfter(before.AddMinutes(4));
        first.ExpiresAt.Should().BeCloseTo(first.LastSeenAt.AddDays(30), TimeSpan.FromSeconds(1));
        (await WithDbAsync(db => db.KioskScreens.Where(s => s.Id == id).Select(s => s.LastSeenAt).SingleAsync())).Should().BeCloseTo(first.LastSeenAt, TimeSpan.FromSeconds(1));

        (await Client().SendAsync(KioskRequest(HttpMethod.Get, Screen, cookie))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await WithDbAsync(db => db.KioskSessions.Where(s => s.KioskId == id).Select(s => s.LastSeenAt).SingleAsync())).Should().Be(first.LastSeenAt, "dentro do minuto não grava de novo");
    }
}
