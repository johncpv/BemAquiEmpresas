using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-08: montagem do DRE e regras de entrada por documento, sem banco (CA 1 a 8).</summary>
public class DreBuilderTests
{
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly IReadOnlyList<DateOnly> Months = DreBuilder.MonthsBetween(Jan, new DateOnly(2026, 3, 31));

    private static readonly Guid R = Guid.NewGuid(), R11 = Guid.NewGuid(), R12 = Guid.NewGuid(), R13 = Guid.NewGuid(), R19 = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid(), C21 = Guid.NewGuid(), C25 = Guid.NewGuid();
    private static readonly Guid E = Guid.NewGuid(), E31 = Guid.NewGuid(), E35 = Guid.NewGuid();
    private static readonly Guid I = Guid.NewGuid(), I41 = Guid.NewGuid();

    /// <summary>Recorte do plano v1 (CAD-05a) com os desvios: 1.3 e 3.5 em Resultado financeiro, 2.5 em Deduções.</summary>
    private static readonly DreAccount[] Chart =
    [
        new(R, "1", "Receitas", null, DreGroup.GrossRevenue),
        new(R11, "1.1", "Venda de equipamentos", R, DreGroup.GrossRevenue),
        new(R12, "1.2", "Serviços de instalação e assistência", R, DreGroup.GrossRevenue),
        new(R13, "1.3", "Receitas financeiras", R, DreGroup.FinancialResult),
        new(R19, "1.9", "Receitas a classificar", R, DreGroup.GrossRevenue),
        new(C, "2", "Custos", null, DreGroup.Costs),
        new(C21, "2.1", "Materiais", C, DreGroup.Costs),
        new(C25, "2.5", "Impostos sobre vendas", C, DreGroup.Deductions),
        new(E, "3", "Despesas", null, DreGroup.OperatingExpenses),
        new(E31, "3.1", "Pessoal administrativo", E, DreGroup.OperatingExpenses),
        new(E35, "3.5", "Financeiras", E, DreGroup.FinancialResult),
        new(I, "4", "Investimentos", null, DreGroup.OutsideDre),
        new(I41, "4.1", "Investimentos e imobilizado", I, DreGroup.OutsideDre),
    ];

    private static DreAmount A(Guid account, int month, decimal value) => new(account, Jan.AddMonths(month - 1), value);

    private static DreStatement Statement() => DreBuilder.Build(Months, Chart,
    [
        A(R11, 1, 10_000m), A(R11, 2, 5_000m), A(R11, 3, 8_000m), A(R12, 3, 2_000m),
        A(C25, 1, -1_000m), A(C25, 3, -1_000m),
        A(C21, 1, -4_000m), A(C21, 2, -6_000m), A(C21, 3, -3_000m),
        A(E31, 1, -2_000m), A(E31, 2, -2_000m), A(E31, 3, -2_000m),
        A(R13, 2, 100m), A(E35, 3, -50m),
        A(I41, 1, -9_999m),
    ], [0m, 300m, 0m]);

    [Fact]
    public void Linhas_na_ordem_do_CA_1_com_o_sinal_no_rotulo()
    {
        var top = Statement().Rows.Where(r => r.Kind != DreRowKind.Account).Select(r => r.Label);
        top.Should().Equal("Receita bruta", "(−) Deduções", "= Receita líquida", "(−) Custos", "= Lucro bruto", "(−) Despesas operacionais", "= Resultado operacional",
            "(±) Resultado financeiro", "(±) Outros resultados", "= Resultado do período", "Adiantamentos de clientes (sinal ainda sem nota)");
    }

    [Fact]
    public void Subtotais_mes_a_mes_e_total()
    {
        var s = Statement();
        s.Row(DreBuilder.GroupKey(DreGroup.GrossRevenue)).Values.Should().Equal(10_000m, 5_000m, 10_000m);
        s.Row(DreBuilder.GroupKey(DreGroup.Deductions)).Values.Should().Equal(1_000m, 0m, 1_000m);
        s.Row(DreBuilder.NetRevenue).Values.Should().Equal(9_000m, 5_000m, 9_000m);
        s.Row(DreBuilder.GroupKey(DreGroup.Costs)).Values.Should().Equal(new[] { 4_000m, 6_000m, 3_000m }, "o custo aparece positivo: o sinal está no rótulo");
        s.Row(DreBuilder.GrossProfit).Values.Should().Equal(5_000m, -1_000m, 6_000m);
        s.Row(DreBuilder.GroupKey(DreGroup.OperatingExpenses)).Values.Should().Equal(2_000m, 2_000m, 2_000m);
        s.Row(DreBuilder.OperatingResult).Values.Should().Equal(3_000m, -3_000m, 4_000m);
        s.Row(DreBuilder.GroupKey(DreGroup.FinancialResult)).Values.Should().Equal(0m, 100m, -50m);
        s.Row(DreBuilder.PeriodResult).Values.Should().Equal(3_000m, -2_900m, 3_950m);
        s.Row(DreBuilder.PeriodResult).Total.Should().Be(4_050m);
    }

