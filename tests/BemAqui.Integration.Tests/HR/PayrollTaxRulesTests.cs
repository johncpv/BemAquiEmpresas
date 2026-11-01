using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-01: regras sem banco das tabelas legais. <b>Dataset sintético</b> (ADR 0039 §4): as tabelas abaixo são inventadas para o teste, com
/// números redondos e resultados calculados à mão; não são a legislação de nenhum ano. Cobre validação (CA 9), escolha por competência e a
/// propriedade "no máximo uma vigente por tipo e data" (CA 8 e 11), o cálculo centavo a centavo com o arredondamento (CA 10) e o pacote.
/// </summary>
public class PayrollTaxRulesTests
{
    /// <summary>INSS sintético: 10% até 1.000, 15% até 2.000, 20% até 4.000 (teto).</summary>
    private static readonly InssContent SyntheticInss = new([new(1000m, 10m), new(2000m, 15m), new(4000m, 20m)]);

    /// <summary>IRRF sintético contínuo: isento até 2.000; 10% (−200) até 3.000; 20% (−500) até 5.000; 30% (−1.000) acima. Redução até 4.000 de 150; até 6.000 de 600 − 10%.</summary>
    private static readonly IrrfContent SyntheticIrrf = new(
        [new(2000m, 0m, 0m), new(3000m, 10m, 200m), new(5000m, 20m, 500m), new(null, 30m, 1000m)], 100m, 400m,
        [new(4000m, 150m, 0m), new(6000m, 600m, 10m)]);

    private static readonly DateOnly Jan = new(2031, 1, 1);

    private static PayrollTaxTable Table(PayrollTaxTableKind kind, DateOnly from, PayrollTaxContent content, PayrollRounding rounding = PayrollRounding.AwayFromZero, Guid? rectifies = null)
    {
        var table = new PayrollTaxTable { Kind = kind, ValidFrom = from, Rounding = rounding, Source = "Sintética", RectifiesId = rectifies };
        table.SetContent(content);
        return table;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(500, 50)]
    [InlineData(1000, 100)]
    [InlineData(1500, 175)]
    [InlineData(3000, 450)]
    [InlineData(4000, 650)]
    [InlineData(5000, 650)]
    [InlineData(1234.57, 135.19)]
    public void Inss_progressivo_ate_o_teto(decimal salary, decimal expected) =>
        PayrollTaxRules.Inss(SyntheticInss, PayrollRounding.AwayFromZero, salary).Amount.Should().Be(expected);

