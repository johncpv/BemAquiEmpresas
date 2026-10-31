using System.Reflection;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Host.Persistence;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// LGPD-01a CA 6 e 7 (ADR 0037 §2 e §3): toda tabela do banco do tenant com propriedade <c>[PersonalData]</c> pertence a um
/// <see cref="IPersonalDataProvider"/> (<see cref="PersonalDataCoversAttribute"/>) ou a um expurgo (<see cref="PersonalDataPurgeAttribute"/>), em todos
/// os módulos; evento do outbox e argumento de job não levam propriedade <c>[PersonalData]</c> nem campo com cara de dado pessoal. Entidade nova
/// com dado pessoal e sem provedor reprova o CI.
/// </summary>
public class PersonalDataInventoryTests
{
    /// <summary>Nomes de propriedade que indicam dado pessoal num evento ou num argumento de job (só ids, ADR 0037 §2).</summary>
    private static readonly string[] SuspiciousNames = ["Cpf", "Email", "Phone", "Document", "Name", "Notes", "Reason", "Justification", "Address", "Latitude", "Longitude", "Payload"];

    /// <summary>Propriedades de nome suspeito que não são dado pessoal, com o motivo (mudou a lista, explique na linha).</summary>
    private static readonly Dictionary<string, string> NotPersonal = new(StringComparer.Ordinal)
    {
        ["BemAqui.Production.Contracts.ProjectForecastChanged.Reason"] = "código do motivo da data prevista (ADR 0019 §19), não texto livre",
        ["BemAqui.Commercial.Contracts.QuoteLost.Reason"] = "nome do enum QuoteLostReason; o detalhe livre fica só na tabela do orçamento",
        ["BemAqui.Finance.Contracts.BillingSlipCancelled.Reason"] = "nome do enum BillingSlipCancelReason (ou \"Expired\"), não texto livre",
    };

    private static IEnumerable<Type> Types() => Solution.Assemblies.SelectMany(a => a.GetTypes());

