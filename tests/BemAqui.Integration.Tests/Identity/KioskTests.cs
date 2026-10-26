using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>
/// Host próprio do Painel da TV: o socket de todo request é o do <c>stack-nginx</c> (172.18.0.7, único proxy confiável), então o IP real
/// vem do <c>X-Forwarded-For</c>, como em produção (CA 16, D19). A revalidação do hub roda a cada 2 s para o teste da conexão aberta.
/// Os escopos e as conexões do hub usam este mesmo host (dono do cache da sessão e do registro de conexões).
/// </summary>
public sealed class KioskFixture : IAsyncLifetime
{
    public const string ProxyIp = "172.18.0.7";

    public TenancyFixture Tenancy { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Tenancy.InitializeAsync();
        Factory = Tenancy.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ReverseProxy:KnownProxies:0", ProxyIp);
            b.UseSetting("Kiosk:RevalidateSeconds", "2");
            b.ConfigureTestServices(s => s.AddSingleton<IStartupFilter>(new SocketIpFilter(IPAddress.Parse(ProxyIp))));
        });
        _ = Factory.Server;
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await Tenancy.DisposeAsync();
    }

    private sealed class SocketIpFilter(IPAddress ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate inner) =>
            {
                context.Connection.RemoteIpAddress = ip;
                return inner(context);
            });
            next(app);
        };
    }
}

/// <summary>
/// PNL-01a (ADR 0030): telas de TV, troca do token de uso único pelo cookie <c>__Host-bq_kiosk</c>, esquema <c>Kiosk</c> só no GET do
/// painel, hub do quiosque, revogação em menos de 1 minuto e a troca da rota. Cada teste usa um IP próprio (o rate limit da troca é por
/// IP e tenant) e filtra pelos ids que criou.
/// </summary>
public partial class KioskTests(KioskFixture kiosk) : IClassFixture<KioskFixture>
{
    private const string Host = "a.localhost";
    private const string Origin = "https://a.localhost";
    private const string KioskCookie = "__Host-bq_kiosk";
    private const string Screens = "/api/v1/kiosk-screens";
    private const string Session = "/api/v1/kiosk/session";
    private const string Screen = "/api/v1/kiosk/screen";
    private const string Manager = "kiosk:view,create,delete;production:view";

    private TenancyFixture Tenancy => kiosk.Tenancy;

    private WebApplicationFactory<Program> Factory => kiosk.Factory;