    [Theory]
    [InlineData(PayrollRounding.AwayFromZero, 100.05)]
    [InlineData(PayrollRounding.ToEven, 100.04)]
    [InlineData(PayrollRounding.Truncate, 100.04)]
    public void Inss_arredonda_pela_regra_da_versao(PayrollRounding rounding, decimal expected)
    {
        // 100 + 0,30 × 15% = 100,045: o meio exato mostra a diferença entre as regras.
        var result = PayrollTaxRules.Inss(SyntheticInss, rounding, 1000.30m);
        result.Amount.Should().Be(expected);
        result.Memory.Should().Contain(m => m.StartsWith("Arredondamento", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(3000, 300, 0, 0)]
    [InlineData(3500, 0, 0, 0)]
    [InlineData(2000, 0, 0, 0)]
    [InlineData(4500, 450, 0, 160)]
    [InlineData(5000, 550, 2, 250)]
    [InlineData(7000, 650, 0, 905)]
    [InlineData(10000, 650, 1, 1775)]
    public void Irrf_com_deducoes_desconto_simplificado_e_reducao(decimal income, decimal inss, int dependents, decimal expected) =>
        PayrollTaxRules.Irrf(SyntheticIrrf, PayrollRounding.AwayFromZero, income, inss, dependents).Amount.Should().Be(expected);

    [Theory]
    [InlineData(PayrollRounding.AwayFromZero, 320.05)]
    [InlineData(PayrollRounding.ToEven, 320.04)]
    [InlineData(PayrollRounding.Truncate, 320.04)]
    public void Irrf_arredonda_no_resultado_depois_da_reducao(PayrollRounding rounding, decimal expected)
    {
        // Base 5.000,15 − 400 (simplificado) = 4.600,15 → 420,03; redução 600 − 500,015 = 99,985 → 320,045.
        var result = PayrollTaxRules.Irrf(SyntheticIrrf, rounding, 5000.15m, 0m, 0);
        result.Amount.Should().Be(expected);
        result.Memory.Should().Contain(m => m.Contains("Desconto simplificado", StringComparison.Ordinal));
        result.Memory.Should().Contain(m => m.StartsWith("Redução adicional", StringComparison.Ordinal));
    }

    [Fact]
    public void Demais_tipos_centavo_a_centavo()
    {
        var family = new FamilyAllowanceContent(1500m, 50m);
        PayrollTaxRules.FamilyAllowance(family, PayrollRounding.AwayFromZero, 1500m, 2).Amount.Should().Be(100m);
        PayrollTaxRules.FamilyAllowance(family, PayrollRounding.AwayFromZero, 1500.01m, 2).Amount.Should().Be(0m);
        PayrollTaxRules.Percent("FGTS", 7.5m, PayrollRounding.AwayFromZero, 1000.10m).Amount.Should().Be(75.01m);
        PayrollTaxRules.Percent("FGTS", 7.5m, PayrollRounding.Truncate, 1000.10m).Amount.Should().Be(75.00m);
        PayrollTaxRules.Percent("VT", 5m, PayrollRounding.AwayFromZero, 1234.50m).Amount.Should().Be(61.73m);
        PayrollTaxRules.Percent("VT", 5m, PayrollRounding.ToEven, 1234.50m).Amount.Should().Be(61.72m);
        var charges = new EmployerChargesContent("Regime sintético", 10m, 2m, 1.5m, 4m);
        PayrollTaxRules.EmployerCharges(charges, PayrollRounding.AwayFromZero, 2000m).Amount.Should().Be(340m);
        PayrollTaxRules.EmployerCharges(charges, PayrollRounding.AwayFromZero, 1000.05m).Amount.Should().Be(170.01m);
    }

    [Fact]
    public void Validacao_recusa_lacuna_sobreposicao_aliquota_fora_e_vigencia_no_meio_do_mes()
    {
        Errors(PayrollTaxTableKind.Inss, Jan, new(Inss: SyntheticInss)).Should().BeEmpty();
        Errors(PayrollTaxTableKind.Inss, Jan, new(Inss: new([new(2000m, 10m), new(1000m, 15m)]))).Should().ContainKey("content.brackets[1]");
        Errors(PayrollTaxTableKind.Inss, Jan, new(Inss: new([new(1000m, 10m), new(1000m, 15m)]))).Should().ContainKey("content.brackets[1]", "limite igual sobrepõe");
        Errors(PayrollTaxTableKind.Inss, Jan, new(Inss: new([new(1000m, 101m)]))).Should().ContainKey("content.brackets[0]");
        Errors(PayrollTaxTableKind.Inss, Jan, new(Inss: new([new(1000m, 7.12345m)]))).Should().ContainKey("content.brackets[0]", "alíquota numeric(7,4)");
        Errors(PayrollTaxTableKind.Inss, Jan, new(Inss: new([new(1000m, 10m), new(null, 15m)]))).Should().ContainKey("content.brackets[1]", "o INSS termina no teto");
        Errors(PayrollTaxTableKind.Inss, Jan, new(Inss: new([new(1000.001m, 10m)]))).Should().ContainKey("content.brackets[0]", "dinheiro numeric(18,2)");
        Errors(PayrollTaxTableKind.Inss, new DateOnly(2031, 1, 2), new(Inss: SyntheticInss)).Should().ContainKey("validFrom");
        Errors(PayrollTaxTableKind.Irrf, Jan, new(Irrf: SyntheticIrrf)).Should().BeEmpty();
        Errors(PayrollTaxTableKind.Irrf, Jan, new(Irrf: SyntheticIrrf with { Brackets = [new(2000m, 0m), new(3000m, 10m, 200m)] })).Should().ContainKey("content.brackets[1]", "a última do IRRF é \"acima de\"");
        Errors(PayrollTaxTableKind.Irrf, Jan, new(Irrf: SyntheticIrrf with { Reductions = [new(6000m, 600m, 10m), new(4000m, 150m, 0m)] })).Should().ContainKey("content.reductions[1]");
        Errors(PayrollTaxTableKind.FamilyAllowance, Jan, new(FamilyAllowance: new(0m, 50m))).Should().ContainKey("content.maxRemuneration");
        Errors(PayrollTaxTableKind.EmployerCharges, Jan, new(EmployerCharges: new("", 10m, 2m, 0m, 4m))).Should().ContainKeys("content.taxRegime", "content.fap");
        Errors(PayrollTaxTableKind.MinimumWage, Jan, new(Inss: SyntheticInss)).Should().ContainKey("content", "a seção precisa ser a do tipo");
        PayrollTaxRules.Validate(PayrollTaxTableKind.Fgts, Jan, new(Fgts: new(7.5m, 30m, 1m)), null, " ").Errors.Should().ContainKeys("rounding", "source");
    }

    [Fact]
    public void Irrf_descontinuo_na_fronteira_avisa_sem_bloquear()
    {
        var broken = SyntheticIrrf with { Brackets = [new(2000m, 0m, 0m), new(3000m, 10m, 200m), new(5000m, 20m, 400m), new(null, 30m, 1000m)] };
        var (errors, warnings) = PayrollTaxRules.Validate(PayrollTaxTableKind.Irrf, Jan, new(Irrf: broken), PayrollRounding.AwayFromZero, "Sintética");
        errors.Should().BeEmpty();
        warnings.Should().HaveCount(2, "em 3.000 e em 5.000 a parcela a deduzir não fecha");
        PayrollTaxRules.Validate(PayrollTaxTableKind.Irrf, Jan, new(Irrf: SyntheticIrrf), PayrollRounding.AwayFromZero, "Sintética").Warnings.Should().BeEmpty();
    }

    /// <summary>CA 11: versão de janeiro e de maio do mesmo ano; abril usa janeiro, junho usa maio; antes de janeiro, nenhuma.</summary>
    [Fact]
    public void Competencia_escolhe_a_versao_vigente_de_maior_vale_a_partir_de()
    {
        var jan = Table(PayrollTaxTableKind.Inss, Jan, new(Inss: SyntheticInss));
        jan.Check(Guid.NewGuid(), DateTimeOffset.UtcNow, "Conferido", null);
        var may = Table(PayrollTaxTableKind.Inss, new DateOnly(2031, 5, 1), new(Inss: SyntheticInss));
        var pendingFix = Table(PayrollTaxTableKind.Inss, Jan, new(Inss: SyntheticInss), rectifies: jan.Id);
        var versions = new[] { jan, may, pendingFix };
        PayrollTaxRules.Pick(versions, PayrollTaxTableKind.Inss, new DateOnly(2031, 4, 15)).Should().BeSameAs(jan, "a retificação a conferir não entra");
        PayrollTaxRules.Pick(versions, PayrollTaxTableKind.Inss, new DateOnly(2031, 6, 1)).Should().BeSameAs(may, "a não conferida também vale (simulação)");
        PayrollTaxRules.Pick(versions, PayrollTaxTableKind.Inss, new DateOnly(2030, 12, 1)).Should().BeNull();
        PayrollTaxRules.Pick(versions, PayrollTaxTableKind.Irrf, new DateOnly(2031, 6, 1)).Should().BeNull();

        // Retificação conferida: a anterior sai.
        jan.Supersede(DateTimeOffset.UtcNow);
        pendingFix.Check(Guid.NewGuid(), DateTimeOffset.UtcNow, "Conferido", null);
        PayrollTaxRules.Pick(versions, PayrollTaxTableKind.Inss, new DateOnly(2031, 4, 1)).Should().BeSameAs(pendingFix);
    }

    /// <summary>CA 11 (propriedade): com cadastros, retificações e conferências ao acaso pelas guardas do domínio, cada data tem no máximo uma vigente por tipo.</summary>
    [Fact]
    public void Propriedade_no_maximo_uma_vigente_por_tipo_e_data()
    {
        var random = new Random(20261031);
        var versions = new List<PayrollTaxTable>();
        var kinds = new[] { PayrollTaxTableKind.Inss, PayrollTaxTableKind.Irrf };
        for (var step = 0; step < 600; step++)
        {
            var kind = kinds[random.Next(kinds.Length)];
            var month = new DateOnly(2031, 1, 1).AddMonths(random.Next(24));
            switch (random.Next(3))
            {
                case 0 when !PayrollTaxRules.Conflicts(versions, kind, month, null):
                    versions.Add(Table(kind, month, kind == PayrollTaxTableKind.Inss ? new(Inss: SyntheticInss) : new(Irrf: SyntheticIrrf)));
                    break;
                case 1 when versions.Where(v => v.Status == PayrollTaxTableStatus.Active).ToList() is { Count: > 0 } active:
                    var source = active[random.Next(active.Count)];
                    if (!PayrollTaxRules.HasPendingRectification(versions, source.Id))
                    {
                        versions.Add(Table(source.Kind, source.ValidFrom, source.ReadContent(), rectifies: source.Id));
                    }

                    break;
                case 2 when versions.Where(v => v.Status == PayrollTaxTableStatus.Unconfirmed).ToList() is { Count: > 0 } open:
                    var target = open[random.Next(open.Count)];
                    if (target.RectifiesId is { } rectified)
                    {
                        versions.Single(v => v.Id == rectified).Supersede(DateTimeOffset.UtcNow);
                    }

                    target.Check(Guid.NewGuid(), DateTimeOffset.UtcNow, "Conferido", null);
                    break;
            }

            foreach (var k in kinds)
            {
                for (var m = 0; m < 26; m++)
                {
                    var date = new DateOnly(2030, 12, 1).AddMonths(m);
                    if (PayrollTaxRules.Pick(versions, k, date) is { } picked)
                    {
                        versions.Count(v => v.Kind == k && v.IsCurrent && v.ValidFrom == picked.ValidFrom).Should().Be(1, $"passo {step}, {k} em {date}");
                    }
                }
            }
        }

        versions.Should().Contain(v => v.Status == PayrollTaxTableStatus.Superseded, "o passeio precisa passar por retificação conferida");
    }

    [Fact]
    public void Dataset_le_base_dependentes_e_esperado_e_confere_pelo_simulador()
    {
        var (cases, error) = PayrollTaxRules.ParseCases("base;dependentes;esperado\n1.500,00;0;175,00\r\n3000.00;0;450.00\n");
        error.Should().BeNull();
        cases.Should().Equal(new PayrollTaxCase(1500m, 0, 175m), new PayrollTaxCase(3000m, 0, 450m));
        PayrollTaxRules.ParseCases("1500;x;175").Error.Should().StartWith("Linha 1");
        PayrollTaxRules.ParseCases("  ").Error.Should().NotBeNull();
        var inss = Table(PayrollTaxTableKind.Inss, Jan, new(Inss: SyntheticInss));
        cases.Select(c => PayrollTaxRules.Evaluate(inss, c, null)).Should().Equal(175m, 450m);
        var irrf = Table(PayrollTaxTableKind.Irrf, Jan, new(Irrf: SyntheticIrrf));
        PayrollTaxRules.Evaluate(irrf, new PayrollTaxCase(5000m, 2, 250m), _ => 550m).Should().Be(250m, "o IRRF usa o INSS da vigência");
    }

    [Fact]
    public void Conteudo_vai_e_volta_pelo_esquema_do_tipo()
    {
        var table = Table(PayrollTaxTableKind.Irrf, Jan, new(Irrf: SyntheticIrrf, Inss: SyntheticInss));
        table.SchemaVersion.Should().Be(PayrollTaxSchema.Current);
        table.Content.Should().NotContain("inss", "só a seção do tipo é gravada");
        var back = table.ReadContent().Irrf!;
        back.Brackets.Should().Equal(SyntheticIrrf.Brackets);
        back.Reductions.Should().Equal(SyntheticIrrf.Reductions);
        back.DependentDeduction.Should().Be(100m);
    }

    /// <summary>CA 1: o pacote do ano passa pela mesma validação do cadastro e cada tipo aparece uma vez; o pendente explica o motivo.</summary>
    [Fact]
    public void Pacotes_do_ano_sao_validos()
    {
        PayrollTaxPackages.All.Should().NotBeEmpty();
        foreach (var package in PayrollTaxPackages.All)
        {
            PayrollTaxPackages.Problems(package).Should().BeEmpty();
            package.Tables.Select(t => t.Kind).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(Enum.GetValues<PayrollTaxTableKind>());
            package.Tables.Where(t => t.Pending is null).Should().OnlyContain(t => t.ValidFrom!.Value.Year == package.Package && !string.IsNullOrWhiteSpace(t.Source));
        }

        PayrollTaxPackages.Problems(PayrollTaxPackages.Parse("""{"package":2099,"tables":[{"kind":"Inss","validFrom":"2099-01-15","source":"x","content":{"inss":{"brackets":[]}}}]}"""))
            .Should().HaveCountGreaterThan(1);
    }

    private static PayrollTaxContent Official2026(PayrollTaxTableKind kind) =>
        PayrollTaxPackages.All.Single(p => p.Package == 2026).Tables.Single(t => t.Kind == kind).Content!;

    /// <summary>
    /// FLH-02a: exemplos oficiais da Receita Federal para 2026 (tabela da Lei 15.191/2025 com a redução da Lei 15.270/2025): rendimento
    /// tributável de R$ 6.000,00 com o desconto simplificado → base 5.392,80, imposto 574,29, redução 179,75 → 394,54; R$ 4.500,00 → 0. O
    /// rendimento da redução é o bruto tributável (como nos exemplos; parâmetro <c>hr.irrf_reduction_income</c>).
    /// </summary>
    [Theory]
    [InlineData(6000, 394.54)]
    [InlineData(4500, 0)]
    [InlineData(5000, 0)]
    [InlineData(7350, 945.54)]
    [InlineData(2428.80, 0)]
    public void Irrf_2026_bate_com_os_exemplos_da_receita(decimal income, decimal expected) =>
        PayrollTaxRules.Irrf(Official2026(PayrollTaxTableKind.Irrf).Irrf!, PayrollRounding.AwayFromZero, income, 0m, 0).Amount.Should().Be(expected);

    /// <summary>FLH-02a: INSS de 2026 (Portaria MPS/MF 13/2026), progressivo, arredondado no total (regra da FLH-01): no teto, 988,0914 → 988,09.</summary>
    [Theory]
    [InlineData(1621.00, 121.58)]
    [InlineData(2000.00, 155.69)]
    [InlineData(3000.00, 248.60)]
    [InlineData(5000.00, 501.51)]
    [InlineData(8475.55, 988.09)]
    [InlineData(12000.00, 988.09)]
    public void Inss_2026_progressivo_com_o_teto(decimal salary, decimal expected) =>
        PayrollTaxRules.Inss(Official2026(PayrollTaxTableKind.Inss).Inss!, PayrollRounding.AwayFromZero, salary).Amount.Should().Be(expected);

    private static Dictionary<string, string[]> Errors(PayrollTaxTableKind kind, DateOnly from, PayrollTaxContent content) =>
        PayrollTaxRules.Validate(kind, from, content, PayrollRounding.AwayFromZero, "Sintética").Errors;
}
