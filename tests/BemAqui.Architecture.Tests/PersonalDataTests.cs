using System.Reflection;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// CAD-01a CA 24 (fail-closed): toda propriedade de entidade auditável do Parties é [PersonalData] ou está na lista de
/// não sensíveis. Propriedade nova sem decisão quebra o teste: assim nenhum dado pessoal entra na auditoria em claro.
/// </summary>
public class PersonalDataTests
{
    private static readonly Dictionary<string, string[]> NonSensitive = new(StringComparer.Ordinal)
    {
        ["Party"] = ["Id", "Kind", "LegalName", "TradeName", "SearchText", "StateRegistration", "MunicipalRegistration", "TaxRegime", "IsActive", "DefaultPaymentTermId", "ImportSource", "CreatedAt", "IsDeleted", "DeletedAt", "Roles", "Addresses", "Contacts"],
        ["PartyRole"] = ["Id", "PartyId", "Role"],
        ["PartyAddress"] = ["Id", "PartyId", "Kind", "ZipCode", "Street", "Number", "Complement", "District", "City", "IbgeCode", "State", "IsDefault"],
        ["PartyContact"] = ["Id", "PartyId", "Role", "ReceivesBilling"],
    };

    public static IReadOnlyList<string> Violations(IEnumerable<Type> auditable, IReadOnlyDictionary<string, string[]> allowed) =>
        auditable.SelectMany(type => allowed.TryGetValue(type.Name, out var safe)
                ? type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetCustomAttribute<PersonalDataAttribute>() is null && !safe.Contains(p.Name))
                    .Select(p => $"{type.Name}.{p.Name}: marque [PersonalData] ou inclua na lista de não sensíveis")
                : [$"{type.Name}: entidade auditável sem lista de não sensíveis"])
            .ToList();

    [Fact]
    public void Entidades_auditaveis_do_Parties_so_tem_dado_pessoal_marcado()
    {
        var domain = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.Parties.Domain");
        var auditable = domain.GetTypes().Where(t => t.IsClass && typeof(IAuditable).IsAssignableFrom(t)).ToList();

        auditable.Should().NotBeEmpty();
        Violations(auditable, NonSensitive).Should().BeEmpty();
    }

    /// <summary>CAD-04a CA 14: o mesmo fail-closed no HR, e os DTOs de lista e resumo sem nenhuma propriedade de salário.</summary>
    private static readonly Dictionary<string, string[]> HrNonSensitive = new(StringComparer.Ordinal)
    {
        ["JobPosition"] = ["Id", "Name", "Cbo", "DefaultWorkCenterId", "WorkScheduleId", "MonthlyHours", "HourlyCost", "HourlyCostManual", "IsActive", "CreatedAt", "IsDeleted", "DeletedAt"],
        ["WorkSchedule"] = ["Id", "Name", "Days", "WeeklyHours", "IsActive", "SeedKey", "CreatedAt", "IsDeleted", "DeletedAt"],
        ["LaborUnion"] = ["Id", "Name", "Document", "BaseMonth", "IsActive", "CreatedAt", "IsDeleted", "DeletedAt"],
        ["Employee"] = ["Id", "Name", "Registration", "PositionId", "WorkScheduleId", "UnionId", "AdmissionDate", "TerminationDate", "Status", "CreatedAt", "Version", "IsDeleted", "DeletedAt"],
        // CAD-04c1: tipo, fim previsto e motivo do afastamento são [PersonalData] (LGPD art. 11); só as marcas de publicação ficam fora.
        ["EmployeeHistoryEntry"] = ["Id", "EmployeeId", "Kind", "EffectiveDate", "UserId", "CreatedAt", "StartPublishedAt", "EndPublishedAt"],
    };

    [Fact]
    public void Entidades_auditaveis_do_HR_so_tem_dado_pessoal_marcado_e_listas_sem_salario()
    {
        var domain = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Domain");
        var auditable = domain.GetTypes().Where(t => t.IsClass && typeof(IAuditable).IsAssignableFrom(t)).ToList();
        auditable.Should().NotBeEmpty();
        Violations(auditable, HrNonSensitive).Should().BeEmpty();
        typeof(BemAqui.HR.Domain.EmployeeHistoryEntry).GetProperty("Reason")!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("o motivo do afastamento é sensível (CAD-04c1 CA 9)");
        typeof(BemAqui.HR.Domain.EmployeeHistoryEntry).GetProperty("LeaveType")!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("o tipo do afastamento é sensível (CAD-04c1 CA 9)");

        var application = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Application");
        var sensitive = auditable.SelectMany(t => t.GetProperties().Where(p => p.GetCustomAttribute<PersonalDataAttribute>() is not null).Select(p => p.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var dto in new[] { "PositionDetail", "PositionSummary", "EmployeeDetail" })
        {
            var type = application.GetTypes().Single(t => t.Name == dto);
            type.GetProperties().Select(p => p.Name).Should().NotIntersectWith(sensitive, dto + " é de lista ou resumo e não pode ter salário");
        }
    }

    /// <summary>CAD-04c1 CA 3 e 6: o resumo para os outros módulos e os eventos de afastamento levam só id, datas e situação.</summary>
    [Fact]
    public void Resumo_e_eventos_de_afastamento_nao_levam_tipo_nem_motivo()
    {
        var contracts = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Contracts");
        foreach (var name in new[] { "EmployeeSummary", "EmployeeLeaveStarted", "EmployeeLeaveEnded" })
        {
            contracts.GetTypes().Single(t => t.Name == name).GetProperties().Select(p => p.Name)
                .Should().NotContain(["LeaveType", "Type", "Reason"], name + " não pode expor o tipo nem o motivo do afastamento");
        }

        contracts.GetTypes().Single(t => t.Name == "EmployeeLeaveStarted").GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["EmployeeId", "StartDate", "ExpectedEndDate"]);
        contracts.GetTypes().Single(t => t.Name == "EmployeeLeaveEnded").GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["EmployeeId", "StartDate", "EndDate"]);
    }

    private sealed class Leaky : IAuditable
    {
        public string Cpf { get; set; } = string.Empty;

        [PersonalData]
        public string Email { get; set; } = string.Empty;
    }

    [Fact]
    public void Regra_acusa_campo_sem_decisao_e_entidade_sem_lista()
    {
        Violations([typeof(Leaky)], new Dictionary<string, string[]> { ["Leaky"] = [] }).Should().ContainSingle().Which.Should().StartWith("Leaky.Cpf");
        Violations([typeof(Leaky)], new Dictionary<string, string[]>()).Should().ContainSingle().Which.Should().Contain("sem lista");
    }
}
