using BemAqui.Commercial.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.SharedKernel;
using BemAqui.Purchasing.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-04: regras puras do recebimento, sem banco: rateio do frete e do IPI com a última linha absorvendo o arredondamento (CA 6, P-16),
/// custo de entrada e valor do recebimento, saldo com tolerância (CA 3), teto (CA 4), frete e IPI proporcionais (CA 1), previsto do saldo
/// (CA 9), situação do pedido (CA 5, 12), unidade do fornecedor e segregação (CA 2, 14).
/// </summary>
public class GoodsReceiptRulesTests
{
    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Frete_de_100_sobre_300_e_700_vai_30_e_70() =>
        CostAllocator.Allocate(100m, [300m, 700m]).Should().Equal(30m, 70m);

    [Fact]
    public void A_ultima_linha_absorve_a_diferenca_de_arredondamento()
    {
        var shares = CostAllocator.Allocate(100m, [1m, 1m, 1m]);

        shares.Should().Equal(33.33m, 33.33m, 33.34m);
        shares.Sum().Should().Be(100m);
    }

    [Theory]
    [InlineData("10.01", "1", "2", "3")]
    [InlineData("0.05", "333.33", "333.33", "333.34")]
    [InlineData("1234.57", "0.10", "999.99", "17")]
    public void O_rateio_sempre_soma_o_total(string total, string a, string b, string c) =>
        CostAllocator.Allocate(D(total), [D(a), D(b), D(c)]).Sum().Should().Be(D(total));

    [Fact]
    public void Sem_peso_vai_tudo_na_ultima_linha_e_sem_linha_nao_rateia()
    {
        CostAllocator.Allocate(10m, [0m, 0m]).Should().Equal(0m, 10m);
        CostAllocator.Allocate(10m, []).Should().BeEmpty();
    }

    /// <summary>
    /// Dataset fixo (CA 6): A = 10 × 30,00 = 300,00; B = 20 × 35,00 = 700,00; frete 100,00 e IPI 5% = 50,00 (não recuperável, P-01). A leva
    /// 30,00 de frete e 15,00 de IPI: (300 + 30 + 15) ÷ 10 = 34,500000; B leva 70,00 e 35,00: (700 + 70 + 35) ÷ 20 = 40,250000. Valor 1.150,00.
    /// </summary>
    [Fact]
    public void Custo_de_entrada_do_dataset_fixo()
    {
        var amounts = new[] { GoodsReceiptRules.LineAmount(10m, 30m), GoodsReceiptRules.LineAmount(20m, 35m) };
        var freight = CostAllocator.Allocate(100m, amounts);
        var ipi = CostAllocator.Allocate(50m, amounts);

        GoodsReceiptRules.EntryUnitCost(amounts[0], freight[0], ipi[0], 10m).Should().Be(34.5m);
        GoodsReceiptRules.EntryUnitCost(amounts[1], freight[1], ipi[1], 20m).Should().Be(40.25m);
        GoodsReceiptRules.Amount(amounts, 100m, 50m).Should().Be(1150m);
    }

    [Fact]
    public void Custo_de_entrada_tem_seis_casas() =>
        GoodsReceiptRules.EntryUnitCost(100m, 0.01m, 0m, 3m).Should().Be(33.336667m);

    [Theory]
    [InlineData("10", "4", "0", "6")]
    [InlineData("10", "4", "10", "7")]
    [InlineData("10", "11", "10", "0")]
    [InlineData("10", "10", "0", "0")]
    [InlineData("3.3333", "0", "0", "3.3333")]
    public void Saldo_mais_a_tolerancia(string ordered, string received, string tolerance, string expected) =>
        GoodsReceiptRules.MaxQuantity(D(ordered), D(received), D(tolerance)).Should().Be(D(expected));

    [Theory]
    [InlineData("1000", "0", "1000")]
    [InlineData("1000", "5", "1050")]
    [InlineData("333.33", "1.5", "338.32")]
    public void Teto_do_aprovado_com_a_tolerancia(string approved, string tolerance, string expected) =>
        GoodsReceiptRules.Ceiling(D(approved), D(tolerance)).Should().Be(D(expected));

