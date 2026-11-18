using BemAqui.Finance.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>VND-01 (unidade): máquina de estados do pagamento, validação por canal, soma capturada, cartão em campo livre e chave do recebível.</summary>
public class SalesRulesUnitTests
{
    [Theory]
    [InlineData(SalePaymentStatus.Created, SalePaymentStatus.Pending, true)]
    [InlineData(SalePaymentStatus.Created, SalePaymentStatus.Captured, true)]
    [InlineData(SalePaymentStatus.Pending, SalePaymentStatus.Unknown, true)]
    [InlineData(SalePaymentStatus.Unknown, SalePaymentStatus.Captured, true)]
    [InlineData(SalePaymentStatus.Captured, SalePaymentStatus.Refunded, true)]
    [InlineData(SalePaymentStatus.Captured, SalePaymentStatus.Voided, true)]
    [InlineData(SalePaymentStatus.Captured, SalePaymentStatus.Failed, false)]
    [InlineData(SalePaymentStatus.Captured, SalePaymentStatus.Pending, false)]
    [InlineData(SalePaymentStatus.Refunded, SalePaymentStatus.Captured, false)]
    [InlineData(SalePaymentStatus.Failed, SalePaymentStatus.Captured, false)]
    [InlineData(SalePaymentStatus.Voided, SalePaymentStatus.Refunded, false)]
    public void Maquina_de_estados_do_pagamento(SalePaymentStatus from, SalePaymentStatus to, bool allowed) => PaymentStateMachine.CanMove(from, to).Should().Be(allowed);

    [Fact]
    public void Pagamento_entregue_ja_estornado_passa_pelo_capturado()
    {
        var payment = SalePayment.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, 0, PaymentIntegration.Manual, 1, null, null, null, null, null, null, null, DateTimeOffset.UtcNow);
        payment.Arrive(SalePaymentStatus.Refunded, Guid.NewGuid(), DateTimeOffset.UtcNow).Should().BeTrue();
        payment.Status.Should().Be(SalePaymentStatus.Refunded);
        payment.ConfirmedBy.Should().NotBeNull("passou por capturado");
        payment.MoveTo(SalePaymentStatus.Captured, null, DateTimeOffset.UtcNow).Should().BeFalse("estornado é final");
    }

    [Fact]
    public void Validacao_por_canal()
    {
        SaleRules.ChannelErrors(SaleChannel.Online, null, null).Should().BeEmpty("canal sem caixa: nulos e válido");
        SaleRules.ChannelErrors(SaleChannel.ServiceOrder, null, Guid.NewGuid()).Keys.Should().Equal("cashSessionId");
        SaleRules.ChannelErrors(SaleChannel.Pos, Guid.NewGuid(), null).Keys.Should().Equal("cashSessionId");
        SaleRules.ChannelErrors(SaleChannel.Pos, null, null).Keys.Should().BeEquivalentTo(["registerId", "cashSessionId"]);
        SaleRules.ChannelErrors(SaleChannel.Pos, Guid.NewGuid(), Guid.NewGuid()).Should().BeEmpty();
        SaleRules.ChannelErrors(SaleChannel.Table, null, null).Should().BeEmpty("mesa e delivery podem vir com ou sem caixa");
    }

    [Fact]
    public void Capturado_e_liquido_do_troco_e_so_dos_capturados() =>
        SaleRules.CapturedNet([(SalePaymentStatus.Captured, 50m, 10m), (SalePaymentStatus.Failed, 30m, 0), (SalePaymentStatus.Refunded, 20m, 0), (SalePaymentStatus.Captured, 5.005m, 0)])
            .Should().Be(45.01m);

    [Theory]
    [InlineData("4111 1111 1111 1111", true)]
    [InlineData("5555-5555-5555-4444", true)]
    [InlineData("pago com 378282246310005", true)]
    [InlineData("4111111111111112", false)]
    [InlineData("NSU 123456", false)]
    [InlineData("7891234567895", false)]
    [InlineData("pedido 2026-0001", false)]
    [InlineData(null, false)]
    public void Campo_livre_com_numero_de_cartao(string? text, bool card) => SaleRules.LooksLikeCardNumber(text).Should().Be(card);

    [Fact]
    public void Chave_do_recebivel_separa_dia_canal_sessao_forma_provedor_e_vencimento()
    {
        var day = new DateOnly(2026, 11, 17);
        var method = Guid.NewGuid();
        var acquirer = Guid.NewGuid();
        var key = new SaleReceivableKey(day, "Online", null, method, acquirer, day.AddDays(30));
        key.Text.Should().Be(new SaleReceivableKey(day, "Online", null, method, acquirer, day.AddDays(30)).Text);
        new[]
        {
            key with { BusinessDate = day.AddDays(1) }, key with { Channel = "ServiceOrder" }, key with { CashSessionId = Guid.NewGuid() }, key with { PaymentMethodId = Guid.NewGuid() },
            key with { ProviderPartyId = null }, key with { DueDate = day.AddDays(2) }, key with { SaleId = Guid.NewGuid() },
        }.Select(k => k.Text).Should().OnlyHaveUniqueItems().And.NotContain(key.Text);
        key.Text.Length.Should().BeLessThanOrEqualTo(200);
    }
}
