using System.Net;
using System.Text.Json;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-03a CA 5 a 9 (casos A a D da FIN-03b adaptados ao <c>PurchaseOrder</c>): alçadas próprias do pedido de compra. Banco desta classe;
/// as regras são montadas uma vez, sem a padrão do tipo:
/// nível 1 — até R$ 5.000,00 → aprovador A; de R$ 5.000,01 a R$ 49.999,99 → Proprietário; a partir de R$ 50.000,00 → aprovador C;
/// nível 2 — de R$ 10.000,01 a R$ 49.999,99 → grupo G (aprovador B e o Proprietário O1).
/// </summary>
public class PurchaseOrderAlcadaTests(TenancyFixture tenancy) : PurchaseOrderTestBase(tenancy), IClassFixture<TenancyFixture>
{
    private const string Buyer = "purchasing:view,create,edit";
    private const string Approver = "purchasing:view,approve";

    private static readonly SemaphoreSlim Once = new(1, 1);
    private static Rules? _rules;

    private sealed record Rules(HttpClient Owner, Guid OwnerId, HttpClient A, Guid AId, HttpClient B, HttpClient C, Guid CId, HttpClient O1, Guid O1Id, Guid LowRule, Guid OwnerRule, Guid LevelTwoRule);

    private async Task<Rules> RulesAsync()
    {
        await Once.WaitAsync();
        try
        {
            if (_rules is not null)
            {
                return _rules;
            }

            var (ownerId, owner) = await ActorAsync("owner");
            var (o1Id, o1) = await ActorAsync("owner");
            var (aId, a) = await ActorAsync(Approver);
            var (bId, b) = await ActorAsync(Approver);
            var (cId, c) = await ActorAsync("purchasing:view,create,edit,approve");
            var defaultRule = await WithDbAsync(db => db.ApprovalRules.Where(r => r.SeedKey == "owner-approves-all-purchase").Select(r => r.Id).SingleAsync());
            (await owner.DeleteAsync($"/api/v1/finance/approval-rules/{defaultRule}")).IsSuccessStatusCode.Should().BeTrue();
            var ownerGroup = await WithDbAsync(db => db.Groups.Where(g => g.SeedKey == "owner").Select(g => g.Id).SingleAsync());
            var groupG = await GroupAsync([bId, o1Id]);
            var low = await RuleAsync(owner, 1, 0m, 5_000m, approverUser: aId);
            var mid = await RuleAsync(owner, 1, 5_000.01m, 49_999.99m, approverGroup: ownerGroup);
            await RuleAsync(owner, 1, 50_000m, null, approverUser: cId);
            var two = await RuleAsync(owner, 2, 10_000.01m, 49_999.99m, approverGroup: groupG);
            _rules = new Rules(owner, ownerId, a, aId, b, c, cId, o1, o1Id, low, mid, two);
            return _rules;
        }
        finally
        {
            Once.Release();
        }
    }

