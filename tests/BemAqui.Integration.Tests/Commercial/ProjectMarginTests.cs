using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Persistence;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Parties.Contracts;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// DSH-02 com PostgreSQL real: dataset fixo dos 4 projetos conferido centavo a centavo nas três saídas (aba Custos, relatório e
/// <see cref="IProjectMargins"/>), ranking e filtros, consolidado ponderado, drill-down com total igual ao da célula e link por permissão,
/// horas sem nome sem <c>hr.view_sensitive</c>, NC informativa, três camadas, allowlist e IDOR. Banco desta classe; cada teste filtra pelo
/// cliente que criou.
/// </summary>
public class ProjectMarginTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly DateOnly Signed = new(2026, 9, 1);

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<HttpClient> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "m" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    /// <summary>
    /// Grava o dataset fixo (<see cref="MarginDataset"/>) para um cliente novo: orçamento, contrato, projeto em andamento, orçado, aditivo
    /// aprovado (P1), razão e títulos. Devolve o cliente e o id de cada projeto pelo nome.
    /// </summary>
    private async Task<(Guid Party, Dictionary<string, Guid> Projects)> SeedDatasetAsync()
    {
        var party = Guid.CreateVersion7();
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var ids = new Dictionary<string, Guid>();
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var revenueAccount = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
        var expenseAccount = await db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync();
        var index = 0;
        foreach (var p in MarginDataset.Projects)
        {
            index++;
            var quote = Guid.CreateVersion7();
            var number = $"M{tag}{index}";
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO commercial.quotes (id, number, revision, is_current, status, party_id, pricing_mode, total_cost, total_price, valid_until, created_at, is_deleted)
                VALUES ({quote}, {number}, 0, true, 'Approved', {party}, 'Markup', 0, {p.ContractValue}, {Signed}, now(), false)
                """);
            var contract = new Contract { Number = "CT" + number, QuoteId = quote, PartyId = party, SignedAt = Signed, TotalValue = p.ContractValue, RevenueAccountId = revenueAccount, CreatedAt = DateTimeOffset.UtcNow };
            var project = new Project { Number = "PR" + number, ContractId = contract.Id, PartyId = party, Name = p.Name, Status = ProjectStatus.InProgress, CreatedAt = DateTimeOffset.UtcNow };
            db.Contracts.Add(contract);
            db.Projects.Add(project);
            db.ProjectBudgets.AddRange(p.Budget.Select(b => new ProjectBudget { ProjectId = project.Id, CostType = b.Key, BudgetedAmount = b.Value }));
            if (p.AmendmentValue != 0)
            {
                db.ContractAmendments.Add(new ContractAmendment
                {
                    ContractId = contract.Id, Number = 1, Description = "Aditivo do teste", ValueDelta = p.AmendmentValue, Status = AmendmentStatus.Approved,
                    BudgetDeltas = $$"""{"Material": {{p.AmendmentMaterial}}}""", ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            var seq = 0;
            var laborEntry = Guid.CreateVersion7();
            foreach (var l in p.Ledger)
            {
                var correction = l.Source == ProjectCostSource.LaborCorrection;
                db.ProjectCosts.Add(new ProjectCost
                {
                    ProjectId = project.Id, CostType = l.CostType, Source = l.Source, SourceId = l.Source is ProjectCostSource.Labor or ProjectCostSource.LaborCorrection ? laborEntry : Guid.CreateVersion7(),
                    Seq = correction ? 2 : 1, Amount = l.Amount, OccurredOn = Signed.AddDays(++seq), RecordedAt = DateTimeOffset.UtcNow,
                    InvoiceAmount = ProjectMarginCalculator.IsInvoiceTax(l.Source) ? l.InvoiceAmount : null,
                });
            }

            var installment = 0;
            foreach (var t in p.Payables)
            {
                db.Payables.Add(new BemAqui.Finance.Domain.Payable
                {
                    Number = $"CP{tag}{index}{++installment}", PartyId = party, Description = "Título do teste", AccountId = expenseAccount, ProjectId = project.Id, CostType = t.CostType,
                    Amount = t.Amount, PaidAmount = t.Status == PayableStatus.Paid ? t.Amount : 0, IssueDate = Signed.AddDays(installment), DueDate = Signed.AddDays(30 + installment),
                    IsEstimate = t.Status == PayableStatus.Planned, Status = t.Status, Origin = t.Origin, OriginId = t.Origin is DocumentOrigin.Manual ? null : Guid.CreateVersion7(),
                    Installment = 1, CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            ids[p.Name] = project.Id;
        }

        await db.SaveChangesAsync();
        return (party, ids);
    }

    /// <summary>O <see cref="IProjectMargins"/> do painel do dono com o banco real e a permissão concedida (a terceira saída do CA 1).</summary>
    private static ProjectMarginService Service(IServiceProvider sp, IProjectProductionCosts? production = null) => new(
        sp.GetRequiredService<TenantDbContext>(), sp.GetRequiredService<IProjectPayableCosts>(), sp.GetRequiredService<IParties>(), sp.GetRequiredService<IUserDirectory>(),
        new AllowAllPermissionChecker(), production ?? sp.GetRequiredService<IProjectProductionCosts>(), sp.GetRequiredService<IProductionOrders>(),
        sp.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantContext>(), sp.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantScopeFactory>());

    private static ProjectMarginFigures FiguresOf(ProjectCostView v) => new(
        v.Lines, v.Total, v.Revenue, v.BudgetedMargin, v.BudgetedMarginPct, v.RealizedMargin, v.RealizedMarginPct, v.ProjectedMargin, v.ProjectedMarginPct, v.MarginDeviationPp, v.TaxBasis, v.TaxGuides);

    [Fact]
    public async Task Dataset_fixo_bate_centavo_a_centavo_nas_tres_saidas()
    {
        var (party, ids) = await SeedDatasetAsync();
        var owner = await ActorAsync(Group.OwnerSeedKey);

        // CA 7 e 1: a aba Custos de cada projeto.
        var tabs = new Dictionary<string, ProjectMarginFigures>();
        foreach (var p in MarginDataset.Projects)
        {
            var view = await ReadAsync<ProjectCostView>(await owner.GetAsync($"/api/v1/projects/{ids[p.Name]}/costs"));
            tabs[p.Name] = FiguresOf(view);
            MarginDataset.Check(p, tabs[p.Name]);
        }

        // O relatório, ranqueado do pior para o melhor desvio (CA 9).
        var ranking = await ReadAsync<ProjectMarginRanking>(await owner.GetAsync($"/api/v1/projects/margins/ranking?partyId={party}"));
        ranking.Items.Select(r => r.Project.Name).Should().Equal(MarginDataset.RankingOrder);
        foreach (var row in ranking.Items)
        {
            row.Figures.Should().BeEquivalentTo(tabs[row.Project.Name], o => o.WithStrictOrdering(), "o relatório usa o mesmo cálculo da aba Custos");
        }

        // A terceira saída: os membros do IProjectMargins (painel do dono), no mesmo serviço registrado para o contrato.
        await using var scope = await ScopeAsync();
        scope.ServiceProvider.GetRequiredService<IProjectMargins>().Should().BeOfType<ProjectMarginService>();
        var margins = Service(scope.ServiceProvider);
        foreach (var p in MarginDataset.Projects)
        {
            var row = await margins.ForProjectAsync(ids[p.Name]);
            row.IsSuccess.Should().BeTrue();
            row.Value.Figures.Should().BeEquivalentTo(tabs[p.Name], o => o.WithStrictOrdering());
        }

        var many = await margins.ForProjectsAsync(ids.Values.ToList());
        many.Value.Should().HaveCount(4);
        var portfolio = await margins.PortfolioAsync(new ProjectMarginFilter(PartyId: party));
        MarginDataset.CheckConsolidated(portfolio.Value.Figures);

        // CA 10: o consolidado da rota é o mesmo do contrato.
        var consolidated = await ReadAsync<ProjectMarginPortfolio>(await owner.GetAsync($"/api/v1/projects/margins/portfolio?partyId={party}"));
        consolidated.ProjectCount.Should().Be(4);
        MarginDataset.CheckConsolidated(consolidated.Figures);
        consolidated.WorstCostType.Should().Be(CostType.Material);
    }

    [Fact]
    public async Task Filtros_ordenacao_e_allowlist()
    {
        var (party, ids) = await SeedDatasetAsync();
        var owner = await ActorAsync(Group.OwnerSeedKey);

        // CA 11: "Só com margem abaixo da orçada" tira o P1 (desvio 0).
        var below = await ReadAsync<ProjectMarginRanking>(await owner.GetAsync($"/api/v1/projects/margins/ranking?partyId={party}&belowBudget=true"));
        below.Items.Select(r => r.Project.Name).Should().Equal(MarginDataset.RankingOrder[..3]);
        below.Items[0].WorstCostType.Should().Be(CostType.Material, "o chip leva à aba Custos com a natureza que mais desviou");

        // Situação: o padrão é "em andamento"; entregue tira os 4.
        await WithDbAsync(db => db.Projects.Where(p => p.Id == ids[MarginDataset.RankingOrder[0]]).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ProjectStatus.Delivered)));
        (await ReadAsync<ProjectMarginRanking>(await owner.GetAsync($"/api/v1/projects/margins/ranking?partyId={party}"))).Total.Should().Be(3);
        (await ReadAsync<ProjectMarginRanking>(await owner.GetAsync($"/api/v1/projects/margins/ranking?partyId={party}&situation=entregue"))).Total.Should().Be(1);
        (await ReadAsync<ProjectMarginRanking>(await owner.GetAsync($"/api/v1/projects/margins/ranking?partyId={party}&situation=todos"))).Total.Should().Be(4);
        (await ReadAsync<ProjectMarginRanking>(await owner.GetAsync($"/api/v1/projects/margins/ranking?partyId={party}&situation=todos&signedFrom=2026-09-02"))).Total.Should().Be(0);
        (await ReadAsync<ProjectMarginRanking>(await owner.GetAsync($"/api/v1/projects/margins/ranking?partyId={party}&situation=todos&signedTo=2026-09-01"))).Total.Should().Be(4);

        // Ordenação por allowlist e paginação.
        var byNumber = await ReadAsync<ProjectMarginRanking>(await owner.GetAsync($"/api/v1/projects/margins/ranking?partyId={party}&situation=todos&sort=number&pageSize=2"));
        (byNumber.Total, byNumber.Items.Count).Should().Be((4, 2));
        byNumber.Items[0].Project.Name.Should().Be(MarginDataset.Projects[0].Name);
        var invalid = await owner.GetAsync($"/api/v1/projects/margins/ranking?sort=total_cost;drop");
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync("/api/v1/projects/margins/ranking?situation=x")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync("/api/v1/projects/margins/portfolio?situation=x")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Drill_down_soma_igual_a_celula_com_link_por_permissao()
    {
        var (party, ids) = await SeedDatasetAsync();
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var commercial = await ActorAsync("commercial");

        // CA 13: toda célula de todo projeto: o total do painel é o da célula e a soma dos lançamentos fecha com ele.
        foreach (var p in MarginDataset.Projects)
        {
            var view = await ReadAsync<ProjectCostView>(await owner.GetAsync($"/api/v1/projects/{ids[p.Name]}/costs"));
            foreach (var line in view.Lines)
            {
                foreach (var (column, cell) in new[] { ("budgeted", line.Budgeted), ("realized", line.Realized), ("committed", line.Committed) })
                {
                    var entries = await ReadAsync<ProjectCostEntries>(await owner.GetAsync($"/api/v1/projects/{ids[p.Name]}/costs/{line.CostType}/entries?column={column}"));
                    entries.Total.Should().Be(cell, $"{p.Name} {line.CostType} {column}");
                    entries.Entries.Sum(e => e.Amount).Should().Be(cell, $"{p.Name} {line.CostType} {column}: soma dos lançamentos");
                }
            }
        }

        // Comprometido de material por saldo (CA 4 e 13): a conta no topo; P4 dá 0, sem crédito.
        var p3 = await ReadAsync<ProjectCostEntries>(await owner.GetAsync($"/api/v1/projects/{ids["P3 compra maior que a baixa"]}/costs/Material/entries?column=committed"));
        (p3.PurchasedTotal, p3.IssuedTotal, p3.Total).Should().Be((10_000m, 6_000m, 4_000m));
        p3.Entries.Should().Contain(e => e.Kind == ProjectCostEntryKind.StockIssuedOffset && e.Amount == -6_000m);
        var p4 = await ReadAsync<ProjectCostEntries>(await owner.GetAsync($"/api/v1/projects/{ids["P4 guia e nota"]}/costs/Material/entries?column=committed"));
        (p4.PurchasedTotal, p4.IssuedTotal, p4.Total).Should().Be((1_000m, 3_000m, 0m));

        // Imposto realizado só pela nota; a guia não aparece (CA 2 e 3).
        var tax = await ReadAsync<ProjectCostEntries>(await owner.GetAsync($"/api/v1/projects/{ids["P4 guia e nota"]}/costs/Tax/entries?column=realized"));
        tax.Entries.Should().ContainSingle().Which.Kind.Should().Be(ProjectCostEntryKind.InvoiceTax);

        // CA 15: orçado = contrato + aditivo, com link.
        var budget = await ReadAsync<ProjectCostEntries>(await owner.GetAsync($"/api/v1/projects/{ids["P1 material e horas"]}/costs/Material/entries?column=budgeted"));
        budget.Entries.Select(e => (e.Kind, e.Amount)).Should().Equal((ProjectCostEntryKind.Contract, 28_000m), (ProjectCostEntryKind.Amendment, 2_000m));
        budget.Entries.Should().OnlyContain(e => e.Link != null);

        // Link só com a permissão do módulo dono: o Comercial (projects.view_cost, sem finance.view e sem inventory.view) vê o documento sem link.
        var ownerFreight = await ReadAsync<ProjectCostEntries>(await owner.GetAsync($"/api/v1/projects/{ids["P2 instalação acima"]}/costs/Freight/entries?column=realized"));
        ownerFreight.Entries.Should().OnlyContain(e => e.Link != null && e.Link.StartsWith("/app/financeiro/pagar/"));
        var commercialFreight = await ReadAsync<ProjectCostEntries>(await commercial.GetAsync($"/api/v1/projects/{ids["P2 instalação acima"]}/costs/Freight/entries?column=realized"));
        commercialFreight.Entries.Should().NotBeEmpty().And.OnlyContain(e => e.Link == null);
        var commercialStock = await ReadAsync<ProjectCostEntries>(await commercial.GetAsync($"/api/v1/projects/{ids["P1 material e horas"]}/costs/Material/entries?column=realized"));
        commercialStock.Entries.Should().OnlyContain(e => e.Link == null);
        var ownerStock = await ReadAsync<ProjectCostEntries>(await owner.GetAsync($"/api/v1/projects/{ids["P1 material e horas"]}/costs/Material/entries?column=realized"));
        ownerStock.Entries.Should().OnlyContain(e => e.Link != null && e.Link.StartsWith("/app/estoque/movimentos/"));

        // Drill-down do consolidado: uma linha por projeto, total igual ao da célula.
        var portfolio = await ReadAsync<ProjectMarginPortfolio>(await owner.GetAsync($"/api/v1/projects/margins/portfolio?partyId={party}"));
        var material = portfolio.Figures.Lines.Single(l => l.CostType == CostType.Material);
        var byProject = await ReadAsync<ProjectCostEntries>(await owner.GetAsync($"/api/v1/projects/margins/entries?partyId={party}&costType=Material&column=realized"));
        (byProject.Total, byProject.Entries.Sum(e => e.Amount), byProject.Entries.Count).Should().Be((material.Realized, material.Realized, 3));
    }

    [Fact]
    public async Task Horas_por_funcionario_so_com_hr_view_sensitive()
    {
        var (_, ids) = await SeedDatasetAsync();
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var commercial = await ActorAsync("commercial");
        var project = ids["P1 material e horas"];

        // CA 14: sem hr.view_sensitive, as horas aparecem agregadas por OS e centro de trabalho, sem nome nem custo-hora.
        var aggregated = await commercial.GetAsync($"/api/v1/projects/{project}/costs/Labor/entries?column=realized");
        var raw = await aggregated.Content.ReadAsStringAsync();
        aggregated.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        raw.Should().NotContainAny("workerName", "hourlyCost", "employee");
        var body = JsonSerializer.Deserialize<ProjectCostEntries>(raw, Json)!;
        body.Total.Should().Be(1_649.75m);
        body.Entries.Select(e => e.Kind).Should().BeEquivalentTo([ProjectCostEntryKind.Labor, ProjectCostEntryKind.LaborCorrection]);

        var denied = await commercial.GetAsync($"/api/v1/projects/{project}/costs/labor/workers");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(denied)).Should().Be("forbidden");
        var workers = await ReadAsync<ProjectLaborWorkers>(await owner.GetAsync($"/api/v1/projects/{project}/costs/labor/workers"));
        workers.Total.Should().Be(1_649.75m);
        workers.Entries.Should().ContainSingle("um apontamento com a correção somada");
    }

    [Fact]
    public async Task Nao_conformidade_e_so_informativa()
    {
        var (_, ids) = await SeedDatasetAsync();
        await using var scope = await ScopeAsync();
        var plain = await Service(scope.ServiceProvider).CostsAsync(ids["P2 instalação acima"]);
        var withNc = await Service(scope.ServiceProvider, new FixedNonConformities(new ProjectNonConformityCost(2, 830m))).CostsAsync(ids["P2 instalação acima"]);

        // CA 12: o custo estimado da NC aparece só como informação, sem somar em nenhuma coluna.
        (withNc.Value.NonConformityCount, withNc.Value.NonConformityCost).Should().Be((2, 830m));
        withNc.Value.Lines.Should().BeEquivalentTo(plain.Value.Lines, o => o.WithStrictOrdering());
        withNc.Value.Total.Should().Be(plain.Value.Total);
        withNc.Value.ProjectedMarginPct.Should().Be(plain.Value.ProjectedMarginPct);
    }

    [Fact]
    public async Task Tres_camadas_custo_restrito_e_IDOR()
    {
        var (party, ids) = await SeedDatasetAsync();
        var project = ids["P2 instalação acima"];
        string[] routes =
        [
            $"/api/v1/projects/margins/ranking?partyId={party}", $"/api/v1/projects/margins/portfolio?partyId={party}", $"/api/v1/projects/margins/entries?costType=Material&column=realized",
            $"/api/v1/projects/{project}/costs", $"/api/v1/projects/{project}/costs/Material/entries?column=realized", $"/api/v1/projects/{project}/costs/labor/workers",
        ];

        // CA 19: Engenharia e Supervisão de produção do seed não têm projects.view_cost.
        foreach (var seed in new[] { "engineering", "production_supervision" })
        {
            var actor = await ActorAsync(seed);
            foreach (var route in routes)
            {
                var denied = await actor.GetAsync(route);
                denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{seed} {route}");
                (await CodeAsync(denied)).Should().Be("forbidden");
            }
        }

        // CA 20: sem grupo, 403 forbidden; módulo não liberado, 403 module_not_released.
        var nobody = await ActorAsync(null);
        (await CodeAsync(await nobody.GetAsync(routes[0]))).Should().Be("forbidden");
        var owner = await ActorAsync(Group.OwnerSeedKey);
        await SetReleasedAsync(false);
        try
        {
            foreach (var route in routes)
            {
                var notReleased = await owner.GetAsync(route);
                notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden, route);
                (await CodeAsync(notReleased)).Should().Be("module_not_released");
            }
        }
        finally
        {
            await SetReleasedAsync(true);
        }

        // IDOR e entradas inválidas: projeto que não existe 404; natureza e coluna fora da lista 422.
        (await owner.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/costs/Material/entries?column=realized")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/costs/labor/workers")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/projects/{project}/costs/Salary/entries?column=realized")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync($"/api/v1/projects/{project}/costs/3/entries?column=realized")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync($"/api/v1/projects/{project}/costs/Material/entries?column=x")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync("/api/v1/projects/margins/entries?costType=Material&column=x")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private async Task SetReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "projects")
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Contrato da Produção com NC fixas (todos os membros); o resto não é usado pela aba Custos.</summary>
    private sealed class FixedNonConformities(ProjectNonConformityCost nc) : IProjectProductionCosts
    {
        public Task<IReadOnlyList<LaborEntryCostInfo>> LaborEntriesAsync(IReadOnlyCollection<Guid> laborEntryIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LaborEntryCostInfo>>([]);

        public Task<IReadOnlyList<LaborEntryWorkerInfo>> LaborWorkersAsync(IReadOnlyCollection<Guid> laborEntryIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LaborEntryWorkerInfo>>([]);

        public Task<ProjectNonConformityCost> NonConformitiesAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult(nc);
    }
}
