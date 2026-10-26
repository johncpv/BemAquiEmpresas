using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application.Inter;
using BemAqui.Finance.Domain;
using BemAqui.Finance.Infrastructure.Inter;
using BemAqui.Integration.Tests.Persistence;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>Logger que guarda a mensagem formatada e os valores estruturados (para provar o log sem segredo, CA 11).</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(';', pairs.Select(p => p.Key + "=" + p.Value)) : string.Empty;
        Lines.Enqueue(formatter(state, exception) + " | " + values + " | " + exception);
    }
}

/// <summary>
/// BNK-01a, unidade: certificado e chave (CA 4), escopos (CA 8), cliente do Inter contra o fake (CA 7, 9 a 11) com token em cache e
/// renovação sob concorrência, e a janela de 15 minutos do 2FA de novo (CA 1). Certificados gerados no próprio teste.
/// </summary>
public class InterClientUnitTests
{
    private const string Cnpj = "47.225.005/0001-86";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static InterCertificateUpload Pem(TestInterCertificate c) => new(c.CertificateBytes, c.KeyBytes, null, null);

    private static InterCertificateUpload Pfx(TestInterCertificate c, string? password = null) => new(null, null, c.Pfx, password ?? c.PfxPassword);

    [Fact]
    public void Certificado_crt_e_key_validos_viram_o_retrato_e_o_material_a_cifrar()
    {
        var c = TestInterCertificate.Create();
        var result = InterCertificateValidator.Validate(Pem(c), Cnpj, Now);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        using var validated = result.Value;
        validated.Info.Format.Should().Be(BankCertificateFormat.Pem);
        validated.Info.Thumbprint.Should().Be(c.Thumbprint);
        validated.Info.NotAfter.Should().BeCloseTo(c.NotAfter, TimeSpan.FromSeconds(1));
        validated.Certificate.HasPrivateKey.Should().BeTrue();
        validated.StoredPrivateKey.Should().Contain("PRIVATE KEY");
        validated.ToString().Should().NotContain("PRIVATE");
        using var reopened = InterCertificateValidator.Load(BankCertificateFormat.Pem, validated.StoredCertificate, validated.StoredPrivateKey, null);
        reopened.HasPrivateKey.Should().BeTrue();
    }

    [Fact]
    public void Pfx_com_senha_abre_em_memoria_e_senha_errada_recusa()
    {
        var c = TestInterCertificate.Create();
        var ok = InterCertificateValidator.Validate(Pfx(c), Cnpj, Now);
        ok.IsSuccess.Should().BeTrue(ok.Error?.Message);
        ok.Value.Info.Format.Should().Be(BankCertificateFormat.Pfx);
        ok.Value.Password.Should().Be(c.PfxPassword);
        ok.Value.Dispose();

        var wrong = InterCertificateValidator.Validate(Pfx(c, "errada"), Cnpj, Now);
        wrong.Error!.Message.Should().Be(InterCertificateValidator.PfxUnreadable);
        wrong.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Recusas_do_certificado_em_PT_BR()
    {
        var c = TestInterCertificate.Create();
        var other = TestInterCertificate.Create();
        InterCertificateValidator.Validate(new InterCertificateUpload(c.CertificateBytes, other.KeyBytes, null, null), Cnpj, Now).Error!.Message
            .Should().Be("A chave privada não corresponde a este certificado.");

        var expired = TestInterCertificate.Create(notBefore: new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero), notAfter: new DateTimeOffset(2025, 3, 10, 15, 0, 0, TimeSpan.Zero));
        InterCertificateValidator.Validate(Pem(expired), Cnpj, Now).Error!.Message.Should().Be("O certificado venceu em 10/03/2025.");

        var weak = TestInterCertificate.Create(bits: 1024);
        InterCertificateValidator.Validate(Pem(weak), Cnpj, Now).Error!.Message.Should().Be(InterCertificateValidator.WeakKey);

        var foreign = TestInterCertificate.Create(subject: "CN=Outra Empresa:11222333000181");
        InterCertificateValidator.Validate(Pem(foreign), Cnpj, Now).Error!.Message.Should().Be("O certificado é de outro CNPJ (11.222.333/0001-81). Use o certificado da conta da empresa.");
        InterCertificateValidator.Validate(Pem(foreign), null, Now).IsSuccess.Should().BeTrue("sem CNPJ do tenant não há o que comparar");
        InterCertificateValidator.Validate(Pem(TestInterCertificate.Create(subject: "CN=Sem documento")), Cnpj, Now).IsSuccess.Should().BeTrue("assunto sem CNPJ passa");

        InterCertificateValidator.Validate(new InterCertificateUpload(c.CertificateBytes, null, null, null), Cnpj, Now).Error!.Message.Should().Be(InterCertificateValidator.MissingFiles);
        InterCertificateValidator.Validate(new InterCertificateUpload(c.CertificateBytes, c.KeyBytes, c.Pfx, "x"), Cnpj, Now).Error!.Message.Should().Be(InterCertificateValidator.MissingFiles);
        InterCertificateValidator.Validate(new InterCertificateUpload(new byte[InterCertificateValidator.MaxFileBytes + 1], c.KeyBytes, null, null), Cnpj, Now).Error!.Message.Should().Be(InterCertificateValidator.TooLarge);
        InterCertificateValidator.Validate(new InterCertificateUpload(Encoding.ASCII.GetBytes("lixo"), c.KeyBytes, null, null), Cnpj, Now).Error!.Message.Should().Be(InterCertificateValidator.UnreadableCertificate);
        var encrypted = Encoding.ASCII.GetBytes("-----BEGIN ENCRYPTED PRIVATE KEY-----\nAAAA\n-----END ENCRYPTED PRIVATE KEY-----\n");
        InterCertificateValidator.Validate(new InterCertificateUpload(c.CertificateBytes, encrypted, null, null), Cnpj, Now).Error!.Message.Should().Be(InterCertificateValidator.EncryptedKey);
    }

