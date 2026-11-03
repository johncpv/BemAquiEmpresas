using BemAqui.BuildingBlocks.Export;
using BemAqui.Finance.Application;
using BemAqui.Finance.Application.Exports;
using BemAqui.Finance.Application.Reconciliation;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using ClosedXML.Excel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>EXP-02: montagem das exportações do financeiro a partir do resultado do serviço da tela (colunas, linhas de saldo e de total, regras).</summary>
public class FinanceExportsUnitTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    private static IReadOnlyList<string> Headers<T>(ExportSpec<T> spec) => spec.OutputColumns(ExportFormat.Xlsx, _ => true).Select(c => c.Header).ToList();

    [Fact]
    public void Extrato_comeca_no_saldo_de_partida_termina_no_saldo_final_e_marca_os_estornos()
    {
        StatementLine Line(decimal amount, decimal balance, bool reversal = false, bool reversed = false) =>
            new(Guid.NewGuid(), Day, "Mov", nameof(TransactionType.BankEntry), "Lançamento sem OP", null, amount, balance, true, null, Guid.NewGuid(), reversal, reversed, 1);
        var statement = new StatementResult(Guid.NewGuid(), "Inter", Day.AddDays(-14), Day.AddDays(16), false, true, "Saldo anterior", 100m, 70m,
            new StatementPage([Line(-50m, 50m, reversed: true), Line(20m, 70m, reversal: true)], 1, 50, 2));

        var rows = FinanceExports.StatementRows(statement);

        rows.Select(r => r.Description).Should().Equal("Saldo anterior", "Mov", "Mov", "Saldo final");
        rows[0].Balance.Should().Be(100m);
        rows[^1].Balance.Should().Be(70m);
        rows[1].Outflow.Should().Be(50m);
        rows[1].Situation.Should().Be("Estornado");
        rows[2].Inflow.Should().Be(20m);
        rows[2].Situation.Should().Be("Estorno");
        FinanceExports.Statement.Styles(rows).Select(s => s?.Bold == true).Should().Equal(true, false, false, true);
        FinanceExports.Statement.OutputColumns(ExportFormat.Pdf, _ => true).Select(c => c.Header).Should().Equal("Data", "Descrição", "Origem", "Entrada", "Saída", "Saldo", "Situação");
    }

    [Theory]
    [InlineData(CashFlowService.ScenarioBoth, true, new[] { "Saldo base", "Entradas ajustadas", "Saldo ajustado" }, new string[0])]
    [InlineData(CashFlowService.ScenarioBase, true, new[] { "Saldo base" }, new[] { "Saldo ajustado" })]
    [InlineData(CashFlowService.ScenarioAdjusted, true, new[] { "Entradas ajustadas", "Saldo ajustado" }, new[] { "Saldo base", "Entradas" })]
    [InlineData(CashFlowService.ScenarioBoth, false, new[] { "Saldo base" }, new[] { "Saldo ajustado" })]
    public void Fluxo_leva_so_as_colunas_do_cenario_da_tela(string scenario, bool hasAdjusted, string[] present, string[] absent)
    {
        var headers = Headers(FinanceExports.CashFlow(scenario, hasAdjusted));
        headers.Take(3).Should().Equal("Início", "Fim", "Tipo");
        headers.Should().Contain(present);
        foreach (var header in absent)
        {
            headers.Should().NotContain(header);
        }
    }

    [Fact]
    public void Fluxo_marca_realizado_hoje_e_projetado_e_avisa_quando_filtra_por_conta()
    {
        var periods = new List<CashFlowPeriod>
        {
            new(Day.AddDays(-1), Day.AddDays(-1), 10m, 0m, 110m, false, false, 0m, 0m),
            new(Day, Day, 0m, 5m, 105m, true, true, 3m, 0m),
            new(Day.AddDays(1), Day.AddDays(1), 0m, 0m, 105m, true, false, 0m, 0m),
        };
        var flow = new CashFlowResult(Day, Day.AddDays(-1), Day, "daily", Guid.NewGuid(), 105m, false, periods, null, null, new CashFlowUndated(0, 0));

        FinanceExports.CashFlowRows(flow, null).Select(r => r.Kind).Should().Equal("Realizado", "Hoje", "Projetado");
        FinanceExports.CashFlowNotes(flow, null).Should().Contain("Projeção só no consolidado.");
        FinanceExports.CashFlowHighlights(flow, null).Select(h => h.Item).Should().Contain(["Saldo atual", "A receber sem data (aguardando marco)"]);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("2026-10-30", true)]
    [InlineData("2026-10-31", false)]
    [InlineData("2026-11-30", false)]
    public void Conciliacao_e_previa_enquanto_o_periodo_nao_fecha(string? closedUntil, bool preview)
    {
        var view = new ReconciliationView(Guid.NewGuid(), "Inter", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), true, [], [], [], [],
            new ReconTotals(0, 0, 0, null, null, null, null), null, closedUntil is null ? null : DateOnly.Parse(closedUntil, System.Globalization.CultureInfo.InvariantCulture), 0m, false);
        FinanceExports.IsPreview(view).Should().Be(preview);
        FinanceExports.ReconciliationSheets(view).Select(s => s.Name).Should().Equal("Sugestões", "Sem par no banco", "No sistema, não no banco", "Resumo");
    }

    [Fact]
    public async Task DRE_tem_um_mes_por_coluna_com_provisorio_e_a_hierarquia_em_recuo_e_negrito()
    {
        var months = new List<DreMonth> { new(new DateOnly(2026, 9, 1), false, false), new(new DateOnly(2026, 10, 1), true, false) };
        var rows = new List<DreRow>
        {
            new("group:GrossRevenue", DreRowKind.Group, "(+) Receita bruta", DreGroup.GrossRevenue, null, null, null, 0, [100m, 200m], 300m, 100m, 100m, 100m, false, true),
            new("account:x", DreRowKind.Account, "Vendas", DreGroup.GrossRevenue, Guid.NewGuid(), "1.1", "group:GrossRevenue", 1, [100m, 200m], 300m, 100m, 100m, 100m, false, false),
            new("subtotal:PeriodResult", DreRowKind.Subtotal, "(=) Resultado do período", null, null, null, null, 0, [-10m, 5m], -5m, null, 15m, null, true, false),
        };
        var report = new DreReport(months[0].Month, new DateOnly(2026, 10, 31), months, rows, true, true, [], null, null, 0);

        var spec = FinanceExports.Dre(report);
        Headers(spec).Should().Equal("Código", "Linha", "set/26", "out/26 (provisório)", "Total do período", "% da receita líquida", "Δ mês anterior", "Δ %");
        spec.OutputColumns(ExportFormat.Xlsx, _ => true)[1].Indented.Should().BeTrue();
        var styles = spec.Styles(rows);
        styles[0].Should().Be(new ExportRowStyle(0, true, false));
        styles[1].Should().Be(new ExportRowStyle(1, false, false));
        styles[2].Should().Be(new ExportRowStyle(0, true, true));

        var table = new ExportTable("DRE gerencial", spec.OutputColumns(ExportFormat.Xlsx, _ => true), spec.Rows(ExportFormat.Xlsx, rows, _ => true), [], "Fulano", DateTimeOffset.UtcNow) { RowStyles = styles };
        using var workbook = new XLWorkbook(new MemoryStream(await new XlsxTabularExporter().ExportAsync(table)));
        var sheet = workbook.Worksheet("DRE gerencial");
        sheet.Cell(3, 2).Style.Alignment.Indent.Should().Be(1, "conta um nível abaixo do grupo");
        sheet.Cell(2, 2).Style.Font.Bold.Should().BeTrue();
        sheet.Cell(3, 2).Style.Font.Bold.Should().BeFalse();
        sheet.Row(3).OutlineLevel.Should().Be(1);
        sheet.Cell(2, 2).GetString().Should().Be("(+) Receita bruta", "o recuo é da célula, sem espaços no texto");
        sheet.Cell(4, 5).GetValue<decimal>().Should().Be(-5m);
        sheet.Cell(4, 5).Style.Font.FontColor.Should().NotBe(sheet.Cell(2, 5).Style.Font.FontColor, "resultado negativo em vermelho");
    }

    [Fact]
    public void Inadimplencia_fecha_com_a_linha_de_total_e_sem_projeto_no_fim()
    {
        var report = new AgingReport(Day, 150m, 3, 2, [new("0–30", 0, 30, 100m, 2), new("31–60", 31, 60, 50m, 1), new("61–90", 61, 90, 0m, 0), new("90+", 91, null, 0m, 0)],
            [new(Guid.NewGuid(), "Cliente A", [100m, 0m, 0m, 0m], 100m, 10, 2, null, 12.5m), new(Guid.NewGuid(), "Cliente B", [0m, 50m, 0m, 0m], 50m, 40, 1, 3m, null)]);
        var rows = FinanceExports.AgingCustomerRows(report);
        rows.Select(r => r.Name).Should().Equal("Cliente A", "Cliente B", "Total");
        rows[^1].Buckets.Should().Equal(100m, 50m, 0m, 0m);
        rows[^1].Total.Should().Be(150m);
        var values = FinanceExports.AgingCustomers.Rows(ExportFormat.Xlsx, rows, _ => true);
        Headers(FinanceExports.AgingCustomers).Should().NotContain(h => h.Contains("CPF", StringComparison.Ordinal) || h.Contains("CNPJ", StringComparison.Ordinal));
        values[0][^2].Should().Be(FinanceExports.NoHistory, "sem histórico de pagamento, o texto da tela");

        var projects = new AgingProjectReport(Day, 150m, [new(null, null, null, null, null, [50m, 0m, 0m, 0m], 50m, 3, 1), new(Guid.NewGuid(), "PRJ-1", "Obra", null, "Cliente A", [100m, 0m, 0m, 0m], 100m, 10, 2)]);
        FinanceExports.AgingProjectRows(projects).Select(r => r.Name).Should().Equal("PRJ-1 · Obra", "Sem projeto", "Total");
    }

    [Fact]
    public void Ordens_somam_o_total_do_filtro_na_ultima_linha()
    {
        var orders = new List<PaymentOrderSummary>
        {
            new(Guid.NewGuid(), "OP-1", PaymentOrderStatus.Pending, 100m, Day, null, "Ana", 2, false, DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), "OP-2", PaymentOrderStatus.Paid, 50.25m, Day, null, "Ana", 1, true, DateTimeOffset.UtcNow),
        };
        var rows = FinanceExports.PaymentOrderRows(orders);
        rows.Select(r => r.Situation).Should().Equal("Aguardando aprovação", "Paga", null);
        rows[^1].Total.Should().Be(150.25m);
        rows[^1].Items.Should().Be(3);
        rows[1].SelfApproved.Should().Be("Sim");
    }
}
