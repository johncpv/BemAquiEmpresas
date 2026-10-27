using System.Text.Json.Nodes;
using BemAqui.Finance.Application;
using BemAqui.Finance.Application.Inter;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Finance.Infrastructure.Inter;
using BemAqui.Parties.Contracts;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>BNK-01b, unidade: valor e elegibilidade, vencimento, limites, "seu número" em base 36, motivos, parâmetros e o mapa do Inter.</summary>
public class BillingSlipUnitTests
{
    private static readonly DateOnly Today = new(2026, 10, 27); // terça-feira

    private static readonly BillingDefaults Defaults = new(2m, 1m, 0m, true, 200, OverdueFineFrom.OriginalDueDate);

    private static readonly DiscountLimits Limits = new(5m, 500m);

    private static Receivable Title(decimal amount, ReceivableStatus status = ReceivableStatus.Open, DateOnly? due = null) =>
        new() { Number = "CR-2026-0001", Amount = amount, Status = status, DueDate = due ?? Today.AddDays(5) };

    [Fact]
    public void Valor_do_boleto_e_o_saldo_e_so_titulo_em_aberto_com_saldo_e_elegivel()
    {
        BillingSlipRules.AmountFor(Title(1000m)).Should().Be(1000m);
        BillingSlipRules.Eligible(Title(1000m)).Should().BeTrue();
        BillingSlipRules.Eligible(Title(1000m, ReceivableStatus.PartiallyPaid)).Should().BeTrue();
        BillingSlipRules.Eligible(Title(1000m, ReceivableStatus.Paid)).Should().BeFalse();
        BillingSlipRules.Eligible(Title(1000m, ReceivableStatus.Cancelled)).Should().BeFalse();
        BillingSlipRules.Eligible(Title(1000m, ReceivableStatus.Planned)).Should().BeFalse();
    }

    [Fact]
    public void Vencimento_sugerido_e_de_onde_contam_multa_e_juros()
    {
        BillingSlipRules.SuggestedDueDate(Today.AddDays(5), Today).Should().Be(Today.AddDays(5));
        BillingSlipRules.SuggestedDueDate(Today, Today).Should().Be(Today);
        BillingSlipRules.SuggestedDueDate(Today.AddDays(-3), Today).Should().Be(Today.AddDays(1), "vencido: próximo dia útil");
        BillingSlipRules.SuggestedDueDate(Today.AddDays(-3), new DateOnly(2026, 10, 30)).Should().Be(new DateOnly(2026, 11, 2), "sexta → segunda");
        BillingSlipRules.FineFrom(Today.AddDays(-10), Today.AddDays(2), OverdueFineFrom.OriginalDueDate).Should().Be(Today.AddDays(-10));
        BillingSlipRules.FineFrom(Today.AddDays(-10), Today.AddDays(2), OverdueFineFrom.SlipDueDate).Should().Be(Today.AddDays(2));
        BillingSlipRules.FineFrom(Today.AddDays(5), Today.AddDays(5), OverdueFineFrom.OriginalDueDate).Should().Be(Today.AddDays(5));
    }

    [Theory]
    [InlineData(10_000, 0, 2, 1, false)]
    [InlineData(10_000, 500, 2, 1, false)]
    [InlineData(10_000, 500.01, 2, 1, true)]
    [InlineData(1_000, 50, 2, 1, false)]
    [InlineData(1_000, 50.01, 2, 1, true)]
    [InlineData(1_000, 0, 2.01, 1, true)]
    [InlineData(1_000, 0, 2, 1.01, true)]
    [InlineData(1_000, 0, 1, 0.5, false)]
    public void Limites_que_exigem_aprovar(double amount, double discount, double fine, double interest, bool expected)
    {
        var terms = new BillingSlipTerms((decimal)amount, Today, (decimal)fine, (decimal)interest, (decimal)discount, Today, null);
        BillingSlipRules.RequiresApproval(terms, Limits, Defaults).Should().Be(expected);
    }

