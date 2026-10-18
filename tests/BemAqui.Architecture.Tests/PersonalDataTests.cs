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
        ["Party"] = ["Id", "Kind", "LegalName", "TradeName", "SearchText", "StateRegistration", "MunicipalRegistration", "TaxRegime", "IsActive", "CreatedAt", "IsDeleted", "DeletedAt", "Roles", "Addresses", "Contacts"],
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
