using System.Text;
using BemAqui.BuildingBlocks.Logging;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Webhooks;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>FND-16: registro de provedores (CA 4), recusas e alerta (CA 3), máscara do corpo (CA 15) e do log (CA 13), limites (CA 8 e 10).</summary>
public class WebhookRulesTests
{
    private sealed class NoopHandler : IWebhookHandler
    {
        public Task<WebhookHandling> HandleAsync(WebhookEventContext context, CancellationToken cancellationToken) => Task.FromResult(WebhookHandling.Processed());
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Alerts : IAlertSink
    {
        public List<Alert> Raised { get; } = [];

        public Task RaiseAsync(Alert alert, CancellationToken cancellationToken = default)
        {
            Raised.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class Sink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static WebhookProvider Provider(string code = "test", IWebhookOriginValidator? validator = null, Type? handler = null, string owner = "finance") => new()
    {
        Code = code,
        Name = "Provedor de teste",
        OwnerModule = owner,
        Validator = validator!,
        HandlerType = handler ?? typeof(NoopHandler),
    };

    [Fact]
    public void Provedor_sem_validador_e_recusado_na_subida()
    {
        WebhookProviderRegistry.Problems(Provider(validator: null)).Should().Contain("sem validador de origem");
        var subir = () => new WebhookProviderRegistry([Provider(validator: null)]);
        subir.Should().Throw<InvalidOperationException>().WithMessage("*sem validador*");
    }

    [Fact]
    public void Provedor_com_handler_errado_modulo_fora_do_catalogo_ou_codigo_invalido_e_recusado()
    {
        WebhookProviderRegistry.Problems(Provider(validator: new UrlTokenValidator(), handler: typeof(string))).Should().ContainSingle().Which.Should().Contain("IWebhookHandler");
        WebhookProviderRegistry.Problems(Provider(validator: new UrlTokenValidator(), owner: "inexistente")).Should().ContainSingle().Which.Should().Contain("módulo dono");
        WebhookProviderRegistry.Problems(Provider(code: "Inter Banco", validator: new UrlTokenValidator())).Should().ContainSingle().Which.Should().Contain("código");
        WebhookProviderRegistry.Problems(Provider(validator: new HmacSha256Validator()) with { SupportsVerificationGet = true }).Should().ContainSingle().Which.Should().Contain("GET");
    }

    [Fact]
    public void Provedor_valido_registra_e_repetido_e_recusado()
    {
        var registry = new WebhookProviderRegistry([Provider(validator: new UrlTokenValidator())]);
        registry.TryGet("test", out var found).Should().BeTrue();
        found!.ViewPermission.Should().Be("finance.view");
        found.ContentPermission.Should().Be("finance.approve");
        registry.TryGet("TEST", out _).Should().BeFalse();
        registry.TryGet(null, out _).Should().BeFalse();

        var repetido = () => new WebhookProviderRegistry([Provider(validator: new UrlTokenValidator()), Provider(validator: new UrlTokenValidator())]);
        repetido.Should().Throw<InvalidOperationException>().WithMessage("*repetido*");
    }

    [Fact]
    public void Corpo_do_Fiscal_passa_a_exigir_fiscal_approve_que_nasce_na_FIS_02a()
    {
        Provider(validator: new HmacSha256Validator(), owner: "fiscal").ContentPermission.Should().Be("fiscal.approve");
        Provider(validator: new HmacSha256Validator(), owner: "settings").ContentPermission.Should().BeNull("sem approve no catálogo, só o Proprietário");
    }

    [Fact]
    public async Task Alerta_sai_na_vigesima_recusa_em_5_minutos_uma_vez_por_janela()
    {
        var alerts = new Alerts();
        var clock = new Clock(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        using var monitor = new WebhookRejectionMonitor(alerts, clock);
        var tenant = Guid.CreateVersion7();

        for (var i = 0; i < WebhookRejectionMonitor.Threshold - 1; i++)
        {
            (await monitor.RecordAsync("test", tenant, "empresa-a")).Should().BeFalse();
        }

        (await monitor.RecordAsync("test", tenant, "empresa-a")).Should().BeTrue();
        (await monitor.RecordAsync("test", tenant, "empresa-a")).Should().BeFalse("uma vez por janela");
        (await monitor.RecordAsync("test", Guid.CreateVersion7(), "empresa-b")).Should().BeFalse("outro tenant tem contagem própria");
        alerts.Raised.Should().ContainSingle().Which.Kind.Should().Be(WebhookAlerts.Rejections);

        clock.Now += WebhookRejectionMonitor.Window;
        for (var i = 0; i < WebhookRejectionMonitor.Threshold; i++)
        {
            await monitor.RecordAsync("test", tenant, "empresa-a");
        }

        alerts.Raised.Should().HaveCount(2, "janela nova, alerta novo");
    }

    [Fact]
    public void Mascara_troca_dado_pessoal_e_deixa_valores_ids_e_datas()
    {
        const string json = """
            {
              "id": "0199f1a2-0000-7000-8000-000000000001",
              "valor": 1520.35,
              "dataPagamento": "2026-10-25",
              "situacao": "RECEBIDO",
              "pagador": { "nome": "Maria da Silva", "cpfCnpj": "529.982.247-25", "endereco": { "cep": "12280000", "cidade": "Caçapava" } },
              "chavePix": "maria@exemplo.com",
              "contatos": [ { "telefone": "(12) 99876-5432" } ],
              "observacao": "pago por joao@exemplo.com",
              "documentoSolto": "47.225.005/0001-86",
              "nomeArquivo": "boleto.pdf",
              "tipo": "nota"
            }
            """;

        var masked = JsonPersonalDataMask.MaskJson(Encoding.UTF8.GetBytes(json));

        masked.Should().NotContain("Maria").And.NotContain("529.982.247-25").And.NotContain("12280000").And.NotContain("Caçapava")
            .And.NotContain("maria@exemplo.com").And.NotContain("99876").And.NotContain("47.225.005/0001-86").And.NotContain("joao@exemplo.com");
        masked.Should().Contain("pago por ***").And.Contain("0199f1a2-0000-7000-8000-000000000001").And.Contain("1520.35").And.Contain("2026-10-25").And.Contain("RECEBIDO")
            .And.Contain("boleto.pdf").And.Contain("nota").And.Contain(JsonPersonalDataMask.Mask);
    }

    [Fact]
    public void Valor_e_nome_de_campo_pessoais_sao_reconhecidos()
    {
        JsonPersonalDataMask.MaskInline("CPF 529.982.247-25, tel. (12) 3652-1234").Should().Be("CPF ***, tel. ***");
        JsonPersonalDataMask.IsPersonalValue("joao@exemplo.com").Should().BeTrue();
        JsonPersonalDataMask.IsPersonalValue("529.982.247-25").Should().BeTrue();
        JsonPersonalDataMask.IsPersonalValue("52998224725").Should().BeTrue();
        JsonPersonalDataMask.IsPersonalValue("12345678900").Should().BeFalse("não é CPF válido: pode ser um id");
        JsonPersonalDataMask.IsPersonalValue("2026-10-25").Should().BeFalse();
        JsonPersonalDataMask.IsPersonalKey("tipo").Should().BeFalse("\"ip\" só como nome inteiro");
        JsonPersonalDataMask.IsPersonalKey("ip").Should().BeTrue();
        JsonPersonalDataMask.IsPersonalKey("razao_social").Should().BeTrue();
        JsonPersonalDataMask.IsPersonalKey("Endereço").Should().BeTrue();
    }

    [Fact]
    public void Corpo_que_nao_e_JSON_nao_e_exibido()
    {
        JsonPersonalDataMask.MaskJson(Encoding.UTF8.GetBytes("cpf=529.982.247-25")).Should().Be(JsonPersonalDataMask.NotJson);
    }

    [Fact]
    public void Log_de_webhook_fica_sem_tenantKey_token_do_caminho_e_token_da_query()
    {
        var sink = new Sink();
        using (var logger = new LoggerConfiguration().Enrich.With<WebhookUrlMask>().WriteTo.Sink(sink).CreateLogger())
        {
            logger.Information("HTTP {RequestMethod} {RequestPath} respondeu {StatusCode}", "POST", "/webhooks/inter/AbCdEfGhIjKlMnOpQrStUv/token-no-caminho-secreto", 401);
            logger.Information("Request starting {Path} {QueryString}", "/webhooks/test/AbCdEfGhIjKlMnOpQrStUv", "?t=token-secreto-da-query");
            logger.Information("URL completa {Url}", "https://plataforma.bemaquiapp.com.br/webhooks/test/AbCdEfGhIjKlMnOpQrStUv?t=token-secreto-da-query&x=1");
        }

        var rendered = string.Join("\n", sink.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)));
        rendered.Should().NotContain("AbCdEfGhIjKlMnOpQrStUv").And.NotContain("token-no-caminho-secreto").And.NotContain("token-secreto-da-query");
        rendered.Should().Contain("/webhooks/inter/***").And.Contain("/webhooks/test/***");
    }

    [Fact]
    public void Id_da_entrega_vem_do_cabecalho_ou_do_hash_do_corpo_e_texto_estranho_e_descartado()
    {
        var provider = Provider(validator: new UrlTokenValidator()) with { DeliveryIdHeader = "X-BemAqui-Delivery-Id" };
        var hash = new byte[32];

        WebhookReceiver.ExternalId(provider, new WebhookRequest(Array.Empty<byte>(), new Dictionary<string, string> { ["x-bemaqui-delivery-id"] = " d-123 " }, null, null), hash).Should().Be("d-123");
        WebhookReceiver.ExternalId(provider, new WebhookRequest(Array.Empty<byte>(), new Dictionary<string, string>(), null, null), hash).Should().StartWith("sha256:");
        WebhookReceiver.Clean("linha\nquebrada", 100).Should().BeNull();
        WebhookReceiver.Clean(new string('a', 201), 200).Should().BeNull();
        WebhookReceiver.Clean("ação", 100).Should().BeNull("só ASCII imprimível");
    }

    [Fact]
    public void Content_Type_compara_o_tipo_de_midia_sem_o_charset()
    {
        WebhookEndpoints.MediaTypeMatches("application/json; charset=utf-8", "application/json").Should().BeTrue();
        WebhookEndpoints.MediaTypeMatches("Application/JSON", "application/json").Should().BeTrue();
        WebhookEndpoints.MediaTypeMatches("text/plain", "application/json").Should().BeFalse();
        WebhookEndpoints.MediaTypeMatches(null, "application/json").Should().BeFalse();
    }

    [Fact]
    public void Erro_gravado_nao_leva_a_mensagem_da_excecao_salvo_a_mensagem_segura_do_handler()
    {
        ProcessWebhookEventJob.SafeError(new InvalidOperationException("CPF 529.982.247-25 não encontrado")).Should().NotContain("529").And.Contain("InvalidOperationException");
        ProcessWebhookEventJob.SafeError(new WebhookHandlerException("Cobrança não encontrada no banco.")).Should().Be("Cobrança não encontrada no banco.");
    }
}
