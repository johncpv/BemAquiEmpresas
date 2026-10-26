using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// Teste de contrato do <see cref="IRevenueRecognition"/> (FIN-08 CA 4): toda implementação (pelo título nesta story; pela nota
/// na FIS-03a e na FIS-06) herda daqui e prepara os seus documentos em <see cref="SeedAsync"/>. O contrato: a receita fica no
/// intervalo pedido, a soma por conta bate com os documentos do drill-down, o filtro por projeto é um recorte do total e a regra
/// declara a sua fonte.
/// </summary>
public abstract class RevenueRecognitionContractTests(TenancyFixture tenancy)
{
    protected TenancyFixture Tenancy { get; } = tenancy;

    protected abstract RevenueBasis ExpectedBasis { get; }

    /// <summary>Documentos de receita no intervalo de <paramref name="from"/> a <paramref name="to"/>, com pelo menos um com projeto; devolve os projetos usados.</summary>
    protected abstract Task<IReadOnlyList<Guid>> SeedAsync(TenantDbContext db, DateOnly from, DateOnly to);

    protected abstract IRevenueRecognition Resolve(IServiceProvider tenantScope);

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await Tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(Tenancy.TenantA.Id))!;
        return await Tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    [Fact]
    public async Task Receita_no_intervalo_igual_aos_documentos_e_recorte_por_projeto()
    {
        var (from, to) = (new DateOnly(2015, 1, 1), new DateOnly(2015, 6, 30));
        await using var scope = await ScopeAsync();
        var projects = await SeedAsync(scope.ServiceProvider.GetRequiredService<TenantDbContext>(), from, to);
        var rule = Resolve(scope.ServiceProvider);
        rule.Basis.Should().Be(ExpectedBasis);

        var result = await rule.RecognizeAsync(from, to, null, TestContext.Current.CancellationToken);
        result.Amounts.Should().NotBeEmpty().And.OnlyContain(a => a.Date >= from && a.Date <= to);
        var accounts = result.Amounts.Select(a => a.AccountId).Distinct().ToList();
        var documents = await rule.DocumentsAsync(accounts, from, to, TestContext.Current.CancellationToken);
        documents.Sum(d => d.Amount).Should().Be(result.Amounts.Sum(a => a.Amount), "o drill-down soma o mesmo que a receita (CA 13)");
        foreach (var account in accounts)
        {
            documents.Where(d => d.AccountId == account).Sum(d => d.Amount).Should().Be(result.Amounts.Where(a => a.AccountId == account).Sum(a => a.Amount));
        }

        documents.Should().OnlyContain(d => d.Date >= from && d.Date <= to && !string.IsNullOrEmpty(d.Link));

        var byProject = await rule.RecognizeAsync(from, to, projects, TestContext.Current.CancellationToken);
        byProject.Amounts.Should().OnlyContain(a => a.ProjectId != null && projects.Contains(a.ProjectId.Value));
        byProject.Amounts.Sum(a => a.Amount).Should().Be(result.Amounts.Where(a => a.ProjectId is { } p && projects.Contains(p)).Sum(a => a.Amount));

        var half = await rule.RecognizeAsync(from, new DateOnly(2015, 3, 31), null, TestContext.Current.CancellationToken);
        half.Amounts.Sum(a => a.Amount).Should().Be(result.Amounts.Where(a => a.Date <= new DateOnly(2015, 3, 31)).Sum(a => a.Amount), "o intervalo corta pela data");
    }
}

/// <summary>A regra pelo título (provisório) cumpre o contrato.</summary>
public class TitleRevenueRecognitionContractTests(TenancyFixture tenancy) : RevenueRecognitionContractTests(tenancy), IClassFixture<TenancyFixture>
{
    protected override RevenueBasis ExpectedBasis => RevenueBasis.Title;

    protected override IRevenueRecognition Resolve(IServiceProvider tenantScope) => tenantScope.GetRequiredService<IEnumerable<IRevenueRecognition>>().Single(r => r.Basis == RevenueBasis.Title);

    protected override async Task<IReadOnlyList<Guid>> SeedAsync(TenantDbContext db, DateOnly from, DateOnly to)
    {
        var a11 = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
        var a12 = await db.Accounts.Where(a => a.Code == "1.2").Select(a => a.Id).SingleAsync();
        var project = Guid.CreateVersion7();
        var contract = Guid.CreateVersion7();
        Receivable R(Guid account, decimal amount, DateOnly issue, Guid? projectId = null, DocumentOrigin origin = DocumentOrigin.Manual, TriggerEvent? trigger = null) => new()
        {
            Number = "RC-" + Guid.NewGuid().ToString("N")[..10], PartyId = Guid.CreateVersion7(), AccountId = account, Description = "CTR — Marco", Amount = amount, IssueDate = issue, DueDate = issue.AddDays(10),
            ProjectId = projectId, ContractId = origin == DocumentOrigin.Contract ? contract : null, OriginId = origin == DocumentOrigin.Contract ? Guid.CreateVersion7() : null, Origin = origin,
            TriggerEvent = trigger, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Receivables.AddRange(
            R(a11, 1_000m, from.AddDays(3)), R(a12, 250.75m, from.AddMonths(4)), R(a11, 600m, from.AddMonths(1), project, DocumentOrigin.Contract, TriggerEvent.Signature),
            R(a11, 1_400m, from.AddMonths(2), project, DocumentOrigin.Contract), R(a11, 999m, to.AddDays(1)), R(a11, 333m, from.AddDays(1), origin: DocumentOrigin.Opening));
        await db.SaveChangesAsync();
        return [project];
    }
}
