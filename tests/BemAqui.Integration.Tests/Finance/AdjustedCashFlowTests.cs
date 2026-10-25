using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// FIN-07: cenário ajustado com o dataset fixo (o da FIN-05 mais os clientes A, B e C), no banco desta classe. Só o teste do
/// dataset grava títulos; os demais leem ou restauram o que mudam.
/// </summary>
public partial class AdjustedCashFlowTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/finance/cash-flow";

    private static DateOnly Today => CashFlowTests.Today;

    private static string Iso(DateOnly d) => CashFlowTests.Iso(d);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<HttpClient> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "aj" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
            await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    /// <summary>Clientes do dataset: os nomes vão para o motivo do detalhe do dia (CA 9).</summary>
    private sealed record Clients(string AName, string CName);

    /// <summary>
    /// A: quitados 4.500 com 8 dias e 3.000 com 14 dias (atraso médio 10,4); em aberto 2.000 em +5, 1.000 vencido em −10 e
    /// 1.500 em +85. B: sem histórico; 3.000 em +20 e 1.200 previsto em +30. C: quitado 11.000 com 3 dias (atraso 3,0);
    /// 4.000 em +12, 5.000 vencido em −45 e 3.000 de implantação vencido em −60: inadimplência 8.000 ÷ 20.000 = 40,00%.
    /// Média da empresa: (36.000 + 42.000 + 33.000) ÷ 18.500 = 6,0 dias.
    /// </summary>
    private static async Task<Clients> SeedClientsAsync(TenantDbContext db)
    {
        var revenue = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
        var suffix = Guid.NewGuid().ToString("N")[..4];
        Party Party(string name) => new() { Kind = PartyKind.PJ, LegalName = $"{name} {suffix}", CreatedAt = DateTimeOffset.UtcNow };
        var (a, b, c) = (Party("Alfa Elevadores"), Party("Beta Condomínios"), Party("Gama Construtora"));
        db.Parties.AddRange(a, b, c);

        void Paid(Guid party, string number, decimal amount, int due, int delay)
        {
            var r = CashFlowTests.Rec(party, revenue, number, amount, Today.AddDays(due), ReceivableStatus.Paid);
            db.Receivables.Add(r);
            db.Entry(r).Property(x => x.PaidAmount).CurrentValue = amount;
            db.ReceivableSettlements.Add(new ReceivableSettlement
            {
                ReceivableId = r.Id, Date = Today.AddDays(due + delay), Principal = amount, Total = amount, BalanceBefore = amount, Kind = SettlementKind.Settlement, CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        Paid(a.Id, "AJ-A1", 4_500m, -90, 8);
        Paid(a.Id, "AJ-A2", 3_000m, -70, 14);
        Paid(c.Id, "AJ-C1", 11_000m, -100, 3);
        db.Receivables.AddRange(
            CashFlowTests.Rec(a.Id, revenue, "AJ-A3", 2_000m, Today.AddDays(5)),
            CashFlowTests.Rec(a.Id, revenue, "AJ-A4", 1_000m, Today.AddDays(-10)),
            CashFlowTests.Rec(a.Id, revenue, "AJ-A5", 1_500m, Today.AddDays(85)),
            CashFlowTests.Rec(b.Id, revenue, "AJ-B1", 3_000m, Today.AddDays(20)),
            CashFlowTests.Rec(b.Id, revenue, "AJ-B2", 1_200m, Today.AddDays(30), ReceivableStatus.Planned),
            CashFlowTests.Rec(c.Id, revenue, "AJ-C2", 4_000m, Today.AddDays(12)),
            CashFlowTests.Rec(c.Id, revenue, "AJ-C3", 5_000m, Today.AddDays(-45)),
            CashFlowTests.Rec(c.Id, revenue, "AJ-C4", 3_000m, Today.AddDays(-60), origin: DocumentOrigin.Opening));
        await db.SaveChangesAsync();
        return new Clients(a.LegalName, c.LegalName);
    }

    /// <summary>Entradas e saídas esperadas por dia nos dois cenários (CA 1).</summary>
    private static (Dictionary<DateOnly, (decimal In, decimal Out)> Base, Dictionary<DateOnly, (decimal In, decimal Out)> Adjusted) Expected()
    {
        var baseline = CashFlowTests.Expected();
        var adjusted = CashFlowTests.Expected();
        void Add(Dictionary<DateOnly, (decimal In, decimal Out)> map, int day, decimal inflow)
        {
            var d = Today.AddDays(day);
            var t = map.GetValueOrDefault(d);
            map[d] = (t.In + inflow, t.Out);
        }

        // Base: os títulos de A, B e C no vencimento (vencidos hoje).
        foreach (var (day, amount) in new[] { (5, 2_000m), (0, 1_000m), (85, 1_500m), (20, 3_000m), (30, 1_200m), (12, 4_000m), (0, 5_000m), (0, 3_000m) })
        {
            Add(baseline, day, amount);
        }

        // Ajustado, FIN-05 (cliente sem histórico, média da empresa de 6 dias): R2 vencido em −3 → +3; R1 e R5 de +5 → +11; R3 de +10 → +16; R4 de +40 → +46.
        foreach (var (from, to, amount) in new[] { (0, 3, 2_000m), (5, 11, 1_000m), (5, 11, 700m), (10, 16, 1_000m), (40, 46, 800m) })
        {
            Add(adjusted, from, -amount);
            Add(adjusted, to, amount);
        }

        // A (10 dias): +5 → +15; vencido em −10 → hoje; +85 → +95 (fora do horizonte). B (6 dias): +20 → +26; +30 → +36.
        // C (3 dias, 60% do valor): +12 → +15 (2.400); vencidos em −45 e −60 → hoje (3.000 e 1.800).
        foreach (var (day, amount) in new[] { (15, 2_000m), (0, 1_000m), (26, 3_000m), (36, 1_200m), (15, 2_400m), (0, 3_000m), (0, 1_800m) })
        {
            Add(adjusted, day, amount);
        }

        return (baseline, adjusted);
    }
}
