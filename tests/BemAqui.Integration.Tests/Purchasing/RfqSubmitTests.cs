using System.Net;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Purchasing.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-02 CA 10 e 15: "Enviar todos para aprovação" com alçadas próprias desta classe (banco próprio): sem a regra padrão, só uma regra de
/// nível 1 até R$ 5.000,00 → Proprietário. O pedido acima fica sem alçada viável (<c>approval_not_viable</c>) e os outros são enviados.
/// </summary>
public class RfqSubmitTests(TenancyFixture tenancy) : RfqTestBase(tenancy), IClassFixture<TenancyFixture>
{
    private static readonly SemaphoreSlim Once = new(1, 1);
    private static bool _rules;

    private async Task RulesAsync(HttpClient owner)
    {
        await Once.WaitAsync();
        try
        {
            if (_rules)
            {
                return;
            }

            var defaultRule = await WithDbAsync(db => db.ApprovalRules.Where(r => r.SeedKey == "owner-approves-all-purchase").Select(r => r.Id).SingleAsync());
            (await owner.DeleteAsync($"/api/v1/finance/approval-rules/{defaultRule}")).IsSuccessStatusCode.Should().BeTrue();
            var ownerGroup = await WithDbAsync(db => db.Groups.Where(g => g.SeedKey == "owner").Select(g => g.Id).SingleAsync());
            await RuleAsync(owner, 1, 0m, 5_000m, approverGroup: ownerGroup);
            _rules = true;
        }
        finally
        {
            Once.Release();
        }
    }

    [Fact]
    public async Task Enviar_todos_envia_os_viaveis_e_lista_o_sem_alcada_com_o_motivo()
    {
        var (_, owner) = await ActorAsync("owner");
        await RulesAsync(owner);
        var (_, buyer) = await ActorAsync(Buyer);
        var (alfa, beta) = (await SupplierAsync(owner), await SupplierAsync(owner));
        var (itemA, itemB) = (await ItemAsync(), await ItemAsync());
        var rfq = await CreateTypedRfqAsync(buyer, [(itemA, 1m), (itemB, 1m)], [alfa, beta]);
        var (a, b) = (LineOf(rfq, itemA), LineOf(rfq, itemB));
        await AttachAsync(SupplierRow(rfq, beta));
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, alfa), [new Quote(a, 1_000m)]);
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, beta), [new Quote(b, 6_000m)]);
        rfq = await CloseAsync(buyer, rfq, new Pick(a, SupplierRow(rfq, alfa)), new Pick(b, SupplierRow(rfq, beta)));
        await GenerateAsync(buyer, rfq);
        var orders = await WithDbAsync(db => db.PurchaseOrders.AsNoTracking().Where(o => o.RfqId == Id(rfq)).ToListAsync());

        var response = await buyer.PostAsync($"{Rfqs}/{Id(rfq)}/purchase-orders/submit", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await JsonAsync(response);
        result.GetProperty("sent").GetInt32().Should().Be(1);
        result.GetProperty("total").GetInt32().Should().Be(2);
        result.GetProperty("message").GetString().Should().Be("Pedidos enviados para aprovação (1 de 2)");
        var items = result.GetProperty("items").EnumerateArray().ToList();
        var sent = items.Single(i => i.GetProperty("sent").GetBoolean());
        sent.GetProperty("purchaseOrderId").GetGuid().Should().Be(orders.Single(o => o.PartyId == alfa).Id);
        sent.GetProperty("levels").GetInt32().Should().Be(1);
        var refused = items.Single(i => !i.GetProperty("sent").GetBoolean());
        refused.GetProperty("code").GetString().Should().Be("approval_not_viable");
        refused.GetProperty("message").GetString().Should().NotBeNullOrEmpty();

        var saved = await WithDbAsync(db => db.PurchaseOrders.AsNoTracking().Where(o => o.RfqId == Id(rfq)).ToListAsync());
        saved.Single(o => o.PartyId == alfa).Status.Should().Be(PurchaseOrderStatus.PendingApproval);
        saved.Single(o => o.PartyId == beta).Status.Should().Be(PurchaseOrderStatus.Draft, "o recusado continua rascunho, de forma independente");
        (await WithDbAsync(db => db.PurchaseOrderLevels.CountAsync(l => l.OrderId == saved.Single(o => o.PartyId == beta).Id))).Should().Be(0, "nada do envio recusado ficou gravado");

        // CA 15: o histórico da cotação guarda a geração (pedido no fornecedor) e o envio (data ou motivo).
        var rows = await WithDbAsync(db => db.RfqSuppliers.AsNoTracking().Where(s => s.RfqId == Id(rfq)).ToListAsync());
        rows.Single(r => r.PartyId == alfa).OrderSubmittedAt.Should().NotBeNull();
        rows.Single(r => r.PartyId == beta).OrderSubmitError.Should().NotBeNullOrEmpty();
        (await AuditOfAsync("RfqSupplier", rows.Single(r => r.PartyId == alfa).Id)).Should()
            .Contain(h => h.Changes.Contains("purchaseOrderId", StringComparison.Ordinal)).And.Contain(h => h.Changes.Contains("orderSubmittedAt", StringComparison.Ordinal));
    }

    /// <summary>CA 10: sem pedido em rascunho, 409 com a orientação.</summary>
    [Fact]
    public async Task Enviar_todos_sem_rascunho_e_recusado()
    {
        var (_, owner) = await ActorAsync("owner");
        await RulesAsync(owner);
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 1m)], [supplier]);

        var response = await buyer.PostAsync($"{Rfqs}/{Id(rfq)}/purchase-orders/submit", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(response)).Should().Be("rfq_no_draft_orders");
    }
}
