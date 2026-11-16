using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Persistence;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// COM-09 com PostgreSQL real: a edição direta do custo orçado tira o projeto do "sem orçamento de custo", grava a trilha (anterior, novo,
/// motivo, quem e quando) e fecha o drill-down do orçado; sem a ação própria, sem ver custo, em projeto encerrado, com versão antiga ou sem
/// motivo, nada é gravado.
/// </summary>
public class ProjectBudgetEditTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

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

    /// <summary>Usuário do Proprietário (sem permissões) ou num grupo novo só com as ações pedidas.</summary>
    private async Task<(HttpClient Client, Guid UserId)> ActorAsync(params string[] permissions)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "o" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        if (permissions.Length == 0)
        {
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync() });
        }
        else
        {
            var group = new Group { Name = "Orçado " + Guid.NewGuid().ToString("N")[..8], Description = "Teste do custo orçado" };
            db.Groups.Add(group);
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "projects", Enabled = true });
            db.GroupPermissions.AddRange(permissions.Select(p => new GroupGrant { GroupId = group.Id, ModuleCode = "projects", Action = p }));
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
        }

        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (client, id);
    }

    /// <summary>Contrato de R$ 100.000,00 com o projeto na situação pedida e o orçado dado (vazio = "sem orçamento de custo").</summary>
    private async Task<Guid> SeedProjectAsync(ProjectStatus status = ProjectStatus.InProgress, params (CostType Type, decimal Amount)[] budget)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var (party, quote, number) = (Guid.CreateVersion7(), Guid.CreateVersion7(), "B" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant());
        var signed = new DateOnly(2026, 9, 1);
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO commercial.quotes (id, number, revision, is_current, status, party_id, pricing_mode, total_cost, total_price, valid_until, created_at, is_deleted)
            VALUES ({quote}, {number}, 0, true, 'Approved', {party}, 'Markup', 0, 100000, {signed}, now(), false)
            """);
        var contract = new Contract
        {
            Number = "CT" + number, QuoteId = quote, PartyId = party, SignedAt = signed, TotalValue = 100_000m,
            RevenueAccountId = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync(), CreatedAt = DateTimeOffset.UtcNow,
        };
        var project = new Project { Number = "PR" + number, ContractId = contract.Id, PartyId = party, Name = "Elevador do teste", Status = status, CreatedAt = DateTimeOffset.UtcNow };
        db.Contracts.Add(contract);
        db.Projects.Add(project);
        db.ProjectBudgets.AddRange(budget.Select(b => new ProjectBudget { ProjectId = project.Id, CostType = b.Type, BudgetedAmount = b.Amount }));
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<(HttpStatusCode Status, string? Code, string? Message)> RefusedAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (response.StatusCode, problem.GetProperty("code").GetString(), problem.GetProperty("detail").GetString());
    }

    private static async Task<uint> VersionAsync(HttpClient client, Guid id) =>
        (await ReadAsync<JsonElement>(await client.GetAsync($"/api/v1/projects/{id}"))).GetProperty("version").GetUInt32();

    private static ProjectBudgetEditInput Input(uint version, string? reason, params (CostType Type, decimal Amount)[] lines) =>
        new(lines.Select(l => new ProjectBudgetEditLine(l.Type, l.Amount)).ToList(), reason, version);

    [Fact]
    public async Task Edicao_grava_o_orcado_a_trilha_e_fecha_o_drill_down()
    {
        var id = await SeedProjectAsync();
        var (owner, ownerId) = await ActorAsync();
        (await ReadAsync<ProjectCostView>(await owner.GetAsync($"/api/v1/projects/{id}/costs"))).NoCostBudget.Should().BeTrue();
        var before = await VersionAsync(owner, id);

        var saved = await ReadAsync<List<ProjectBudgetLine>>(await owner.PutAsJsonAsync($"/api/v1/projects/{id}/budget",
            Input(before, "  Contrato aprovado sem a composição de custo.  ", (CostType.Material, 50_000m), (CostType.Labor, 20_000.55m), (CostType.Freight, 0m)), Json));
        saved.Should().BeEquivalentTo([new ProjectBudgetLine(CostType.Material, 50_000m), new ProjectBudgetLine(CostType.Labor, 20_000.55m)], "frete continuou zero: não vira linha nem edição");

        var costs = await ReadAsync<ProjectCostView>(await owner.GetAsync($"/api/v1/projects/{id}/costs"));
        (costs.NoCostBudget, costs.Total.Budgeted, costs.BudgetedMarginPct).Should().Be((false, 70_000.55m, (decimal?)29.9995m));
        var after = await VersionAsync(owner, id);
        after.Should().NotBe(before, "a edição muda a versão do projeto");

        // Segunda edição, com a versão nova: o histórico vem da mais recente à mais antiga, com o anterior e o novo.
        await ReadAsync<List<ProjectBudgetLine>>(await owner.PutAsJsonAsync($"/api/v1/projects/{id}/budget", Input(after, "Material renegociado.", (CostType.Material, 45_000m), (CostType.Labor, 20_000.55m)), Json));
        var history = await ReadAsync<List<ProjectBudgetRevisionView>>(await owner.GetAsync($"/api/v1/projects/{id}/budget/revisions"));
        history.Select(h => (h.Reason, h.ChangedBy, h.ChangedByName)).Should().Equal(
            ("Material renegociado.", ownerId, "Usuário de teste"), ("Contrato aprovado sem a composição de custo.", ownerId, "Usuário de teste"));
        history[0].Lines.Should().Equal(new ProjectBudgetRevisionLine(CostType.Material, 50_000m, 45_000m));
        history[1].Lines.Should().Equal(new ProjectBudgetRevisionLine(CostType.Material, 0m, 50_000m), new ProjectBudgetRevisionLine(CostType.Labor, 0m, 20_000.55m));
        history.Should().OnlyContain(h => h.ChangedAt > DateTimeOffset.UtcNow.AddMinutes(-5));

        // O drill-down do orçado continua somando a célula: o orçamento do contrato (zero) mais cada edição pela diferença.
        var entries = await ReadAsync<ProjectCostEntries>(await owner.GetAsync($"/api/v1/projects/{id}/costs/Material/entries?column=budgeted"));
        entries.Total.Should().Be(45_000m);
        entries.Entries.Select(e => (e.Kind, e.Amount)).Should().Equal((ProjectCostEntryKind.Contract, 0m), (ProjectCostEntryKind.BudgetRevision, 50_000m), (ProjectCostEntryKind.BudgetRevision, -5_000m));

        // Auditoria geral: quem e quando no orçado e na edição, sem os valores (sigilo comercial, D7); o motivo fica na edição.
        var audit = await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.UserId == ownerId && (a.Entity == nameof(ProjectBudget) || a.Entity == nameof(ProjectBudgetRevision))).ToListAsync());
        audit.Count(a => a.Entity == nameof(ProjectBudgetRevision) && a.Action == AuditActions.Created).Should().Be(3);
        audit.Should().Contain(a => a.Entity == nameof(ProjectBudget) && a.Action == AuditActions.Updated);
        audit.Should().Contain(a => a.Changes.Contains("Material renegociado.")).And.OnlyContain(a => !a.Changes.Contains("mount\":{\"old\""), "os valores não vão para a auditoria geral");
    }

    [Fact]
    public async Task Sem_permissao_encerrado_versao_antiga_ou_sem_motivo_nada_e_gravado()
    {
        var id = await SeedProjectAsync(ProjectStatus.InProgress, (CostType.Material, 10_000m));
        var closed = await SeedProjectAsync(ProjectStatus.Closed, (CostType.Material, 10_000m));
        var (owner, _) = await ActorAsync();
        var (version, closedVersion) = (await VersionAsync(owner, id), await VersionAsync(owner, closed));
        var input = Input(version, "Correção do material.", (CostType.Material, 12_000m));

        // A ação própria: editar o projeto, aprovar aditivo e ver custo não bastam; e ela sozinha, sem ver custo, também não.
        var (manager, _) = await ActorAsync("view", "edit", "approve", "view_cost");
        (await manager.PutAsJsonAsync($"/api/v1/projects/{id}/budget", input, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (blind, _) = await ActorAsync("view", "edit_budget");
        (await blind.PutAsJsonAsync($"/api/v1/projects/{id}/budget", input, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await RefusedAsync(await owner.PutAsJsonAsync($"/api/v1/projects/{closed}/budget", Input(closedVersion, "Correção do material.", (CostType.Material, 12_000m)), Json)))
            .Should().Be((HttpStatusCode.Conflict, "project_closed", "Projeto encerrado: o custo orçado não pode mais ser editado."));
        (await RefusedAsync(await owner.PutAsJsonAsync($"/api/v1/projects/{id}/budget", Input(version, " ", (CostType.Material, 12_000m)), Json))).Status.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Quem tem a ação e vê custo edita; a segunda gravação sobre a mesma versão é recusada.
        var (editor, _) = await ActorAsync("view", "view_cost", "edit_budget");
        (await editor.PutAsJsonAsync($"/api/v1/projects/{id}/budget", input, Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RefusedAsync(await owner.PutAsJsonAsync($"/api/v1/projects/{id}/budget", Input(version, "Outra correção.", (CostType.Material, 15_000m)), Json)))
            .Should().Be((HttpStatusCode.Conflict, "concurrency_conflict", "O projeto mudou desde que você abriu. Recarregue."));

        (await WithDbAsync(db => db.ProjectBudgets.Where(b => b.ProjectId == id || b.ProjectId == closed).OrderBy(b => b.BudgetedAmount).Select(b => b.BudgetedAmount).ToListAsync()))
            .Should().Equal(10_000m, 12_000m);
        (await WithDbAsync(db => db.ProjectBudgetRevisions.CountAsync(r => r.ProjectId == id || r.ProjectId == closed))).Should().Be(1);
    }
}
