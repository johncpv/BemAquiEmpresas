using System.Text;
using BemAqui.BuildingBlocks.Export;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>RH-03: regras sem banco do layout padrão v1 do pacote para a contadora (linhas, formatos, proteção de célula e o leia-me).</summary>
public class AccountantExportRulesTests
{
    private static readonly DateOnly March = new(2024, 3, 1);

    /// <summary>CA 7: o teste golden trava o cabeçalho de cada arquivo da versão atual. Mudou coluna, sobe a versão (v2) e este teste muda junto.</summary>
    [Fact]
    public void Golden_os_cabecalhos_do_layout_v1_estao_travados()
    {
        AccountantLayoutV1.Version.Should().Be("v1");
        string.Join(';', AccountantLayoutV1.MovementsHeader).Should().Be("evento;matricula;nome;cpf;nascimento;cargo;cbo;salario;jornada;data;tipo_afastamento;fim_previsto");
        string.Join(';', AccountantLayoutV1.PayrollHeader).Should().Be("registro;tipo_folha;matricula_ou_cpf;codigo_rubrica;descricao_rubrica;referencia;valor;tipo_guia;vencimento");
        AccountantLayoutV1.MovementsFile.Should().Be("movimentacao.csv");
        AccountantLayoutV1.PayrollFile.Should().Be("folha.csv");
        AccountantLayoutV1.ReadmeFile.Should().Be("leia-me.txt");
        AccountantLayouts.Default.Should().Be("v2", "RH-01c: o v2 traz o ponto.csv");
        AccountantLayoutV2.Version.Should().Be("v2");
        AccountantLayoutV2.TimesheetFile.Should().Be("ponto.csv");
        string.Join(';', AccountantLayoutV2.TimesheetHeader).Should().Be("matricula_ou_cpf;nome;inicio;fim;dias_trabalhados;horas_esperadas;horas_trabalhadas;horas_extras;faltas_dias;faltas_horas;atrasos_horas;horas_abonadas;abonos_por_motivo;banco_periodo;banco_acumulado");
    }

    /// <summary>CA 4: minimização, nenhuma coluna de endereço, contato, conta, dependente, localização ou motivo.</summary>
    [Fact]
    public void Cabecalhos_nao_tem_colunas_proibidas()
    {
        var forbidden = new[] { "endereco", "rua", "cep", "bairro", "cidade", "telefone", "email", "banco", "agencia", "conta", "pix", "dependente", "motivo", "latitude", "longitude", "localizacao", "documento", "anexo" };
        AccountantLayoutV1.MovementsHeader.Concat(AccountantLayoutV1.PayrollHeader).Should().NotContain(c => forbidden.Any(f => c.Contains(f, StringComparison.Ordinal)));
    }

    /// <summary>CA 2: uma linha por evento, cada um só com as colunas dele, datas dd/mm/aaaa e decimal com vírgula.</summary>
    [Fact]
    public void Movimentacao_com_o_dataset_fixo()
    {
        var rows = AccountantLayoutV1.MovementRows(
        [
            new(AccountantEventKind.Leave, "003", "Carla", "11144477735", new DateOnly(2024, 2, 25), LeaveType: AbsenceReason.MedicalCertificate, LeaveEnd: new DateOnly(2024, 3, 5)),
            new(AccountantEventKind.Admission, "001", "Ana", "01234567890", new DateOnly(2024, 3, 5), new DateOnly(1990, 5, 10), "Montador", "715505", 3500m, "44 h"),
            new(AccountantEventKind.Termination, null, "Bruno", "52998224725", new DateOnly(2024, 3, 20), Salary: 9999m),
            new(AccountantEventKind.SalaryChange, "003", "Carla", "11144477735", new DateOnly(2024, 3, 1), Salary: 4000.5m),
            new(AccountantEventKind.PositionChange, "003", "Carla", "11144477735", new DateOnly(2024, 3, 10), Position: "Soldador", Cbo: "724315"),
        ]);
        rows.Should().HaveCount(5);
        rows[0].Should().Equal("afastamento", "003", "Carla", "111.444.777-35", null, null, null, null, null, "25/02/2024", "Atestado médico", "05/03/2024");
        rows[1].Should().Equal("reajuste", "003", "Carla", "111.444.777-35", null, null, null, "4000,50", null, "01/03/2024", null, null);
        rows[2].Should().Equal("admissao", "001", "Ana", "012.345.678-90", "10/05/1990", "Montador", "7155-05", "3500,00", "44 h", "05/03/2024", null, null);
        rows[3].Should().Equal("troca_cargo", "003", "Carla", "111.444.777-35", null, "Soldador", "7243-15", null, null, "10/03/2024", null, null);
        rows[4].Should().Equal("desligamento", null, "Bruno", "529.982.247-25", null, null, null, null, null, "20/03/2024", null, null);
        rows.Should().OnlyContain(r => r.Count == AccountantLayoutV1.MovementsHeader.Count);
    }

