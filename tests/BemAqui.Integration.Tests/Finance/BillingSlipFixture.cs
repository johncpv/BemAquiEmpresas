using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Application;
using BemAqui.Finance.Infrastructure.Inter;
using BemAqui.Host.Persistence;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>Cenário pronto para emitir: Proprietário, conta 077 com a integração Inter salva e testada, cliente completo e título.</summary>
public sealed record BillingScenario(HttpClient Owner, Guid OwnerId, Guid Account, string ClientId, Guid Party, JsonElement Receivable)
{
    public Guid ReceivableId => Receivable.GetProperty("id").GetGuid();

    public uint Version => Receivable.GetProperty("version").GetUInt32();
}

/// <summary>
/// BNK-01b: tenants reais (Testcontainers), o Host com o fake do Inter (mTLS de teste e a API de Cobrança simulada) e os jobs
/// capturados (sem servidor do Hangfire): o teste executa o <see cref="BillingSlipProcessor"/> à mão, no escopo do tenant.
/// </summary>
public sealed class BillingSlipFixture : IAsyncLifetime
{
    public const string HostA = "a.localhost";

    public TenancyFixture Tenancy { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public FakeInter Inter { get; } = new();

    public CapturingJobClient Jobs { get; } = new();

    public MemorySink Logs { get; } = new();

    public Tenant TenantA => Tenancy.TenantA;

    public async ValueTask InitializeAsync()
    {
        await Tenancy.InitializeAsync();
        Factory = Tenancy.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Inter:HandlerDisposeDelay", "00:00:00");
            b.UseSetting("Inter:BillingPollDelay", "00:00:00");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IInterHandlerFactory>();
                s.AddSingleton<IInterHandlerFactory>(Inter);
                s.RemoveAll<IBackgroundJobClient>();
                s.AddSingleton<IBackgroundJobClient>(Jobs);
                s.AddSingleton<ILogEventSink>(Logs);
            });
        });
        _ = Factory.Server;
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await Tenancy.DisposeAsync();
    }

    public static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    public static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(TenantA.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    public async Task<T> QueryAsync<T>(Func<TenantDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync();
        return await query(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Roda o corpo de um job do boleto no escopo do tenant, como o <c>TenantJob</c>.</summary>
    public async Task ProcessAsync(Func<BillingSlipProcessor, Task> job)
    {
        await using var scope = await ScopeAsync();
        await job(scope.ServiceProvider.GetRequiredService<BillingSlipProcessor>());
    }

    public Task IssueJobAsync(Guid slipId) => ProcessAsync(p => p.IssueAsync(slipId));

    public Task CancelJobAsync(Guid slipId) => ProcessAsync(p => p.CancelAsync(slipId));

    /// <summary>Jobs do boleto capturados para <paramref name="slipId"/> (tipo do job e o estado do Hangfire).</summary>
    public IReadOnlyList<(Type Type, string State)> JobsFor(Guid slipId) =>
        Jobs.Created.Where(c => c.Job.Args.Count >= 2 && c.Job.Args[1] is BillingSlipJobArgs a && a.BillingSlipId == slipId).Select(c => (c.Job.Type, c.State.Name)).ToList();

    /// <summary>Usuário do Proprietário (com a prova recente do 2FA) ou de um grupo com as ações dadas.</summary>
    public async Task<(Guid Id, HttpClient Client)> ActorAsync(bool owner, params string[] grants)
    {
        var (id, stamp) = await Tenancy.SeedUserAsync(TenantA, "b" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (owner || grants.Length > 0)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Identity.Application.IIdentityDb>();
            Guid groupId;
            if (owner)
            {
                groupId = await db.Groups.Where(g => g.SeedKey == BemAqui.Identity.Domain.Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            }
            else
            {
                var group = new BemAqui.Identity.Domain.Group { Name = "Boleto " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(group);
                foreach (var module in grants.Select(g => g.Split('.')[0]).Distinct(StringComparer.Ordinal))
                {
                    db.GroupModules.Add(new BemAqui.Identity.Domain.GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                }

                db.GroupPermissions.AddRange(grants.Select(g => new BemAqui.Identity.Domain.GroupGrant { GroupId = group.Id, ModuleCode = g.Split('.')[0], Action = g.Split('.')[1] }));
                groupId = group.Id;
            }

            db.UserGroups.Add(new BemAqui.Identity.Domain.UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = HostA;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
            TestTokens.Create(TenantA.Id, HostA, userId: id, stamp: stamp, otpAt: owner ? DateTimeOffset.UtcNow : null));
        return (id, client);
    }

    private Guid? _account;
    private string? _clientId;

    /// <summary>Conta 077 com a integração Inter (Sandbox) salva e testada; uma por classe de teste.</summary>
    public async Task<(Guid Account, string ClientId)> IntegrationAsync(HttpClient owner)
    {
        if (_account is { } ready)
        {
            return (ready, _clientId!);
        }

        var account = await BankIntegrationFixture.BankAccountAsync(owner);
        var clientId = "bol-" + Guid.NewGuid().ToString("N")[..12];
        Inter.Account(clientId).ClientSecret = "segredo-boleto";
        var saved = await owner.PutAsync($"{BankIntegrationFixture.Base}/{account}", BankIntegrationFixture.Form(clientId, "segredo-boleto", TestInterCertificate.Create()));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (_account, _clientId) = (account, clientId);
        return (account, clientId);
    }

    public static string NewCnpj()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("12345678").Take(8).Select(c => c - '0').Concat([0, 0, 0, 1]).ToList();
        int Dv(int[] weights) { var r = digits.Select((x, i) => x * weights[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        digits.Add(Dv([5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        digits.Add(Dv([6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(digits);
    }

    /// <summary>Cliente PJ com CNPJ, endereço de cobrança e contato que recebe cobrança (cada parte pode faltar).</summary>
    public static async Task<Guid> CustomerAsync(HttpClient owner, bool document = true, bool address = true, bool email = true)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new
        {
            kind = "PJ", document = document ? NewCnpj() : null, legalName = "Cliente Boleto " + Guid.NewGuid().ToString("N")[..6], tradeName = (string?)null, roles = new[] { "Customer" },
            billingAddress = address ? new { kind = "Billing", zipCode = "01310100", street = "Avenida Paulista", number = "1000", complement = (string?)null, district = "Bela Vista", city = "São Paulo", ibgeCode = "3550308", state = "SP", isDefault = true } : null,
            mainContact = email ? new { name = "Financeiro do cliente", role = "Financeiro", email = "cobranca@cliente-boleto.com.br", phone = (string?)null, receivesBilling = true } : null,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public Task<Guid> RevenueAsync() => QueryAsync(db => db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync());

    public async Task<JsonElement> ReceivableAsync(HttpClient client, Guid party, decimal amount = 1000m, DateOnly? due = null)
    {
        var body = new { partyId = party, description = "Venda com boleto", accountId = await RevenueAsync(), costCenterId = (Guid?)null, projectId = (Guid?)null, amount, issueDate = Iso(Today.AddDays(-30)), dueDate = Iso(due ?? Today.AddDays(10)) };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/finance/receivables") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var created = await client.SendAsync(request);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")[0];
    }

    /// <summary>Proprietário, integração, cliente completo e um título de <paramref name="amount"/>.</summary>
    public async Task<BillingScenario> ScenarioAsync(decimal amount = 1000m, DateOnly? due = null, bool email = true)
    {
        var (ownerId, owner) = await ActorAsync(owner: true);
        var (account, clientId) = await IntegrationAsync(owner);
        var party = await CustomerAsync(owner, email: email);
        return new BillingScenario(owner, ownerId, account, clientId, party, await ReceivableAsync(owner, party, amount, due));
    }

    public static string Slips(Guid receivableId) => $"/api/v1/finance/receivables/{receivableId}/billing-slips";

    /// <summary>"Emitir boleto" com os padrões do diálogo (ou o que vier), com o Idempotency-Key.</summary>
    public static async Task<HttpResponseMessage> IssueAsync(HttpClient client, Guid receivableId, Guid account, uint version, Guid? key = null, DateOnly? due = null,
        decimal fine = 2m, decimal interest = 1m, decimal discount = 0m, DateOnly? discountUntil = null, bool sendEmail = true, string? message = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Slips(receivableId))
        {
            Content = JsonContent.Create(new
            {
                bankAccountId = account, dueDate = Iso(due ?? Today.AddDays(10)), finePct = fine, interestMonthPct = interest, discountAmount = discount,
                discountUntil = discountUntil is { } u ? Iso(u) : null, message, sendEmail, version,
            }),
        };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    public static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    /// <summary>Emite e roda o job até <c>Issued</c>; devolve o id do boleto.</summary>
    public async Task<Guid> IssuedAsync(BillingScenario s, bool sendEmail = false)
    {
        var response = await IssueAsync(s.Owner, s.ReceivableId, s.Account, s.Version, sendEmail: sendEmail);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var slipId = (await JsonAsync(response)).GetProperty("slip").GetProperty("id").GetGuid();
        await IssueJobAsync(slipId);
        (await QueryAsync(db => db.BillingSlips.Where(x => x.Id == slipId).Select(x => x.Status).SingleAsync())).Should().Be(BemAqui.Finance.Domain.BillingSlipStatus.Issued);
        return slipId;
    }

    public async Task SetModuleAsync(string module, bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == TenantA.Id && m.ModuleCode == module)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }
}
