using System.Net;
using System.Text;
using BemAqui.BuildingBlocks.Webhooks;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>INT-02: regras puras dos avisos de saída (assinatura, novas tentativas, URL e IP, catálogo e corpo).</summary>
public class OutboundWebhookRulesTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"a\":1}");

    [Fact]
    public void Assinatura_com_vetor_fixo_uma_e_duas_v1()
    {
        // printf '1700000000.{"a":1}' | openssl dgst -sha256 -hmac "segredo-de-teste"
        OutboundWebhookSigner.Sign("segredo-de-teste", 1700000000, Body).Should().Be("v1=46f6b67858f936aaf560640112fd5cb71ed2236fe5db65edfc7eaa5c871dbdaf");
        OutboundWebhookSigner.Header("segredo-de-teste", "segredo-antigo", 1700000000, Body).Should()
            .Be("v1=46f6b67858f936aaf560640112fd5cb71ed2236fe5db65edfc7eaa5c871dbdaf,v1=bbb00a37dade19b85e5141c1215fb9b4385d925e252027f87a9ca6e7c185b606");
        OutboundWebhookSigner.NewSecret().Should().HaveLength(43);
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 300)]
    [InlineData(3, 1800)]
    [InlineData(4, 7200)]
    [InlineData(5, 21600)]
    [InlineData(6, 43200)]
    [InlineData(7, 86400)]
    public void Agenda_das_novas_tentativas(int attempts, int seconds)
    {
        OutboundRetryPolicy.NextDelay(attempts, null, 0).Should().Be(TimeSpan.FromSeconds(seconds));
        OutboundRetryPolicy.NextDelay(attempts, null, 1)!.Value.TotalSeconds.Should().BeApproximately(seconds * 1.1, 0.01);
        OutboundRetryPolicy.NextDelay(attempts, null, -1)!.Value.TotalSeconds.Should().BeApproximately(seconds * 0.9, 0.01);
    }

    [Fact]
    public void Oitava_falha_para_e_Retry_After_vale_ate_uma_hora()
    {
        OutboundRetryPolicy.NextDelay(8, null, 0).Should().BeNull();
        OutboundRetryPolicy.NextDelay(1, TimeSpan.FromSeconds(30), 0).Should().Be(TimeSpan.FromSeconds(30));
        OutboundRetryPolicy.NextDelay(1, TimeSpan.FromHours(5), 0).Should().Be(TimeSpan.FromHours(1));
    }

    [Theory]
    [InlineData("https://conecta.test/webhooks", null)]
    [InlineData("http://conecta.test/webhooks", OutboundUrlGuard.InvalidUrl)]
    [InlineData("https://conecta.test:8443/webhooks", OutboundUrlGuard.InvalidUrl)]
    [InlineData("https://user:senha@conecta.test/webhooks", OutboundUrlGuard.InvalidUrl)]
    [InlineData("https://outro.test/webhooks", OutboundUrlGuard.HostNotAllowed)]
    [InlineData("nao-e-url", OutboundUrlGuard.InvalidUrl)]
    public void URL_so_https_443_sem_credencial_e_host_na_lista(string url, string? expected) =>
        OutboundUrlGuard.Validate(url, ["conecta.test"], allowPrivate: false).Should().Be(expected);

    [Fact]
    public void IP_literal_privado_na_URL_e_recusado() =>
        OutboundUrlGuard.Validate("https://10.0.0.5/x", ["10.0.0.5"], allowPrivate: false).Should().Be(OutboundUrlGuard.Refused);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("0.1.2.3")]
    [InlineData("192.0.0.8")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.1")]
    [InlineData("240.0.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:a00:1::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    public void Faixas_recusadas(string ip) => OutboundUrlGuard.IsBlocked(IPAddress.Parse(ip)).Should().BeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2.25.82.127")]
    [InlineData("100.128.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700::1111")]
    public void Enderecos_publicos_passam(string ip) => OutboundUrlGuard.IsBlocked(IPAddress.Parse(ip)).Should().BeFalse();

    [Fact]
    public async Task Conexao_a_localhost_e_recusada_no_ConnectCallback()
    {
        using var client = new HttpClient(OutboundUrlGuard.CreateHandler(allowPrivate: false));
        var act = () => client.GetAsync(new Uri("http://localhost:9/"));
        (await act.Should().ThrowAsync<HttpRequestException>()).Which.InnerException.Should().BeOfType<OutboundAddressRefusedException>();
        var mapped = () => client.GetAsync(new Uri("http://[::ffff:127.0.0.1]:9/"));
        (await mapped.Should().ThrowAsync<HttpRequestException>()).Which.InnerException.Should().BeOfType<OutboundAddressRefusedException>();
    }

    [Fact]
    public void Catalogo_sem_campo_proibido_e_corpo_deterministico()
    {
        OutboundWebhookCatalog.All.SelectMany(t => t.Fields).Intersect(OutboundWebhookCatalog.ForbiddenFields).Should().BeEmpty();
        OutboundWebhookCatalog.All.Select(t => t.Type).Should().OnlyHaveUniqueItems();
        var id = Guid.Parse("01938a4c-0000-7000-8000-000000000001");
        var conn = Guid.Parse("01938a4c-0000-7000-8000-000000000002");
        var at = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.FromHours(-3));
        var data = OutboundWebhookBody.Data(new Dictionary<string, object?> { ["id"] = id, ["total"] = 10.5m, ["due_date"] = new DateOnly(2026, 12, 1) });
        var first = OutboundWebhookBody.Build(id, "billing_slip.issued", at, "konebras", conn, data, "/api/v1/integration/charges/" + id);
        var again = OutboundWebhookBody.Build(id, "billing_slip.issued", at, "konebras", conn, data, "/api/v1/integration/charges/" + id);
        again.Should().Equal(first);
        Encoding.UTF8.GetString(first).Should().Be(
            "{\"id\":\"01938a4c-0000-7000-8000-000000000001\",\"type\":\"billing_slip.issued\",\"contract\":1,\"occurred_at\":\"2026-11-27T13:00:00.000Z\",\"tenant\":\"konebras\"," +
            "\"connection_id\":\"01938a4c-0000-7000-8000-000000000002\",\"data\":{\"id\":\"01938a4c-0000-7000-8000-000000000001\",\"total\":10.5,\"due_date\":\"2026-12-01\"}," +
            "\"resource\":\"/api/v1/integration/charges/01938a4c-0000-7000-8000-000000000001\"}");
    }

    /// <summary>
    /// INT-04 CA 1: o resource de cada tipo é uma rota do contrato integration-v1 (docs/integracoes/openapi-integration-v1.json); o boleto aponta para o
    /// título. whatsapp_message.requested aponta para a rota de leitura da mensagem, que nasce com a ATD-06a (nenhum aviso desse tipo sai antes dela).
    /// </summary>
    [Fact]
    public void Resource_de_cada_tipo_e_uma_rota_do_integration_v1() => ResourcesMatchContract();

    private static void ResourcesMatchContract([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var root = Path.GetDirectoryName(here)!;
        while (!File.Exists(Path.Combine(root, "BemAquiEmpresas.slnx")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        using var contract = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "integracoes", "openapi-integration-v1.json")));
        var paths = contract.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        var id = Guid.CreateVersion7();
        var receivable = Guid.CreateVersion7();
        foreach (var type in OutboundWebhookCatalog.All.Where(t => t.Type != OutboundWebhookCatalog.WhatsAppMessageRequested))
        {
            var resource = OutboundWebhookCatalog.ResourceFor(type, id, new Dictionary<string, object?> { ["receivable_id"] = receivable });
            paths.Should().Contain(p => System.Text.RegularExpressions.Regex.IsMatch(resource, "^" + System.Text.RegularExpressions.Regex.Replace(p, @"\{[^}]+\}", "[0-9a-f-]{36}") + "$"), type.Type + " -> " + resource);
        }

        OutboundWebhookCatalog.ResourceFor(OutboundWebhookCatalog.Find("billing_slip.paid")!, id, new Dictionary<string, object?> { ["receivable_id"] = receivable })
            .Should().Be("/api/v1/integration/charges/" + receivable, "data.id continua o do boleto; o resource é o título");
        OutboundWebhookCatalog.ResourceFor(OutboundWebhookCatalog.Find("sales_order.paid")!, id).Should().Be("/api/v1/integration/orders/" + id);
        OutboundWebhookCatalog.ResourceFor(OutboundWebhookCatalog.Find(OutboundWebhookCatalog.Ping)!, id).Should().Be("/api/v1/integration/connection");
    }
}
