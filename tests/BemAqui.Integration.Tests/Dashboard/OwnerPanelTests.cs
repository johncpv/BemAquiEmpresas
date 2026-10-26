using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-01b com PostgreSQL real: cada card igual à origem (Margem × "Orçado × realizado", carteira × a lista de projetos de origem, produção ×
/// Kanban e ocupação, entregas × previsão), a faixa "Atenção" com o projeto que vai atrasar, a geração do cache pelos eventos novos, a
/// visibilidade por permissão e por módulo e o resultado gerencial por projeto com o dataset fixo. Banco desta classe; os testes leem os
/// números do tenant inteiro antes e depois da carga deles (as classes rodam os testes em série).
/// </summary>
public partial class OwnerPanelTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/dashboard";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

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

    /// <summary>Usuário novo (cache frio) no grupo Proprietário.</summary>
    private async Task<HttpClient> OwnerAsync()
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "o" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        }

        return ClientFor(id, stamp);
    }

    /// <summary>Usuário num grupo novo só com as ações pedidas (módulos habilitados no grupo pelas ações).</summary>
    private async Task<HttpClient> ActorWithAsync(params string[] permissions)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        var group = new Group { Name = "Painel " + Guid.NewGuid().ToString("N")[..8], Description = "Teste do painel" };
        db.Groups.Add(group);
        foreach (var module in permissions.Select(p => p.Split('.')[0]).Distinct())
        {
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
        }

        foreach (var permission in permissions)
        {
            db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = permission.Split('.')[0], Action = permission.Split('.')[1] });
        }

        db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
        await db.SaveChangesAsync();
        return ClientFor(id, stamp);
    }

    private HttpClient ClientFor(Guid id, string stamp)
    {
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private static async Task<JsonElement> OkAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, url + ": " + text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<(HttpStatusCode Status, string? Code)> StatusAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        string? code = null;
        if (text.StartsWith('{'))
        {
            var json = JsonDocument.Parse(text).RootElement;
            code = json.TryGetProperty("code", out var c) ? c.GetString() : null;
        }

        return (response.StatusCode, code);
    }

    private static decimal? Dec(JsonElement e, string name) => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetDecimal();

    /// <summary>Um projeto do teste: situação, contrato, prometido, previsão, entrega, orçado e a razão (todos opcionais).</summary>
    private sealed record Seed(
        string Name, ProjectStatus Status, decimal ContractValue, ContractStatus ContractStatus = ContractStatus.Active, decimal Amendment = 0, DateOnly? Promised = null,
        DateOnly? Forecast = null, DateOnly? DeliveredOn = null, DateOnly? SignedAt = null, Dictionary<CostType, decimal>? Budget = null,
        (CostType Type, ProjectCostSource Source, decimal Amount, DateOnly On)[]? Ledger = null);

    /// <summary>Grava os projetos (orçamento, contrato, aditivo aprovado, previsão, orçado e razão) para um cliente novo; devolve o cliente e os ids pelo nome.</summary>
    private async Task<(Guid Party, Dictionary<string, Guid> Ids)> SeedAsync(params Seed[] seeds)
    {
        var party = Guid.CreateVersion7();
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var ids = new Dictionary<string, Guid>();
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var revenueAccount = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
        var index = 0;
        foreach (var s in seeds)
        {
            index++;
            var quote = Guid.CreateVersion7();
            var number = $"D{tag}{index}";
            var signed = s.SignedAt ?? Today.AddDays(-60);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO commercial.quotes (id, number, revision, is_current, status, party_id, pricing_mode, total_cost, total_price, valid_until, created_at, is_deleted)
                VALUES ({quote}, {number}, 0, true, 'Approved', {party}, 'Markup', 0, {s.ContractValue}, {signed}, now(), false)
                """);
            var contract = new Contract
            {
                Number = "CT" + number, QuoteId = quote, PartyId = party, SignedAt = signed, TotalValue = s.ContractValue, RevenueAccountId = revenueAccount, Status = s.ContractStatus,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var project = new Project
            {
                Number = "PR" + number, ContractId = contract.Id, PartyId = party, Name = s.Name + " " + tag, Status = s.Status, PromisedDate = s.Promised, DeliveredOn = s.DeliveredOn,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Contracts.Add(contract);
            db.Projects.Add(project);
            if (s.Amendment != 0)
            {
                db.ContractAmendments.Add(new ContractAmendment
                {
                    ContractId = contract.Id, Number = 1, Description = "Aditivo do teste", ValueDelta = s.Amendment, Status = AmendmentStatus.Approved, BudgetDeltas = "{}",
                    ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            if (s.Forecast is { } forecast)
            {
                db.ProjectForecasts.Add(new ProjectForecast { ProjectId = project.Id, ForecastDate = forecast, FabricationEnd = forecast, ComputedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            }

            db.ProjectBudgets.AddRange((s.Budget ?? []).Select(b => new ProjectBudget { ProjectId = project.Id, CostType = b.Key, BudgetedAmount = b.Value }));
            foreach (var l in s.Ledger ?? [])
            {
                db.ProjectCosts.Add(new ProjectCost
                {
                    ProjectId = project.Id, CostType = l.Type, Source = l.Source, SourceId = Guid.CreateVersion7(), Seq = 1, Amount = l.Amount, OccurredOn = l.On, RecordedAt = DateTimeOffset.UtcNow,
                    InvoiceAmount = ProjectMarginCalculator.IsInvoiceTax(l.Source) ? l.Amount * 10 : null,
                });
            }

            ids[s.Name] = project.Id;
        }

        await db.SaveChangesAsync();
        return (party, ids);
    }

    /// <summary>Título a receber com projeto, na conta 1.1 (Receita bruta), emitido no dia dado.</summary>
    private async Task ReceivableAsync(Guid party, Guid project, decimal amount, DateOnly issued, decimal paid = 0, ReceivableStatus status = ReceivableStatus.Open)
    {
        await WithDbAsync(async db =>
        {
            var account = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var receivable = new BemAqui.Finance.Domain.Receivable
            {
                Number = "DR-" + Guid.NewGuid().ToString("N")[..10], PartyId = party, AccountId = account, ProjectId = project, Description = "Parcela do teste", Amount = amount, IssueDate = issued,
                DueDate = issued.AddDays(30), Status = status, Origin = DocumentOrigin.Manual, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Receivables.Add(receivable);
            db.Entry(receivable).Property(r => r.PaidAmount).CurrentValue = paid;
            await db.SaveChangesAsync();
            return 0;
        });
    }

    /// <summary>Título a pagar com projeto (natureza opcional), na conta 3.3.</summary>
    private async Task PayableAsync(Guid party, Guid project, CostType? type, decimal amount, DateOnly issued, PayableStatus status = PayableStatus.Open, DocumentOrigin origin = DocumentOrigin.Manual)
    {
        await WithDbAsync(async db =>
        {
            var account = await db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync();
            db.Payables.Add(new BemAqui.Finance.Domain.Payable
            {
                Number = "DP-" + Guid.NewGuid().ToString("N")[..10], PartyId = party, Description = "Título do teste", AccountId = account, ProjectId = project, CostType = type, Amount = amount,
                PaidAmount = status == PayableStatus.Paid ? amount : 0, IssueDate = issued, DueDate = issued.AddDays(30), IsEstimate = status == PayableStatus.Planned, Status = status, Origin = origin,
                OriginId = origin == DocumentOrigin.Manual ? null : Guid.CreateVersion7(), Installment = 1, CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            return 0;
        });
    }
}
