using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>CMP-01: regras puras da solicitação de compra, sem banco (posição contra o mínimo, necessidade, recomposição e situação).</summary>
public class PurchaseRequestRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 23);

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("10", "4", "0", "0", "6")] // abaixo do mínimo: compra até o mínimo
    [InlineData("10", "4", "3", "0", "3")] // o que já está em SC "Estoque geral" conta na posição
    [InlineData("10", "4", "3", "3", "0")] // com pedido em aberto, chega ao mínimo: nada
    [InlineData("10", "10", "0", "0", "0")] // no mínimo: nada (só abaixo compra)
    [InlineData("10", "12", "0", "0", "0")]
    [InlineData("10", "9.5", "0", "0", "1")] // arredonda para cima na unidade de estoque
    [InlineData("10", "-2", "0", "0", "12")] // disponível negativo (tudo reservado): compra o que falta até o mínimo
    [InlineData("0", "0", "0", "0", "0")] // sem mínimo
    public void Posicao_contra_o_minimo(string min, string available, string pending, string onOrder, string expected) =>
        PurchaseRequestRules.MinStockQuantity(D(min), PurchaseRequestRules.Position(D(available), D(pending), D(onOrder))).Should().Be(D(expected));

    [Fact]
    public void Necessidade_da_falta_com_e_sem_data_prometida()
    {
        PurchaseRequestRules.ShortageNeededBy(new DateOnly(2026, 12, 20), Today, 15).Should().Be(new DateOnly(2026, 12, 5), "data prometida − prazo padrão");
        PurchaseRequestRules.ShortageNeededBy(null, Today, 15).Should().Be(new DateOnly(2026, 11, 7), "sem data prometida: hoje + prazo padrão");
        PurchaseRequestRules.ShortageNeededBy(new DateOnly(2026, 10, 30), Today, 15).Should().Be(new DateOnly(2026, 10, 15), "pode ficar no passado (Atrasada)");
        PurchaseRequestRules.SuggestedNeededBy(Today, 15).Should().Be(new DateOnly(2026, 11, 7));
    }

    [Fact]
    public void Recomposicao_leva_a_linha_aberta_ate_a_falta_atual()
    {
        PurchaseRequestRules.PlanShortage(8m, []).Should().Be(new ShortagePlan(false, 8m, null, null), "falta nova vira linha");
        var open = new ShortageLineState(Guid.NewGuid(), PurchaseRequestStatus.Open, 8m, false);
        PurchaseRequestRules.PlanShortage(5m, [open]).OpenTarget.Should().Be(5m, "a falta diminuiu: a linha é reduzida");
        PurchaseRequestRules.PlanShortage(0m, [open]).OpenTarget.Should().Be(0m, "a falta zerou: a linha é cancelada");
        PurchaseRequestRules.PlanShortage(12m, [open]).OpenTarget.Should().Be(12m, "a falta subiu");
    }

    [Fact]
    public void Linha_em_cotacao_ou_pedido_nao_muda_e_o_comprador_e_avisado()
    {
        var quoting = new ShortageLineState(Guid.NewGuid(), PurchaseRequestStatus.Quoting, 8m, false);
        var plan = PurchaseRequestRules.PlanShortage(5m, [quoting]);
        plan.OpenTarget.Should().Be(0m, "o que está em cotação já cobre a falta");
        plan.DropNotice.Should().Be(5m);
        plan.DropStatus.Should().Be(PurchaseRequestStatus.Quoting);

        var ordered = new ShortageLineState(Guid.NewGuid(), PurchaseRequestStatus.Ordered, 4m, false);
        PurchaseRequestRules.PlanShortage(10m, [ordered]).Should().Be(new ShortagePlan(false, 6m, null, null), "o que falta além do pedido vira linha aberta");
        PurchaseRequestRules.PlanShortage(3m, [ordered]).DropStatus.Should().Be(PurchaseRequestStatus.Ordered);
    }

    [Fact]
    public void Linha_editada_a_mao_nao_e_sobrescrita()
    {
        var edited = new ShortageLineState(Guid.NewGuid(), PurchaseRequestStatus.Open, 3m, true);
        PurchaseRequestRules.PlanShortage(10m, [edited]).Skip.Should().BeTrue();
        var cancelledByBuyer = new ShortageLineState(Guid.NewGuid(), PurchaseRequestStatus.Cancelled, 3m, true);
        PurchaseRequestRules.PlanShortage(10m, [cancelledByBuyer]).Skip.Should().BeTrue("o comprador cancelou: a recomposição não recria");
        var cancelledBySystem = new ShortageLineState(Guid.NewGuid(), PurchaseRequestStatus.Cancelled, 3m, false);
        PurchaseRequestRules.PlanShortage(10m, [cancelledBySystem]).OpenTarget.Should().Be(10m, "cancelada pela fonte não conta");
    }

    [Theory]
    [InlineData(new[] { PurchaseRequestStatus.Open, PurchaseRequestStatus.Cancelled }, PurchaseRequestStatus.Open)]
    [InlineData(new[] { PurchaseRequestStatus.Cancelled, PurchaseRequestStatus.Cancelled }, PurchaseRequestStatus.Cancelled)]
    [InlineData(new[] { PurchaseRequestStatus.Open, PurchaseRequestStatus.Quoting }, PurchaseRequestStatus.Quoting)]
    [InlineData(new[] { PurchaseRequestStatus.Ordered, PurchaseRequestStatus.Cancelled }, PurchaseRequestStatus.Ordered)]
    [InlineData(new[] { PurchaseRequestStatus.Ordered, PurchaseRequestStatus.Open }, PurchaseRequestStatus.Open)]
    public void Situacao_da_SC_vem_das_linhas(PurchaseRequestStatus[] lines, PurchaseRequestStatus expected) =>
        PurchaseRequestRules.DeriveStatus(lines).Should().Be(expected);

    [Fact]
    public void Textos_dos_avisos()
    {
        ShortageRecomposer.DropNotice("MP-1 Cabo", "OS-2026-00012", 4.5m, PurchaseRequestStatus.Quoting)
            .Should().Be("A falta de MP-1 Cabo na OS-2026-00012 caiu para 4,5; a linha já está em cotação.");
        ShortageRecomposer.DropNotice("MP-1 Cabo", "OS-2026-00012", 0m, PurchaseRequestStatus.Ordered).Should().EndWith("caiu para 0; a linha já está em pedido.");
        ShortageRecomposer.ShortageLabel("OS-2026-00012").Should().Be("Sistema · Falta da BOM OS-2026-00012");
        MinStockPurchaseRequests.Notice(3).Should().Be("3 itens abaixo do mínimo.");
        MinStockPurchaseRequests.Notice(1).Should().Be("1 item abaixo do mínimo.");
        PurchaseRequestRules.DaysUntil(new DateOnly(2026, 10, 20), Today).Should().Be(-3);
    }
}