    [Fact]
    public void Percentual_da_receita_liquida_e_margem_bruta()
    {
        var s = Statement();
        s.Row(DreBuilder.NetRevenue).PercentOfNetRevenue.Should().Be(100m);
        s.Row(DreBuilder.GrossProfit).PercentOfNetRevenue.Should().Be(43.48m, "10.000 ÷ 23.000");
        s.Row(DreBuilder.GroupKey(DreGroup.Costs)).PercentOfNetRevenue.Should().Be(56.52m);
    }

    [Fact]
    public void Delta_mes_anterior_em_valor_e_percentual()
    {
        var s = Statement();
        s.Row(DreBuilder.PeriodResult).Delta.Should().Be(6_850m);
        s.Row(DreBuilder.PeriodResult).DeltaPercent.Should().Be(236.21m, "6.850 sobre |−2.900|");
        s.Row(DreBuilder.GroupKey(DreGroup.Costs)).Delta.Should().Be(-3_000m);
        s.Row(DreBuilder.GroupKey(DreGroup.Costs)).DeltaPercent.Should().Be(-50m);
        s.Row(DreBuilder.GroupKey(DreGroup.OtherResults)).DeltaPercent.Should().BeNull("sem base no mês anterior");
        DreBuilder.Delta([10m]).Should().Be(((decimal?)null, (decimal?)null), "um mês só não tem anterior");
    }

    [Fact]
    public void Critico_so_nos_resultados()
    {
        var s = Statement();
        s.Rows.Where(r => r.CriticalWhenNegative).Select(r => r.Key).Should().BeEquivalentTo([DreBuilder.GrossProfit, DreBuilder.OperatingResult, DreBuilder.PeriodResult]);
        s.Rows.Where(r => r.Group is DreGroup.Costs or DreGroup.OperatingExpenses or DreGroup.Deductions).Should().OnlyContain(r => !r.CriticalWhenNegative);
    }

    [Fact]
    public void Arvore_por_grupo_com_sinteticas_e_sem_Fora_da_DRE()
    {
        var s = Statement();
        var receitas = s.Row(DreBuilder.AccountKey(R));
        receitas.Should().Match<DreRow>(r => r.Level == 2 && r.HasChildren && r.ParentKey == DreBuilder.GroupKey(DreGroup.GrossRevenue));
        receitas.Values.Should().Equal(10_000m, 5_000m, 10_000m);
        s.Row(DreBuilder.AccountKey(R11)).Should().Match<DreRow>(r => r.Level == 3 && r.ParentKey == DreBuilder.AccountKey(R));
        s.Rows.Should().NotContain(r => r.AccountId == R19, "conta sem valor no período não aparece");
        s.Row(DreBuilder.AccountKey(R13)).Should().Match<DreRow>(r => r.Level == 2 && r.ParentKey == DreBuilder.GroupKey(DreGroup.FinancialResult), "1.3 fica na raiz do Resultado financeiro");
        s.Row(DreBuilder.AccountKey(C25)).ParentKey.Should().Be(DreBuilder.GroupKey(DreGroup.Deductions));
        s.Row(DreBuilder.AccountKey(C21)).Values.Should().Equal(4_000m, 6_000m, 3_000m);
        s.Rows.Should().NotContain(r => r.AccountId == I || r.AccountId == I41);
        var order = s.Rows.Select(r => r.Key).ToList();
        order.IndexOf(DreBuilder.AccountKey(R11)).Should().BeLessThan(order.IndexOf(DreBuilder.AccountKey(R12)));
        order.IndexOf(DreBuilder.AccountKey(R12)).Should().BeLessThan(order.IndexOf(DreBuilder.GroupKey(DreGroup.Deductions)));
    }

    [Fact]
    public void Linha_de_memoria_dos_adiantamentos_fica_fora_do_resultado()
    {
        var s = Statement();
        var memo = s.Row(DreBuilder.Advances);
        memo.Values.Should().Equal(0m, 300m, 0m);
        memo.Total.Should().Be(0m, "saldo no fim do período, não soma");
        s.Row(DreBuilder.PeriodResult).Values.Should().Equal(new[] { 3_000m, -2_900m, 3_950m }, "o adiantamento não muda o resultado");
    }