    /// <summary>IP de cliente só deste teste (10.x.y.z), pelo cabeçalho do proxy confiável.</summary>
    private readonly string _ip = $"10.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}.{Random.Shared.Next(1, 250)}";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static string? CookieOf(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.LastOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal)) : null;

    private static string ValueOf(string setCookie) => setCookie.Split(';')[0][(setCookie.IndexOf('=', StringComparison.Ordinal) + 1)..];

    private static Guid Id(JsonElement row) => row.GetProperty("id").GetGuid();

    private static uint Version(JsonElement row) => row.GetProperty("version").GetUInt32();

    private HttpClient Client(string host = Host)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    private async Task<AsyncServiceScope> ScopeAsync(Guid? tenantId = null)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenantId ?? Tenancy.TenantA.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action, Guid? tenantId = null)
    {
        await using var scope = await ScopeAsync(tenantId);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task WithDbAsync(Func<TenantDbContext, Task> action, Guid? tenantId = null)
    {
        await using var scope = await ScopeAsync(tenantId);
        await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Usuário ativo (sem login por senha: token de teste com o stamp) com grupos do seed ou um grupo com as ações.</summary>
    private async Task<(Guid Id, string Token)> UserAsync(string? groups, string name = "Rita Prado", Tenant? owner = null, string host = Host)
    {
        var tenant = owner ?? Tenancy.TenantA;
        var (id, stamp) = await Tenancy.SeedUserAsync(tenant, "tv-" + Guid.NewGuid().ToString("N")[..10] + "@konebras.com.br");
        await using var scope = await ScopeAsync(tenant.Id);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        (await db.Users.SingleAsync(u => u.Id == id)).Name = name;
        foreach (var spec in (groups ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            Guid groupId;
            if (spec.Contains(':', StringComparison.Ordinal))
            {
                var group = new Group { Name = "Grupo " + Guid.NewGuid().ToString("N")[..8], Description = "Teste" };
                db.Groups.Add(group);
                foreach (var part in spec.Split(';'))
                {
                    var (module, actions) = (part.Split(':')[0], part.Split(':')[1].Split(','));
                    db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                    db.GroupPermissions.AddRange(actions.Select(a => new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = a }));
                }

                groupId = group.Id;
            }
            else
            {
                groupId = await db.Groups.Where(g => g.SeedKey == spec).Select(g => g.Id).SingleAsync();
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
        }

        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        return (id, TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
    }

    /// <summary>Proprietário ativo criado pelo teste: os avisos vão aos Proprietários ativos.</summary>
    private Task<(Guid Id, string Token)> OwnerOnDutyAsync(Tenant? tenant = null, string host = Host) => UserAsync(Group.OwnerSeedKey, "Proprietário de plantão", tenant, host);

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    /// <summary>Cria a tela pela API e devolve a linha e o token do link (o que vem depois do #).</summary>
    private async Task<(JsonElement Row, string Token)> CreateScreenAsync(string token, string? name = null, string[]? networks = null, string host = Host)
    {
        var response = await Client(host).SendAsync(Authorized(HttpMethod.Post, Screens, token, new { name = name ?? "TV " + Guid.NewGuid().ToString("N")[..8], line = (string?)null, views = Array.Empty<string>(), rotationSeconds = 20, allowedNetworks = networks ?? [] }));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        var link = body.GetProperty("link").GetString()!;
        return (body.GetProperty("screen"), link[(link.IndexOf('#', StringComparison.Ordinal) + 1)..]);
    }

    /// <summary>Troca do token como a TV faz: corpo JSON, Origin do host e X-CSRF; IP real pelo proxy.</summary>
    private HttpRequestMessage OpenRequest(string? token, string? ip = null, bool csrf = true, string origin = Origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Session) { Content = JsonContent.Create(new { token }) };
        request.Headers.Add("X-Forwarded-For", ip ?? _ip);
        request.Headers.Add("Origin", origin);
        if (csrf)
        {
            request.Headers.Add("X-CSRF", "1");
        }

        return request;
    }

    /// <summary>Request da TV com o cookie de sessão.</summary>
    private HttpRequestMessage KioskRequest(HttpMethod method, string url, string cookie, string? ip = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", KioskCookie + "=" + cookie);
        request.Headers.Add("X-Forwarded-For", ip ?? _ip);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    /// <summary>Tela criada por um gestor e pareada: devolve a linha, o cookie da TV e o token do gestor.</summary>
    private async Task<(JsonElement Row, string Cookie, string ManagerToken)> PairedAsync(string? name = null, string[]? networks = null)
    {
        await OwnerOnDutyAsync();
        var (_, manager) = await UserAsync(Manager);
        var (row, token) = await CreateScreenAsync(manager, name, networks);
        var opened = await Client().SendAsync(OpenRequest(token));
        opened.StatusCode.Should().Be(HttpStatusCode.OK, await opened.Content.ReadAsStringAsync());
        // O pareamento ativa a tela e muda a versão: devolve a linha como está depois dele.
        var id = Id(row);
        var current = await JsonAsync(await Client().SendAsync(Authorized(HttpMethod.Get, Screens, manager)));
        var paired = current.GetProperty("screens").EnumerateArray().Single(s => Id(s) == id).Clone();
        return (paired, ValueOf(CookieOf(opened, KioskCookie)!), manager);
    }

    private async Task<List<string>> NotificationsAsync(string startsWith, Guid? tenantId = null) =>
        await WithDbAsync(db => db.Notifications.Where(n => n.Title.StartsWith(startsWith)).Select(n => n.Title).ToListAsync(), tenantId);

    private async Task SetKioskReleasedAsync(Guid tenantId, bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenantId && m.ModuleCode == "kiosk").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Tenant só do teste (suspensão e módulo não liberado sem afetar os outros testes).</summary>
    private async Task<(Tenant Tenant, string Host)> OwnTenantAsync()
    {
        var key = "tv-" + Guid.NewGuid().ToString("N")[..8];
        var host = key + ".localhost";
        return (await Tenancy.ProvisionAsync(key, "Fábrica " + key, host, Factory.Services), host);
    }

    /// <summary>Gestor e Proprietário num tenant próprio, com uma tela pareada nele.</summary>
    private async Task<(Tenant Tenant, string Host, JsonElement Row, string Cookie, string Manager)> PairedInOwnTenantAsync()
    {
        var (tenant, host) = await OwnTenantAsync();
        await OwnerOnDutyAsync(tenant, host);
        var (_, manager) = await UserAsync(Manager, owner: tenant, host: host);
        var (row, token) = await CreateScreenAsync(manager, host: host);
        var opened = await Client(host).SendAsync(OpenRequest(token, origin: "https://" + host));
        opened.StatusCode.Should().Be(HttpStatusCode.OK, await opened.Content.ReadAsStringAsync());
        return (tenant, host, row, ValueOf(CookieOf(opened, KioskCookie)!), manager);
    }

    private static async Task<bool> EventuallyAsync(Func<Task<bool>> condition, TimeSpan within)
    {
        var until = DateTime.UtcNow + within;
        while (DateTime.UtcNow < until)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return await condition();
    }
}
