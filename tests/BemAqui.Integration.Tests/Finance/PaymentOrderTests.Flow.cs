using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class PaymentOrderTests
{
    [Fact]
    public async Task Financeiro_envia_proprietario_aprova_financeiro_paga_parcial_e_o_saldo_entra_noutra_OP()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (financeId, finance) = await ActorAsync("finance");
        var supplier = await SupplierAsync(owner);
        var payable = await PayableAsync(finance, supplier);
        var payableId = payable.GetProperty("id").GetGuid();
        var bank = await BankAsync(owner);

        // CA 2: mesma chave, mesma OP; o título fica aguardando aprovação.
        var key = Guid.NewGuid();
        var submitted = await finance.SendAsync(Post(Base, Items((payableId, 600m)), key));
        submitted.StatusCode.Should().Be(HttpStatusCode.Created, await submitted.Content.ReadAsStringAsync());
        var order = await JsonAsync(submitted);
        var orderId = order.GetProperty("summary").GetProperty("id").GetGuid();
        order.GetProperty("summary").GetProperty("number").GetString().Should().StartWith("OP-");
        (await finance.SendAsync(Post(Base, Items((payableId, 600m)), key))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await WithDbAsync(db => db.PaymentOrderItems.CountAsync(i => i.PayableId == payableId))).Should().Be(1);
        (await WithDbAsync(db => db.Payables.Where(p => p.Id == payableId).Select(p => p.Status).SingleAsync())).Should().Be(PayableStatus.AwaitingApproval);

        // CA 3: o título já está numa OP.
        var again = await finance.SendAsync(Post(Base, Items((payableId, 100m))));
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await again.Content.ReadAsStringAsync()).Should().Contain("não está disponível para pagamento");

        // CA 4 e 6: retrato do envio, mascarado, com o aviso do fornecedor novo.
        var item = order.GetProperty("items")[0];
        item.GetProperty("approvedTotal").GetDecimal().Should().Be(600m);
        item.GetProperty("payment").GetProperty("pixKey").GetString().Should().NotBe("52998224725");
        item.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).Should().Contain(w => w!.StartsWith("Fornecedor cadastrado há"));

        // CA 5 e 16: aprovadores avisados; sem aprovação não paga.
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.Title.Contains("aguarda sua aprovação") && n.UserId == financeId))).Should().Be(0, "o solicitante não é avisado");
        var payBody = new { bankAccountId = bank, date = Iso(Today), items = Array.Empty<object>(), backdateReason = (string?)null, version = Version(order) };
        var notApproved = await finance.SendAsync(Post($"{Base}/{orderId}/pay", payBody));
        Code(await JsonAsync(notApproved)).Should().Be("payment_order_not_approved");

        // CA 7: o Proprietário aprova; quem paga é avisado.
        (await finance.PostAsJsonAsync($"{Base}/{orderId}/approve", new { comment = (string?)null, version = Version(order) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var approved = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{orderId}/approve", new { comment = "Ok", version = Version(order) }));
        approved.GetProperty("summary").GetProperty("status").GetString().Should().Be("Approved");
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == financeId && n.Title.EndsWith("aprovada. Pode pagar.")))).Should().Be(1);

        // CA 14: acima do aprovado, 409; reenviar volta a Pending.
        var over = new { bankAccountId = bank, date = Iso(Today), items = new[] { new { itemId = item.GetProperty("id").GetGuid(), interest = 10m, fine = 0m, discount = 0m } }, backdateReason = (string?)null, version = Version(approved) };
        var exceeded = await finance.SendAsync(Post($"{Base}/{orderId}/pay", over));
        exceeded.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(exceeded)).Should().Be("exceeds_approved");
        var resubmitted = await JsonAsync(await finance.PostAsJsonAsync($"{Base}/{orderId}/resubmit", new { items = over.items, version = Version(approved) }));
        resubmitted.GetProperty("summary").GetProperty("status").GetString().Should().Be("Pending");
        resubmitted.GetProperty("summary").GetProperty("total").GetDecimal().Should().Be(610m);
        approved = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{orderId}/approve", new { comment = (string?)null, version = Version(resubmitted) }));

        // CA 13: pagamento parcial com juros; transação Payment negativa; título PartiallyPaid.
        var paid = await finance.SendAsync(Post($"{Base}/{orderId}/pay", over with { version = Version(approved) }));
        paid.StatusCode.Should().Be(HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
        (await JsonAsync(paid)).GetProperty("total").GetDecimal().Should().Be(610m);
        var tx = await WithDbAsync(db => db.FinancialTransactions.Where(t => t.PayableId == payableId).Select(t => new { t.Amount, t.Type }).ToListAsync());
        tx.Should().ContainSingle().Which.Should().Be(new { Amount = -610m, Type = TransactionType.Payment });
        var after = await WithDbAsync(db => db.Payables.Where(p => p.Id == payableId).Select(p => new { p.Status, p.PaidAmount }).SingleAsync());
        after.Should().Be(new { Status = PayableStatus.PartiallyPaid, PaidAmount = 600m });

        // O saldo (350,00) entra noutra OP; mais que isso, 422.
        (await finance.SendAsync(Post(Base, Items((payableId, 350.01m))))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await finance.SendAsync(Post(Base, Items((payableId, 350m))))).StatusCode.Should().Be(HttpStatusCode.Created);

        await ReverseAsync(owner, finance, orderId);
    }

    private async Task ReverseAsync(HttpClient owner, HttpClient finance, Guid orderId)
    {
        var order = await JsonAsync(await owner.GetAsync($"{Base}/{orderId}"));
        (await owner.PostAsJsonAsync($"{Base}/{orderId}/cancel", new { reason = "Erro", version = Version(order) })).StatusCode.Should().Be(HttpStatusCode.Conflict, "OP paga não é cancelada (CA 20)");
        var reversed = await finance.PostAsJsonAsync($"{Base}/{orderId}/reverse", new { reason = "Pix devolvido", version = Version(order) });
        reversed.StatusCode.Should().Be(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());
        (await JsonAsync(reversed)).GetProperty("summary").GetProperty("status").GetString().Should().Be("Reversed");
        var payableId = order.GetProperty("items")[0].GetProperty("payableId").GetGuid();
        (await WithDbAsync(db => db.FinancialTransactions.Where(t => t.PayableId == payableId).SumAsync(t => t.Amount))).Should().Be(0m, "estorno com transação inversa (CA 21)");
        (await WithDbAsync(db => db.Payables.Where(p => p.Id == payableId).Select(p => p.PaidAmount).SingleAsync())).Should().Be(0m);
    }
}