    [Fact]
    public void Seu_numero_em_base_36_curto_e_opaco()
    {
        BillingReference.ToBase36(1).Should().Be("000001");
        BillingReference.ToBase36(35).Should().Be("00000Z");
        BillingReference.ToBase36(36).Should().Be("000010");
        BillingReference.ToBase36(2_176_782_336).Should().Be("1000000");
        BillingReference.FromBase36(BillingReference.ToBase36(987_654_321)).Should().Be(987_654_321);
        BillingReference.ToBase36(long.MaxValue).Length.Should().BeLessThanOrEqualTo(BillingReference.MaxLength);
        var act = () => BillingReference.ToBase36(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Motivos_do_cancelamento_automatico_e_o_texto_do_Inter()
    {
        BillingSlipRules.IsAutomatic(BillingSlipCancelReason.DueDateChanged).Should().BeTrue();
        BillingSlipRules.IsAutomatic(BillingSlipCancelReason.AmountChanged).Should().BeTrue();
        BillingSlipRules.IsAutomatic(BillingSlipCancelReason.ReceivableCancelled).Should().BeTrue();
        BillingSlipRules.IsAutomatic(BillingSlipCancelReason.SettledOtherMeans).Should().BeTrue();
        BillingSlipRules.IsManual(BillingSlipCancelReason.CustomerRequest).Should().BeTrue();
        BillingSlipRules.ReasonText(BillingSlipCancelReason.DueDateChanged).Should().Be("o vencimento do título mudou");
        BillingSlipRules.BankReason(BillingSlipCancelReason.PaidOtherMeans).Should().Be("PAGODIRETOAOCLIENTE");
        BillingSlipRules.BankReason(BillingSlipCancelReason.CustomerRequest).Should().Be("APEDIDODOCLIENTE");
        BillingSlipRules.ValidMessage("linha 1\nlinha 2\nlinha 3").Should().BeTrue();
        BillingSlipRules.ValidMessage("1\n2\n3\n4").Should().BeFalse();
        BillingSlipRules.ValidMessage(new string('x', 79)).Should().BeFalse();
    }

    [Fact]
    public void Transicoes_do_boleto()
    {
        var slip = new BillingSlip { OurReference = "000001", Amount = 10m };
        slip.IsActive.Should().BeTrue();
        slip.MarkFailed("O CEP do cliente não foi aceito pelo banco.", BillingFailureActions.FixParty, null);
        slip.IsActive.Should().BeFalse();
        slip.HoldsReceivable.Should().BeTrue();
        slip.Retry();
        slip.Status.Should().Be(BillingSlipStatus.Requested);
        slip.MarkIssued(new BillingSlipBankData("1", "2", "3", "4"), Guid.NewGuid(), "{}", DateTimeOffset.UtcNow);
        var act = () => slip.Retry();
        act.Should().Throw<InvalidOperationException>();
        slip.RequestCancel(BillingSlipCancelReason.CustomerRequest, null, DateTimeOffset.UtcNow);
        slip.CancelPending.Should().BeTrue();
        slip.Cancel(DateTimeOffset.UtcNow, null);
        slip.Status.Should().Be(BillingSlipStatus.Cancelled);
        slip.CancelPending.Should().BeFalse();
    }

    [Theory]
    [InlineData(2.01, 1, 0, 200, "finePct")]
    [InlineData(-0.01, 1, 0, 200, "finePct")]
    [InlineData(2, 1.01, 0, 200, "interestMonthPct")]
    [InlineData(2, 1, 100.01, 200, "defaultDiscountPct")]
    [InlineData(2, 1, 0, 0, "dailyEmailLimit")]
    [InlineData(2, 1, 0, 2001, "dailyEmailLimit")]
    public void Validacao_da_cobranca(double fine, double interest, double discount, int limit, string field)
    {
        var error = BillingSettingsService.Validate(new BillingSettingsInput((decimal)fine, (decimal)interest, (decimal)discount, true, limit, OverdueFineFrom.OriginalDueDate));
        error.Should().NotBeNull();
        error!.Fields!.Keys.Should().Contain(field);
        BillingSettingsService.Validate(new BillingSettingsInput(2m, 1m, 0m, true, 2000, OverdueFineFrom.SlipDueDate)).Should().BeNull();
        BillingSettingsService.Validate(new BillingSettingsInput(2.01m, 1m, 0m, true, 200, OverdueFineFrom.OriginalDueDate))!.Message.Should().Be("A multa não pode passar de 2,00%.");
    }

    [Fact]
    public void Corpo_da_emissao_e_o_retorno_guardado_sem_pagador()
    {
        var payer = new BillingPayer(PartyKind.PJ, "Cliente Ltda", "47225005000186", "01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "sp", "fin@cliente.com.br");
        payer.ToString().Should().NotContain("Cliente").And.NotContain("4722");
        var body = InterBillingProvider.IssueBody(new BillingIssueRequest("00000A", new BillingSlipTerms(1500m, Today.AddDays(10), 2m, 1m, 50m, Today.AddDays(5), "Obrigado\nPague no Pix"), payer), 60);
        body["seuNumero"]!.GetValue<string>().Should().Be("00000A");
        body["valorNominal"]!.GetValue<decimal>().Should().Be(1500m);
        body["dataVencimento"]!.GetValue<string>().Should().Be("2026-11-06");
        body["pagador"]!["tipoPessoa"]!.GetValue<string>().Should().Be("JURIDICA");
        body["pagador"]!["uf"]!.GetValue<string>().Should().Be("SP");
        body["desconto"]!["quantidadeDias"]!.GetValue<int>().Should().Be(5);
        body["mensagem"]!["linha2"]!.GetValue<string>().Should().Be("Pague no Pix");

        var node = JsonNode.Parse("""
            {"cobranca":{"codigoSolicitacao":"abc","seuNumero":"00000A","situacao":"A_RECEBER","dataSituacao":"2026-10-27","valorNominal":1500.00,
              "pagador":{"nome":"Cliente Ltda","cpfCnpj":"47225005000186","email":"fin@cliente.com.br"}},
             "boleto":{"nossoNumero":"123","codigoBarras":"0779","linhaDigitavel":"07790.0001"},"pix":{"txid":"tx","pixCopiaECola":"000201"}}
            """)!;
        var slip = InterBillingProvider.Slip(node)!;
        slip.Ready.Should().BeTrue();
        slip.Data.DigitableLine.Should().Be("07790.0001");
        slip.Payload.Should().NotContain("Cliente").And.NotContain("4722").And.NotContain("fin@").And.Contain("A_RECEBER");
        InterBillingProvider.Situation("EXPIRADO").Should().Be(BankSlipSituation.Expired);
        InterBillingProvider.Situation("RECEBIDO").Should().Be(BankSlipSituation.Paid);
        InterBillingProvider.Situation("EM_PROCESSAMENTO").Should().Be(BankSlipSituation.Processing);
    }

    [Theory]
    [InlineData("pagador.cep", "O CEP do cliente não foi aceito pelo banco.", BillingFailureActions.FixParty, BillingFailureKind.Rejected)]
    [InlineData("pagador.cpfCnpj", "O CPF/CNPJ do cliente não foi aceito pelo banco.", BillingFailureActions.FixParty, BillingFailureKind.Rejected)]
    [InlineData("pagador.bairro", "O endereço de cobrança do cliente não foi aceito pelo banco.", BillingFailureActions.FixParty, BillingFailureKind.Rejected)]
    [InlineData("dataVencimento", "O vencimento do boleto não foi aceito pelo banco.", BillingFailureActions.Retry, BillingFailureKind.Rejected)]
    [InlineData("seuNumero", "O Inter já tem uma cobrança com este número.", BillingFailureActions.Retry, BillingFailureKind.Duplicate)]
    public void Recusa_do_Inter_traduzida_sem_o_valor(string property, string message, string action, BillingFailureKind kind)
    {
        var failure = InterBillingProvider.Refusal(new InterResponse(400, $$"""{"title":"inválido","violacoes":[{"razao":"x","propriedade":"{{property}}","valor":"Rua do Cliente 123"}]}"""));
        failure.Message.Should().Be(message).And.NotContain("Rua do Cliente");
        failure.Action.Should().Be(action);
        failure.Kind.Should().Be(kind);
        InterBillingProvider.Refusal(new InterResponse(503, "{}")).Kind.Should().Be(BillingFailureKind.Unavailable);
        InterBillingProvider.Refusal(new InterResponse(403, "{}")).Kind.Should().Be(BillingFailureKind.Credentials);
    }
}