    private static List<Type> PersonalDataEntities()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql("Host=localhost").UseSnakeCaseNamingConvention().Options;
        using var db = new TenantDbContext(options);
        return db.Model.GetEntityTypes().Select(t => t.ClrType).Distinct().Where(HasPersonalData).ToList();
    }

    private static bool HasPersonalData(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p => p.GetCustomAttribute<PersonalDataAttribute>() is not null);

    /// <summary>Entidades sem provedor nem expurgo (função pura, testada com tipos fictícios).</summary>
    public static IReadOnlyList<string> Uncovered(IEnumerable<Type> entities, IEnumerable<Type> classes)
    {
        var all = classes.ToList();
        var covered = all.SelectMany(t => t.GetCustomAttribute<PersonalDataCoversAttribute>()?.Entities ?? [])
            .Concat(all.SelectMany(t => t.GetCustomAttributes<PersonalDataPurgeAttribute>().Select(a => a.Entity)))
            .ToHashSet();
        return entities.Where(e => !covered.Contains(e)).Select(e => $"{e.FullName}: tem [PersonalData] e nenhum IPersonalDataProvider ([PersonalDataCovers]) nem expurgo ([PersonalDataPurge])").ToList();
    }

    /// <summary>Tipo (evento ou argumento de job) que leva dado pessoal: <c>[PersonalData]</c> ou um nome suspeito.</summary>
    public static IReadOnlyList<string> CarriesPersonalData(IEnumerable<Type> types) =>
        types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<PersonalDataAttribute>() is not null
                    || (SuspiciousNames.Any(n => p.Name.Contains(n, StringComparison.Ordinal) && !p.Name.EndsWith("Id", StringComparison.Ordinal)) && !NotPersonal.ContainsKey($"{t.FullName}.{p.Name}")))
                .Select(p => $"{t.FullName}.{p.Name}: evento e argumento de job levam só ids (ADR 0037 §2)"))
            .ToList();

    [Fact]
    public void Toda_tabela_com_dado_pessoal_tem_provedor_ou_expurgo_em_todos_os_modulos()
    {
        var entities = PersonalDataEntities();
        entities.Should().Contain([typeof(BemAqui.Parties.Domain.Party), typeof(BemAqui.HR.Domain.Employee), typeof(BemAqui.Identity.Domain.User), typeof(BemAqui.BuildingBlocks.Email.EmailLog),
            typeof(BemAqui.BuildingBlocks.Webhooks.WebhookEvent), typeof(BemAqui.Finance.Domain.PaymentOrderItem), typeof(BemAqui.Identity.Domain.PrivacyRequest)]);
        Uncovered(entities, Types()).Should().BeEmpty();
    }

    [Fact]
    public void Provedor_cobre_so_entidade_com_dado_pessoal_e_implementa_o_contrato()
    {
        var providers = Types().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPersonalDataProvider).IsAssignableFrom(t)).ToList();
        providers.Select(p => p.Name).Should().Contain(["PartiesPersonalDataProvider", "HrPersonalDataProvider", "IdentityPersonalDataProvider", "CommercialPersonalDataProvider",
            "ProductionPersonalDataProvider", "FinancePersonalDataProvider", "FiscalPersonalDataProvider", "PurchasingPersonalDataProvider", "EmailLogPersonalDataProvider", "NotificationPersonalDataProvider"]);
        Types().Where(t => t.GetCustomAttribute<PersonalDataCoversAttribute>() is not null).Should().OnlyContain(t => typeof(IPersonalDataProvider).IsAssignableFrom(t), "só provedor declara o que cobre");
        providers.SelectMany(p => p.GetCustomAttribute<PersonalDataCoversAttribute>()?.Entities ?? []).Should().OnlyContain(e => HasPersonalData(e), "o provedor só declara entidade com [PersonalData]");
        Types().Where(t => t.GetCustomAttributes<PersonalDataPurgeAttribute>().Any()).Should().OnlyContain(t => t.BaseType != null && t.BaseType.IsGenericType && t.BaseType.GetGenericTypeDefinition() == typeof(TenantJob<>),
            "o expurgo é um job de tenant");
    }

    [Fact]
    public void Texto_livre_do_RH_e_dado_pessoal()
    {
        foreach (var (type, name) in new (Type, string)[]
        {
            (typeof(BemAqui.HR.Domain.EmployeeHistoryEntry), "Reason"), (typeof(BemAqui.HR.Domain.TimeAdjustment), "Justification"), (typeof(BemAqui.HR.Domain.TimeAdjustment), "DecisionComment"),
            (typeof(BemAqui.HR.Domain.Employee), "BankMismatchReason"), (typeof(BemAqui.HR.Domain.Employee), "BankReviewNote"), (typeof(BemAqui.HR.Domain.PayrollRun), "ReopenReason"),
            (typeof(BemAqui.HR.Domain.PayrollForecast), "CancelReason"),
        })
        {
            type.GetProperty(name)!.GetCustomAttribute<PersonalDataAttribute>().Should().NotBeNull($"{type.Name}.{name} é texto livre do RH (ADR 0037 §3)");
        }
    }

    [Fact]
    public void Eventos_do_outbox_e_argumentos_de_job_levam_so_ids()
    {
        var events = Types().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t)).ToList();
        events.Should().NotBeEmpty();
        CarriesPersonalData(events).Should().BeEmpty();

        var args = Types().Where(t => t is { IsClass: true, IsAbstract: false }).Select(JobArgs).OfType<Type>().Distinct().ToList();
        args.Should().Contain(typeof(BemAqui.Identity.Application.Privacy.PrivacyExportArgs));
        CarriesPersonalData(args).Should().BeEmpty();
    }

    private static Type? JobArgs(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(TenantJob<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private sealed class NewLeaky
    {
        [PersonalData]
        public string Cpf { get; set; } = string.Empty;
    }

    private sealed record LeakyEvent(Guid OrderId, string CustomerEmail) : IIntegrationEvent;

    [PersonalDataPurge(typeof(NewLeaky), 30, "teste")]
    private sealed class FakePurge;

    [Fact]
    public void Regra_acusa_entidade_nova_sem_provedor_e_evento_com_dado_pessoal()
    {
        Uncovered([typeof(NewLeaky)], [typeof(PersonalDataInventoryTests)]).Should().ContainSingle().Which.Should().Contain("NewLeaky");
        Uncovered([typeof(NewLeaky)], [typeof(FakePurge)]).Should().BeEmpty();
        CarriesPersonalData([typeof(LeakyEvent)]).Should().ContainSingle().Which.Should().Contain("CustomerEmail");
        CarriesPersonalData([typeof(BemAqui.Identity.Application.Privacy.PrivacyExportArgs)]).Should().BeEmpty();
    }
}
