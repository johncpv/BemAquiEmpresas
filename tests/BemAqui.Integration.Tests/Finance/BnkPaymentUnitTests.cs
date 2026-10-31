using System.Text;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>BNK-02, unidade: divisão do valor pago em principal, juros, multa e desconto (CA 7 e 9) e a chave composta do aviso (CA 6).</summary>
public class BnkPaymentUnitTests
{
    // Boleto de R$ 1.000,00 com vencimento em 10/10, multa 2%, juros 1% ao mês (de 10/10) e R$ 50,00 de desconto até 05/10.
    private static readonly BillingSlipPaymentTerms Terms = new(1000m, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10), 2m, 1m, 50m, new DateOnly(2026, 10, 5));

    public static TheoryData<decimal, string, bool, decimal, decimal, decimal, decimal, string?> Cases => new()
    {
        // pago, crédito, automático, principal, juros, multa, desconto, motivo
        { 1000m, "2026-10-08", true, 1000m, 0m, 0m, 0m, null },
        { 1000m, "2026-10-20", true, 1000m, 0m, 0m, 0m, null }, // banco não cobrou: pagou o nominal
        { 1023.33m, "2026-10-20", true, 1000m, 3.33m, 20m, 0m, null }, // 10 dias: 2% + 1%/30 × 10
        { 1010m, "2026-10-20", true, 1000m, 0m, 10m, 0m, null }, // pagou parte da multa
        { 1030m, "2026-10-20", false, 0m, 0m, 0m, 0m, BillingSlipSettlement.Overpaid },
        { 1005m, "2026-10-08", false, 0m, 0m, 0m, 0m, BillingSlipSettlement.OverpaidOnTime },
        { 950m, "2026-10-04", true, 1000m, 0m, 0m, 50m, null }, // desconto dentro da data
        { 960m, "2026-10-05", true, 1000m, 0m, 0m, 40m, null }, // desconto menor que o do boleto, na data
        { 940m, "2026-10-04", false, 0m, 0m, 0m, 0m, BillingSlipSettlement.UnderpaidBeyondDiscount },
        { 950m, "2026-10-08", false, 0m, 0m, 0m, 0m, BillingSlipSettlement.Underpaid }, // fora da data do desconto
        { 0m, "2026-10-08", false, 0m, 0m, 0m, 0m, BillingSlipSettlement.NothingPaid },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Divide_o_valor_pago_pelas_condicoes_do_boleto(decimal paid, string credit, bool automatic, decimal principal, decimal interest, decimal fine, decimal discount, string? reason)
    {
        var split = BillingSlipSettlement.Split(Terms, 1000m, paid, DateOnly.Parse(credit, System.Globalization.CultureInfo.InvariantCulture));

        split.Automatic.Should().Be(automatic);
        split.Divergence.Should().Be(reason);
        if (automatic)
        {
            (split.Principal, split.Interest, split.Fine, split.Discount).Should().Be((principal, interest, fine, discount));
            ReceivableSettlement.TotalOf(split.Principal, split.Interest, split.Fine, split.Discount).Should().Be(paid, "o total da baixa é o que entrou na conta");
        }
    }

    [Fact]
    public void Titulo_que_mudou_depois_da_emissao_e_sempre_divergencia()
    {
        BillingSlipSettlement.Split(Terms, 800m, 800m, new DateOnly(2026, 10, 8)).Divergence.Should().Be(BillingSlipSettlement.BalanceChanged);
    }

    [Fact]
    public void Juros_contam_de_fine_from_quando_o_titulo_ja_estava_vencido()
    {
        var overdue = Terms with { FineFrom = new DateOnly(2026, 9, 30) };

        BillingSlipSettlement.MaxInterest(overdue, new DateOnly(2026, 10, 20)).Should().Be(6.67m, "20 dias desde o vencimento original");
        BillingSlipSettlement.MaxInterest(overdue, new DateOnly(2026, 10, 10)).Should().Be(0m, "até o vencimento do boleto não há mora");
    }

    private static WebhookRequest Request(string body) => new(Encoding.UTF8.GetBytes(body), new Dictionary<string, string>(), "t", null);

    [Fact]
    public void Chave_composta_e_cobranca_situacao_e_data_e_o_lote_tem_chave_propria()
    {
        const string one = """[{"codigoSolicitacao":"abc-1","seuNumero":"00000A","situacao":"RECEBIDO","dataHoraSituacao":"2026-10-20T10:00:00Z","valorTotalRecebido":"1000.00"}]""";
        InterWebhook.CompositeKey(Request(one)).Should().Be("cobranca:abc-1:RECEBIDO:2026-10-20T10:00:00Z");
        InterWebhook.CompositeKey(Request(one.Replace("1000.00", "9999.00", StringComparison.Ordinal))).Should().Be("cobranca:abc-1:RECEBIDO:2026-10-20T10:00:00Z",
            "o valor do corpo não entra na chave: um reenvio com outro valor é o mesmo aviso");

        const string two = """[{"codigoSolicitacao":"b","situacao":"RECEBIDO","dataHoraSituacao":"x"},{"codigoSolicitacao":"a","situacao":"RECEBIDO","dataHoraSituacao":"x"}]""";
        const string twoReversed = """[{"codigoSolicitacao":"a","situacao":"RECEBIDO","dataHoraSituacao":"x"},{"codigoSolicitacao":"b","situacao":"RECEBIDO","dataHoraSituacao":"x"}]""";
        InterWebhook.CompositeKey(Request(two)).Should().StartWith("lote:").And.Be(InterWebhook.CompositeKey(Request(twoReversed)));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"texto\"")]
    [InlineData("[{\"situacao\":\"RECEBIDO\"}]")]
    [InlineData("não é json")]
    public void Aviso_sem_identificador_da_cobranca_nao_tem_chave(string body)
    {
        InterWebhook.CompositeKey(Request(body)).Should().BeNull();
        InterWebhook.TryRead(Encoding.UTF8.GetBytes(body), out _).Should().BeFalse();
    }

    [Fact]
    public void Provedor_inter_e_valido_nos_dois_modos_do_token()
    {
        foreach (var mode in new[] { UrlTokenMode.Query, UrlTokenMode.PathToken })
        {
            var provider = InterWebhook.Provider(mode);
            WebhookProviderRegistry.Problems(provider).Should().BeEmpty();
            provider.Validator.Should().BeOfType<UrlTokenValidator>().Which.Mode.Should().Be(mode);
            provider.DeliveryIdHeader.Should().BeNull("sem id de entrega conhecido: a chave composta é o external_id");
        }

        InterWebhook.Provider(deliveryIdHeader: "X-Inter-Delivery").DeliveryIdHeader.Should().Be("X-Inter-Delivery");
        InterWebhook.ParseMode("pathtoken").Should().Be(UrlTokenMode.PathToken);
        InterWebhook.ParseMode(null).Should().Be(UrlTokenMode.Query);
        InterWebhookService.Masked("PathToken").Should().NotContain("?t=");
    }
}