    [Fact]
    public void Frete_e_IPI_proporcionais_ao_que_esta_sendo_recebido()
    {
        GoodsReceiptRules.ProportionalShare(100m, 1000m, 300m, 0m, completes: false).Should().Be(30m);
        GoodsReceiptRules.ProportionalShare(100m, 1000m, 300m, 90m, completes: false).Should().Be(10m, "nunca passa do que sobrou");
        GoodsReceiptRules.ProportionalShare(100m, 1000m, 300m, 30m, completes: true).Should().Be(70m, "o recebimento que completa leva o resto");
        GoodsReceiptRules.ProportionalShare(0m, 1000m, 300m, 0m, completes: false).Should().Be(0m);
    }

    [Fact]
    public void Previsto_do_pedido_e_o_saldo_nunca_negativo()
    {
        GoodsReceiptRules.PlannedBalance(1150m, 345m).Should().Be(805m);
        GoodsReceiptRules.PlannedBalance(1150m, 1150.01m).Should().Be(0m);
    }

    [Fact]
    public void Situacao_do_pedido_depois_de_receber_ou_estornar()
    {
        GoodsReceiptRules.StatusAfter([(10m, 10m), (5m, 5m)], PurchaseOrderStatus.Sent).Should().Be(PurchaseOrderStatus.Received);
        GoodsReceiptRules.StatusAfter([(10m, 4m), (5m, 0m)], PurchaseOrderStatus.Approved).Should().Be(PurchaseOrderStatus.PartiallyReceived);
        GoodsReceiptRules.StatusAfter([(10m, 0m), (5m, 0m)], PurchaseOrderStatus.Sent).Should().Be(PurchaseOrderStatus.Sent, "o estorno do único volta à situação de antes");
        GoodsReceiptRules.StatusAfter([(10m, 0m)], PurchaseOrderStatus.PartiallyReceived).Should().Be(PurchaseOrderStatus.Approved);
    }

    [Fact]
    public void O_excedente_da_tolerancia_fica_separado_do_pedido()
    {
        GoodsReceiptRules.Split(10m, 10.5m).Should().Be((10m, 0.5m));
        GoodsReceiptRules.Split(10m, 4m).Should().Be((4m, 0m));
        GoodsReceiptRules.Split(10m, -1m).Should().Be((0m, 0m));
    }

    [Fact]
    public void Unidade_do_fornecedor_converte_pelo_fator_em_4_casas()
    {
        GoodsReceiptRules.FromSupplierUnit(3m, 12m).Should().Be(36m);
        GoodsReceiptRules.FromSupplierUnit(1m, 15.700005m).Should().Be(15.7000m);
    }

    [Fact]
    public void Quem_pediu_aprovou_ou_corrigiu_o_preco_esta_envolvido()
    {
        var requester = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var corrector = Guid.NewGuid();
        var other = Guid.NewGuid();

        GoodsReceiptRules.IsInvolved(requester, requester, [approver], []).Should().BeTrue();
        GoodsReceiptRules.IsInvolved(approver, requester, [approver], []).Should().BeTrue();
        GoodsReceiptRules.IsInvolved(corrector, requester, [approver], [corrector]).Should().BeTrue();
        GoodsReceiptRules.IsInvolved(other, requester, [approver, null], [corrector]).Should().BeFalse();
    }

    [Fact]
    public void Textos_do_recebimento()
    {
        GoodsReceiptRules.InstallmentDescription("REC-2026-0001", "PC-2026-0003", 2, 3).Should().Be("Recebimento REC-2026-0001 do pedido PC-2026-0003 — parcela 2/3");
        GoodsReceiptRules.ClosedLabel(805m).Should().StartWith("Encerrado com saldo de R$").And.EndWith("805,00");
    }

    /// <summary>
    /// CA 10 (D2, ADR 0025 §1): o título do recebimento de item estocável (origem <c>GoodsReceipt</c>, natureza Material, em aberto) fica em
    /// Comprometido junto com o previsto do saldo, e não no realizado, até a baixa do material para a OS.
    /// </summary>
    [Fact]
    public void Titulo_do_recebimento_de_material_e_comprometido_ate_a_baixa()
    {
        var payables = new[]
        {
            new CostPayable(CostType.Material, DocumentOrigin.GoodsReceipt, PayableStatus.Open, 747.5m),
            new CostPayable(CostType.Material, DocumentOrigin.Purchase, PayableStatus.Planned, 402.5m),
        };

        var view = ProjectCosts.Compute(10_000m, new Dictionary<CostType, decimal>(), payables, []);

        var material = view.Lines.Single(l => l.CostType == CostType.Material);
        material.Realized.Should().Be(0m);
        material.Committed.Should().Be(1150m);
    }
}