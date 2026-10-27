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
        // CAD-04c2: banco, Pix, titular, motivo da divergência, adicionais, vale-transporte, CNH, reservista e o e-mail anterior são
        // [PersonalData]; só as marcas da conta (troca, quarentena, revisão e versão, sem dado bancário, expostas no IEmployees) e a
        // data da troca do e-mail ficam fora.
        ["Employee"] = ["Id", "Name", "Registration", "PositionId", "WorkScheduleId", "UnionId", "AdmissionDate", "TerminationDate", "Status", "CreatedAt", "Version", "IsDeleted", "DeletedAt",
            "EmailChangedAt", "BankOwnerMismatch", "BankChangedAt", "BankChangedBy", "BankQuarantineUntil", "BankReviewedAt", "BankReviewedBy", "BankSelfReviewed", "BankAccountVersion"],
        // CAD-04c2: dependente (pode ser menor) é todo dado pessoal; só as chaves, a criação e as marcas técnicas ficam fora.
        ["EmployeeDependent"] = ["Id", "EmployeeId", "CreatedAt", "Version", "IsDeleted", "DeletedAt"],
        // CAD-04c1: tipo, fim previsto e motivo do afastamento são [PersonalData] (LGPD art. 11); só as marcas de publicação ficam fora.
        // CAD-04c1b: o vínculo da mudança com o afastamento, o que mudou e a marca do cancelamento não são dado pessoal; o tipo
        // anterior é.
        ["EmployeeHistoryEntry"] = ["Id", "EmployeeId", "Kind", "EffectiveDate", "UserId", "CreatedAt", "StartPublishedAt", "EndPublishedAt", "LeaveId", "LeaveChange", "CancelledAt"],
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
        typeof(BemAqui.HR.Domain.EmployeeHistoryEntry).GetProperty("PreviousLeaveType")!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("o tipo anterior do afastamento é sensível (CAD-04c1b)");
        typeof(BemAqui.HR.Domain.EmployeeHistoryEntry).GetProperty("OldValue")!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull("o período antes da mudança do afastamento é sensível (CAD-04c1b)");

        var application = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Application");
        // O nome do dependente é dado pessoal, mas o nome do funcionário e do cargo não: a lista e o resumo são conferidos contra o
        // funcionário e o cargo.
        var sensitive = auditable.Where(t => t != typeof(BemAqui.HR.Domain.EmployeeDependent)).SelectMany(t => t.GetProperties().Where(p => p.GetCustomAttribute<PersonalDataAttribute>() is not null).Select(p => p.Name)).ToHashSet(StringComparer.Ordinal);
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

    /// <summary>
    /// CAD-04c2 CA 7 e 11 (ADR 0019 §7 adendo): a situação da conta no <c>IEmployees</c> não leva dado bancário; o dono de anexo
    /// <c>EmployeeDocument</c> lê e grava com <c>hr.view_sensitive</c>, pdf, png e jpg até 10 MB, confere o dono e audita o download.
    /// </summary>
    [Fact]
    public void Situacao_da_conta_sem_dado_bancario_e_dono_EmployeeDocument_com_as_regras_da_ADR()
    {
        var contracts = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Contracts");
        contracts.GetTypes().Single(t => t.Name == "EmployeeBankAccountStatus").GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(["EmployeeId", "IsFirstAccount", "ChangedAt", "QuarantineUntil", "InQuarantine", "ReviewedAt", "SelfReviewed", "Version"]);
        var application = Solution.Assemblies.Single(a => a.GetName().Name == "BemAqui.HR.Application");
        application.GetTypes().Single(t => t.Name == "EmployeeBankStatusView").GetProperties().Select(p => p.Name)
            .Should().NotContain(["BankCode", "Agency", "Account", "HolderName", "HolderCpf", "PixKey"], "o chip do cabeçalho não lê dado bancário");

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        BemAqui.HR.Infrastructure.HrInfrastructure.AddHrInfrastructure(services);
        var rules = services.Select(d => d.ImplementationInstance).OfType<BemAqui.BuildingBlocks.Attachments.AttachmentOwnerRegistration>().Single(r => r.OwnerType == "EmployeeDocument").Rules;
        rules.Module.Should().Be("hr");
        rules.ReadAction.Should().Be("view_sensitive");
        rules.WriteAction.Should().Be("view_sensitive");
        rules.AlternateWriteAction.Should().BeNull();
        rules.MaxSize.Should().Be(10 * 1024 * 1024);
        rules.ContentTypes.Should().BeEquivalentTo(["application/pdf", "image/png", "image/jpeg"]);
        rules.OwnerExists.Should().NotBeNull();
        rules.AuditReads.Should().BeTrue("cada download de documento do funcionário é leitura sensível (CA 12)");
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
