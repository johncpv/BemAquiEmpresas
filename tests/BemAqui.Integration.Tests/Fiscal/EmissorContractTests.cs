using System.Globalization;
using System.Text;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using BemAqui.Fiscal.Infrastructure.Emissor;
using BemAqui.Integration.Tests.Observability;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>Logger que guarda as mensagens formatadas (canário de log, CA 12).</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Messages)
        {
            Messages.Add(formatter(state, exception) + " " + exception);
        }
    }
}

/// <summary>
/// Suíte de contrato do adaptador (FIS-01 CA 4 e 15, no CI): o <c>EmissorFiscalHttpEmitter</c> real contra o fake HTTP do Emissor, com as
/// respostas do guia de integração de lá: 201, 202 (1 e 6), 422 (3 e 5), 400, 401, 403, 409, 429, o <c>GET /me</c>, a consulta forçada,
/// a DANFE só em PDF, o envio do A1 por formulário e o webhook assinado com timestamp.
/// </summary>
public sealed class EmissorContractTests : IAsyncLifetime
{
    private FakeEmissorServer _server = null!;
    private readonly ListLogger<EmissorFiscalHttpEmitter> _logs = new();

    public async ValueTask InitializeAsync() => _server = await FakeEmissorServer.StartAsync();

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    private IFiscalEmitter Emitter(string key = FakeEmissorServer.ValidKey) =>
        new EmissorFiscalEmitterFactory(new FakeEmissorHttpClients(_server), Options.Create(new FiscalIntegrationOptions()),
                new IntegrationHealthTracker(new CapturingAlertSink(), Options.Create(new AlertOptions())), TimeProvider.System, _logs)
            .Create(new FiscalEmitterConnection(new Uri("http://emissor-fiscal:8080/"), new Secret(key), Guid.Parse("0199f1a2-0000-7000-8000-000000000001")));

    private static NfeIssueRequest Request(string scenario) => FiscalSamples.Sample(scenario);