    /// <summary>Grupo G com purchasing.approve e os membros dados.</summary>
    private async Task<Guid> GroupAsync(Guid[] members)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Identity.Application.IIdentityDb>();
        var group = new BemAqui.Identity.Domain.Group { Name = "Aprovadores G " + Guid.NewGuid().ToString("N")[..4], Description = "Teste" };
        db.Groups.Add(group);
        db.GroupModules.Add(new BemAqui.Identity.Domain.GroupModule { GroupId = group.Id, ModuleCode = "purchasing", Enabled = true });
        db.GroupPermissions.AddRange(new[] { "view", "approve" }.Select(a => new BemAqui.Identity.Domain.GroupGrant { GroupId = group.Id, ModuleCode = "purchasing", Action = a }));
        db.UserGroups.AddRange(members.Select(m => new BemAqui.Identity.Domain.UserGroup { UserId = m, GroupId = group.Id }));
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync(members);
        return group.Id;
    }

    private static Guid RuleOf(JsonElement order, int level) =>
        order.GetProperty("levels").EnumerateArray().Single(l => l.GetProperty("level").GetInt32() == level).GetProperty("ruleId").GetGuid();

    /// <summary>
    /// CA 6: dois pedidos de R$ 3.000,00 ao mesmo fornecedor em 30 dias: o segundo é resolvido por R$ 6.000,00 (soma dos pedidos, nunca dos
    /// títulos) e cai na regra do Proprietário, com o aviso de fracionamento no nível.
    /// </summary>
    [Fact]
    public async Task Fracionamento_soma_os_pedidos_do_fornecedor_em_30_dias()
    {
        var rules = await RulesAsync();
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(rules.Owner);

        var first = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 3_000m, null))));
        var second = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 3_000m, null))));

        RuleOf(first, 1).Should().Be(rules.LowRule, "R$ 3.000,00 cabe na faixa do aprovador A");
        first.GetProperty("splitWarning").ValueKind.Should().Be(JsonValueKind.Null);
        second.GetProperty("referenceAmount").GetDecimal().Should().Be(6_000m);
        RuleOf(second, 1).Should().Be(rules.OwnerRule, "a referência passa do teto de A");
        second.GetProperty("splitWarning").GetString().Should().Be("Possível fracionamento: 2 pedidos para o mesmo fornecedor em 30 dias, R$ 6.000,00.");
        var level = await WithDbAsync(db => db.PurchaseOrderLevels.AsNoTracking().SingleAsync(l => l.OrderId == second.GetProperty("id").GetGuid()));
        level.SplitWarning.Should().NotBeNull("o aviso fica gravado no nível");
        (await DecideAsync(rules.A, second, true)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "fora da alçada de A");
        Code(await JsonAsync(await DecideAsync(rules.A, second, true))).Should().Be("outside_approval_rule");
        (await DecideAsync(rules.A, first, true)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>CA 7: nível sem aprovador elegível (o único da regra é quem pediu): 422 approval_not_viable e o pedido continua Draft.</summary>
    [Fact]
    public async Task Nivel_sem_aprovador_elegivel_e_recusado_e_o_pedido_fica_rascunho()
    {
        var rules = await RulesAsync();
        var supplier = await SupplierAsync(rules.Owner);
        var order = await CreateAsync(rules.C, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 60_000m, null)));

        var refused = await SubmitAsync(rules.C, order, HttpStatusCode.UnprocessableEntity);

        Code(refused).Should().Be("approval_not_viable");
        refused.GetProperty("detail").GetString().Should().Contain("não tem aprovador elegível");
        (await GetAsync(rules.C, order.GetProperty("id").GetGuid())).GetProperty("status").GetString().Should().Be("Draft");
    }

    /// <summary>
    /// CA 9: multinível. O nível 2 só depois do 1; o mesmo aprovador não decide dois níveis (403 already_decided_level); quem está fora da
    /// regra do nível recebe 403 outside_approval_rule; no último nível, o pedido fica aprovado.
    /// </summary>
    [Fact]
    public async Task Multinivel_na_ordem_e_sem_o_mesmo_aprovador_em_dois_niveis()
    {
        var rules = await RulesAsync();
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(rules.Owner);
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 2m, 10_000m, null))));
        RuleOf(order, 2).Should().Be(rules.LevelTwoRule);

        var early = await DecideAsync(rules.B, order, true);
        early.StatusCode.Should().Be(HttpStatusCode.Forbidden, "o nível 1 vem antes");
        Code(await JsonAsync(early)).Should().Be("outside_approval_rule");

        var levelOne = await DecideAsync(rules.O1, order, true);
        levelOne.StatusCode.Should().Be(HttpStatusCode.OK, await levelOne.Content.ReadAsStringAsync());
        var afterOne = await JsonAsync(levelOne);
        afterOne.GetProperty("status").GetString().Should().Be("PendingApproval");
        (await PayablesOfAsync(order.GetProperty("id").GetGuid())).Should().BeEmpty("previsto só no último nível");

        var twice = await DecideAsync(rules.O1, afterOne, true);
        twice.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(twice)).Should().Be("already_decided_level");

        var levelTwo = await DecideAsync(rules.B, afterOne, true);
        levelTwo.StatusCode.Should().Be(HttpStatusCode.OK, await levelTwo.Content.ReadAsStringAsync());
        (await JsonAsync(levelTwo)).GetProperty("status").GetString().Should().Be("Approved");
        (await PayablesOfAsync(order.GetProperty("id").GetGuid())).Should().NotBeEmpty();
    }

    /// <summary>CA 20: "Aguardando você" só lista o que o usuário pode decidir agora, no nível em que o pedido está.</summary>
    [Fact]
    public async Task Aguardando_voce_mostra_so_o_nivel_atual_do_usuario()
    {
        var rules = await RulesAsync();
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(rules.Owner);
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 20_000m, null))));
        var id = order.GetProperty("id").GetGuid();

        async Task<IEnumerable<Guid>> Awaiting(HttpClient client) =>
            (await JsonAsync(await client.GetAsync($"{Base}?show=aguardando-voce&pageSize=100"))).GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetGuid());

        (await Awaiting(rules.O1)).Should().Contain(id);
        (await Awaiting(rules.B)).Should().NotContain(id, "o nível 2 ainda não foi liberado");
        (await DecideAsync(rules.O1, order, true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Awaiting(rules.B)).Should().Contain(id);
        (await Awaiting(rules.O1)).Should().NotContain(id, "já decidiu o nível 1");
    }
}
