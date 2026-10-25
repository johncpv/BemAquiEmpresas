using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

public partial class DreTests
{
    /// <summary>Implementação de teste da regra pela nota (CA 4): R$ 1.000,00 na conta 1.1 no primeiro dia de cada intervalo.</summary>
    private sealed class FakeInvoiceRecognition(IFinanceDb db) : IRevenueRecognition
    {
        public RevenueBasis Basis => RevenueBasis.Invoice;

        private Task<Guid> AccountAsync(CancellationToken ct) => db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync(ct);

        public async Task<RevenueRecognitionResult> RecognizeAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken cancellationToken = default) =>
            new([new RevenueAmount(await AccountAsync(cancellationToken), null, from, 1_000m)], []);

        public async Task<IReadOnlyList<RevenueDocument>> DocumentsAsync(IReadOnlyCollection<Guid> accountIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            var account = await AccountAsync(cancellationToken);
            return accountIds.Contains(account) ? [new RevenueDocument("Invoice", Guid.CreateVersion7(), "NF-1", Guid.CreateVersion7(), account, null, from, 1_000m, null, "/app/fiscal")] : [];
        }
    }

    private sealed class FixedCutover(DateOnly on) : IRevenueCutover
    {
        public Task<DateOnly?> CutoverOnAsync(CancellationToken cancellationToken = default) => Task.FromResult<DateOnly?>(on);
    }

    [Fact]
    public async Task Antes_da_data_de_corte_e_provisorio_pelo_titulo_e_depois_vale_a_regra_pela_nota()
    {
        var a11 = await AccountAsync("1.1");
        await WithDbAsync(async db =>
        {
            db.Receivables.AddRange(Rec(a11, 500m, new DateOnly(2019, 1, 10)), Rec(a11, 700m, new DateOnly(2019, 4, 10)));
            return await db.SaveChangesAsync();
        });
        var (from, to) = (new DateOnly(2019, 1, 1), new DateOnly(2019, 5, 1));
        var gross = DreBuilder.GroupKey(DreGroup.GrossRevenue);

        // CA 3: sem data de corte, tudo pelo título e "Provisório".
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var provisional = await DreAsync(owner, from, to);
        Values(Row(provisional, gross)).Should().Equal(500m, 0m, 0m, 700m, 0m);
        provisional.GetProperty("months").EnumerateArray().Should().OnlyContain(m => m.GetProperty("isProvisional").GetBoolean());

        // CA 4: com a regra pela nota registrada e a data de corte em 01/03/2019, os meses antes continuam pelo título.
        var withInvoices = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddScoped<IProjectShipments, FakeShipments>();
            s.AddScoped<IRevenueRecognition, FakeInvoiceRecognition>();
            s.AddScoped<IRevenueCutover>(_ => new FixedCutover(new DateOnly(2019, 3, 1)));
        }));
        var (_, owner2) = await ActorAsync(Group.OwnerSeedKey, withInvoices);
        var mixed = await DreAsync(owner2, from, to);
        Values(Row(mixed, gross)).Should().Equal(500m, 0m, 1_000m, 0m, 0m);
        mixed.GetProperty("months").EnumerateArray().Select(m => m.GetProperty("isProvisional").GetBoolean()).Should().Equal(true, true, false, false, false);
        var cell = await JsonAsync(await owner2.GetAsync($"{Base}/cells?accountId={a11}&month=2019-03-01&from={Iso(from)}&to={Iso(to)}"));
        cell.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()).Should().Equal("Invoice");
        cell.GetProperty("total").GetDecimal().Should().Be(1_000m);
    }

    [Fact]
    public async Task Mudou_desde_a_ultima_consulta_lista_o_que_foi_lancado_cancelado_ou_estornado()
    {
        var a11 = await AccountAsync("1.1");
        var a33 = await AccountAsync("3.3");
        var a35 = await AccountAsync("3.5");
        var before = Rec(a11, 1_000m, new DateOnly(2018, 3, 10));
        var bank = new BankAccount { Name = "Banco 2018 " + Guid.NewGuid().ToString("N")[..4], Kind = BankAccountKind.Bank, OpeningBalance = 0m, IsActive = true };
        await WithDbAsync(async db =>
        {
            db.Receivables.Add(before);
            db.BankAccounts.Add(bank);
            return await db.SaveChangesAsync();
        });
        var (_, viewer) = await ActorAsync("view");
        var (from, to) = (new DateOnly(2018, 1, 1), new DateOnly(2018, 12, 1));
        (await DreAsync(viewer, from, to)).GetProperty("changes").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "primeira consulta: sem marca, sem aviso");
        (await viewer.PostAsync($"{Base}/view-mark", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await WithDbAsync(async db =>
        {
            var late = Rec(a11, 800m, new DateOnly(2018, 5, 10), createdAt: DateTimeOffset.UtcNow);
            db.Receivables.Add(late);
            var entry = Entry(bank.Id, a35, BankEntryKind.BankFee, -12m, new DateOnly(2018, 6, 1), DateTimeOffset.UtcNow);
            db.BankEntries.Add(entry);
            var payable = Pay(a33, 90m, new DateOnly(2018, 7, 1), createdAt: DateTimeOffset.UtcNow);
            db.Payables.Add(payable);
            var tracked = await db.Receivables.SingleAsync(r => r.Id == before.Id);
            tracked.Status = ReceivableStatus.Cancelled;
            tracked.CancelledAt = DateTimeOffset.UtcNow;
            return await db.SaveChangesAsync();
        });

        var report = await DreAsync(viewer, from, to);
        var changes = report.GetProperty("changes");
        changes.GetProperty("count").GetInt32().Should().Be(4);
        changes.GetProperty("months").EnumerateArray().Select(m => m.GetString()).Should().Equal("2018-03-01", "2018-05-01", "2018-06-01", "2018-07-01");
        report.GetProperty("lastViewedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
        report.GetProperty("months").EnumerateArray().Where(m => m.GetProperty("changed").GetBoolean()).Should().HaveCount(4);

        var list = await JsonAsync(await viewer.GetAsync($"{Base}/changes?from={Iso(from)}&to={Iso(to)}"));
        var effects = list.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("kind").GetString(), i.GetProperty("effect").GetString(), i.GetProperty("amount").GetDecimal())).ToList();
        effects.Should().BeEquivalentTo([("Receivable", "Cancelado", -1_000m), ("Receivable", "Lançado", 800m), ("BankEntry", "Lançado", -12m), ("Payable", "Lançado", -90m)]);

        (await viewer.PostAsync($"{Base}/view-mark", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DreAsync(viewer, from, to)).GetProperty("changes").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "a marca foi atualizada ao abrir");
    }

    private async Task<Guid> PartyAsync(string name)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IPartiesDb>();
        var party = new Party { Kind = PartyKind.PF, LegalName = name, CreatedAt = DateTimeOffset.UtcNow };
        db.Parties.Add(party);
        await db.SaveChangesAsync();
        return party.Id;
    }

    [Fact]
    public async Task Folha_no_drill_down_so_com_nomes_para_quem_ve_dado_sensivel()
    {
        var a31 = await AccountAsync("3.1");
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var ana = await PartyAsync("Ana Folha " + suffix);
        var bruno = await PartyAsync("Bruno Folha " + suffix);
        var supplier = await PartyAsync("Fornecedor Folha " + suffix);
        var issue = new DateOnly(2017, 5, 5);
        var salaries = new[] { Pay(a31, 3_100.55m, issue, origin: DocumentOrigin.Payroll, party: ana), Pay(a31, 2_900.45m, issue, origin: DocumentOrigin.Payroll, party: bruno) };
        await WithDbAsync(async db =>
        {
            db.Payables.AddRange(salaries);
            db.Payables.Add(Pay(a31, 100m, issue, party: supplier));
            return await db.SaveChangesAsync();
        });
        var url = $"{Base}/cells?accountId={a31}&month=2017-05-01&from=2017-01-01&to=2017-12-01";

        var (_, viewer) = await ActorAsync("view");
        var protectedCell = await viewer.GetAsync(url);
        protectedCell.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await protectedCell.Content.ReadAsStringAsync();
        raw.Should().NotContain("Ana Folha").And.NotContain("Bruno Folha").And.NotContain("3100.55").And.NotContain("2900.45")
            .And.NotContain(salaries[0].Number).And.NotContain(salaries[1].Number, "sem hr.view_sensitive, nem nome nem valor por funcionário (CA 14)");
        var body = System.Text.Json.JsonDocument.Parse(raw).RootElement;
        body.GetProperty("total").GetDecimal().Should().Be(6_101m);
        body.GetProperty("hasProtected").GetBoolean().Should().BeTrue();
        var payroll = body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "Payroll");
        payroll.GetProperty("description").GetString().Should().Be("Folha de 05/2017: 2 funcionários");
        payroll.GetProperty("amount").GetDecimal().Should().Be(6_001m);
        payroll.GetProperty("protected").GetBoolean().Should().BeTrue();
        payroll.GetProperty("id").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        body.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("counterparty").GetString() == "Fornecedor Folha " + suffix);

        var (_, sensitive) = await ActorAsync("sensitive");
        var open = await JsonAsync(await sensitive.GetAsync(url));
        open.GetProperty("hasProtected").GetBoolean().Should().BeFalse();
        open.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("counterparty").GetString()).Should().Contain(["Ana Folha " + suffix, "Bruno Folha " + suffix]);
        open.GetProperty("total").GetDecimal().Should().Be(6_101m);
    }

    private async Task SetFinanceReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "finance")
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    [Fact]
    public async Task Tres_camadas_allowlist_de_ordenacao_e_IDOR_nas_celulas()
    {
        var (_, nobody) = await ActorAsync();
        var noGroup = await nobody.GetAsync(Base);
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(noGroup)).Should().Be("forbidden");

        var (_, viewer) = await ActorAsync("view");
        (await viewer.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK, "padrão: os 12 meses até o mês corrente");
        var period = "from=2016-01-01&to=2016-12-01";
        var a11 = await AccountAsync("1.1");
        (await viewer.GetAsync($"{Base}/cells?accountId={a11}&{period}&sort=-amount")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync($"{Base}/cells?accountId={a11}&{period}&sort=amount;drop")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "ordenação só pela allowlist");
        (await viewer.GetAsync($"{Base}/cells?accountId={Guid.NewGuid()}&{period}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "só conta do plano do tenant");
        (await viewer.GetAsync($"{Base}/cells?accountId={a11}&month=2015-12-01&{period}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "só mês do período");
        (await viewer.GetAsync($"{Base}/cells?accountId={await AccountAsync("4.1")}&{period}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Fora da DRE");
        (await viewer.GetAsync($"{Base}/cells?group=Bogus&{period}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await viewer.GetAsync($"{Base}/cells?group=OutsideDre&{period}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await viewer.GetAsync($"{Base}/cells?accountId={a11}&group=Costs&{period}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await viewer.GetAsync($"{Base}?from=2014-01-01&to=2016-12-01")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "até 24 meses");
        (await viewer.GetAsync($"{Base}?from=2016-12-01&to=2016-01-01")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await viewer.PutAsJsonAsync("/api/v1/finance/parameters/dre", new { downPaymentAsAdvance = false })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "parâmetros só com settings.edit");

        await SetFinanceReleasedAsync(false);
        try
        {
            var notReleased = await viewer.GetAsync(Base);
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetFinanceReleasedAsync(true);
        }
    }
}
