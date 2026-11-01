using BemAqui.BuildingBlocks.Privacy;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>LGPD-01b, regras sem banco: a decisão de retenção por tipo de dado e data (CA 1 e 3) e o marcador (CA 4 e 9).</summary>
public class PersonalDataRetentionTests
{
    private static readonly DateOnly Today = new(2026, 10, 31);

    private static PersonalDataRetentionRule Rule(PersonalDataSubjectKind kind, string group) => PersonalDataRetention.Defaults.RuleOf(kind, group);

    [Fact]
    public void Tabela_pre_configurada_pela_ADR_0018()
    {
        RetentionGroups.Defaults.Should().HaveCount(12);
        Rule(PersonalDataSubjectKind.Employee, RetentionGroups.Contact).Rule.Should().Be(RetentionRuleKind.Anytime, "telefone e e-mail pessoal a qualquer momento");
        Rule(PersonalDataSubjectKind.Employee, RetentionGroups.Geolocation).Rule.Should().Be(RetentionRuleKind.Anytime);
        Rule(PersonalDataSubjectKind.Employee, RetentionGroups.Dependents).Rule.Should().Be(RetentionRuleKind.Anytime);
        Rule(PersonalDataSubjectKind.Employee, RetentionGroups.Bank).Should().Be(new PersonalDataRetentionRule(PersonalDataSubjectKind.Employee, RetentionGroups.Bank, RetentionRuleKind.AfterYears, 0, RetentionBase.LastPayment));
        Rule(PersonalDataSubjectKind.Employee, RetentionGroups.Labor).Should().Be(new PersonalDataRetentionRule(PersonalDataSubjectKind.Employee, RetentionGroups.Labor, RetentionRuleKind.AfterYears, 5, RetentionBase.Termination));
        Rule(PersonalDataSubjectKind.Party, RetentionGroups.Identification).Should().Be(new PersonalDataRetentionRule(PersonalDataSubjectKind.Party, RetentionGroups.Identification, RetentionRuleKind.AfterYears, 5, RetentionBase.FiscalYearEnd));
        RetentionGroups.Defaults.Select(d => RetentionGroups.SeedKey(d.Rule.SubjectType, d.Rule.FieldGroup)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Trabalhista_fica_retido_sem_desligamento_e_ate_5_anos_depois_dele()
    {
        var labor = Rule(PersonalDataSubjectKind.Employee, RetentionGroups.Labor);
        var active = PersonalDataRetention.Decide(labor, null, Today);
        active.Retained.Should().BeTrue("sem desligamento, nada trabalhista é anonimizado (CA 3)");
        active.Until.Should().BeNull();
        active.Reason.Should().Be("obrigação trabalhista e previdenciária");

        PersonalDataRetention.Decide(labor, new DateOnly(2023, 3, 10), Today).Should().Be(new RetentionDecision(true, new DateOnly(2028, 3, 10), "obrigação trabalhista e previdenciária"));
        PersonalDataRetention.Decide(labor, new DateOnly(2021, 10, 31), Today).Retained.Should().BeTrue("no último dia do prazo ainda retém");
        PersonalDataRetention.Decide(labor, new DateOnly(2021, 10, 30), Today).Should().Be(RetentionDecision.Free);
    }

    [Fact]
    public void Nome_nas_notas_retido_ate_o_fim_do_exercicio_mais_5_anos_e_livre_sem_documento()
    {
        var identification = Rule(PersonalDataSubjectKind.Party, RetentionGroups.Identification);
        PersonalDataRetention.Decide(identification, new DateOnly(2022, 2, 1), Today).Until.Should().Be(new DateOnly(2027, 12, 31));
        PersonalDataRetention.Decide(identification, new DateOnly(2020, 12, 30), Today).Should().Be(RetentionDecision.Free, "31/12/2025 já passou");
        PersonalDataRetention.Decide(identification, null, Today).Should().Be(RetentionDecision.Free, "nenhum documento com movimento");
    }

    [Fact]
    public void Conta_bancaria_sai_depois_do_ultimo_pagamento_e_a_qualquer_momento_nunca_retem()
    {
        var bank = Rule(PersonalDataSubjectKind.Employee, RetentionGroups.Bank);
        PersonalDataRetention.Decide(bank, null, Today).Retained.Should().BeTrue("pagamento ainda corre");
        PersonalDataRetention.Decide(bank, Today, Today).Retained.Should().BeTrue("no dia do pagamento");
        PersonalDataRetention.Decide(bank, Today.AddDays(-1), Today).Should().Be(RetentionDecision.Free);
        PersonalDataRetention.Decide(Rule(PersonalDataSubjectKind.Employee, RetentionGroups.Contact), null, Today).Should().Be(RetentionDecision.Free);
    }

    [Fact]
    public void Tabela_editada_vale_e_o_contexto_usa_a_data_base_reunida()
    {
        var edited = new PersonalDataRetention([new PersonalDataRetentionRule(PersonalDataSubjectKind.Employee, RetentionGroups.Labor, RetentionRuleKind.AfterYears, 10, RetentionBase.Termination)]);
        var subject = new PersonalDataSubject(PersonalDataSubjectKind.Employee, Guid.NewGuid(), "Ana Lima", []);
        var context = new PersonalDataAnonymizationContext(subject, "LGPD-2026-0007", Today, edited, new Dictionary<string, DateOnly> { [RetentionAnchors.Termination] = new(2019, 1, 15) });
        context.Decide(RetentionGroups.Labor).Until.Should().Be(new DateOnly(2029, 1, 15), "10 anos pela tabela editada");
        context.Decide(RetentionGroups.Address).Should().Be(RetentionDecision.Free, "o grupo sem linha usa o sugerido: 5 anos");
        context.Marker.Should().Be("Titular anonimizado LGPD-2026-0007");
        context.IdentityGroup.Should().Be(RetentionGroups.Labor);
        context.FullName.Should().Be("Ana Lima");
    }

    [Fact]
    public void Marcador_troca_so_os_enderecos_do_titular_e_documento_unico_nunca_e_CPF()
    {
        PersonalDataMarker.Recipients("Ana@Exemplo.com, outro@exemplo.com", ["ana@exemplo.com"], "Titular anonimizado X").Should().Be("Titular anonimizado X, outro@exemplo.com");
        PersonalDataMarker.HasAny("outro@exemplo.com", ["ana@exemplo.com"]).Should().BeFalse();
        var id = Guid.CreateVersion7();
        var document = PersonalDataMarker.Document(id);
        document.Should().HaveLength(11);
        BrazilianDocuments.KindOf(document).Should().BeNull("não pode virar um CPF de outra pessoa");
        PersonalDataMarker.Document(Guid.CreateVersion7()).Should().NotBe(document, "único pelo id (índice único do CPF)");
        PersonalDataMarker.Email("LGPD-2026-0001", id).Should().EndWith("@anonimizado.invalid");
    }
}