    [Fact]
    public void Escopos_de_pagamento_recusam_e_os_que_faltam_sao_listados()
    {
        var options = new InterOptions();
        var ok = InterScopes.Evaluate(InterScopes.Parse("extrato.read boleto-cobranca.read boleto-cobranca.write webhook.read webhook.write"), options);
        ok.CanPay.Should().BeFalse();
        ok.Missing.Should().BeEmpty();

        var pays = InterScopes.Evaluate(InterScopes.Parse("extrato.read pagamento-pix.write"), options);
        pays.CanPay.Should().BeTrue();
        pays.NotAllowed.Should().Equal("pagamento-pix.write");
        InterScopes.Evaluate(InterScopes.Parse("extrato.read cob.read"), options).CanPay.Should().BeTrue("qualquer escopo fora de cobrança, extrato e webhook (D13)");

        var missing = InterScopes.Evaluate(InterScopes.Parse("extrato.read"), options);
        missing.Missing.Should().Equal("boleto-cobranca.read", "boleto-cobranca.write", "webhook.read", "webhook.write");
        InterScopes.MissingMessage(missing.Missing).Should().StartWith("Faltam escopos na credencial: Cobrança (consultar boletos)");
    }

    private sealed record Kit(InterApiClient Client, FakeInter Fake, InterHandlerCache Handlers, InterTokenCache Tokens, MutableClock Clock, CapturingLogger<InterApiClient> Logger);

    private static Kit NewKit()
    {
        var options = Options.Create(new InterOptions { HandlerDisposeDelay = TimeSpan.Zero });
        var fake = new FakeInter();
        var clock = new MutableClock(Now);
        var handlers = new InterHandlerCache(fake, options);
        var tokens = new InterTokenCache(clock);
        var logger = new CapturingLogger<InterApiClient>();
        return new Kit(new InterApiClient(handlers, tokens, fake, options, clock, logger), fake, handlers, tokens, clock, logger);
    }

    private static InterCredentials Credentials(TestInterCertificate c, string clientId = "cliente-a", string secret = "segredo-de-teste", Guid? tenant = null, Guid? integration = null, BankEnvironment environment = BankEnvironment.Sandbox) =>
        new(tenant ?? Guid.Parse("0199f1a2-0000-7000-8000-000000000001"), integration ?? Guid.Parse("0199f1a2-0000-7000-8000-0000000000aa"), environment, clientId, new Secret(secret),
            () => InterCertificateValidator.Load(BankCertificateFormat.Pem, c.CertificatePem, c.KeyPem, null), c.Thumbprint, "12345678");

