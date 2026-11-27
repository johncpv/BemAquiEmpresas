using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Application.ConnectedApps;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Application.Modules;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>
/// INT-01: o ERP como servidor de autorização do Conecta, com PostgreSQL real. O módulo <c>conecta</c> é liberado por exceção nos dois tenants; cada
/// teste cria o próprio Proprietário com 2FA. Os testes da classe rodam em sequência (a conexão é uma por aplicativo e tenant).
/// </summary>
public class ConnectedAppTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string HostA = "a.localhost";
    private const string HostB = "b.localhost";
    private const string Redirect = "https://conecta.test/oauth/callback";
    private const string ClientSecret = "segredo-de-teste-do-conecta-123";
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    private MemorySink Logs => tenancy.State<MemorySink>();

    private WebApplicationFactory<Program> Factory => tenancy.Shared("conecta", b =>
    {
        b.UseSetting("Integrations:Conecta:StartUrl", "https://conecta.test/conectar");
        b.UseSetting("Integrations:Conecta:RedirectUris:0", Redirect);
        b.UseSetting("Integrations:Conecta:ClientSecretHash", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ClientSecret))));
        b.UseSetting("Integrations:AppRateLimit:TokenPerMinute", "100000");
        b.UseSetting("Integrations:AppRateLimit:ConsentPerMinute", "100000");
        b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(tenancy.State<MemorySink>()));
    });

    private sealed record Owner(Guid Id, HttpClient Client, Func<Task<string>> Totp);

    private sealed record Tokens(string Access, string Refresh, Guid ConnectionId, JsonElement Body);

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static Dictionary<string, string> Query(string url) =>
        QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);

    private Tenant TenantOf(string host) => host == HostA ? tenancy.TenantA : tenancy.TenantB;

    private async Task<AsyncServiceScope> ScopeAsync(string host = HostA)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(TenantOf(host).Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task ReleaseAsync(string host, bool released)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TenantModuleService>().SaveAsync(TenantOf(host).Id, [new ModuleReleaseChange("conecta", released)], null, null))
            .IsSuccess.Should().BeTrue();
        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Proprietário com o 2FA ligado e um gerador de código que libera o passo atual de novo (o anti-replay aceita cada código uma vez).</summary>
    private async Task<Owner> OwnerAsync(string host = HostA, bool totp = true, string name = "Paula Lima")
    {
        await ReleaseAsync(host, true);
        var tenant = TenantOf(host);
        var (id, _) = await tenancy.SeedUserAsync(tenant, "con" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        var secret = Totp.NewSecret();
        string stamp;
        await using (var scope = await ScopeAsync(host))
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var user = await db.Users.SingleAsync(u => u.Id == id);
            user.Name = name;
            if (totp)
            {
                user.BeginTotpSetup(Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenant.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
                user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            }

            var groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
            stamp = user.SecurityStamp;
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
        async Task<string> Code()
        {
            var step = Totp.StepAt(DateTimeOffset.UtcNow);
            await using var scope = await ScopeAsync(host);
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE identity.users SET totp_last_step = {step - 10} WHERE id = {id}");
            return Totp.Code(secret, step);
        }

        return new Owner(id, client, Code);
    }

    private static object Request(string intent, string scope = "parties.view finance.view sales.view", string state = "st-123", string redirect = Redirect,
        string clientId = "conecta", string method = "S256") =>
        new { responseType = "code", clientId, redirectUri = redirect, scope, state, codeChallenge = Challenge, codeChallengeMethod = method, intent, accountLabel = "Loja do João" };

    private static async Task<string> IntentAsync(Owner owner)
    {
        var start = await OkAsync(await owner.Client.PostAsync("/api/v1/oauth/connect", null));
        var url = start.GetProperty("redirectUrl").GetString()!;
        url.Should().StartWith("https://conecta.test/conectar?");
        Query(url)["erp"].Should().Be(owner.Client.DefaultRequestHeaders.Host);
        return Query(url)["intent"];
    }

    private static Task<HttpResponseMessage> InspectAsync(Owner owner, object request) => owner.Client.PostAsJsonAsync("/api/v1/oauth/authorize/inspect", request);

    private static async Task<HttpResponseMessage> ConsentAsync(Owner owner, object request, string? code = null) =>
        await owner.Client.PostAsJsonAsync("/api/v1/oauth/authorize/consent", new { request, code = code ?? await owner.Totp() });

    /// <summary>Conectar → conferir → autorizar com 2FA; devolve o código de autorização.</summary>
    private static async Task<string> AuthorizeAsync(Owner owner, string scope = "parties.view finance.view sales.view")
    {
        var request = Request(await IntentAsync(owner), scope);
        (await OkAsync(await InspectAsync(owner, request))).GetProperty("outcome").GetString().Should().Be("consent");
        var redirect = (await OkAsync(await ConsentAsync(owner, request))).GetProperty("redirectTo").GetString()!;
        var query = Query(redirect);
        query["state"].Should().Be("st-123");
        query["iss"].Should().Be("https://" + owner.Client.DefaultRequestHeaders.Host);
        redirect.Should().StartWith(Redirect + "?");
        return query["code"];
    }

    private HttpClient App(string host = HostA, string? secret = ClientSecret)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        if (secret is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("conecta:" + secret)));
        }

        return client;
    }

    private static Task<HttpResponseMessage> TokenAsync(HttpClient app, params (string Key, string Value)[] form) =>
        app.PostAsync("/api/v1/oauth/token", new FormUrlEncodedContent(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))));

    private static Task<HttpResponseMessage> ExchangeAsync(HttpClient app, string code, string verifier = Verifier, string redirect = Redirect) =>
        TokenAsync(app, ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", redirect), ("code_verifier", verifier));

    private async Task<Tokens> ConnectAsync(Owner owner, string host = HostA)
    {
        var response = await ExchangeAsync(App(host), await AuthorizeAsync(owner));
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await OkAsync(response);
        return new Tokens(body.GetProperty("access_token").GetString()!, body.GetProperty("refresh_token").GetString()!, body.GetProperty("connection_id").GetGuid(), body);
    }

    private HttpClient Bearer(string token, string host = HostA)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<AppConnection> ConnectionAsync(Guid id, string host = HostA)
    {
        await using var scope = await ScopeAsync(host);
        return await scope.ServiceProvider.GetRequiredService<IIdentityDb>().AppConnections.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response, HttpStatusCode status) => Code(await OkAsync(response, status));

    [Fact]
    public async Task Fluxo_completo_conectar_autorizar_trocar_o_codigo_e_usar_o_token()
    {
        var owner = await OwnerAsync();
        var request = Request(await IntentAsync(owner));
        var screen = await OkAsync(await InspectAsync(owner, request));
        screen.GetProperty("appName").GetString().Should().Be("Conecta");
        screen.GetProperty("accountLabel").GetString().Should().Be("Loja do João");
        screen.GetProperty("granted").EnumerateArray().Select(s => s.GetProperty("text").GetString()).Should()
            .Equal("Ver clientes (nome, telefone, e-mail, documento)", "Ver cobranças");
        screen.GetProperty("notHeld").EnumerateArray().Select(s => s.GetProperty("scope").GetString()).Should().Equal(new[] { "sales.view" }, "a fábrica não tem Vendas liberado");

        var redirect = (await OkAsync(await ConsentAsync(owner, request))).GetProperty("redirectTo").GetString()!;
        var code = Query(redirect)["code"];
        var tokens = await ExchangeAsync(App(), code);
        var body = await OkAsync(tokens);
        body.GetProperty("token_type").GetString().Should().Be("Bearer");
        body.GetProperty("expires_in").GetInt32().Should().Be(900);
        body.GetProperty("scope").GetString().Should().Be("parties.view finance.view");
        body.GetProperty("access_token").GetString().Should().StartWith("bqa_");
        body.GetProperty("refresh_token").GetString().Should().StartWith("bqr_");
        body.GetProperty("webhook_secret").ValueKind.Should().Be(JsonValueKind.Null, "sem Integrations:Conecta:WebhookUrl no ambiente, a conexão nasce sem inscrição de avisos (INT-02)");

        var app = Bearer(body.GetProperty("access_token").GetString()!);
        var self = await OkAsync(await app.GetAsync("/api/v1/integration/connection"));
        self.GetProperty("connection_id").GetGuid().Should().Be(body.GetProperty("connection_id").GetGuid());
        self.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).Should().Equal("parties.view", "finance.view");

        // CA 18: prova do consentimento e conexão com o autorizador na auditoria; último uso gravado.
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var connectionId = body.GetProperty("connection_id").GetGuid().ToString();
        (await db.AuditEntries.AnyAsync(a => a.Entity == "AppConnection" && a.EntityId == connectionId && a.Action == "AppConnected" && a.UserId == owner.Id)).Should().BeTrue();
        (await db.AuditEntries.AnyAsync(a => a.Entity == "ConnectedApp" && a.Action == "AppConsentGranted" && a.UserId == owner.Id)).Should().BeTrue();
        (await ConnectionAsync(Guid.Parse(connectionId))).LastUsedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Intencao_de_outra_pessoa_sem_intencao_vencida_usada_e_pedido_trocado_sao_recusados()
    {
        var owner = await OwnerAsync();
        var other = await OwnerAsync(name: "Outra Pessoa");
        var intent = await IntentAsync(owner);

        foreach (var (who, req) in new[] { (other, Request(intent)), (owner, Request(string.Empty)), (owner, Request("inventado")) })
        {
            var refused = await OkAsync(await InspectAsync(who, req));
            refused.GetProperty("outcome").GetString().Should().Be("error");
            refused.GetProperty("message").GetString().Should().Be(OAuthAuthorizeService.IntentInvalidMessage);
        }

        // CA 5: o primeiro inspect amarra; o consent com outro state recusa como o CA 4.
        (await OkAsync(await InspectAsync(owner, Request(intent)))).GetProperty("outcome").GetString().Should().Be("consent");
        (await OkAsync(await InspectAsync(owner, Request(intent, state: "outro")))).GetProperty("outcome").GetString().Should().Be("error");
        (await ProblemCodeAsync(await ConsentAsync(owner, Request(intent, state: "outro")), HttpStatusCode.UnprocessableEntity)).Should().Be(OAuthAuthorizeService.IntentInvalidCode);

        await OkAsync(await ConsentAsync(owner, Request(intent)));
        (await ProblemCodeAsync(await ConsentAsync(owner, Request(intent)), HttpStatusCode.Conflict)).Should().Be(OAuthAuthorizeService.IntentUsedCode);

        var late = await IntentAsync(owner);
        await using (var scope = await ScopeAsync())
        {
            var hash = OpaqueToken.Hash(late);
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().AppConnectIntents.Where(i => i.IntentHash == hash)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        (await OkAsync(await InspectAsync(owner, Request(late)))).GetProperty("outcome").GetString().Should().Be("error");
    }

    [Fact]
    public async Task Cliente_URI_e_PKCE_errados_ficam_no_ERP_e_escopo_ou_recusa_voltam_ao_aplicativo()
    {
        var owner = await OwnerAsync();
        var intent = await IntentAsync(owner);
        foreach (var bad in new[] { Request(intent, clientId: "outro"), Request(intent, redirect: Redirect + "/"), Request(intent, method: "plain") })
        {
            var local = await OkAsync(await InspectAsync(owner, bad));
            local.GetProperty("outcome").GetString().Should().Be("error");
            local.GetProperty("redirectTo").ValueKind.Should().Be(JsonValueKind.Null, "nada vai para um endereço não conferido");
        }

        var scoped = await OkAsync(await InspectAsync(owner, Request(intent, scope: "parties.view sales.approve")));
        Query(scoped.GetProperty("redirectTo").GetString()!).Should().Contain("error", "invalid_scope").And.Contain("state", "st-123");

        var empty = await OkAsync(await InspectAsync(owner, Request(await IntentAsync(owner), scope: "sales.view")));
        Query(empty.GetProperty("redirectTo").GetString()!)["error"].Should().Be("access_denied", "a pessoa não tem nada do que foi pedido (CA 9)");

        // CA 10: Cancelar consome a intenção e volta com access_denied.
        var deny = Request(await IntentAsync(owner));
        await OkAsync(await InspectAsync(owner, deny));
        var back = await OkAsync(await owner.Client.PostAsJsonAsync("/api/v1/oauth/authorize/deny", deny));
        Query(back.GetProperty("redirectTo").GetString()!).Should().Contain("error", "access_denied").And.Contain("state", "st-123");
        (await ProblemCodeAsync(await ConsentAsync(owner, deny), HttpStatusCode.Conflict)).Should().Be(OAuthAuthorizeService.IntentUsedCode);
    }

    [Fact]
    public async Task Autorizar_exige_2FA_ativo_e_o_codigo_certo()
    {
        var owner = await OwnerAsync();
        var request = Request(await IntentAsync(owner));
        await OkAsync(await InspectAsync(owner, request));
        (await ProblemCodeAsync(await ConsentAsync(owner, request, "000000"), HttpStatusCode.UnprocessableEntity)).Should().Be("auth.2fa_invalid_code");

        var without = await OwnerAsync(totp: false);
        var other = Request(await IntentAsync(without));
        await OkAsync(await InspectAsync(without, other));
        var refused = await OkAsync(await ConsentAsync(without, other, "123456"), HttpStatusCode.Forbidden);
        Code(refused).Should().Be("two_factor_required");
        refused.GetProperty("enrolled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Codigo_reusado_revoga_a_conexao_e_cliente_ou_PKCE_errados_sao_recusados()
    {
        var owner = await OwnerAsync();
        var code = await AuthorizeAsync(owner);
        var wrongSecret = await TokenAsync(App(secret: "errado"), ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", Redirect), ("code_verifier", Verifier));
        wrongSecret.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrongSecret.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_client");
        (await OkAsync(await ExchangeAsync(App(), code, verifier: Verifier[..42] + "Z"), HttpStatusCode.BadRequest)).GetProperty("error").GetString().Should().Be("invalid_grant");

        var body = await OkAsync(await ExchangeAsync(App(), code));
        var app = Bearer(body.GetProperty("access_token").GetString()!);
        await OkAsync(await app.GetAsync("/api/v1/integration/connection"));

        (await OkAsync(await ExchangeAsync(App(), code), HttpStatusCode.BadRequest)).GetProperty("error").GetString().Should().Be("invalid_grant");
        (await ProblemCodeAsync(await app.GetAsync("/api/v1/integration/connection"), HttpStatusCode.Unauthorized)).Should().Be("invalid_token");
        (await ConnectionAsync(body.GetProperty("connection_id").GetGuid())).StatusReason.Should().Be(AppConnectionReason.CredentialReuse);
    }

    [Fact]
    public async Task Novo_consentimento_substitui_a_conexao_anterior()
    {
        var owner = await OwnerAsync();
        var first = await ConnectAsync(owner);
        var second = await ConnectAsync(owner);

        var old = await ConnectionAsync(first.ConnectionId);
        old.Status.Should().Be(AppConnectionStatus.Revoked);
        old.StatusReason.Should().Be(AppConnectionReason.Replaced);
        old.RevokedReason.Should().StartWith("Substituída por nova conexão em ").And.EndWith("por Paula Lima");
        (await ProblemCodeAsync(await Bearer(first.Access).GetAsync("/api/v1/integration/connection"), HttpStatusCode.Unauthorized)).Should().Be("invalid_token");
        await OkAsync(await Bearer(second.Access).GetAsync("/api/v1/integration/connection"));

        await using var scope = await ScopeAsync();
        (await scope.ServiceProvider.GetRequiredService<IIdentityDb>().AppConnections.CountAsync(c => c.Status != AppConnectionStatus.Revoked)).Should().Be(1);
    }

    [Fact]
    public async Task Renovacao_rotativa_e_reuso_do_substituido_revoga_e_avisa()
    {
        var owner = await OwnerAsync();
        var tokens = await ConnectAsync(owner);
        var renewed = await OkAsync(await TokenAsync(App(), ("grant_type", "refresh_token"), ("refresh_token", tokens.Refresh)));
        renewed.GetProperty("refresh_token").GetString().Should().NotBe(tokens.Refresh);
        await OkAsync(await Bearer(renewed.GetProperty("access_token").GetString()!).GetAsync("/api/v1/integration/connection"));

        await using (var scope = await ScopeAsync())
        {
            var hash = OpaqueToken.Hash(tokens.Refresh);
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().AppTokens.Where(t => t.TokenHash == hash)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedAt, DateTimeOffset.UtcNow.AddSeconds(-20)));
        }

        (await OkAsync(await TokenAsync(App(), ("grant_type", "refresh_token"), ("refresh_token", tokens.Refresh)), HttpStatusCode.BadRequest))
            .GetProperty("error").GetString().Should().Be("invalid_grant");
        var connection = await ConnectionAsync(tokens.ConnectionId);
        connection.Status.Should().Be(AppConnectionStatus.Revoked);
        connection.RevokedReason.Should().Be(AppConnectionLifecycle.ReuseText);
        (await ProblemCodeAsync(await Bearer(renewed.GetProperty("access_token").GetString()!).GetAsync("/api/v1/integration/connection"), HttpStatusCode.Unauthorized))
            .Should().Be("invalid_token");
        await using var check = await ScopeAsync();
        (await check.ServiceProvider.GetRequiredService<TenantDbContext>().Notifications.AnyAsync(n => n.UserId == owner.Id && n.Type == "conecta")).Should().BeTrue();
    }

    [Fact]
    public async Task Doze_meses_suspende_e_o_aviso_sai_30_dias_antes()
    {
        var owner = await OwnerAsync();
        var tokens = await ConnectAsync(owner);
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await db.AppConnections.Where(c => c.Id == tokens.ConnectionId).ExecuteUpdateAsync(s => s.SetProperty(c => c.ReconsentDueAt, DateTimeOffset.UtcNow.AddDays(10)));
            await scope.ServiceProvider.GetRequiredService<ConnectedAppMaintenance>().ReconsentAsync();
            (await db.Notifications.AnyAsync(n => n.UserId == owner.Id && n.Title.Contains("autorização vence"))).Should().BeTrue();
            await db.AppConnections.Where(c => c.Id == tokens.ConnectionId).ExecuteUpdateAsync(s => s.SetProperty(c => c.ReconsentDueAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        await using (var scope = await ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<ConnectedAppMaintenance>().ReconsentAsync();
        }

        var connection = await ConnectionAsync(tokens.ConnectionId);
        connection.Status.Should().Be(AppConnectionStatus.Suspended);
        connection.StatusReason.Should().Be(AppConnectionReason.ReconsentDue);
        (await ProblemCodeAsync(await Bearer(tokens.Access).GetAsync("/api/v1/integration/connection"), HttpStatusCode.Unauthorized)).Should().Be("invalid_token");
        var page = await OkAsync(await owner.Client.GetAsync("/api/v1/connected-apps/conecta"));
        page.GetProperty("connection").GetProperty("statusText").GetString().Should().Be("Suspensa");
    }

    [Fact]
    public async Task Permissao_efetiva_e_a_concessao_limitada_pelas_permissoes_atuais_do_autorizador()
    {
        var owner = await OwnerAsync();
        var tokens = await ConnectAsync(owner);
        await using (var scope = await ScopeAsync())
        {
            scope.ServiceProvider.GetRequiredService<ConnectedAppContext>().Current =
                new ConnectedAppGrant(tokens.ConnectionId, Guid.Empty, "conecta", "Conecta", owner.Id, "Paula Lima", new HashSet<string> { "parties.view", "finance.view" });
            var effective = await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(owner.Id);
            effective.Permissions.Should().BeEquivalentTo(["parties.view", "finance.view"]);
            effective.IsOwner.Should().BeFalse("o aplicativo nunca age como Proprietário");
        }

        // Sai do Proprietário: perde tudo na chamada seguinte; volta: a concessão volta (CA 16).
        await SetOwnerAsync(owner.Id, member: false);
        (await EffectiveForAppAsync(owner.Id, tokens.ConnectionId)).Should().BeEmpty();
        await SetOwnerAsync(owner.Id, member: true);
        (await EffectiveForAppAsync(owner.Id, tokens.ConnectionId)).Should().BeEquivalentTo(["parties.view", "finance.view"]);
    }

    private async Task SetOwnerAsync(Guid userId, bool member)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        var groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
        if (member)
        {
            db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = groupId });
        }
        else
        {
            db.UserGroups.Remove(await db.UserGroups.SingleAsync(g => g.UserId == userId && g.GroupId == groupId));
        }

        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([userId]);
    }

    private async Task<IReadOnlySet<string>> EffectiveForAppAsync(Guid grantor, Guid connectionId)
    {
        await using var scope = await ScopeAsync();
        scope.ServiceProvider.GetRequiredService<ConnectedAppContext>().Current =
            new ConnectedAppGrant(connectionId, Guid.Empty, "conecta", "Conecta", grantor, "Paula Lima", new HashSet<string> { "parties.view", "finance.view" });
        return (await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(grantor)).Permissions;
    }

    [Fact]
    public async Task Autorizador_desativado_suspende_e_2FA_zerado_so_avisa()
    {
        var owner = await OwnerAsync();
        var watcher = await OwnerAsync(name: "Outro Dono");
        var tokens = await ConnectAsync(owner);

        await using (var scope = await ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<AppConnectionLifecycle>().NotifyTwoFactorResetAsync(owner.Id);
            (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Notifications.AnyAsync(n => n.UserId == watcher.Id && n.Title.StartsWith("O 2FA de Paula Lima"))).Should().BeTrue();
        }

        (await ConnectionAsync(tokens.ConnectionId)).Status.Should().Be(AppConnectionStatus.Active, "2FA zerado não suspende");

        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<UserLifecycle>().DeactivateAsync(owner.Id, watcher.Id)).IsSuccess.Should().BeTrue();
        }

        (await ProblemCodeAsync(await Bearer(tokens.Access).GetAsync("/api/v1/integration/connection"), HttpStatusCode.Unauthorized)).Should().Be("app_grantor_inactive");
        var connection = await ConnectionAsync(tokens.ConnectionId);
        connection.Status.Should().Be(AppConnectionStatus.Suspended);
        connection.StatusReason.Should().Be(AppConnectionReason.GrantorInactive);
        var page = await OkAsync(await watcher.Client.GetAsync("/api/v1/connected-apps/conecta"));
        page.GetProperty("connection").GetProperty("reasonText").GetString().Should().Be("Quem autorizou perdeu o acesso");
    }

    [Fact]
    public async Task Desconectar_no_ERP_e_pelo_aplicativo()
    {
        var owner = await OwnerAsync();
        var tokens = await ConnectAsync(owner);
        (await owner.Client.PostAsync($"/api/v1/connected-apps/{tokens.ConnectionId}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ProblemCodeAsync(await Bearer(tokens.Access).GetAsync("/api/v1/integration/connection"), HttpStatusCode.Unauthorized)).Should().Be("invalid_token");
        var page = await OkAsync(await owner.Client.GetAsync("/api/v1/connected-apps/conecta"));
        page.GetProperty("connection").GetProperty("statusText").GetString().Should().Be("Desconectada");
        page.GetProperty("connection").GetProperty("reasonText").GetString().Should().StartWith("Desconectada por Paula Lima em ");

        var again = await ConnectAsync(owner);
        await OkAsync(await App().PostAsync("/api/v1/oauth/revoke", new FormUrlEncodedContent([new("token", "bqr_desconhecido")])));
        await OkAsync(await App().PostAsync("/api/v1/oauth/revoke", new FormUrlEncodedContent([new("token", again.Refresh)])));
        var byApp = await ConnectionAsync(again.ConnectionId);
        byApp.Status.Should().Be(AppConnectionStatus.Revoked);
        byApp.RevokedReason.Should().Be(AppConnectionLifecycle.ByAppText);
        (await ProblemCodeAsync(await Bearer(again.Access).GetAsync("/api/v1/integration/connection"), HttpStatusCode.Unauthorized)).Should().Be("invalid_token");
        (await App(secret: "errado").PostAsync("/api/v1/oauth/revoke", new FormUrlEncodedContent([new("token", again.Refresh)]))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_de_aplicativo_so_vale_no_proprio_tenant_e_so_nas_rotas_de_integracao()
    {
        await ReleaseAsync(HostB, true); // o outro tenant também tem o conecta: a recusa é pelo token, não pelo módulo
        var owner = await OwnerAsync();
        var tokens = await ConnectAsync(owner);
        (await ProblemCodeAsync(await Bearer(tokens.Access, HostB).GetAsync("/api/v1/integration/connection"), HttpStatusCode.Unauthorized)).Should().Be("invalid_token");
        (await Bearer(tokens.Access).GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "token de aplicativo não é sessão de pessoa");
        (await Bearer(tokens.Access).GetAsync("/api/v1/connected-apps/conecta")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(await owner.Client.GetAsync("/api/v1/integration/connection"), HttpStatusCode.Unauthorized)).Should().Be("invalid_token", "JWT de pessoa não vale nas rotas do aplicativo");
        (await OkAsync(await TokenAsync(App(HostB), ("grant_type", "refresh_token"), ("refresh_token", tokens.Refresh)), HttpStatusCode.BadRequest))
            .GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Modulo_nao_liberado_responde_module_not_released()
    {
        var owner = await OwnerAsync(HostB);
        var tokens = await ConnectAsync(owner, HostB);
        await ReleaseAsync(HostB, false);
        try
        {
            (await ProblemCodeAsync(await owner.Client.PostAsync("/api/v1/oauth/connect", null), HttpStatusCode.Forbidden)).Should().Be("module_not_released");
            (await ProblemCodeAsync(await owner.Client.GetAsync("/api/v1/connected-apps/conecta"), HttpStatusCode.Forbidden)).Should().Be("module_not_released");
            (await ProblemCodeAsync(await Bearer(tokens.Access, HostB).GetAsync("/api/v1/integration/connection"), HttpStatusCode.Forbidden)).Should().Be("module_not_released");
        }
        finally
        {
            await ReleaseAsync(HostB, true);
        }
    }

    [Fact]
    public async Task Canario_nada_do_codigo_verificador_segredo_ou_tokens_vai_para_o_log()
    {
        var owner = await OwnerAsync();
        var code = await AuthorizeAsync(owner);
        var body = await OkAsync(await ExchangeAsync(App(), code));
        var access = body.GetProperty("access_token").GetString()!;
        var refresh = body.GetProperty("refresh_token").GetString()!;
        await Bearer(access).GetAsync("/api/v1/integration/connection");
        await Bearer(access).GetAsync("/api/v1/me");
        await TokenAsync(App(), ("grant_type", "refresh_token"), ("refresh_token", refresh));

        List<string> lines;
        lock (Logs.Events)
        {
            lines = Logs.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(' ', e.Properties.Values.Select(v => v.ToString())) + " " + e.Exception).ToList();
        }

        lines.Should().NotBeEmpty();
        foreach (var canary in new[] { code, Verifier, ClientSecret, access, refresh, access[4..], refresh[4..] })
        {
            lines.Should().NotContain(l => l.Contains(canary, StringComparison.Ordinal));
        }
    }
}
