using System.Text.Json;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Identity.Application.Privacy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Settings;

/// <summary>LGPD-01a, regras sem banco: prazo em dias corridos (CA 1), chip do prazo (CA 3), identidade exigida (CA 5), máscaras e o JSON (CA 8 e 9).</summary>
public class PrivacyRulesTests
{
    [Fact]
    public void Prazo_e_em_dias_corridos_e_o_chip_avisa_a_tres_dias_e_vencido()
    {
        PrivacyRequestRules.DueOn(new DateOnly(2026, 12, 20), 15).Should().Be(new DateOnly(2027, 1, 4), "conta fim de semana e feriado");
        var due = new DateOnly(2026, 11, 10);
        PrivacyRequestRules.DueChip(PrivacyRequestStatus.Open, due, new DateOnly(2026, 11, 6)).Should().BeNull();
        PrivacyRequestRules.DueChip(PrivacyRequestStatus.Open, due, new DateOnly(2026, 11, 7)).Should().Be("warning");
        PrivacyRequestRules.DueChip(PrivacyRequestStatus.Open, due, due).Should().Be("warning");
        PrivacyRequestRules.DueChip(PrivacyRequestStatus.Open, due, due.AddDays(1)).Should().Be("critical");
        PrivacyRequestRules.DueChip(PrivacyRequestStatus.Done, due, due.AddDays(1)).Should().BeNull("fechado não avisa");
    }

    [Fact]
    public void Identidade_exigida_nos_tipos_que_agem_sobre_o_dado_e_exportacao_so_em_acesso_e_portabilidade()
    {
        Enum.GetValues<PrivacyRequestKind>().Where(PrivacyRequestRules.RequiresIdentity).Should().BeEquivalentTo(
            [PrivacyRequestKind.Access, PrivacyRequestKind.Portability, PrivacyRequestKind.Correction, PrivacyRequestKind.Anonymization, PrivacyRequestKind.Deletion]);
        Enum.GetValues<PrivacyRequestKind>().Where(PrivacyRequestRules.Exports).Should().BeEquivalentTo([PrivacyRequestKind.Access, PrivacyRequestKind.Portability]);
    }

    [Fact]
    public void Mascaras_de_terceiros()
    {
        PersonalDataMask.Recipients("titular@x.com, outra@y.com", ["titular@x.com"]).Should().Be("titular@x.com, o***@y.com");
        PersonalDataMask.Cpf("52998224725").Should().Be("***.982.247-**");
        PersonalDataMask.Name("Maria da Silva Souza").Should().Be("Maria S.");
        PersonalDataMask.Phone("(11) 98888-7766").Should().Be("***66");
        PrivacyRequestRules.MaskName("Maria Souza").Should().Be("Maria S.");
    }

    [Fact]
    public void Json_tem_um_bloco_por_modulo_com_origem_campos_e_datas()
    {
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var sections = new[]
        {
            new PersonalDataSection("hr", "RH", [new PersonalDataBlock("Dependentes", "hr.employee_dependents", [PersonalDataRecord.Of(at, ("Nome", "Ana"), ("CPF", PersonalDataMask.Cpf("52998224725")))])],
                [new PersonalDataFile(Guid.NewGuid(), "EmployeeDocument", Guid.NewGuid(), "rg.pdf")], "Observação"),
        };
        var json = JsonDocument.Parse(PrivacyExportService.BuildJson("LGPD-2026-0001", new PersonalDataSubject(PersonalDataSubjectKind.Employee, Guid.NewGuid(), "Fulana", []), at, sections)).RootElement;
        json.GetProperty("pedido").GetString().Should().Be("LGPD-2026-0001");
        var module = json.GetProperty("modulos")[0];
        module.GetProperty("modulo").GetString().Should().Be("hr");
        var block = module.GetProperty("blocos")[0];
        block.GetProperty("origem").GetString().Should().Be("hr.employee_dependents");
        block.GetProperty("registros")[0].GetProperty("campos").GetProperty("CPF").GetString().Should().Be("***.982.247-**");
        block.GetProperty("registros")[0].TryGetProperty("data", out _).Should().BeTrue();
        module.GetProperty("anexos")[0].GetString().Should().Be("anexos/rg.pdf");
    }
}