    [Fact]
    public async Task Token_client_credentials_com_mTLS_e_URL_fixa_por_ambiente()
    {
        var kit = NewKit();
        var c = TestInterCertificate.Create();
        kit.Fake.Account("cliente-a");

        var test = await kit.Client.TestConnectionAsync(Credentials(c));

        test.Ok.Should().BeTrue(test.Failure?.Message);
        test.Value!.BalanceRead.Should().BeTrue();
        var token = kit.Fake.CallsTo(InterEnvironments.TokenPath).First();
        token.Host.Should().Be("cdpj-sandbox.partners.uatinter.co");
        token.Form["grant_type"].Should().Be("client_credentials");
        token.Form["client_id"].Should().Be("cliente-a");
        token.Form["scope"].Should().Contain("extrato.read").And.Contain("pagamento-pix.write", "o token de prova pede os de pagamento para recusar a credencial que paga");
        token.CertificateThumbprint.Should().Be(c.Thumbprint, "o handler leva o certificado da conta no mTLS");
        var balance = kit.Fake.CallsTo(InterEnvironments.BalancePath).Single();
        balance.Authorization.Should().StartWith("Bearer tok-");
        balance.AccountHeader.Should().Be("12345678");

        var production = await kit.Client.ProbeAsync(Credentials(c, environment: BankEnvironment.Production));
        production.Ok.Should().BeTrue();
        kit.Fake.Calls.Last().Host.Should().Be("cdpj.partners.bancointer.com.br");
    }

    [Fact]
    public async Task Pedido_de_escopo_recusado_cai_para_os_necessarios_e_credencial_errada_recusa()
    {
        var kit = NewKit();
        var c = TestInterCertificate.Create();
        kit.Fake.Account("cliente-a").RejectUnknownScopes = true;
        var probe = await kit.Client.ProbeAsync(Credentials(c));
        probe.Ok.Should().BeTrue(probe.Failure?.Message);
        probe.Value!.GrantedScopes.Should().NotContain(s => s.StartsWith("pagamento", StringComparison.Ordinal));
        kit.Fake.CallsTo(InterEnvironments.TokenPath).Should().HaveCount(2);

        var refused = await kit.Client.ProbeAsync(Credentials(c, secret: "errado"));
        refused.Failure!.Kind.Should().Be(InterFailureKind.CredentialsRefused);
        refused.Failure.Message.Should().Be("O Inter recusou o client id, o client secret ou o certificado.");
    }

