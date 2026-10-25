using System.Collections.Concurrent;
using System.Globalization;
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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// FIN-08: DRE gerencial. Cada teste usa um ano próprio no passado (o DRE soma tudo do tenant): 2021 é o dataset fixo do CA 11,
/// 2019 a troca da regra da receita, 2018 o "Mudou desde", 2017 a folha. Os parâmetros ficam nos padrões do seed (os testes que
/// os mudam estão em <see cref="DreParametersTests"/>, com banco próprio).
/// </summary>
public partial class DreTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/finance/dre";

    /// <summary>Primeiro romaneio expedido por projeto, no lugar do <c>IShipments</c> da PRD-06b.</summary>
    private static readonly ConcurrentDictionary<Guid, DateOnly> Shipped = new();

    private sealed class FakeShipments : IProjectShipments
    {
        public Task<IReadOnlyDictionary<Guid, DateOnly>> FirstShippedOnAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, DateOnly>>(projectIds.Where(Shipped.ContainsKey).ToDictionary(p => p, p => Shipped[p]));
    }

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IProjectShipments, FakeShipments>()));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<AsyncServiceScope> ScopeAsync(WebApplicationFactory<Program>? factory = null)
    {
        var services = (factory ?? Factory).Services;
        var info = (await services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>
    /// Usuário num grupo do seed (pela seed_key), num grupo só com <c>finance.view</c> ("view"), com <c>finance.view</c> e
    /// <c>hr.view_sensitive</c> ("sensitive") ou sem grupo.
    /// </summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey = null, WebApplicationFactory<Program>? factory = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "dre" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey is "view" or "sensitive")
            {
                var group = new Group { Name = "DRE " + seedKey + " " + Guid.NewGuid().ToString("N")[..6], Description = "Teste do DRE" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "finance", Enabled = true });
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "finance", Action = "view" });
                if (seedKey == "sensitive")
                {
                    db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "hr", Enabled = true });
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "hr", Action = "view" });
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "hr", Action = "view_sensitive" });
                }

                groupId = group.Id;
            }
            else
            {
                groupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync();
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
        }

        var client = (factory ?? Factory).CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    private static readonly ConcurrentDictionary<string, Guid> Accounts = new();

    private Task<Guid> AccountAsync(string code) => Accounts.TryGetValue(code, out var id)
        ? Task.FromResult(id)
        : WithDbAsync(async db => Accounts[code] = await db.Accounts.Where(a => a.Code == code).Select(a => a.Id).SingleAsync());

    private static int _seq;

    private static string Number(string prefix) => prefix + Interlocked.Increment(ref _seq).ToString("0000", CultureInfo.InvariantCulture);

    private static Receivable Rec(Guid account, decimal amount, DateOnly issue, ReceivableStatus status = ReceivableStatus.Open, DocumentOrigin origin = DocumentOrigin.Manual, Guid? party = null, DateTimeOffset? createdAt = null) => new()
    {
        Number = Number("DR"), PartyId = party ?? Guid.CreateVersion7(), AccountId = account, Description = "Venda de teste", Amount = amount, IssueDate = issue,
        DueDate = status == ReceivableStatus.Planned ? null : issue.AddDays(30), Status = status, Origin = origin, CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-1),
    };

    /// <summary>Parcela do contrato (COM-04b): o sinal é a do marco com gatilho Assinatura.</summary>
    private static Receivable Parcel(Guid account, decimal amount, DateOnly issue, Guid contract, Guid project, TriggerEvent? trigger, string milestone, ReceivableStatus status = ReceivableStatus.Open)
    {
        var r = Rec(account, amount, issue, status, DocumentOrigin.Contract);
        r.ContractId = contract;
        r.ProjectId = project;
        r.OriginId = Guid.CreateVersion7();
        r.TriggerEvent = trigger;
        r.Description = "CTR-T — " + milestone;
        return r;
    }

    private static Payable Pay(Guid account, decimal amount, DateOnly issue, PayableStatus status = PayableStatus.Open, DocumentOrigin origin = DocumentOrigin.Manual, DateOnly? competence = null, Guid? party = null, DateTimeOffset? createdAt = null) => new()
    {
        Number = Number("DP"), PartyId = party ?? Guid.CreateVersion7(), AccountId = account, Description = "Despesa de teste", Amount = amount, IssueDate = issue, DueDate = issue.AddDays(20),
        Status = status, Origin = origin, Competence = competence, CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-1),
    };

    private static BankEntry Entry(Guid bank, Guid account, BankEntryKind kind, decimal amount, DateOnly date, DateTimeOffset? createdAt = null) => new()
    {
        BankAccountId = bank, AccountId = account, Kind = kind, Amount = amount, Date = date, Reason = "Lançamento de teste " + kind, CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-1),
    };

    private static decimal[] Values(JsonElement row) => row.GetProperty("values").EnumerateArray().Select(v => v.GetDecimal()).ToArray();

    private static JsonElement Row(JsonElement report, string key) => report.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key);

    private static async Task<JsonElement> DreAsync(HttpClient client, DateOnly from, DateOnly to)
    {
        var response = await client.GetAsync($"{Base}?from={Iso(from)}&to={Iso(to)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private sealed record Dataset(Guid ProjectShipped, Guid ProjectNoShipment, Guid ProjectMilestone);

    /// <summary>
    /// Dataset fixo do CA 11 (janeiro a março de 2021), com a planilha do teste nos comentários: recebíveis de contrato com sinal
    /// (com romaneio, sem romaneio e com o marco seguinte) e manuais, um <c>Opening</c>, um cancelado depois do mês e um previsto;
    /// títulos a pagar de recorrente com competência, de instalador, de compra para estoque (e um previsto) e manuais com retenção;
    /// lançamentos bancários (um estornado); baixas com juros, multa e desconto (uma estornada) e pagamentos com juros e desconto.
    /// </summary>
    private Task<Dataset> SeedDatasetAsync() => WithDbAsync(async db =>
    {
        DateOnly D(int month, int day) => new(2021, month, day);
        var a11 = await AccountAsync("1.1");
        var a12 = await AccountAsync("1.2");
        var a13 = await AccountAsync("1.3");
        var a14 = await AccountAsync("1.4");
        var a19 = await AccountAsync("1.9");
        var a21 = await AccountAsync("2.1");
        var a23 = await AccountAsync("2.3");
        var a25 = await AccountAsync("2.5");
        var a32 = await AccountAsync("3.2");
        var a33 = await AccountAsync("3.3");
        var a35 = await AccountAsync("3.5");
        var a39 = await AccountAsync("3.9");
        var bank = new BankAccount { Name = "Banco DRE " + Guid.NewGuid().ToString("N")[..4], Kind = BankAccountKind.Bank, OpeningBalance = 0m, IsActive = true };
        db.BankAccounts.Add(bank);

        var (p1, p2, p3) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var (c1, c2, c3) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        Shipped[p1] = D(2, 10);
        var manual12 = Rec(a12, 2_000m, D(2, 1));
        var manual11 = Rec(a11, 4_000m, D(3, 4));
        var cancelled = Rec(a11, 5_555m, D(1, 2), ReceivableStatus.Cancelled);
        cancelled.CancelledAt = DateTimeOffset.UtcNow;
        var c3Next = Parcel(a12, 1_500m, D(3, 2), c3, p3, null, "Entrega (30%)");
        db.Receivables.AddRange(
            Parcel(a11, 3_000m, D(1, 5), c1, p1, TriggerEvent.Signature, "Sinal (30%)"),               // 1.1: fev (romaneio 10/02); adiantamento em jan
            Parcel(a11, 7_000m, D(1, 5), c1, p1, TriggerEvent.Delivered, "Entrega (70%)"),             // 1.1: jan 7.000
            Parcel(a11, 1_000m, D(2, 3), c2, p2, TriggerEvent.Signature, "Sinal (10%)"),               // adiantamento fev e mar (sem romaneio, marco previsto)
            Parcel(a11, 8_888m, D(2, 3), c2, p2, TriggerEvent.FabricationDone, "Fabricação (90%)", ReceivableStatus.Planned), // fora
            Parcel(a12, 500m, D(2, 1), c3, p3, TriggerEvent.Signature, "Sinal (25%)"),                 // 1.2: mar (com o marco seguinte)
            c3Next,                                                                                    // 1.2: mar 1.500
            manual12,                                                                                  // 1.2: fev 2.000
            manual11,                                                                                  // 1.1: mar 4.000
            Rec(a11, 9_999m, D(1, 1), origin: DocumentOrigin.Opening),                                 // fora (implantação)
            cancelled,                                                                                 // fora (cancelado depois)
            Rec(a19, 300m, D(3, 1)));                                                                  // 1.9: mar 300 (a classificar)

        var withRetention = Pay(a33, 1_000m, D(3, 3));                                                 // 3.3: mar 1.000 (bruto)
        withRetention.RetentionIss = 50m;
        withRetention.RetentionIr = 15m;
        var purchase = Pay(a21, 4_200m, D(1, 12), origin: DocumentOrigin.Purchase);                    // 2.1: jan 4.200
        var installer = Pay(a23, 2_500m, D(2, 8), origin: DocumentOrigin.Installer);                   // 2.3: fev 2.500
        var cancelledPay = Pay(a33, 999m, D(2, 11), PayableStatus.Cancelled);
        cancelledPay.CancelledAt = DateTimeOffset.UtcNow;
        db.Payables.AddRange(
            purchase, installer, withRetention, cancelledPay,
            Pay(a21, 777m, D(2, 15), PayableStatus.Planned, DocumentOrigin.Purchase),                  // fora (previsto)
            Pay(a25, 630m, D(3, 10)),                                                                  // 2.5: mar 630 (deduções)
            Pay(a32, 1_500m, new DateOnly(2020, 11, 26), origin: DocumentOrigin.Recurring, competence: new DateOnly(2020, 12, 1)), // fora (competência de dez/20)
            Pay(a32, 1_500m, new DateOnly(2020, 12, 26), origin: DocumentOrigin.Recurring, competence: D(1, 1)), // 3.2: jan
            Pay(a32, 1_500m, D(1, 26), origin: DocumentOrigin.Recurring, competence: D(2, 1)),        // 3.2: fev
            Pay(a32, 1_500m, D(2, 26), origin: DocumentOrigin.Recurring, competence: D(3, 1)),        // 3.2: mar
            Pay(a32, 1_500m, D(3, 26), origin: DocumentOrigin.Recurring, competence: D(4, 1)),        // fora (competência de abr/21)
            Pay(a33, 4_444m, D(1, 10), origin: DocumentOrigin.Opening),                                // fora (implantação)
            Pay(a39, 200m, D(2, 2)));                                                                  // 3.9: fev 200 (a classificar)

        var reversedEntry = Entry(bank.Id, a35, BankEntryKind.InterestPaid, -60m, D(3, 1));
        reversedEntry.ReversedAt = DateTimeOffset.UtcNow;
        db.BankEntries.AddRange(
            Entry(bank.Id, a35, BankEntryKind.BankFee, -35m, D(1, 15)),                                // 3.5: jan −35
            Entry(bank.Id, a13, BankEntryKind.Yield, 80m, D(2, 20)),                                   // 1.3: fev +80
            Entry(bank.Id, a14, BankEntryKind.OtherIn, 120m, D(2, 25)),                                // 1.4: fev +120 (outros resultados)
            reversedEntry);                                                                            // fora (estornado)

        db.ReceivableSettlements.AddRange(
            new ReceivableSettlement { ReceivableId = manual12.Id, BankAccountId = bank.Id, Date = D(3, 2), Principal = 2_000m, Interest = 20m, Fine = 40m, Total = 2_060m, BalanceBefore = 2_000m, CreatedAt = DateTimeOffset.UtcNow }, // 1.3: mar +60
            new ReceivableSettlement { ReceivableId = manual11.Id, Date = D(3, 10), Principal = 150m, Discount = 150m, Total = 0m, BalanceBefore = 150m, Kind = SettlementKind.DiscountWriteOff, CreatedAt = DateTimeOffset.UtcNow }, // 3.5: mar −150
            new ReceivableSettlement { ReceivableId = manual11.Id, BankAccountId = bank.Id, Date = D(3, 11), Principal = 100m, Interest = 999m, Total = 1_099m, BalanceBefore = 3_850m, CreatedAt = DateTimeOffset.UtcNow, ReversedAt = DateTimeOffset.UtcNow }); // fora (estornada)
        db.PayableSettlements.AddRange(
            new PayableSettlement { PayableId = purchase.Id, PaymentOrderId = Guid.CreateVersion7(), BankAccountId = bank.Id, Date = D(2, 5), Principal = 4_200m, Interest = 10m, Fine = 5m, Total = 4_215m, CreatedAt = DateTimeOffset.UtcNow }, // 3.5: fev −15
            new PayableSettlement { PayableId = installer.Id, PaymentOrderId = Guid.CreateVersion7(), BankAccountId = bank.Id, Date = D(3, 5), Principal = 2_500m, Discount = 25m, Total = 2_475m, CreatedAt = DateTimeOffset.UtcNow }); // 1.3: mar +25
        await db.SaveChangesAsync();
        return new Dataset(p1, p2, p3);
    });

    [Fact]
    public async Task Dataset_fixo_de_tres_meses_bate_centavo_a_centavo()
    {
        var data = await SeedDatasetAsync();
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var report = await DreAsync(owner, new DateOnly(2021, 1, 1), new DateOnly(2021, 3, 1));

        report.GetProperty("months").EnumerateArray().Select(m => m.GetProperty("month").GetString()).Should().Equal("2021-01-01", "2021-02-01", "2021-03-01");
        report.GetProperty("isProvisional").GetBoolean().Should().BeTrue("sem notas reais a receita é provisória (CA 3)");
        report.GetProperty("months").EnumerateArray().Should().OnlyContain(m => m.GetProperty("isProvisional").GetBoolean());

        // Planilha do teste (CA 11).
        var expected = new Dictionary<string, decimal[]>
        {
            [DreBuilder.GroupKey(DreGroup.GrossRevenue)] = [7_000m, 5_000m, 6_300m],
            [DreBuilder.GroupKey(DreGroup.Deductions)] = [0m, 0m, 630m],
            [DreBuilder.NetRevenue] = [7_000m, 5_000m, 5_670m],
            [DreBuilder.GroupKey(DreGroup.Costs)] = [4_200m, 2_500m, 0m],
            [DreBuilder.GrossProfit] = [2_800m, 2_500m, 5_670m],
            [DreBuilder.GroupKey(DreGroup.OperatingExpenses)] = [1_500m, 1_700m, 2_500m],
            [DreBuilder.OperatingResult] = [1_300m, 800m, 3_170m],
            [DreBuilder.GroupKey(DreGroup.FinancialResult)] = [-35m, 65m, -65m],
            [DreBuilder.GroupKey(DreGroup.OtherResults)] = [0m, 120m, 0m],
            [DreBuilder.PeriodResult] = [1_265m, 985m, 3_105m],
            [DreBuilder.Advances] = [3_000m, 1_500m, 1_000m],
            [DreBuilder.AccountKey(await AccountAsync("1.1"))] = [7_000m, 3_000m, 4_000m],
            [DreBuilder.AccountKey(await AccountAsync("1.2"))] = [0m, 2_000m, 2_000m],
            [DreBuilder.AccountKey(await AccountAsync("1.9"))] = [0m, 0m, 300m],
            [DreBuilder.AccountKey(await AccountAsync("1.3"))] = [0m, 80m, 85m],
            [DreBuilder.AccountKey(await AccountAsync("3.5"))] = [-35m, -15m, -150m],
            [DreBuilder.AccountKey(await AccountAsync("2.1"))] = [4_200m, 0m, 0m],
            [DreBuilder.AccountKey(await AccountAsync("2.3"))] = [0m, 2_500m, 0m],
            [DreBuilder.AccountKey(await AccountAsync("3.2"))] = [1_500m, 1_500m, 1_500m],
            [DreBuilder.AccountKey(await AccountAsync("3.3"))] = [0m, 0m, 1_000m],
            [DreBuilder.AccountKey(await AccountAsync("3.9"))] = [0m, 200m, 0m],
        };
        foreach (var (key, values) in expected)
        {
            Values(Row(report, key)).Should().Equal(values, key);
        }

        var result = Row(report, DreBuilder.PeriodResult);
        result.GetProperty("total").GetDecimal().Should().Be(5_355m);
        result.GetProperty("delta").GetDecimal().Should().Be(2_120m);
        result.GetProperty("deltaPercent").GetDecimal().Should().Be(215.23m);
        Row(report, DreBuilder.GrossProfit).GetProperty("percentOfNetRevenue").GetDecimal().Should().Be(62.08m, "margem bruta = 10.970 ÷ 17.670");
        Row(report, DreBuilder.GroupKey(DreGroup.Costs)).GetProperty("delta").GetDecimal().Should().Be(-2_500m);
        Row(report, DreBuilder.Advances).GetProperty("delta").GetDecimal().Should().Be(-500m);
        report.GetProperty("rows").EnumerateArray().Should().NotContain(r => r.GetProperty("label").GetString()!.Contains("Investimentos"), "Fora da DRE não aparece");

        // CA 10: contas a classificar.
        report.GetProperty("toClassify").EnumerateArray().Select(n => (n.GetProperty("code").GetString(), n.GetProperty("total").GetDecimal()))
            .Should().BeEquivalentTo([("1.9", 300m), ("3.9", 200m)]);

        // CA 13: o drill-down de cada célula soma o valor da célula (contas e grupos, cada mês e o Total).
        var from = "from=2021-01-01&to=2021-03-01";
        foreach (var row in report.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("kind").GetString() is "Account" or "Group"))
        {
            var target = row.GetProperty("kind").GetString() == "Account" ? "accountId=" + row.GetProperty("accountId").GetGuid() : "group=" + row.GetProperty("group").GetString();
            var values = Values(row);
            for (var i = 0; i <= values.Length; i++)
            {
                var month = i < values.Length ? $"&month=2021-0{i + 1}-01" : string.Empty;
                var cell = await owner.GetAsync($"{Base}/cells?{target}{month}&{from}");
                cell.StatusCode.Should().Be(HttpStatusCode.OK, await cell.Content.ReadAsStringAsync());
                var body = await JsonAsync(cell);
                var total = i < values.Length ? values[i] : row.GetProperty("total").GetDecimal();
                body.GetProperty("total").GetDecimal().Should().Be(total, $"{row.GetProperty("label").GetString()} {month}");
                body.GetProperty("items").EnumerateArray().Sum(x => x.GetProperty("amount").GetDecimal()).Should().Be(total);
            }
        }

        // CA 5: o drill-down mostra como o sinal foi reconhecido.
        var feb11 = await JsonAsync(await owner.GetAsync($"{Base}/cells?accountId={await AccountAsync("1.1")}&month=2021-02-01&{from}"));
        feb11.GetProperty("items").EnumerateArray().Should().Contain(x => x.GetProperty("kind").GetString() == "DownPayment" && x.GetProperty("note").GetString() == "Sinal reconhecido na expedição de 10/02/2021");
        var mar12 = await JsonAsync(await owner.GetAsync($"{Base}/cells?accountId={await AccountAsync("1.2")}&month=2021-03-01&{from}"));
        mar12.GetProperty("items").EnumerateArray().Should().Contain(x => x.GetProperty("note").GetString() == "Sinal reconhecido com o marco Entrega (30%)");

        // CA 15: o resumo para o painel é igual à linha Receita bruta, mensal e por projeto.
        await using var scope = await ScopeAsync();
        var summary = scope.ServiceProvider.GetRequiredService<IRevenueSummary>();
        var monthly = await summary.GrossRevenueAsync(new DateOnly(2021, 1, 1), new DateOnly(2021, 3, 31));
        monthly.Select(m => m.Amount).Should().Equal(Values(Row(report, DreBuilder.GroupKey(DreGroup.GrossRevenue))));
        monthly.Should().OnlyContain(m => m.IsProvisional);
        var byProject = await summary.GrossRevenueByProjectsAsync([data.ProjectShipped, data.ProjectNoShipment, data.ProjectMilestone], new DateOnly(2021, 1, 1), new DateOnly(2021, 3, 31));
        byProject.Where(p => p.ProjectId == data.ProjectShipped).Select(p => p.Amount).Should().Equal(7_000m, 3_000m, 0m);
        byProject.Where(p => p.ProjectId == data.ProjectNoShipment).Select(p => p.Amount).Should().Equal(new[] { 0m, 0m, 0m }, "o sinal sem romaneio nem marco seguinte fica em adiantamentos");
        byProject.Where(p => p.ProjectId == data.ProjectMilestone).Select(p => p.Amount).Should().Equal(0m, 0m, 2_000m);
    }
}