    [Fact]
    public void Afastamento_sem_o_tipo_sai_com_a_coluna_vazia()
    {
        var row = AccountantLayoutV1.MovementRows([new(AccountantEventKind.Leave, "1", "X", "11144477735", March, LeaveEnd: March.AddDays(3))]).Single();
        row[10].Should().BeNull();
        row[11].Should().Be("04/03/2024");
    }

    /// <summary>CA 3: uma linha por linha de holerite e uma por guia, com a matrícula ou o CPF.</summary>
    [Fact]
    public void Folha_com_o_dataset_fixo()
    {
        var rows = AccountantLayoutV1.PayrollRows(
            [new(PayrollRunType.Monthly, "001", "SAL", "Salário", "30 dias", 3500m), new(PayrollRunType.Monthly, AccountantLayoutV1.EmployeeKey(null, "01234567890"), "INSS", "INSS", null, 1234.567m)],
            [new(PayrollRunType.Monthly, PayrollChargeKind.Fgts, 480m, new DateOnly(2024, 4, 19))]);
        rows.Should().HaveCount(3);
        rows[0].Should().Equal("holerite", "Mensal", "001", "SAL", "Salário", "30 dias", "3500,00", null, null);
        rows[1].Should().Equal("holerite", "Mensal", "012.345.678-90", "INSS", "INSS", null, "1234,57", null, null);
        rows[2].Should().Equal("guia", "Mensal", null, null, null, null, "480,00", "FGTS", "19/04/2024");
        rows.Should().OnlyContain(r => r.Count == AccountantLayoutV1.PayrollHeader.Count);
    }

    /// <summary>CA 4 (MIG-01a): célula que começa com =, +, - ou @ sai como texto, com o apóstrofo.</summary>
    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+5511", "'+5511")]
    [InlineData("-1+2", "'-1+2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("Ana; Maria", "\"Ana; Maria\"")]
    [InlineData("Ana", "Ana")]
    public void Celula_protegida_contra_formula(string value, string expected)
    {
        var rows = AccountantLayoutV1.MovementRows([new(AccountantEventKind.Termination, null, value, "11144477735", March)]);
        var text = SafeCsv.Text(AccountantLayoutV1.MovementsHeader, rows);
        text.Split("\r\n")[1].Split(';')[2].Should().Be(expected.Split(';')[0]);
        SafeCsv.Cell(value).Should().Be(expected);
    }

    [Fact]
    public void Arquivo_em_utf8_com_bom_e_separador_ponto_e_virgula()
    {
        var bytes = SafeCsv.Bytes(SafeCsv.Text(AccountantLayoutV1.MovementsHeader, []));
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Should().Be(string.Join(';', AccountantLayoutV1.MovementsHeader) + "\r\n");
    }

    [Fact]
    public void Competencia_sugerida_e_a_do_mes_anterior_e_so_o_mes_atual_ou_anterior_exporta()
    {
        var today = new DateOnly(2026, 1, 15);
        AccountantLayoutV1.SuggestedPeriod(today).Should().Be(new DateOnly(2025, 12, 1));
        AccountantLayoutV1.Exportable(new DateOnly(2026, 1, 1), today).Should().BeTrue();
        AccountantLayoutV1.Exportable(new DateOnly(2025, 6, 1), today).Should().BeTrue();
        AccountantLayoutV1.Exportable(new DateOnly(2026, 2, 1), today).Should().BeFalse();
    }

    [Theory]
    [InlineData("2024-02-25", "2024-03-05", true)]
    [InlineData("2024-03-31", "2024-05-01", true)]
    [InlineData("2024-01-01", "2024-02-29", false)]
    [InlineData("2024-04-01", "2024-04-10", false)]
    [InlineData("2024-02-01", "2024-04-30", true)]
    public void Afastamento_entra_quando_cobre_algum_dia_da_competencia(string start, string end, bool expected) =>
        AccountantLayoutV1.LeaveInPeriod(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), March).Should().Be(expected);

    /// <summary>CA 1 e 6: versão, competência, geração, o que substitui e o que entrou e não entrou.</summary>
    [Fact]
    public void Leia_me_com_versao_competencia_e_o_que_entrou()
    {
        var text = AccountantLayoutV1.Readme("v1", false, March, new DateTime(2024, 4, 2, 9, 30, 0), new DateTime(2024, 4, 1, 8, 5, 0),
            ["movimentacao.csv: Movimentação: 3 eventos."], ["Ponto: não incluído; o ponto do sistema ainda não está em uso."]);
        text.Should().Contain("Layout: padrão do sistema v1 (A confirmar com a contadora)");
        text.Should().Contain("Competência: março de 2024 (03/2024)");
        text.Should().Contain("Gerado em: 02/04/2024 09:30");
        text.Should().Contain("Substitui a exportação de 01/04/2024 08:05.");
        text.Should().Contain("- movimentacao.csv: Movimentação: 3 eventos.");
        text.Should().Contain("- Ponto: não incluído; o ponto do sistema ainda não está em uso.");
        AccountantLayoutV1.Readme("v1", true, March, new DateTime(2024, 4, 2), null, [], []).Should().Contain("(conferido com a contadora)").And.NotContain("Substitui").And.Contain("- Nada.");
    }
}