    [Fact]
    public async Task Token_em_cache_uma_renovacao_por_vez_e_401_renova_uma_vez()
    {
        var kit = NewKit();
        var c = TestInterCertificate.Create();
        kit.Fake.Account("cliente-a").TokenDelayMs = 100;
        var request = new InterRequest("saldo", HttpMethod.Get, InterEnvironments.BalancePath);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => kit.Client.SendAsync(Credentials(c), request)));

        results.Should().OnlyContain(r => r.Ok && r.Value!.Status == 200);
        kit.Fake.TokenRequests.Should().Be(1, "20 chamadas juntas, uma renovação sob lock (CA 10)");
        kit.Handlers.Keys.Should().ContainSingle().Which.Should().EndWith(c.Thumbprint);

        // Até 60 s antes de vencer, usa o mesmo; depois, renova.
        kit.Clock.Now = Now.AddSeconds(3600 - 61);
        (await kit.Client.SendAsync(Credentials(c), request)).Ok.Should().BeTrue();
        kit.Fake.TokenRequests.Should().Be(1);
        kit.Clock.Now = Now.AddSeconds(3600 - 59);
        (await kit.Client.SendAsync(Credentials(c), request)).Ok.Should().BeTrue();
        kit.Fake.TokenRequests.Should().Be(2);

        // 401 na chamada: renova uma vez e repete.
        kit.Fake.Account("cliente-a").UnauthorizedOnce = 1;
        var retried = await kit.Client.SendAsync(Credentials(c), request);
        retried.Value!.Status.Should().Be(200);
        kit.Fake.TokenRequests.Should().Be(3);

        // 401 de novo depois da renovação: não entra em laço.
        kit.Fake.Account("cliente-a").UnauthorizedOnce = 2;
        (await kit.Client.SendAsync(Credentials(c), request)).Value!.Status.Should().Be(401);
        kit.Fake.TokenRequests.Should().Be(4);
    }

    [Fact]
    public async Task Um_handler_por_conta_e_tenant_descartado_ao_esquecer()
    {
        var kit = NewKit();
        var a = TestInterCertificate.Create();
        var b = TestInterCertificate.Create();
        kit.Fake.Account("cliente-a");
        kit.Fake.Account("cliente-b");
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var integration = Guid.CreateVersion7();
        var request = new InterRequest("saldo", HttpMethod.Get, InterEnvironments.BalancePath);

        await kit.Client.SendAsync(Credentials(a, tenant: tenantA, integration: integration), request);
        await kit.Client.SendAsync(Credentials(b, "cliente-b", tenant: tenantB, integration: integration), request);
        await kit.Client.SendAsync(Credentials(a, tenant: tenantA, integration: integration), request);

        kit.Handlers.Keys.Should().HaveCount(2, "mesmo id de integração em dois tenants nunca divide handler");
        kit.Fake.CallsTo(InterEnvironments.BalancePath).Select(x => x.CertificateThumbprint).Should().Equal(a.Thumbprint, b.Thumbprint, a.Thumbprint);

        kit.Client.Forget(tenantA, integration);
        kit.Handlers.Keys.Should().ContainSingle().Which.Should().StartWith(tenantB.ToString("N"));
        kit.Fake.Disposed.Should().Be(1, "o handler da conta trocada é descartado");
        kit.Tokens.Count.Should().Be(1);
    }

    [Fact]
    public async Task Allowlist_recusa_host_fora_do_Inter()
    {
        using var invoker = new HttpMessageInvoker(new InterHostAllowListHandler(new FakeOk()));
        var outside = () => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://exemplo.com/oauth/v2/token"), CancellationToken.None);
        await outside.Should().ThrowAsync<InvalidOperationException>();
        var http = () => invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://cdpj-sandbox.partners.uatinter.co/x"), CancellationToken.None);
        await http.Should().ThrowAsync<InvalidOperationException>("só HTTPS");
        (await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://cdpj-sandbox.partners.uatinter.co/x"), CancellationToken.None)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class FakeOk : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    [Fact]
    public async Task Log_da_chamada_sem_token_segredo_corpo_nem_certificado()
    {
        var kit = NewKit();
        var c = TestInterCertificate.Create();
        kit.Fake.Account("cliente-a").ClientSecret = "CANARIO-SEGREDO-123";
        await kit.Client.TestConnectionAsync(Credentials(c, secret: "CANARIO-SEGREDO-123"));
        kit.Fake.Account("cliente-a").ClientSecret = "outro";
        await kit.Client.ProbeAsync(Credentials(c, secret: "CANARIO-SEGREDO-123"));

        var log = string.Join('\n', kit.Logger.Lines);
        kit.Logger.Lines.Should().NotBeEmpty();
        log.Should().Contain("token").And.Contain("saldo").And.Contain("corr-", "id de correlação do Inter");
        log.Should().NotContain("CANARIO-SEGREDO-123").And.NotContain("tok-").And.NotContain("Bearer").And.NotContain("PRIVATE KEY")
            .And.NotContain("Maria da Silva").And.NotContain("52998224725").And.NotContain("client_secret").And.NotContain(c.Thumbprint);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    [InlineData(-5, false)]
    public void Prova_do_2FA_vale_por_15_minutos(int minutesAgo, bool valid)
    {
        var at = Now.AddMinutes(-minutesAgo);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "x"), new Claim("amr", "otp"), new Claim("otp_at", at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))], "jwt"));

        RecentTwoFactorEndpoints.HasRecentProof(user, Now).Should().Be(valid);
    }

    [Fact]
    public void Sem_prova_sessao_por_PIN_ou_anonimo_nao_passam()
    {
        var proof = Now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        RecentTwoFactorEndpoints.HasRecentProof(new ClaimsPrincipal(new ClaimsIdentity([new Claim("amr", "otp")], "jwt")), Now).Should().BeFalse();
        RecentTwoFactorEndpoints.HasRecentProof(new ClaimsPrincipal(new ClaimsIdentity([new Claim("amr", "pin"), new Claim("otp_at", proof)], "jwt")), Now).Should().BeFalse();
        RecentTwoFactorEndpoints.HasRecentProof(new ClaimsPrincipal(new ClaimsIdentity([new Claim("otp_at", proof)])), Now).Should().BeFalse("não autenticado");
        RecentTwoFactorEndpoints.IsEnrolled(new ClaimsPrincipal(new ClaimsIdentity([new Claim("amr", "pwd")], "jwt"))).Should().BeFalse();
    }
}
