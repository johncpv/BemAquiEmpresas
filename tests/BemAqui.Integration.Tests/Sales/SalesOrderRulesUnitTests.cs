using System.Security.Claims;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Sales.Application;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Sales;

/// <summary>VND-02a, regras puras: máquina de estados (CA 6), limites de desconto (CA 5 e 7), prazos (CA 12), desconto do pedido na venda, parâmetros e escopo da idempotência (CA 3).</summary>
public sealed class SalesOrderRulesUnitTests
{
    public static TheoryData<SalesOrderStatus, SalesOrderAction, SalesOrderStatus?> Transitions => new()
    {
        { SalesOrderStatus.Draft, SalesOrderAction.Confirm, SalesOrderStatus.Open },
        { SalesOrderStatus.Draft, SalesOrderAction.Edit, SalesOrderStatus.Draft },
        { SalesOrderStatus.Draft, SalesOrderAction.Cancel, SalesOrderStatus.Cancelled },
        { SalesOrderStatus.Draft, SalesOrderAction.Charge, null },
        { SalesOrderStatus.Draft, SalesOrderAction.Pay, null },
        { SalesOrderStatus.Open, SalesOrderAction.Charge, SalesOrderStatus.AwaitingPayment },
        { SalesOrderStatus.Open, SalesOrderAction.Pay, SalesOrderStatus.Paid },
        { SalesOrderStatus.Open, SalesOrderAction.Edit, SalesOrderStatus.Open },
        { SalesOrderStatus.Open, SalesOrderAction.Cancel, SalesOrderStatus.Cancelled },
        { SalesOrderStatus.Open, SalesOrderAction.Confirm, null },
        { SalesOrderStatus.AwaitingPayment, SalesOrderAction.Pay, SalesOrderStatus.Paid },
        { SalesOrderStatus.AwaitingPayment, SalesOrderAction.ChargeLapsed, SalesOrderStatus.Open },
        { SalesOrderStatus.AwaitingPayment, SalesOrderAction.Cancel, SalesOrderStatus.Cancelled },
        { SalesOrderStatus.AwaitingPayment, SalesOrderAction.Edit, null },
        { SalesOrderStatus.Paid, SalesOrderAction.CancelPaid, SalesOrderStatus.Cancelled },
        { SalesOrderStatus.Paid, SalesOrderAction.Cancel, null },
        { SalesOrderStatus.Paid, SalesOrderAction.Edit, null },
        { SalesOrderStatus.Cancelled, SalesOrderAction.Reopen, SalesOrderStatus.Paid },
        { SalesOrderStatus.Cancelled, SalesOrderAction.Refund, SalesOrderStatus.Cancelled },
        { SalesOrderStatus.Cancelled, SalesOrderAction.Confirm, null },
        { SalesOrderStatus.Cancelled, SalesOrderAction.Pay, null },
    };

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Maquina_de_estados_do_pedido(SalesOrderStatus from, SalesOrderAction action, SalesOrderStatus? to) =>
        SalesOrderRules.Target(from, action).Should().Be(to);

    [Fact]
    public void Mensagem_da_transicao_invalida() =>
        SalesOrderRules.TransitionMessage(SalesOrderStatus.AwaitingPayment, SalesOrderAction.Edit).Should().Be("Este pedido está aguardando pagamento e não pode ser alterado.");

    [Fact]
    public void Desconto_de_linha_ou_do_pedido_acima_do_limite()
    {
        SalesOrderRules.DiscountAboveLimit([(100m, 10m)], 0m, 100m, 10m).Should().BeFalse();
        SalesOrderRules.DiscountAboveLimit([(100m, 10.01m)], 0m, 100m, 10m).Should().BeTrue();
        SalesOrderRules.DiscountAboveLimit([(100m, 5m), (100m, 5m)], 15m, 200m, 10m).Should().BeTrue("12,5% no pedido");
        SalesOrderRules.DiscountAboveLimit([(100m, 0.01m)], 0m, 100m, 0m).Should().BeTrue("limite do aplicativo de 0%");
    }

    [Fact]
    public void Dias_uteis_pulam_o_fim_de_semana()
    {
        var friday = new DateOnly(2026, 11, 27);
        SalesOrderRules.AddBusinessDays(friday, 1).Should().Be(new DateOnly(2026, 11, 30));
        SalesOrderRules.ChargeHoldDate(friday).Should().Be(new DateOnly(2026, 11, 30));
        SalesOrderRules.AddBusinessDays(new DateOnly(2026, 11, 24), 3).Should().Be(friday);
    }

    [Fact]
    public void Desconto_do_pedido_quebrado_entre_as_linhas_fecha_o_valor()
    {
        var parts = SalesOrderRules.SpreadOrderDiscount([100m, 33.33m, 66.67m], 10m);
        parts.Sum().Should().Be(10m);
        parts.Zip([100m, 33.33m, 66.67m]).Should().OnlyContain(p => p.First <= p.Second);
        SalesOrderRules.SpreadOrderDiscount([50m], 0m).Should().Equal(0m);
    }

    [Fact]
    public void Rotulo_do_pedido_de_aplicativo()
    {
        SalesOrderRules.AppLabel(SalesOrderOrigin.Conecta, "Conecta").Should().Be("Pedido do Conecta: confirme com o cliente");
        SalesOrderRules.AppLabel(SalesOrderOrigin.Marketplace, "Mercado Livre").Should().Be("Pedido de Mercado Livre");
    }

    [Fact]
    public void Parametros_fora_da_faixa_sao_recusados()
    {
        var ok = new SalesOrderParametersInput(10, 0, 48, 2, 1, 3, 50, 200, 10, null);
        SalesOrderParametersService.Validate(ok).Should().BeNull();
        SalesOrderParametersService.Validate(ok with { AppChargeDays = 4 }).Should().NotBeNull("aplicativo vence em até 3 dias úteis");
        SalesOrderParametersService.Validate(ok with { DiscountLimitPct = 101 }).Should().NotBeNull();
        SalesOrderParametersService.Validate(ok with { AppMaxOrderQty = 10 }).Should().NotBeNull();
        new SalesOrderParameters().AppDiscountLimitPct.Should().Be(0m, "padrão do aplicativo: 0%");
        new SalesOrderParameters().AppHoldHours.Should().Be(2);
    }

    [Fact]
    public void Idempotencia_de_aplicativo_e_por_aplicativo_e_nao_pela_conexao_nem_pelo_autorizador()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        HttpContext Context(string? connection, string app = "conecta")
        {
            var http = new DefaultHttpContext { RequestServices = services };
            http.Request.Method = "POST";
            http.Request.Path = "/api/v1/integration/orders";
            if (connection is not null)
            {
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(IdempotencyFilter.ConnectionClaim, connection), new Claim(AppClaims.App, app)], "App"));
            }

            return http;
        }

        // INT-04 CA 6: reconectar (conexão nova) não muda o escopo; outro aplicativo, sim.
        IdempotencyFilter.Scope(Context("c1")).Should().Be("POST /api/v1/integration/orders app:conecta");
        IdempotencyFilter.Scope(Context("c1")).Should().Be(IdempotencyFilter.Scope(Context("c2")));
        IdempotencyFilter.Scope(Context("c1")).Should().NotBe(IdempotencyFilter.Scope(Context("c1", "outro")));
        IdempotencyFilter.Scope(Context(null)).Should().Be("POST /api/v1/integration/orders -");
    }
}