    [Fact]
    public async Task Autorizada_201_com_chaves_no_cabecalho_e_o_JSON_do_Emissor()
    {
        var result = await Emitter().IssueNfeAsync(Request("AUTORIZAR"), "nfe-0199-1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(FiscalDocumentStatus.Authorized);
        result.Value.AccessKey.Should().Be(FakeEmissorServer.AuthorizedKey);
        result.Value.Protocol.Should().Be("135260000000001");
        result.Value.Number.Should().Be(42);
        var sent = _server.Requests.Single(r => r.Path == "/api/v1/nfe");
        sent.ApiKey.Should().Be(FakeEmissorServer.ValidKey);
        sent.Idempotency.Should().Be("nfe-0199-1");
        var json = JsonText.Parse(sent.Body);
        json.GetProperty("tipoOperacao").GetInt32().Should().Be(1);
        json.GetProperty("emitente").GetString().Should().Be("47225005000186");
        json.GetProperty("itens")[0].GetProperty("valorTotal").GetRawText().Should().Be("1000.00");
        json.GetProperty("itens")[0].GetProperty("quantidade").GetRawText().Should().Be("1.0000");
    }

    [Theory]
    [InlineData("PROCESSAR", FiscalDocumentStatus.Processing)]
    [InlineData("CONTINGENCIA", FiscalDocumentStatus.ContingencyPending)]
    [InlineData("REJEITAR", FiscalDocumentStatus.Rejected)]
    [InlineData("DENEGAR", FiscalDocumentStatus.Denied)]
    public async Task Situacao_202_e_422_vira_documento(string scenario, FiscalDocumentStatus expected)
    {
        var result = await Emitter().IssueNfeAsync(Request(scenario), "nfe-" + scenario);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(expected);
        if (expected == FiscalDocumentStatus.Rejected)
        {
            result.Value.Rejection.Should().BeEquivalentTo(new FiscalRejection(225, "Rejeicao: Falha no Schema XML", "Reporte com o número da nota."));
        }
    }

    [Fact]
    public async Task Erro_400_vira_validacao_campo_a_campo()
    {
        var result = await Emitter().IssueNfeAsync(Request("INVALIDO"), "nfe-x-1");

        result.Error!.Code.Should().Be(FiscalErrorCodes.Validation);
        result.Error.Fields!.Keys.Should().Contain(["emitente", "itens"]);
    }

    [Theory]
    [InlineData("chave-recusada-000000000000")]
    [InlineData("chave-sem-escopo-0000000000000")]
    public async Task Chave_recusada_401_ou_sem_escopo_403_vira_integracao_mal_configurada(string key)
    {
        (await Emitter(key).IssueNfeAsync(Request("AUTORIZAR"), "nfe-x-1")).Error!.Code.Should().Be(FiscalErrorCodes.IntegrationMisconfigured);
        (await Emitter(key).GetAccountAsync()).Error!.Code.Should().Be(FiscalErrorCodes.IntegrationMisconfigured);
    }

    [Fact]
    public async Task Conflito_409_limite_429_e_5xx_na_emissao_nao_tentam_de_novo()
    {
        (await Emitter().IssueNfeAsync(Request("CONFLITO"), "nfe-c-1")).Error!.Code.Should().Be(FiscalErrorCodes.Conflict);

        var limited = await Emitter().IssueNfeAsync(Request("LIMITE"), "nfe-l-1");
        limited.Error!.Code.Should().Be(FiscalErrorCodes.RateLimited);
        limited.Error.Data!["retryAfterSeconds"].Should().Be(7);

        var down = await Emitter().IssueNfeAsync(Request("QUEDA"), "nfe-q-1");
        down.Error!.Code.Should().Be(FiscalErrorCodes.Unavailable);
        _server.Requests.Count(r => r.Idempotency == "nfe-q-1").Should().Be(1, "escrita nunca tenta de novo: a dúvida fica com quem chamou, que reenvia a mesma chave");
    }

    [Fact]
    public async Task Leitura_tenta_de_novo_uma_vez_em_falha_transitoria()
    {
        _server.ReadFailuresBeforeSuccess = 1;

        var result = await Emitter().GetNfeAsync(FakeEmissorServer.AuthorizedKey);

        result.IsSuccess.Should().BeTrue();
        _server.Requests.Count(r => r.Method == "GET" && r.Path.EndsWith(FakeEmissorServer.AuthorizedKey, StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public async Task Me_traz_tenant_escopos_emitente_ambiente_e_certificado()
    {
        var account = (await Emitter().GetAccountAsync()).Value;

        account.TenantId.Should().Be(FakeEmissorServer.TenantId);
        account.Scopes.Should().BeEquivalentTo(["nfe:emitir", "nfe:eventos", "consultar"]);
        var issuer = account.Issuers.Single();
        issuer.Document.Should().Be("47225005000186");
        issuer.Environment.Should().Be(FiscalEnvironment.Homologation);
        issuer.Certificate.ValidUntil.Should().Be(DateTimeOffset.Parse("2027-03-10T23:59:59+00:00", CultureInfo.InvariantCulture));
        FiscalAccountCheck.Evaluate(account, "47.225.005/0001-86", BemAqui.Platform.Contracts.FiscalEnvironmentAllowance.Homologation).Ok.Should().BeTrue();

        var admin = (await Emitter("chave-admin-000000000000000").GetAccountAsync()).Value;
        FiscalAccountCheck.Evaluate(admin, "47225005000186", BemAqui.Platform.Contracts.FiscalEnvironmentAllowance.Homologation).Message.Should().Be("Use uma chave sem o escopo admin.");
    }

    [Fact]
    public async Task Consulta_forcada_xml_e_status_da_SEFAZ()
    {
        (await Emitter().ForceConsultAsync(FakeEmissorServer.AuthorizedKey)).Value.Status.Should().Be(FiscalDocumentStatus.Authorized);
        _server.Requests.Should().Contain(r => r.Method == "POST" && r.Path == "/api/v1/nfe/" + FakeEmissorServer.AuthorizedKey + "/consultar");
        var xml = (await Emitter().GetXmlAsync(FakeEmissorServer.AuthorizedKey)).Value;
        Encoding.UTF8.GetString(xml.Content).Should().Contain(FakeEmissorServer.AuthorizedKey);
        (await Emitter().SefazStatusAsync("47.225.005/0001-86")).Value.Online.Should().BeFalse();
        _server.Requests.Should().Contain(r => r.Path == "/api/v1/sefaz/status");
        (await Emitter().ForceConsultAsync("35261047225005000186550020000000000000000000")).Error!.Code.Should().Be(FiscalErrorCodes.NotFound);
    }

    [Fact]
    public async Task DANFE_so_em_PDF_e_nunca_em_HTML()
    {
        (await Emitter().GetDanfePdfAsync(FakeEmissorServer.AuthorizedKey)).Value.ContentType.Should().Be("application/pdf");
        (await Emitter().GetDanfePdfAsync(FakeEmissorServer.HtmlDanfeKey)).IsFailure.Should().BeTrue("o HTML carrega texto do cliente (XSS)");
    }

    [Fact]
    public async Task Certificado_vai_por_formulario_sem_a_senha_na_URL()
    {
        var result = await Emitter().UploadCertificateAsync("47.225.005/0001-86", "PFX-BYTES"u8.ToArray(), new Secret("senha-do-pfx"));

        result.IsSuccess.Should().BeTrue();
        var sent = _server.Requests.Single(r => r.Path.Contains("/certificado", StringComparison.Ordinal));
        sent.Path.Should().Be("/api/v1/emitentes/47225005000186/certificado");
        sent.Body.Should().StartWith("multipart/form-data");
        _logs.Messages.Should().NotContain(m => m.Contains("senha-do-pfx", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Eventos_e_inutilizacao()
    {
        (await Emitter().CancelNfeAsync(FakeEmissorServer.AuthorizedKey, "Pedido cancelado a pedido do cliente")).Value.Accepted.Should().BeTrue();
        (await Emitter().InvalidateRangeAsync(new NumberRangeInvalidation("47225005000186", 2, 10, 12, "Numeração pulada por falha do sistema"))).Value.Accepted.Should().BeTrue();
        JsonText.Parse(_server.Requests.Single(r => r.Path.EndsWith("/cancelamento", StringComparison.Ordinal)).Body).GetProperty("justificativa").GetString().Should().Contain("cliente");
    }

    [Fact]
    public async Task Log_traz_operacao_e_situacao_sem_chave_payload_ou_XML()
    {
        await Emitter().IssueNfeAsync(Request("REJEITAR"), "nfe-log-1");
        await Emitter().GetXmlAsync(FakeEmissorServer.AuthorizedKey);

        var text = string.Join("\n", _logs.Messages);
        text.Should().Contain("nfe.emitir").And.Contain("422").And.Contain("225");
        text.Should().NotContain(FakeEmissorServer.ValidKey).And.NotContain("CLIENTE DE TESTE").And.NotContain("fiscal@exemplo.com.br").And.NotContain("<nfeProc");
    }

    [Fact]
    public void Webhook_assinado_pelo_Emissor_com_timestamp_e_aceito_e_o_antigo_e_recusado()
    {
        const string secret = "segredo-do-webhook-de-teste-0001";
        var body = "{\"tipo\":\"nfe.autorizada\",\"tenantId\":\"" + FakeEmissorServer.TenantId + "\",\"documentoId\":\"" + FakeEmissorServer.DocumentId + "\"}";
        var now = DateTimeOffset.UtcNow;
        var validator = (TimestampedSignatureValidator)EmissorFiscalWebhook().Validator;
        WebhookRequest Signed(long ts) => new(Encoding.UTF8.GetBytes(body), new Dictionary<string, string>
        {
            ["X-BemAqui-Signature"] = FakeEmissorServer.Sign(secret, body, ts),
            ["X-BemAqui-Timestamp"] = ts.ToString(CultureInfo.InvariantCulture),
        }, null, null);
        var keys = new WebhookKeys(Encoding.UTF8.GetBytes(secret), null, null);

        validator.Validate(Signed(now.ToUnixTimeSeconds()), keys, now).Should().Be(WebhookOriginMatch.Current);
        validator.Validate(Signed(now.AddMinutes(-6).ToUnixTimeSeconds()), keys, now).Should().Be(WebhookOriginMatch.Invalid);
    }

    private static WebhookProvider EmissorFiscalWebhook() => BemAqui.Fiscal.Application.Invoices.EmissorFiscalWebhook.Provider;
}

/// <summary>
/// Suíte manual contra a homologação do EmissorFiscal no tenant <c>demo</c> (CA 15; nunca o <c>konebras</c>), fora do CI padrão: só roda
/// com <c>EMISSOR_HOMOLOG_URL</c> e <c>EMISSOR_HOMOLOG_KEY</c> (chave do demo, sem admin). Confere o <c>GET /me</c> e o
/// <c>tipoOperacao</c> de entrada e saída pela prévia (L-l), que não gasta número. Roteiro no runbook (docs/operacao/emissor-fiscal.md).
/// </summary>
[Trait("contract", "emissor-homolog")]
public sealed class EmissorHomologContractTests
{
    private static string? Url => Environment.GetEnvironmentVariable("EMISSOR_HOMOLOG_URL");

    private static string? Key => Environment.GetEnvironmentVariable("EMISSOR_HOMOLOG_KEY");

    private static IFiscalEmitter Emitter() =>
        new EmissorFiscalEmitterFactory(new RealHttpClients(), Options.Create(new FiscalIntegrationOptions()), new IntegrationHealthTracker(new CapturingAlertSink(), Options.Create(new AlertOptions())),
            TimeProvider.System, new ListLogger<EmissorFiscalHttpEmitter>()).Create(new FiscalEmitterConnection(new Uri(Url!), new Secret(Key!), Guid.Empty));

    private sealed class RealHttpClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SocketsHttpHandler { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Me_da_chave_do_demo_nao_tem_admin_e_tem_os_escopos()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Url) || string.IsNullOrEmpty(Key), "Sem EMISSOR_HOMOLOG_URL/KEY: suíte manual da homologação (runbook).");

        var account = await Emitter().GetAccountAsync(TestContext.Current.CancellationToken);

        account.IsSuccess.Should().BeTrue(account.Error?.Message);
        FiscalScopes.HasAdmin(account.Value.Scopes).Should().BeFalse();
        FiscalScopes.Missing(account.Value.Scopes).Should().BeEmpty();
        account.Value.Issuers.Should().OnlyContain(i => i.Environment == FiscalEnvironment.Homologation, "nada de produção antes da GOL-01b");
    }

    [Theory]
    [InlineData(NfeOperationType.Outbound)]
    [InlineData(NfeOperationType.Inbound)]
    public async Task Previa_aceita_o_tipoOperacao_de_entrada_e_saida(NfeOperationType type)
    {
        Assert.SkipWhen(string.IsNullOrEmpty(Url) || string.IsNullOrEmpty(Key), "Sem EMISSOR_HOMOLOG_URL/KEY: suíte manual da homologação (runbook).");
        var issuer = (await Emitter().GetAccountAsync(TestContext.Current.CancellationToken)).Value.Issuers[0].Document;

        var preview = await Emitter().PreviewAsync(FiscalSamples.Sample("VENDA DE MERCADORIA") with { IssuerDocument = issuer, OperationType = type },
            TestContext.Current.CancellationToken);

        preview.Error?.Fields?.Keys.Should().NotContain("tipoOperacao");
    }
}
