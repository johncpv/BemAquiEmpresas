using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Agenda;

/// <summary>
/// DSH-04, Minha pauta: cada teste usa um tenant próprio (contagens exatas, sem depender dos dados dos outros testes), com um Proprietário
/// ativo. Fontes do Financeiro pelo fluxo real (CA 2 e 6), grupos do seed (CA 13), dois tenants (CA 14), sem login (CA 15), o contrato
/// igual à pauta (CA 11), falha e atraso de fonte (CA 10) e desempenho (CA 12). Qualidade e segregação em <c>FabricationTests.Agenda</c>.
/// </summary>
public partial class AgendaTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Agenda = "/api/v1/me/agenda";

    private static readonly string[] SeedGroups = [Group.OwnerSeedKey, "finance", "commercial", "engineering", "production_supervision", "operators", "warehouse", "hr"];

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record Workspace(Tenant Tenant, string Host);

    /// <summary>Tenant novo só deste teste, com o Proprietário ativo (o aviso ao Proprietário e a alçada padrão dependem dele).</summary>
    private async Task<(Workspace Space, Guid OwnerId, HttpClient Owner)> SpaceAsync()
    {
        var key = "pauta-" + Guid.NewGuid().ToString("N")[..8];
        var space = new Workspace(await tenancy.ProvisionAsync(key, "Pauta " + key, key + ".localhost"), key + ".localhost");
        var (ownerId, owner) = await ActorAsync(space, Group.OwnerSeedKey);
        return (space, ownerId, owner);
    }

    private async Task<AsyncServiceScope> ScopeAsync(Workspace space)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(space.Tenant.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Workspace space, Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync(space);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<(Guid Id, HttpClient Client)> ActorAsync(Workspace space, string? seedKey, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? factory = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(space.Tenant, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync(space);
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = (factory ?? tenancy.Factory).CreateClient();
        client.DefaultRequestHeaders.Host = space.Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(space.Tenant.Id, space.Host, userId: id, stamp: stamp));
        return (id, client);
    }

    private static async Task<JsonElement> AgendaAsync(HttpClient client, string? categories = null)
    {
        var response = await client.GetAsync(categories is null ? Agenda : Agenda + "?categories=" + categories);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static JsonElement? Category(JsonElement view, string category) =>
        view.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("category").GetString() == category).Select(i => (JsonElement?)i).FirstOrDefault();

    private static List<string> Links(JsonElement view, string category) =>
        Category(view, category) is { } item ? [.. item.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("link").GetString()!)] : [];

    private static HttpRequestMessage Post(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    private static async Task<Guid> PartyAsync(HttpClient owner, string role)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = role + " " + Guid.NewGuid().ToString("N")[..6], roles = new[] { role } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    /// <summary>Título a pagar de 1.000,00 (ISS 50,00), por Pix, vencendo em <paramref name="dueInDays"/> dias.</summary>
    private async Task<Guid> PayableAsync(Workspace space, HttpClient client, Guid supplier, int dueInDays = 3)
    {
        var account = await WithDbAsync(space, db => db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync());
        var pix = new { kind = "Pix", pixKeyType = "Cpf", pixKey = "52998224725", holderName = "Fulano de Tal", holderDocument = "52998224725" };
        var body = new { partyId = supplier, description = "Serviço", accountId = account, costCenterId = (Guid?)null, amount = 1_000m, issueDate = Iso(Today.AddDays(-5)), dueDate = Iso(Today.AddDays(dueInDays)),
            retentionIss = 50m, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, paymentDetails = pix };
        var created = await client.SendAsync(Post("/api/v1/finance/payables", body));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    private static Receivable Rec(Guid party, Guid account, decimal amount, DateOnly? due, ReceivableStatus status = ReceivableStatus.Open) => new()
    {
        Number = "P-" + Guid.NewGuid().ToString("N")[..10], PartyId = party, AccountId = account, Description = "Venda", Amount = amount, IssueDate = Today.AddDays(-200), DueDate = due,
        Status = status, Origin = DocumentOrigin.Manual, CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>Dois vencidos (1.000,00 e 500,00, de dois clientes), um a vencer e um cancelado vencido: só os dois contam.</summary>
    private async Task<List<Guid>> OverdueReceivablesAsync(Workspace space, HttpClient owner)
    {
        var (alfa, beta) = (await PartyAsync(owner, "Customer"), await PartyAsync(owner, "Customer"));
        return await WithDbAsync(space, async db =>
        {
            var account = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var overdue = new[] { Rec(alfa, account, 1_000m, Today.AddDays(-10)), Rec(beta, account, 500m, Today.AddDays(-2)) };
            db.Receivables.AddRange(overdue);
            db.Receivables.AddRange(Rec(alfa, account, 300m, Today.AddDays(5)), Rec(beta, account, 700m, Today.AddDays(-20), ReceivableStatus.Cancelled));
            await db.SaveChangesAsync();
            return overdue.Select(r => r.Id).ToList();
        });
    }

    /// <summary>Categoria → permissão das fontes registradas (as mesmas do agregador).</summary>
    private async Task<Dictionary<string, AgendaCategory>> RegisteredAsync(Workspace space)
    {
        await using var scope = await ScopeAsync(space);
        return scope.ServiceProvider.GetServices<IAgendaSource>().SelectMany(s => s.Categories).ToDictionary(c => c.Code);
    }

    [Fact]
    public async Task Sem_login_responde_401()
    {
        var client = tenancy.ClientFor("a.localhost");
        (await client.GetAsync(Agenda)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Resposta_nao_e_cacheada_e_vazia_mostra_zero()
    {
        var (space, _, _) = await SpaceAsync();
        var (_, nobody) = await ActorAsync(space, null);

        var response = await nobody.GetAsync(Agenda);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue("CA 13: nunca cacheada entre usuários");
        var view = await JsonAsync(response);
        view.GetProperty("items").GetArrayLength().Should().Be(0);
        view.GetProperty("failures").GetArrayLength().Should().Be(0);
        view.GetProperty("total").GetInt32().Should().Be(0);
    }

    /// <summary>CA 2, 4, 5 e 6: o título entra em "vencendo sem OP", sai ao ir para a OP; a OP aguarda o Proprietário e, aprovada, vai para pagar.</summary>
    [Fact]
    public async Task Titulo_sai_da_pauta_ao_entrar_na_OP_e_a_OP_vai_de_aguardando_a_pagar()
    {
        var (space, _, owner) = await SpaceAsync();
        var (_, finance) = await ActorAsync(space, "finance");
        var supplier = await PartyAsync(owner, "Supplier");
        var payable = await PayableAsync(space, finance, supplier);

        var before = await AgendaAsync(finance);
        var due = Category(before, "finance.payables_due_soon")!.Value;
        due.GetProperty("count").GetInt32().Should().Be(1);
        due.GetProperty("amount").GetDecimal().Should().Be(950m, "saldo líquido das retenções");
        due.GetProperty("tier").GetString().Should().Be("DueSoon");
        due.GetProperty("action").GetString().Should().Be("Gerar OP");
        due.GetProperty("title").GetString().Should().Be("1 título a pagar vence em breve sem OP");
        due.GetProperty("why").GetString().Should().StartWith("R$ 950,00 no total; o primeiro vence ");
        Links(before, "finance.payables_due_soon").Should().Equal($"/app/financeiro/ordens-de-pagamento/nova?titulos={payable}");

        var submitted = await finance.SendAsync(Post("/api/v1/finance/payment-orders", new { items = new[] { new { payableId = payable, amount = 950m, expectedCharges = 0m } }, expectedDate = (string?)null }));
        submitted.StatusCode.Should().Be(HttpStatusCode.Created, await submitted.Content.ReadAsStringAsync());
        var order = await JsonAsync(submitted);
        var orderId = order.GetProperty("summary").GetProperty("id").GetGuid();

        var afterSubmit = await AgendaAsync(finance);
        Category(afterSubmit, "finance.payables_due_soon").Should().BeNull("CA 6: o título foi para a OP");
        Category(afterSubmit, "finance.payment_orders_awaiting").Should().BeNull("o Financeiro do seed não tem finance.approve");
        var ownerView = await AgendaAsync(owner);
        var awaiting = Category(ownerView, "finance.payment_orders_awaiting")!.Value;
        awaiting.GetProperty("title").GetString().Should().Be("1 ordem de pagamento aguarda você");
        awaiting.GetProperty("link").GetString().Should().Be("/app/financeiro/ordens-de-pagamento?filtro=aguardando-voce");
        Links(ownerView, "finance.payment_orders_awaiting").Should().Equal($"/app/financeiro/ordens-de-pagamento/{orderId}?acao=aprovar");
        ownerView.GetProperty("items")[0].GetProperty("tier").GetString().Should().Be("Unblocks", "CA 4: o nível 1 vem primeiro");

        var approved = await owner.PostAsJsonAsync($"/api/v1/finance/payment-orders/{orderId}/approve", new { comment = (string?)null, version = order.GetProperty("version").GetUInt32() });
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());

        var afterApprove = await AgendaAsync(owner);
        Category(afterApprove, "finance.payment_orders_awaiting").Should().BeNull("CA 6: decidida, some");
        Links(afterApprove, "finance.payment_orders_to_pay").Should().Equal($"/app/financeiro/ordens-de-pagamento/{orderId}?acao=pagar");
        Links(await AgendaAsync(finance), "finance.payment_orders_to_pay").Should().Equal([$"/app/financeiro/ordens-de-pagamento/{orderId}?acao=pagar"], "o Financeiro do seed paga a OP aprovada (finance.pay, ADR 0008 adendo D5)");
    }

    /// <summary>CA 2 e 5: vencidos e em aberto (total e clientes) para quem tem <c>finance.edit</c>; Comercial não vê.</summary>
    [Fact]
    public async Task Recebiveis_vencidos_com_total_e_clientes_so_para_finance_edit()
    {
        var (space, _, owner) = await SpaceAsync();
        var ids = await OverdueReceivablesAsync(space, owner);
        var (_, finance) = await ActorAsync(space, "finance");
        var (_, commercial) = await ActorAsync(space, "commercial");

        var view = await AgendaAsync(finance);
        var overdue = Category(view, "finance.receivables_overdue")!.Value;
        overdue.GetProperty("count").GetInt32().Should().Be(2);
        overdue.GetProperty("amount").GetDecimal().Should().Be(1_500m);
        overdue.GetProperty("tier").GetString().Should().Be("Overdue");
        overdue.GetProperty("why").GetString().Should().StartWith("R$ 1.500,00 no total de 2 clientes; o mais antigo venceu em ");
        overdue.GetProperty("dueDate").GetString().Should().Be(Iso(Today.AddDays(-10)));
        Links(view, "finance.receivables_overdue").Should().Equal(ids.Select(id => "/app/financeiro/receber/" + id), "o mais antigo primeiro");
        Category(await AgendaAsync(commercial), "finance.receivables_overdue").Should().BeNull();
        Category(await AgendaAsync(owner), "finance.receivables_overdue").Should().NotBeNull();
    }

    /// <summary>
    /// CA 13: o usuário de cada grupo do seed só vê categorias cuja permissão está na permissão efetiva dele (∩ módulos liberados), e todo
    /// item é de módulo que ele pode abrir; o Operador não vê nada; o Proprietário vê o que existe.
    /// </summary>
    [Fact]
    public async Task Cada_grupo_do_seed_so_ve_as_categorias_das_suas_permissoes()
    {
        var (space, _, owner) = await SpaceAsync();
        var (_, finance) = await ActorAsync(space, "finance");
        await OverdueReceivablesAsync(space, owner);
        var payable = await PayableAsync(space, finance, await PartyAsync(owner, "Supplier"));
        (await finance.SendAsync(Post("/api/v1/finance/payment-orders", new { items = new[] { new { payableId = payable, amount = 950m, expectedCharges = 0m } }, expectedDate = (string?)null })))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await PayableAsync(space, finance, await PartyAsync(owner, "Supplier"), dueInDays: 1);
        var registered = await RegisteredAsync(space);

        var problems = new List<string>();
        var seen = new Dictionary<string, HashSet<string>>();
        foreach (var group in SeedGroups)
        {
            var (_, client) = await ActorAsync(space, group);
            var permissions = (await JsonAsync(await client.GetAsync("/api/v1/me/permissions"))).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToHashSet();
            var view = await AgendaAsync(client);
            var categories = view.GetProperty("items").EnumerateArray().Concat(view.GetProperty("failures").EnumerateArray()).Select(i => i.GetProperty("category").GetString()!).ToHashSet();
            seen[group] = categories;
            foreach (var category in categories)
            {
                var declared = registered[category];
                if (!permissions.Contains(declared.Permission))
                {
                    problems.Add($"{group} viu {category} sem {declared.Permission}");
                }

                if (!permissions.Contains(declared.Module + ".view"))
                {
                    problems.Add($"{group} viu {category} sem poder abrir o módulo {declared.Module}");
                }
            }
        }

        problems.Should().BeEmpty();
        seen["operators"].Should().BeEmpty("o Operador só tem o app do operador");
        seen["commercial"].Should().NotContain(c => c.StartsWith("finance.", StringComparison.Ordinal));
        seen["finance"].Should().Contain(["finance.receivables_overdue", "finance.payables_due_soon"]).And.NotContain("finance.payment_orders_awaiting", "aprovar a OP é só do Proprietário; pagar (finance.pay) o Financeiro do seed pode, ADR 0008 adendo D5");
        seen[Group.OwnerSeedKey].Should().Contain(["finance.receivables_overdue", "finance.payables_due_soon", "finance.payment_orders_awaiting"]);
    }

    /// <summary>CA 14: o vencido do tenant X não aparece no tenant Y.</summary>
    [Fact]
    public async Task Cada_tenant_so_ve_os_proprios_itens()
    {
        var (x, _, ownerX) = await SpaceAsync();
        var (_, _, ownerY) = await SpaceAsync();
        var ids = await OverdueReceivablesAsync(x, ownerX);

        Links(await AgendaAsync(ownerX), "finance.receivables_overdue").Should().BeEquivalentTo(ids.Select(id => "/app/financeiro/receber/" + id));
        Category(await AgendaAsync(ownerY), "finance.receivables_overdue").Should().BeNull();
    }

    /// <summary>CA 11: o painel do dono pede as categorias do nível 1 e recebe os mesmos itens, na mesma ordem.</summary>
    [Fact]
    public async Task Pedido_das_categorias_do_nivel_1_devolve_os_mesmos_itens_da_pauta()
    {
        var (space, _, owner) = await SpaceAsync();
        var (_, finance) = await ActorAsync(space, "finance");
        await OverdueReceivablesAsync(space, owner);
        var payable = await PayableAsync(space, finance, await PartyAsync(owner, "Supplier"));
        (await finance.SendAsync(Post("/api/v1/finance/payment-orders", new { items = new[] { new { payableId = payable, amount = 950m, expectedCharges = 0m } }, expectedDate = (string?)null })))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var tier1 = (await RegisteredAsync(space)).Values.Where(c => c.Tier == AgendaTier.Unblocks).Select(c => c.Code).ToList();

        var full = await AgendaAsync(owner);
        var filtered = await AgendaAsync(owner, string.Join(',', tier1));

        var expected = full.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("tier").GetString() == "Unblocks").Select(i => i.GetRawText()).ToList();
        expected.Should().NotBeEmpty();
        filtered.GetProperty("items").EnumerateArray().Select(i => i.GetRawText()).Should().Equal(expected);
    }
}
