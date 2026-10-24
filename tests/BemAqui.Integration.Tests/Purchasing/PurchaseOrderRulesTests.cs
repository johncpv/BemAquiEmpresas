using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-03a: regras puras do pedido de compra, sem banco: totais (CA 3), valor de referência e fracionamento (CA 5, 6), parcelas com a
/// última absorvendo a diferença e as datas estimadas (CA 12), avisos (CA 11, 20) e o mapa tipo de documento → permissão (CA 10).
/// </summary>
public class PurchaseOrderRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 23);

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("3", "1.005", "3.02")] // 3,015 → 3,02 (AwayFromZero, nunca 3,01)
    [InlineData("2.5", "10.123456", "25.31")]
    [InlineData("1", "0.004999", "0.00")]
    [InlineData("0.3333", "3", "1.00")]
    public void Total_da_linha_tem_duas_casas_AwayFromZero(string quantity, string price, string expected) =>
        PurchaseOrderRules.LineTotal(D(quantity), D(price)).Should().Be(D(expected));

    [Fact]
    public void Total_do_pedido_e_a_soma_das_linhas_mais_frete_e_IPI()
    {
        var totals = PurchaseOrderRules.Totals([(10m, 12.345m), (3m, 1.005m)], 150m, 5m);

        totals.Subtotal.Should().Be(123.45m + 3.02m);
        totals.IpiAmount.Should().Be(6.32m, "126,47 × 5% = 6,3235 → 6,32");
        totals.Total.Should().Be(totals.Subtotal + 150m + totals.IpiAmount);
    }

    [Fact]
    public void IPI_arredonda_uma_vez_sobre_o_subtotal()
    {
        var totals = PurchaseOrderRules.Totals([(1m, 100.10m)], 0m, 3.25m);
        totals.IpiAmount.Should().Be(3.25m, "100,10 × 3,25% = 3,253250 → 3,25");
        PurchaseOrderRules.Totals([(1m, 0.5m)], 0m, 1m).IpiAmount.Should().Be(0.01m, "0,005 → 0,01 (AwayFromZero)");
    }

    [Theory]
    [InlineData("3000", "3000", "6000")] // CA 6: dois pedidos de R$ 3.000,00 ao mesmo fornecedor em 30 dias → R$ 6.000,00
    [InlineData("3000", "0", "3000")] // sem outro pedido: o próprio total
    [InlineData("10000", "500", "10500")]
    public void Valor_de_referencia_soma_os_pedidos_do_fornecedor_em_30_dias(string total, string others, string expected) =>
        PurchaseOrderRules.ReferenceAmount(D(total), D(others)).Should().Be(D(expected));

    [Fact]
    public void Situacoes_que_contam_no_fracionamento_sao_aprovadas_ou_pendentes()
    {
        PurchaseOrderRules.CountsForReference.Should().BeEquivalentTo(
            [PurchaseOrderStatus.PendingApproval, PurchaseOrderStatus.Approved, PurchaseOrderStatus.Sent, PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Received]);
        PurchaseOrderRules.CountsForReference.Should().NotContain([PurchaseOrderStatus.Draft, PurchaseOrderStatus.Cancelled]);
    }

    [Fact]
    public void Aviso_de_fracionamento_diz_quantos_pedidos_e_o_valor() =>
        PurchaseOrderRules.SplitWarning(2, 6000m).Should().Be("Possível fracionamento: 2 pedidos para o mesmo fornecedor em 30 dias, R$ 6.000,00.");

    [Fact]
    public void Aviso_de_uma_cotacao_so_com_cotacao_de_uma_resposta()
    {
        var rfq = Guid.CreateVersion7();
        PurchaseOrderRules.SingleQuoteWarning(rfq, 1).Should().Be("1 cotação: este pedido veio de uma cotação com uma resposta só.");
        PurchaseOrderRules.SingleQuoteWarning(rfq, 3).Should().BeNull();
        PurchaseOrderRules.SingleQuoteWarning(null, 1).Should().BeNull("sem cotação não há aviso");
    }

    /// <summary>CA 12: parcelas pela condição sobre o total, a última absorve a diferença; a data é a prevista de entrega + dias.</summary>
    [Fact]
    public void Parcelas_previstas_fecham_com_o_total_e_partem_da_data_prevista()
    {
        var expected = new DateOnly(2026, 11, 10);
        var dues = PaymentTermSplitter.Split([new Installment(30, 33.3333m), new Installment(60, 33.3333m), new Installment(90, 33.3334m)], 1000m, expected);

        dues.Select(d => d.Amount).Should().Equal(333.33m, 333.33m, 333.34m);
        dues.Sum(d => d.Amount).Should().Be(1000m);
        dues.Select(d => d.DueDate).Should().Equal(expected.AddDays(30), expected.AddDays(60), expected.AddDays(90));
        PurchaseOrderRules.InstallmentDescription("PC-2026-0001", 2, 3).Should().Be("Pedido PC-2026-0001 — parcela 2/3");
    }

    [Fact]
    public void Destino_e_o_projeto_quando_todas_as_SC_sao_do_mesmo_projeto()
    {
        var p1 = Guid.CreateVersion7();
        PurchaseOrderRules.DestinationOf([p1, p1]).Should().Be(p1);
        PurchaseOrderRules.DestinationOf([p1, Guid.CreateVersion7()]).Should().BeNull("projetos diferentes: estoque geral");
        PurchaseOrderRules.DestinationOf([p1, null]).Should().BeNull("uma linha do estoque geral: estoque geral");
        PurchaseOrderRules.DestinationOf([null]).Should().BeNull();
    }

    [Fact]
    public void Atraso_so_conta_para_pedido_com_saldo_a_receber()
    {
        PurchaseOrderRules.DaysLate(PurchaseOrderStatus.Approved, Today.AddDays(-4), Today).Should().Be(4);
        PurchaseOrderRules.DaysLate(PurchaseOrderStatus.Sent, Today, Today).Should().BeNull("hoje ainda não está atrasado");
        PurchaseOrderRules.DaysLate(PurchaseOrderStatus.Received, Today.AddDays(-4), Today).Should().BeNull();
        PurchaseOrderRules.DaysLate(PurchaseOrderStatus.Draft, Today.AddDays(-4), Today).Should().BeNull();
        PurchaseOrderRules.DefaultExpectedDate(Today, 15).Should().Be(new DateOnly(2026, 11, 7));
    }

    [Theory]
    [InlineData(PurchaseOrderStatus.Draft, "0", true)]
    [InlineData(PurchaseOrderStatus.PendingApproval, "0", true)]
    [InlineData(PurchaseOrderStatus.Approved, "0", true)]
    [InlineData(PurchaseOrderStatus.Sent, "0", true)]
    [InlineData(PurchaseOrderStatus.Sent, "1", false)] // CA 16: com recebimento não cancela nem edita
    [InlineData(PurchaseOrderStatus.PartiallyReceived, "0", false)]
    [InlineData(PurchaseOrderStatus.Cancelled, "0", false)]
    public void So_muda_sem_recebimento(PurchaseOrderStatus status, string received, bool expected) =>
        PurchaseOrderRules.IsChangeable(status, D(received)).Should().Be(expected);

    [Fact]
    public void Edicao_fora_do_rascunho_volta_a_aprovacao()
    {
        PurchaseOrderRules.NeedsReapproval(PurchaseOrderStatus.Draft).Should().BeFalse();
        PurchaseOrderRules.NeedsReapproval(PurchaseOrderStatus.PendingApproval).Should().BeTrue();
        PurchaseOrderRules.NeedsReapproval(PurchaseOrderStatus.Approved).Should().BeTrue();
        PurchaseOrderRules.NeedsReapproval(PurchaseOrderStatus.Sent).Should().BeTrue();
    }

    /// <summary>CA 10 (ADR 0024 §4): o mapa tipo de documento → permissão do aprovador fica no Finance.</summary>
    [Fact]
    public void Mapa_tipo_de_documento_para_permissao_do_aprovador()
    {
        ApprovalDocumentTypes.ApproverPermission(ApprovalDocumentTypes.PaymentOrder).Should().Be("finance.approve");
        ApprovalDocumentTypes.ApproverPermission(ApprovalDocumentTypes.PurchaseOrder).Should().Be("purchasing.approve");
        var unknown = () => ApprovalDocumentTypes.ApproverPermission("Contract");
        unknown.Should().Throw<ArgumentOutOfRangeException>();
        ApprovalRule.SeedKeyOf(ApprovalDocumentTypes.PurchaseOrder).Should().Be("owner-approves-all-purchase", "regra padrão própria do tipo (ponto 44)");
        ApprovalRule.SeedKeyOf(ApprovalDocumentTypes.PaymentOrder).Should().Be(ApprovalRule.DefaultSeedKey);
    }

    /// <summary>CA 6: com a regra até R$ 5.000,00 e acima para o Proprietário, o pedido de R$ 3.000,00 cai na regra de cima pela referência.</summary>
    [Fact]
    public void Referencia_acima_da_faixa_muda_a_regra()
    {
        var approver = new ApprovalRule { DocumentType = ApprovalRule.PurchaseOrder, MinAmount = 0m, MaxAmount = 5000m, Level = 1, ApproverUserId = Guid.CreateVersion7() };
        var owner = new ApprovalRule { DocumentType = ApprovalRule.PurchaseOrder, MinAmount = 5000.01m, Level = 1, ApproverGroupId = Guid.CreateVersion7() };
        var reference = PurchaseOrderRules.ReferenceAmount(3000m, 3000m);

        ApprovalRules.Resolve([approver, owner], 3000m, null).Should().ContainSingle().Which.Should().Be(approver);
        ApprovalRules.Resolve([approver, owner], reference, null).Should().ContainSingle().Which.Should().Be(owner);
    }
}
