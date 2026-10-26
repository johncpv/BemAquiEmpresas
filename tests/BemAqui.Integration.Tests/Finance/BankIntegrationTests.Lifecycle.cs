using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BemAqui.Integration.Tests.Finance.BankIntegrationFixture;

namespace BemAqui.Integration.Tests.Finance;

public partial class BankIntegrationTests
{
    [Fact]
    public async Task Producao_sem_a_flag_salva_e_testa_mas_efeito_responde_409_e_com_a_flag_pede_confirmacao()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (owner, account, clientId) = await SetupAsync();

        // CA 12: sem bank_production_allowed, salva e testa (D−1 da GOL-01b) ...
        var saved = await SaveAsync(owner, account, clientId, Canary, TestInterCertificate.Create(), environment: "Production");
        saved.GetProperty("situation").GetString().Should().Be("Production");
        saved.GetProperty("productionAllowed").GetBoolean().Should().BeFalse();
        (await JsonAsync(await owner.PostAsync($"{Base}/{account}/test", null))).GetProperty("ok").GetBoolean().Should().BeTrue();
        fixture.Inter.Calls.Last().Host.Should().Be("cdpj.partners.bancointer.com.br", "URL fixa de produção");

        // ... mas emitir, registrar webhook e importar extrato respondem 409.
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var effect = await scope.ServiceProvider.GetRequiredService<BankIntegrationService>().EffectsAllowedAsync(account);
            effect.Error!.Code.Should().Be("bank_production_not_allowed");
            effect.Error.Message.Should().Be("A cobrança real será ligada na virada do sistema.");
        }

        // Com a flag (GOL-01b), ligar a produção pede o diálogo "Ligar a cobrança real".
        await fixture.SetProductionAllowedAsync(tenant, true);
        try
        {
            var other = await BankAccountAsync(owner);
            var unconfirmed = await owner.PutAsync($"{Base}/{other}", Form(clientId, Canary, TestInterCertificate.Create(), "Production"));
            unconfirmed.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var problem = await JsonAsync(unconfirmed);
            Code(problem).Should().Be("bank_production_confirmation_required");
            problem.GetProperty("detail").GetString().Should().Be("Boletos emitidos em produção são cobranças reais ao cliente.");
            (await SaveAsync(owner, other, clientId, Canary, TestInterCertificate.Create(), "Production", confirm: true)).GetProperty("productionAllowed").GetBoolean().Should().BeTrue();
            await using var scope = await fixture.ScopeAsync(tenant);
            (await scope.ServiceProvider.GetRequiredService<BankIntegrationService>().EffectsAllowedAsync(other)).IsSuccess.Should().BeTrue();
        }
        finally
        {
            await fixture.SetProductionAllowedAsync(tenant, false);
        }
    }

    [Fact]
    public async Task Troca_testa_a_credencial_nova_antes_e_a_falha_mantem_as_atuais()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (owner, account, clientId) = await SetupAsync();
        var first = TestInterCertificate.Create();
        await SaveAsync(owner, account, clientId, Canary, first);
        await owner.PostAsync($"{Base}/{account}/test", null);
        var integrationId = await fixture.QueryAsync(tenant, db => db.BankIntegrations.Where(i => i.BankAccountId == account).Select(i => i.Id).SingleAsync());
        var cache = fixture.Factory.Services.GetRequiredService<BemAqui.Finance.Infrastructure.Inter.InterHandlerCache>();
        cache.Keys.Should().Contain(k => k.Contains(integrationId.ToString("N"), StringComparison.Ordinal) && k.EndsWith(first.Thumbprint, StringComparison.Ordinal));

        // Credencial nova recusada pelo Inter: 422 e as atuais continuam.
        var second = TestInterCertificate.Create();
        fixture.Inter.Account(clientId).RequiredThumbprint = first.Thumbprint;
        var failed = await owner.PostAsync($"{Base}/{account}/rotate", Form(string.Empty, string.Empty, second, environment: string.Empty));
        failed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(failed)).GetProperty("detail").GetString().Should().StartWith("As credenciais novas não passaram no teste e as atuais continuam valendo.");
        (await fixture.QueryAsync(tenant, db => db.BankIntegrations.Where(i => i.Id == integrationId).Select(i => i.CertificateThumbprint).SingleAsync())).Should().Be(first.Thumbprint);
        (await JsonAsync(await owner.PostAsync($"{Base}/{account}/test", null))).GetProperty("ok").GetBoolean().Should().BeTrue("a atual segue funcionando");

        // Passou: troca na mesma transação, descarta o handler e o token, avisa o Proprietário.
        fixture.Inter.Account(clientId).RequiredThumbprint = null;
        var rotated = await owner.PostAsync($"{Base}/{account}/rotate", Form(string.Empty, string.Empty, second, environment: string.Empty));
        rotated.StatusCode.Should().Be(HttpStatusCode.OK, await rotated.Content.ReadAsStringAsync());
        var detail = await JsonAsync(rotated);
        detail.GetProperty("certificate").GetProperty("thumbprint").GetString().Should().Be(second.Thumbprint);
        detail.GetProperty("rotatedAt").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Null);
        cache.Keys.Should().NotContain(k => k.Contains(integrationId.ToString("N"), StringComparison.Ordinal), "o handler da credencial antiga sai do cache (CA 9)");
        (await fixture.QueryAsync(tenant, db => db.Notifications.AnyAsync(n => n.Link == BankIntegrationService.Link(account) && n.Title.StartsWith("Credenciais do Inter trocadas por")))).Should().BeTrue();

        await owner.PostAsync($"{Base}/{account}/test", null);
        fixture.Inter.Calls.Last().CertificateThumbprint.Should().Be(second.Thumbprint, "o handler novo usa o certificado novo");

        // Trocar só o client secret: o certificado continua; a combinação nova é testada.
        fixture.Inter.Account(clientId).ClientSecret = "segredo-novo";
        var secretOnly = await owner.PostAsync($"{Base}/{account}/rotate", Form(string.Empty, "segredo-novo", null, environment: string.Empty));
        secretOnly.StatusCode.Should().Be(HttpStatusCode.OK, await secretOnly.Content.ReadAsStringAsync());
        (await JsonAsync(await owner.PostAsync($"{Base}/{account}/test", null))).GetProperty("ok").GetBoolean().Should().BeTrue();

        var nothing = await owner.PostAsync($"{Base}/{account}/rotate", Form(string.Empty, string.Empty, null, environment: string.Empty));
        Code(await JsonAsync(nothing)).Should().Be("bank_rotation_empty");
    }

    [Fact]
    public async Task Desligar_apaga_os_segredos_tira_o_handler_e_a_conta_volta_a_None()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (owner, account, clientId) = await SetupAsync();
        await SaveAsync(owner, account, clientId, Canary, TestInterCertificate.Create());
        await owner.PostAsync($"{Base}/{account}/test", null);
        var (ownerId, _, stamp) = await fixture.ActorAsync(tenant, HostA, owner: true, recentTwoFactor: false);
        (await fixture.Client(tenant, HostA, ownerId, stamp, null).DeleteAsync($"{Base}/{account}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "desligar pede o 2FA de novo");

        (await owner.DeleteAsync($"{Base}/{account}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var row = await fixture.QueryAsync(tenant, db => db.BankIntegrations.AsNoTracking().SingleAsync(i => i.BankAccountId == account));
        row.Status.Should().Be(BankIntegrationStatus.Disabled);
        new[] { row.ClientIdCipher, row.ClientSecretCipher, row.CertificateCipher, row.PrivateKeyCipher, row.CertificatePasswordCipher, row.CertificateThumbprint }.Should().OnlyContain(v => v == null);
        (await fixture.QueryAsync(tenant, db => db.BankAccounts.Where(a => a.Id == account).Select(a => a.Provider).SingleAsync())).Should().Be(BankProvider.None);
        fixture.Factory.Services.GetRequiredService<BemAqui.Finance.Infrastructure.Inter.InterHandlerCache>().Keys.Should().NotContain(k => k.Contains(row.Id.ToString("N"), StringComparison.Ordinal));
        (await JsonAsync(await owner.GetAsync($"{Base}/{account}"))).GetProperty("configured").GetBoolean().Should().BeFalse();
        Code(await JsonAsync(await owner.PostAsync($"{Base}/{account}/test", null))).Should().Be("bank_integration_not_configured");
        (await fixture.QueryAsync(tenant, db => db.AuditEntries.CountAsync(e => e.Entity == nameof(BankIntegration) && e.EntityId == row.Id.ToString()))).Should().BeGreaterThanOrEqualTo(2);

        // Religar na mesma conta (mesma linha).
        (await SaveAsync(owner, account, clientId, Canary, TestInterCertificate.Create())).GetProperty("integrationId").GetGuid().Should().Be(row.Id);
    }

    [Fact]
    public async Task Vencimento_avisa_por_limiar_sem_repetir_por_tenant_e_vencido_alerta_e_bloqueia_efeito()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (owner, account, clientId) = await SetupAsync();
        await SaveAsync(owner, account, clientId, Canary, TestInterCertificate.Create(notAfter: DateTimeOffset.UtcNow.AddDays(10)));
        var (_, ownerB, _) = await fixture.ActorAsync(fixture.Tenancy.TenantB, HostB, owner: true, recentTwoFactor: true);
        var accountB = await BankAccountAsync(ownerB);
        await SaveAsync(ownerB, accountB, clientId, Canary, TestInterCertificate.Create(subject: "CN=Empresa B", notAfter: DateTimeOffset.UtcNow.AddDays(20)));
        (await JsonAsync(await owner.GetAsync(Base))).GetProperty("items").EnumerateArray().Single(r => r.GetProperty("bankAccountId").GetGuid() == account)
            .GetProperty("situation").GetString().Should().Be("ExpiringSoon");

        var job = ActivatorUtilities.CreateInstance<InterCertificateExpiryJob>(fixture.Factory.Services);
        await job.ExecuteAsync(tenant.Id, new InterCertificateExpiryArgs(), CancellationToken.None);
        await job.ExecuteAsync(tenant.Id, new InterCertificateExpiryArgs(), CancellationToken.None);
        await job.ExecuteAsync(fixture.Tenancy.TenantB.Id, new InterCertificateExpiryArgs(), CancellationToken.None);

        var dedup = $"inter-cert:{account}:15";
        var noticesA = await fixture.QueryAsync(tenant, db => db.Notifications.AsNoTracking().Where(n => n.DedupKey == dedup).ToListAsync());
        noticesA.Should().NotBeEmpty();
        noticesA.GroupBy(n => n.UserId).Should().OnlyContain(g => g.Count() == 1, "execução dupla não repete o aviso");
        noticesA[0].Title.Should().MatchRegex(@"O certificado do Inter da conta .+ vence em 10 dias\.");
        noticesA[0].Link.Should().Be(BankIntegrationService.Link(account) + "?acao=trocar");
        (await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.Notifications.CountAsync(n => n.DedupKey == $"inter-cert:{accountB}:30"))).Should().BeGreaterThan(0, "cada tenant no seu banco");

        // Vencido: aviso, alerta bank_integration e emissão com 409 bank_integration_expired.
        await fixture.QueryAsync(tenant, db => db.BankIntegrations.Where(i => i.BankAccountId == account).ExecuteUpdateAsync(s => s.SetProperty(i => i.CertificateNotAfter, DateTimeOffset.UtcNow.AddMinutes(-5))));
        await job.ExecuteAsync(tenant.Id, new InterCertificateExpiryArgs(), CancellationToken.None);
        fixture.Alerts.Raised.Should().Contain(a => a.Kind == AlertKinds.BankIntegration && a.Tenant == tenant.Key);
        (await fixture.QueryAsync(tenant, db => db.Notifications.AnyAsync(n => n.DedupKey == $"inter-cert:{account}:vencido"))).Should().BeTrue();
        await using var scope = await fixture.ScopeAsync(tenant);
        (await scope.ServiceProvider.GetRequiredService<BankIntegrationService>().EffectsAllowedAsync(account)).Error!.Code.Should().Be("bank_integration_expired");
        InterCertificateExpiryNotifier.ThresholdFor(31).Should().BeNull();
        InterCertificateExpiryNotifier.ThresholdFor(30).Should().Be(30);
        InterCertificateExpiryNotifier.ThresholdFor(6).Should().Be(7);
        InterCertificateExpiryNotifier.ThresholdFor(1).Should().Be(1);
    }

    [Fact]
    public async Task Confirmar_o_codigo_troca_so_o_access_token_e_libera_a_acao()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (owner, account, clientId) = await SetupAsync();
        var (ownerId, _, _) = await fixture.ActorAsync(tenant, HostA, owner: true, recentTwoFactor: false);
        var secret = await fixture.EnableTotpAsync(tenant, ownerId);
        var stamp = await fixture.StampAsync(tenant, ownerId);
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = HostA;
        var login = BemAqui.Integration.Tests.Tenancy.TestTokens.Create(tenant.Id, HostA, userId: ownerId, stamp: stamp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login);

        // Sem a prova (token sem otp_at): 403 e o front abre o passo do código.
        var refused = await client.PutAsync($"{Base}/{account}", Form(clientId, Canary, TestInterCertificate.Create()));
        Code(await JsonAsync(refused)).Should().Be("two_factor_required");

        var wrong = await client.PostAsJsonAsync("/api/v1/me/2fa/step-up", new { code = "000000" });
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var code = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow));
        var stepUp = await client.PostAsJsonAsync("/api/v1/me/2fa/step-up", new { code });
        stepUp.StatusCode.Should().Be(HttpStatusCode.OK, await stepUp.Content.ReadAsStringAsync());
        stepUp.Headers.CacheControl!.NoStore.Should().BeTrue();
        stepUp.Headers.Contains("Set-Cookie").Should().BeFalse("a sessão (refresh) não muda");
        var token = (await JsonAsync(stepUp)).GetProperty("accessToken").GetString()!;
        var claims = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().ReadJsonWebToken(token).Claims.ToList();
        claims.Should().Contain(c => c.Type == AuthMethods.OtpAtClaim);
        claims.Where(c => c.Type == "amr").Select(c => c.Value).Should().Contain("otp");
        (await client.PostAsJsonAsync("/api/v1/me/2fa/step-up", new { code })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o mesmo código não vale duas vezes");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.PutAsync($"{Base}/{account}", Form(clientId, Canary, TestInterCertificate.Create()))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Sem 2FA ativo, o passo de confirmar responde o texto de ativar.
        var (_, plain, _) = await fixture.ActorAsync(tenant, HostA, owner: true, recentTwoFactor: false);
        var notEnrolled = await plain.PostAsJsonAsync("/api/v1/me/2fa/step-up", new { code = "123456" });
        notEnrolled.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(notEnrolled)).GetProperty("detail").GetString().Should().Be(RecentTwoFactorEndpoints.EnrollMessage);
    }

    [Fact]
    public async Task Log_das_chamadas_ao_Inter_sem_token_segredo_corpo_nem_certificado()
    {
        var (owner, account, clientId) = await SetupAsync();
        var certificate = TestInterCertificate.Create();
        await SaveAsync(owner, account, clientId, Canary, certificate);
        await owner.PostAsync($"{Base}/{account}/test", null);

        var logs = fixture.RenderedLogs();
        logs.Should().Contain("Inter token").And.Contain("Inter saldo");
        logs.Should().NotContain(Canary).And.NotContain("tok-").And.NotContain("Bearer ").And.NotContain("PRIVATE KEY").And.NotContain("BEGIN CERTIFICATE")
            .And.NotContain("Maria da Silva").And.NotContain("52998224725").And.NotContain(certificate.Thumbprint);
    }

    [Fact]
    public void Acoes_da_D6_que_existem_hoje_tem_o_2FA_de_novo()
    {
        string[] d6 = ["PayPaymentOrder", "ReversePaymentOrder", "SaveBankIntegration", "RotateBankIntegration", "DisableBankIntegration"];
        var endpoints = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IEndpointNameMetadata>()?.EndpointName is { } name && d6.Contains(name)).ToList();

        endpoints.Select(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName).Should().BeEquivalentTo(d6);
        endpoints.Should().OnlyContain(e => e.Metadata.GetMetadata<RequiresRecentTwoFactorMetadata>() != null);
        var tested = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "TestBankIntegration");
        tested.Metadata.GetMetadata<RequiresRecentTwoFactorMetadata>().Should().BeNull("testar não tem efeito: não pede o código");
    }
}