    [Fact]
    public void Documento_em_conta_fora_do_plano_vai_para_outros_resultados()
    {
        var lost = Guid.NewGuid();
        var s = DreBuilder.Build(Months, Chart, [A(lost, 2, -70m)]);
        s.Row(DreBuilder.GroupKey(DreGroup.OtherResults)).Values.Should().Equal(0m, -70m, 0m);
        s.Row(DreBuilder.PeriodResult).Total.Should().Be(-70m);
    }

    [Theory]
    [InlineData(ReceivableStatus.Open, DocumentOrigin.Manual, true)]
    [InlineData(ReceivableStatus.Paid, DocumentOrigin.Contract, true)]
    [InlineData(ReceivableStatus.PartiallyPaid, DocumentOrigin.Manual, true)]
    [InlineData(ReceivableStatus.Cancelled, DocumentOrigin.Manual, false)]
    [InlineData(ReceivableStatus.Planned, DocumentOrigin.Contract, false)]
    [InlineData(ReceivableStatus.Open, DocumentOrigin.Opening, false)]
    public void Recebivel_entra_na_receita(ReceivableStatus status, DocumentOrigin origin, bool expected) => DreRules.IncludesReceivable(status, origin).Should().Be(expected);

    [Theory]
    [InlineData(PayableStatus.Open, DocumentOrigin.Purchase, true)]
    [InlineData(PayableStatus.Paid, DocumentOrigin.Installer, true)]
    [InlineData(PayableStatus.AwaitingApproval, DocumentOrigin.Recurring, true)]
    [InlineData(PayableStatus.Cancelled, DocumentOrigin.Manual, false)]
    [InlineData(PayableStatus.Planned, DocumentOrigin.Purchase, false)]
    [InlineData(PayableStatus.Open, DocumentOrigin.Opening, false)]
    public void Titulo_a_pagar_entra_na_despesa(PayableStatus status, DocumentOrigin origin, bool expected) => DreRules.IncludesPayable(status, origin).Should().Be(expected);

