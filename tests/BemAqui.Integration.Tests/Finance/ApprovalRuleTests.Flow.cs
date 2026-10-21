using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class ApprovalRuleTests
{
    private const string Rules = "/api/v1/finance/approval-rules";

    [Fact]
    public async Task Regras_multinivel_fracionamento_e_alcada()
    {
        var (requesterId, requester) = await ActorAsync(Group.OwnerSeedKey);
        var (level1Id, level1) = await ActorAsync(Group.OwnerSeedKey);
        var (level2Id, level2) = await ActorAsync(Group.OwnerSeedKey);
        var (_, finance) = await ActorAsync("finance");

        // CA 1: a regra padrão existe e só o Proprietário mexe.
        var rules = await JsonAsync(await requester.GetAsync(Rules));
        rules.EnumerateArray().Should().ContainSingle(r => r.GetProperty("isDefault").GetBoolean());
        var ownerGroup = rules.EnumerateArray().Single(r => r.GetProperty("isDefault").GetBoolean()).GetProperty("approverGroupId").GetGuid();
        object Rule(decimal min, decimal? max, int level, Guid? group, Guid? user) => new { minAmount = min, maxAmount = max, accountId = (Guid?)null, approverGroupId = group, approverUserId = user, level };
        (await finance.PostAsJsonAsync(Rules, Rule(1_500.01m, null, 2, null, level2Id))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 3");

        // CA 4 e 5: sobreposição no mesmo nível; o mesmo aprovador em dois níveis.
        var overlap = await requester.PostAsJsonAsync(Rules, Rule(100m, null, 1, ownerGroup, null));
        overlap.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await overlap.Content.ReadAsStringAsync()).Should().Contain("A faixa se sobrepõe à regra");
        var sameApprover = await requester.PostAsJsonAsync(Rules, Rule(1_500.01m, null, 2, ownerGroup, null));
        (await sameApprover.Content.ReadAsStringAsync()).Should().Contain("O mesmo aprovador não pode decidir dois níveis da mesma faixa.");
        var created = await requester.PostAsJsonAsync(Rules, Rule(1_500.01m, null, 2, null, level2Id));
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());

        // CA 6 e 8: OP pequena só no nível 1; a segunda do mesmo fornecedor soma e cai no nível 2, com o aviso.
        var supplier = await SupplierAsync(requester);
        var p1 = await PayableAsync(requester, supplier);
        var first = await JsonAsync(await requester.SendAsync(Post(Base, Items((p1.GetProperty("id").GetGuid(), 950m)))));
        first.GetProperty("levels").GetArrayLength().Should().Be(1);
        var p2 = await PayableAsync(requester, supplier);
        var second = await JsonAsync(await requester.SendAsync(Post(Base, Items((p2.GetProperty("id").GetGuid(), 950m)))));
        var levels = second.GetProperty("levels");
        levels.GetArrayLength().Should().Be(2);
        levels[1].GetProperty("splitWarning").GetString().Should().StartWith("Possível fracionamento: 2 ordens");
        var orderId = second.GetProperty("summary").GetProperty("id").GetGuid();

        // CA 12, 13 e 15: nível 1 aprovado continua pendente; quem aprovou não decide o 2; fora da alçada, 403.
        second = await JsonAsync(await level1.PostAsJsonAsync($"{Base}/{orderId}/approve", new { comment = (string?)null, version = Version(second) }));
        second.GetProperty("summary").GetProperty("status").GetString().Should().Be("Pending");
        var again = await level1.PostAsJsonAsync($"{Base}/{orderId}/approve", new { comment = (string?)null, version = Version(second) });
        again.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await again.Content.ReadAsStringAsync()).Should().Contain("Você já aprovou o nível 1 desta ordem.");
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == level2Id && n.Title.Contains("(nível 2)")))).Should().Be(1);
        (await WithDbAsync(db => db.EmailLogs.CountAsync(e => e.Template == "payment-order-pending" && e.RelatedId == orderId))).Should().BeGreaterThan(0, "CA 16");
        second = await JsonAsync(await level2.PostAsJsonAsync($"{Base}/{orderId}/approve", new { comment = (string?)null, version = Version(second) }));
        second.GetProperty("summary").GetProperty("status").GetString().Should().Be("Approved");

        // Simulador (telas).
        var simulation = await JsonAsync(await requester.GetAsync($"{Rules}/simulate?amount=2000"));
        simulation.GetProperty("levels").GetArrayLength().Should().Be(2);
        _ = requesterId;
    }
}
