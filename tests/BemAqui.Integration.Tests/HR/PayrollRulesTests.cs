using System.Text;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>RH-02a: regras sem banco da folha lançada (totais, competência, vencimento) e o leitor do CSV v1 (erros e injeção).</summary>
public class PayrollRulesTests
{
    private static PayslipLineValue E(decimal v) => new(PayrollItemKind.Earning, v, null);

    private static PayslipLineValue D(decimal v) => new(PayrollItemKind.Deduction, v, null);

    private static PayslipLineValue I(decimal v) => new(PayrollItemKind.Information, v, null);

    [Fact]
    public void Totais_somam_proventos_e_descontos_e_a_informativa_fica_fora_do_liquido()
    {
        var totals = PayrollRules.Totals([E(3000m), E(450.55m), D(330m), D(120.45m), I(276m)]);
        totals.Should().Be(new PayslipTotals(3450.55m, 450.45m, 3000.10m));
        PayrollRules.Totals([]).Should().Be(new PayslipTotals(0m, 0m, 0m));
        PayrollRules.Totals([E(100m), D(150m)]).Net.Should().Be(-50m, "o serviço recusa o líquido negativo");
    }

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(1234.56, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(10.555, false)]
    public void Valor_da_linha_maior_que_zero_com_duas_casas(double amount, bool valid) => PayrollRules.ValidAmount((decimal)amount).Should().Be(valid);

    [Fact]
    public void Funcionario_da_competencia_admitido_ate_o_fim_e_nao_desligado_antes_do_inicio()
    {
        var period = new DateOnly(2026, 10, 1);
        PayrollRules.InPeriod(new DateOnly(2026, 10, 31), null, period).Should().BeTrue("admitido no último dia");
        PayrollRules.InPeriod(new DateOnly(2026, 11, 1), null, period).Should().BeFalse("admitido depois");
        PayrollRules.InPeriod(new DateOnly(2020, 1, 1), new DateOnly(2026, 10, 1), period).Should().BeTrue("desligado no primeiro dia ainda entra (rescisão)");
        PayrollRules.InPeriod(new DateOnly(2020, 1, 1), new DateOnly(2026, 9, 30), period).Should().BeFalse("desligado antes");
    }

    [Fact]
    public void Vencimento_proposto_no_dia_do_mes_seguinte_com_o_ultimo_dia_do_mes_curto()
    {
        PayrollRules.DueDateNextMonth(new DateOnly(2026, 10, 1), 20).Should().Be(new DateOnly(2026, 11, 20));
        PayrollRules.DueDateNextMonth(new DateOnly(2026, 12, 1), 20).Should().Be(new DateOnly(2027, 1, 20));
        PayrollRules.DueDateNextMonth(new DateOnly(2027, 1, 1), 31).Should().Be(new DateOnly(2027, 2, 28));
    }

    [Fact]
    public void Competencia_variacao_e_alerta()
    {
        PayrollRules.ParsePeriod("2026-10").Should().Be(new DateOnly(2026, 10, 1));
        PayrollRules.ParsePeriod("2026-13").Should().BeNull();
        PayrollRules.ParsePeriod("out/2026").Should().BeNull();
        PayrollRules.Variation(1100m, 1000m).Should().Be(10m);
        PayrollRules.Variation(1000m, null).Should().BeNull();
        PayrollRules.Variation(1000m, 0m).Should().BeNull();
        PayrollRules.AboveLimit(10m, 10m).Should().BeFalse("acima do limite, não igual");
        PayrollRules.AboveLimit(-10.01m, 10m).Should().BeTrue("queda também alerta");
        PayrollRules.PeriodLabel(new DateOnly(2026, 10, 1)).Should().Be("outubro de 2026");
    }