    [Fact]
    public void Data_do_titulo_a_pagar_e_a_competencia_senao_a_do_documento()
    {
        DreRules.PayableDate(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 25)).Should().Be(new DateOnly(2026, 2, 1));
        DreRules.PayableDate(null, new DateOnly(2026, 1, 25)).Should().Be(new DateOnly(2026, 1, 25));
    }

    [Theory]
    [InlineData(DocumentOrigin.Contract, TriggerEvent.Signature, true)]
    [InlineData(DocumentOrigin.Contract, TriggerEvent.FabricationDone, false)]
    [InlineData(DocumentOrigin.Contract, null, false)]
    [InlineData(DocumentOrigin.Manual, TriggerEvent.Signature, false)]
    public void Sinal_e_o_marco_de_assinatura_do_contrato(DocumentOrigin origin, TriggerEvent? trigger, bool expected) => DreRules.IsDownPayment(origin, trigger).Should().Be(expected);

    [Fact]
    public void Baixas_mapeiam_juros_multa_e_descontos_para_as_contas_dos_parametros()
    {
        var accounts = new DreSettlementAccounts(R13, E35, Guid.NewGuid(), Guid.NewGuid());
        DreRules.ReceivableSettlement(12.5m, 3m, accounts).Should().Equal((R13, 12.5m), (accounts.DiscountGranted, -3m));
        DreRules.PayableSettlement(7m, 2m, accounts).Should().Equal((E35, -7m), (accounts.DiscountReceived, 2m));
        DreRules.ReceivableSettlement(0m, 0m, accounts).Should().BeEmpty();
    }

    [Fact]
    public void Sinal_reconhecido_na_expedicao_ou_com_o_marco_seguinte()
    {
        DownPaymentRule.Recognition(new DateOnly(2026, 3, 10), new DateOnly(2026, 2, 1), "Entrega (40%)")
            .Should().Be((new DateOnly(2026, 3, 10), "Sinal reconhecido na expedição de 10/03/2026"));
        DownPaymentRule.Recognition(null, new DateOnly(2026, 2, 1), "Entrega (40%)").Should().Be((new DateOnly(2026, 2, 1), "Sinal reconhecido com o marco Entrega (40%)"));
        DownPaymentRule.Recognition(null, null, null).Should().BeNull();
        DownPaymentRule.MilestoneName("CTR-2026-0001 — Fabricação concluída (40%)").Should().Be("Fabricação concluída (40%)");
    }

    [Fact]
    public void Saldo_dos_adiantamentos_no_fim_do_mes()
    {
        (DateOnly, DateOnly?, decimal)[] downs = [(new DateOnly(2026, 1, 10), new DateOnly(2026, 3, 5), 300m), (new DateOnly(2026, 2, 20), null, 50m)];
        DownPaymentRule.BalanceAt(new DateOnly(2026, 1, 31), downs).Should().Be(300m);
        DownPaymentRule.BalanceAt(new DateOnly(2026, 2, 28), downs).Should().Be(350m);
        DownPaymentRule.BalanceAt(new DateOnly(2026, 3, 31), downs).Should().Be(50m);
    }

    private sealed class FakeRecognition(RevenueBasis basis, decimal perDay) : IRevenueRecognition
    {
        public List<(DateOnly From, DateOnly To)> Calls { get; } = [];

        public RevenueBasis Basis => basis;

        public Task<RevenueRecognitionResult> RecognizeAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken cancellationToken = default)
        {
            Calls.Add((from, to));
            return Task.FromResult(new RevenueRecognitionResult([new RevenueAmount(R11, null, from, perDay)], [new RevenueAdvance(Guid.NewGuid(), "S", Guid.NewGuid(), R11, null, from, null, perDay, null, basis)]));
        }

        public Task<IReadOnlyList<RevenueDocument>> DocumentsAsync(IReadOnlyCollection<Guid> accountIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RevenueDocument>>([new RevenueDocument(basis.ToString(), Guid.NewGuid(), "N", Guid.NewGuid(), R11, null, from, perDay, null, "/x")]);
    }

    private sealed class FixedCutover(DateOnly? on) : IRevenueCutover
    {
        public Task<DateOnly?> CutoverOnAsync(CancellationToken cancellationToken = default) => Task.FromResult(on);
    }

    [Fact]
    public async Task Sem_regra_pela_nota_tudo_e_provisorio_pelo_titulo()
    {
        var title = new FakeRecognition(RevenueBasis.Title, 1m);
        var router = new RevenueRecognitionRouter([title], new FixedCutover(new DateOnly(2026, 2, 1)));
        var routed = await router.RecognizeAsync(Jan, new DateOnly(2026, 3, 31), null, TestContext.Current.CancellationToken);
        title.Calls.Should().Equal((Jan, new DateOnly(2026, 3, 31)));
        routed.EffectiveCutover.Should().BeNull("sem a implementação pela nota a data de corte não vale");
        Months.Should().OnlyContain(m => routed.IsProvisional(m));
    }

    [Fact]
    public async Task Com_data_de_corte_os_meses_antes_ficam_pelo_titulo_e_provisorios()
    {
        var title = new FakeRecognition(RevenueBasis.Title, 1m);
        var invoice = new FakeRecognition(RevenueBasis.Invoice, 2m);
        var router = new RevenueRecognitionRouter([title, invoice], new FixedCutover(new DateOnly(2026, 2, 15)));
        var routed = await router.RecognizeAsync(Jan, new DateOnly(2026, 3, 31), null, TestContext.Current.CancellationToken);
        title.Calls.Should().Equal((Jan, new DateOnly(2026, 2, 14)));
        invoice.Calls.Should().Equal((new DateOnly(2026, 2, 15), new DateOnly(2026, 3, 31)));
        routed.Amounts.Sum(a => a.Amount).Should().Be(3m);
        routed.IsProvisional(Jan).Should().BeTrue();
        routed.IsProvisional(new DateOnly(2026, 2, 1)).Should().BeTrue("o mês começou antes da data de corte");
        routed.IsProvisional(new DateOnly(2026, 3, 1)).Should().BeFalse();
        routed.AdvanceBalance(Jan).Should().Be(1m, "o saldo de janeiro vem da regra pelo título");
        routed.AdvanceBalance(new DateOnly(2026, 3, 1)).Should().Be(2m, "o de março, da regra pela nota");
        var docs = await router.DocumentsAsync([R11], Jan, new DateOnly(2026, 3, 31), TestContext.Current.CancellationToken);
        docs.Select(d => d.Kind).Should().Equal("Title", "Invoice");
    }

    [Fact]
    public async Task Data_de_corte_antes_do_periodo_usa_so_a_nota()
    {
        var title = new FakeRecognition(RevenueBasis.Title, 1m);
        var invoice = new FakeRecognition(RevenueBasis.Invoice, 2m);
        var router = new RevenueRecognitionRouter([title, invoice], new FixedCutover(new DateOnly(2025, 6, 1)));
        await router.RecognizeAsync(Jan, new DateOnly(2026, 3, 31), null, TestContext.Current.CancellationToken);
        title.Calls.Should().BeEmpty();
        invoice.Calls.Should().Equal((Jan, new DateOnly(2026, 3, 31)));
    }
}
