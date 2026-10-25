using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-04 CA 2 (ADR 0024 §5, <c>SoleOwnerRule</c>): com um Proprietário só no tenant (nenhum outro usuário ativo com
/// <c>purchasing.receive</c>), quem pediu e aprovou registra o recebimento com a marca "Recebido pelo Proprietário único" e o aviso.
/// Banco desta classe, sem Almoxarifado.
/// </summary>
public class GoodsReceiptSoleOwnerTests(TenancyFixture tenancy) : GoodsReceiptTestBase(tenancy), IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Proprietario_unico_recebe_o_proprio_pedido_com_a_marca()
    {
        var (ownerId, owner) = await ActorAsync("owner");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, owner, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 1m, 300m, null));

        var receipt = await ReceivedAsync(owner, order, ReceiptBody(order, null, null, (LineOf(order, item), 1m, null)));

        receipt.GetProperty("soleOwner").GetBoolean().Should().BeTrue();
        var number = receipt.GetProperty("number").GetString()!;
        (await WithDbAsync(db => db.Notifications.AsNoTracking().CountAsync(n => n.UserId == ownerId && n.Title.Contains(number) && n.Title.Contains("Proprietário único"))))
            .Should().Be(1);
    }
}