    [Theory]
    [InlineData("he50", "HE50")]
    [InlineData(" 13SAL ", "13SAL")]
    [InlineData("=HE", null)]
    [InlineData("-SAL", null)]
    [InlineData("ABCDEFGHIJK", null)]
    [InlineData("", null)]
    public void Codigo_da_rubrica(string code, string? expected) => PayrollRules.NormalizeCode(code).Should().Be(expected);

    private static byte[] Csv(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void CSV_v1_com_cabecalho_BOM_aspas_e_decimal_com_virgula()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Csv("matricula_ou_cpf;codigo_rubrica;referencia;valor\r\n123;SAL;30 dias;3.300,00\r\n529.982.247-25;HE50;\"10:00 h\";250,5\r\n\r\n")).ToArray();
        var result = PayrollCsv.Parse(bytes);
        result.Errors.Should().BeEmpty();
        result.Rows.Should().BeEquivalentTo(new[]
        {
            new PayrollCsvRow(2, "123", "SAL", "30 dias", 3300.00m),
            new PayrollCsvRow(3, "529.982.247-25", "HE50", "10:00 h", 250.5m),
        }, o => o.WithStrictOrdering());
        PayrollCsv.IsCpf("529.982.247-25", out var cpf).Should().BeTrue();
        cpf.Should().Be("52998224725");
        PayrollCsv.IsCpf("123", out _).Should().BeFalse("matrícula");
    }

    [Fact]
    public void CSV_com_erros_por_linha_e_formula_tratada_como_texto()
    {
        var result = PayrollCsv.Parse(Csv("1;SAL;;=1+1\n2;SAL;;-100\n3;SAL;;+5\n4;SAL;;@SOMA(A1)\n5;SAL;;abc\n6;SAL\n7;;;10,00\n;SAL;;10,00\n9;SAL;" + new string('x', 21) + ";10,00\n10;SAL;;0,00\n11;SAL;;1,005\n12;SAL;;100,00"));
        result.Rows.Should().ContainSingle().Which.Line.Should().Be(12);
        result.Errors.Should().HaveCount(11);
        result.Errors.Take(5).Should().OnlyContain(e => e.Contains("valor inválido", StringComparison.Ordinal), "célula com =, -, + ou @ é texto, não número (MIG-01a)");
        result.Errors[5].Should().StartWith("Linha 6: são 4 colunas");
        result.Errors[6].Should().StartWith("Linha 7: informe o código");
        result.Errors[7].Should().StartWith("Linha 8: informe a matrícula");
        result.Errors[8].Should().StartWith("Linha 9: a referência");
        result.Errors.Should().NotContain(e => e.Contains("=1+1", StringComparison.Ordinal) || e.Contains("SOMA", StringComparison.Ordinal), "a mensagem não repete o conteúdo da célula");
    }

    [Fact]
    public void CSV_limites_de_tamanho_linhas_e_codificacao()
    {
        PayrollCsv.Parse([]).Errors.Should().ContainSingle().Which.Should().Contain("vazio");
        PayrollCsv.Parse(new byte[PayrollCsv.MaxBytes + 1]).Errors.Should().ContainSingle().Which.Should().Contain("2 MB");
        var many = string.Join('\n', Enumerable.Range(1, PayrollCsv.MaxLines + 2).Select(i => $"{i};SAL;;1,00"));
        PayrollCsv.Parse(Csv(many)).Errors.Should().ContainSingle().Which.Should().Contain("5 mil linhas");
        var exact = "matricula_ou_cpf;codigo_rubrica;referencia;valor\n" + string.Join('\n', Enumerable.Range(1, PayrollCsv.MaxLines).Select(i => $"{i};SAL;;1,00"));
        PayrollCsv.Parse(Csv(exact)).Rows.Should().HaveCount(PayrollCsv.MaxLines, "5 mil linhas mais o cabeçalho");
        PayrollCsv.Parse([0x31, 0x3B, 0xFF, 0xFE]).Errors.Should().ContainSingle().Which.Should().Contain("UTF-8");
        PayrollCsv.SafeCode("HE'75<script>").Should().Be("HE75script");
    }
}
