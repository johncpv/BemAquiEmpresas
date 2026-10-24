using System.Net;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-03a CA 9 (ADR 0024 §4, ADR 0023 §5): o Proprietário único — o único usuário ativo com <c>purchasing.approve</c> — aprova o próprio
/// pedido pelo <c>SoleOwnerRule</c>, com a marca "Autoaprovado" e o aviso ao Proprietário. Banco desta classe, com um aprovador só.
/// </summary>
public class PurchaseOrderSoleOwnerTests(TenancyFixture tenancy) : PurchaseOrderTestBase(tenancy), IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Proprietario_unico_aprova_o_proprio_pedido_com_a_marca_Autoaprovado()
    {
        var (ownerId, owner) = await ActorAsync("owner");
        var supplier = await SupplierAsync(owner);
        var order = await SubmitAsync(owner, await CreateAsync(owner, OrderBody(supplier, null, null, (await ItemAsync(), 1m, 500m, null))));

        var approved = await DecideAsync(owner, order, true);

        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var body = await JsonAsync(approved);
        body.GetProperty("status").GetString().Should().Be("Approved");
        body.GetProperty("selfApproved").GetBoolean().Should().BeTrue();
        var id = order.GetProperty("id").GetGuid();
        (await WithDbAsync(db => db.PurchaseOrderApprovals.AsNoTracking().SingleAsync(a => a.OrderId == id && a.Decision == BemAqui.Purchasing.Domain.PurchaseDecision.Approved)))
            .SelfApproved.Should().BeTrue();
        (await WithDbAsync(db => db.Notifications.AsNoTracking().CountAsync(n => n.UserId == ownerId && n.Title.Contains("Autoaprovado")))).Should().Be(1);
        (await PayablesOfAsync(id)).Should().NotBeEmpty();
    }
}
