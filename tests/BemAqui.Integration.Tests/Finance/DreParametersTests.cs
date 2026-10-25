using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// FIN-08 CA 8 e 16 (ADR 0042): parâmetros do DRE pré-configurados, "A confirmar com a contadora", editáveis com
/// <c>settings.edit</c>, com auditoria e "Marcar como conferido"; mudar o parâmetro muda o resultado na próxima leitura. Banco
/// próprio, porque os parâmetros valem para o tenant inteiro.
/// </summary>
public class DreParametersTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Url = "/api/v1/finance/parameters/dre";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<HttpClient> ActorAsync(string seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "drep" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await WithDbAsync(async db =>
        {
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            return await db.SaveChangesAsync();
        });
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private Task<Guid> AccountAsync(string code) => WithDbAsync(db => db.Accounts.Where(a => a.Code == code).Select(a => a.Id).SingleAsync());

    private static JsonElement Item(JsonElement view, string key) => view.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == key);

    private static decimal[] Values(JsonElement report, string key) =>
        report.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key).GetProperty("values").EnumerateArray().Select(v => v.GetDecimal()).ToArray();

    private static async Task<JsonElement> DreAsync(HttpClient client, string from, string to)
    {
        var response = await client.GetAsync($"/api/v1/finance/dre?from={from}&to={to}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    [Fact]
    public async Task Seed_traz_as_contas_propostas_a_confirmar_e_o_DRE_avisa()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var view = await JsonAsync(await owner.GetAsync(Url));
        Item(view, DreParametersService.InterestIncomeAccount).GetProperty("accountCode").GetString().Should().Be("1.3");
        Item(view, DreParametersService.InterestExpenseAccount).GetProperty("accountCode").GetString().Should().Be("3.5");
        Item(view, DreParametersService.DiscountGrantedAccount).GetProperty("accountCode").GetString().Should().Be("3.5");
        Item(view, DreParametersService.DiscountReceivedAccount).GetProperty("accountCode").GetString().Should().Be("1.3");
        Item(view, DreParametersService.DownPaymentAsAdvance).GetProperty("enabled").GetBoolean().Should().BeTrue();
        view.GetProperty("items").EnumerateArray().Should().OnlyContain(i => i.GetProperty("reviewState").GetString() == "ToConfirm");
        view.GetProperty("pendingCount").GetInt32().Should().Be(5);
        Item(view, DreParametersService.InterestIncomeAccount).GetProperty("options").EnumerateArray().Select(o => o.GetProperty("code").GetString())
            .Should().Contain(["1.3", "1.4"]).And.NotContain(["1.1", "3.5"], "só conta de receita do Resultado financeiro ou de Outros resultados");
        (await DreAsync(owner, "2026-01-01", "2026-01-01")).GetProperty("pendingParameters").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task Trocar_a_conta_valida_o_grupo_audita_muda_o_DRE_e_volta_o_selo()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var a11 = await AccountAsync("1.1");
        var a13 = await AccountAsync("1.3");
        var a14 = await AccountAsync("1.4");
        var receivable = new Receivable
        {
            Number = "DPT-" + Guid.NewGuid().ToString("N")[..8], PartyId = Guid.CreateVersion7(), AccountId = a11, Description = "Venda", Amount = 1_000m, IssueDate = new DateOnly(2016, 4, 1),
            DueDate = new DateOnly(2016, 4, 30), CreatedAt = DateTimeOffset.UtcNow,
        };
        await WithDbAsync(async db =>
        {
            db.Receivables.Add(receivable);
            db.ReceivableSettlements.Add(new ReceivableSettlement { ReceivableId = receivable.Id, Date = new DateOnly(2016, 4, 20), Principal = 1_000m, Interest = 30m, Fine = 20m, Total = 1_050m, BalanceBefore = 1_000m, CreatedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });
        Values(await DreAsync(owner, "2016-04-01", "2016-04-01"), DreBuilder.AccountKey(a13)).Should().Equal(50m);

        var wrong = await owner.PutAsJsonAsync(Url, new { interestIncomeAccountId = await AccountAsync("3.3") });
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(wrong)).GetProperty("errors").TryGetProperty("interestIncomeAccountId", out _).Should().BeTrue();

        var finance = await ActorAsync("finance");
        (await finance.PutAsJsonAsync(Url, new { interestIncomeAccountId = a14 })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "editar exige settings.edit");

        var confirmed = await JsonAsync(await owner.PostAsJsonAsync(Url + "/confirm", new { key = DreParametersService.InterestIncomeAccount }));
        var item = Item(confirmed, DreParametersService.InterestIncomeAccount);
        item.GetProperty("reviewState").GetString().Should().Be("Confirmed");
        item.GetProperty("reviewedByName").ValueKind.Should().Be(JsonValueKind.String);
        item.GetProperty("reviewedAt").ValueKind.Should().Be(JsonValueKind.String);
        confirmed.GetProperty("pendingCount").GetInt32().Should().Be(4);

        var saved = await owner.PutAsJsonAsync(Url, new { interestIncomeAccountId = a14 });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        Item(await JsonAsync(saved), DreParametersService.InterestIncomeAccount).GetProperty("reviewState").GetString().Should().Be("ToConfirm", "valor novo volta o selo (ADR 0042)");

        var report = await DreAsync(owner, "2016-04-01", "2016-04-01");
        Values(report, DreBuilder.AccountKey(a14)).Should().Equal(50m);
        Values(report, DreBuilder.GroupKey(DreGroup.FinancialResult)).Should().Equal(0m);
        Values(report, DreBuilder.GroupKey(DreGroup.OtherResults)).Should().Equal(50m);

        var parameterId = item.GetProperty("parameterId").GetGuid().ToString();
        var audit = await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(FinanceParameter) && a.EntityId == parameterId).OrderBy(a => a.At).ToListAsync());
        audit.Should().HaveCountGreaterThanOrEqualTo(2, "conferência e troca de conta auditadas");
        audit.Last().Changes.Should().Contain(a13.ToString()).And.Contain(a14.ToString(), "antes e depois (ADR 0016 M6)");

        (await owner.PostAsJsonAsync(Url + "/confirm", new { key = "finance.nao_existe" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsJsonAsync(Url, new { interestIncomeAccountId = a13 })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Regra_do_sinal_a_confirmar_muda_a_receita_e_os_adiantamentos()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var a11 = await AccountAsync("1.1");
        await WithDbAsync(async db =>
        {
            db.Receivables.Add(new Receivable
            {
                Number = "DPS-" + Guid.NewGuid().ToString("N")[..8], PartyId = Guid.CreateVersion7(), AccountId = a11, Description = "CTR — Sinal (30%)", Amount = 1_000m,
                IssueDate = new DateOnly(2016, 6, 1), DueDate = new DateOnly(2016, 6, 10), Origin = DocumentOrigin.Contract, OriginId = Guid.CreateVersion7(), ContractId = Guid.CreateVersion7(),
                ProjectId = Guid.CreateVersion7(), TriggerEvent = TriggerEvent.Signature, CreatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });
        var gross = DreBuilder.GroupKey(DreGroup.GrossRevenue);
        var asAdvance = await DreAsync(owner, "2016-06-01", "2016-06-01");
        Values(asAdvance, gross).Should().Equal(0m);
        Values(asAdvance, DreBuilder.Advances).Should().Equal(1_000m);

        (await owner.PutAsJsonAsync(Url, new { downPaymentAsAdvance = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            var asRevenue = await DreAsync(owner, "2016-06-01", "2016-06-01");
            Values(asRevenue, gross).Should().Equal(1_000m);
            Values(asRevenue, DreBuilder.Advances).Should().Equal(0m);
        }
        finally
        {
            (await owner.PutAsJsonAsync(Url, new { downPaymentAsAdvance = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
