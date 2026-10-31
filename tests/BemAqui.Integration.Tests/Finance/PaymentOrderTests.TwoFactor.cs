using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-01a CA 2 (D6, P-19): pagar e estornar a OP passam a exigir o 2FA de novo. Com a prova recente passa; sem ela, ou com a prova
/// de mais de 15 minutos, 403 <c>two_factor_required</c> e nada é gravado.
/// </summary>
public partial class PaymentOrderTests
{
    [Fact]
    public async Task Pagar_e_estornar_exigem_o_2FA_recente_e_a_recusa_nao_grava_nada()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, finance) = await ActorAsync("finance");
        var (_, noProof) = await ActorAsync("finance", recentTwoFactor: false);
        var (_, stale) = await ActorAsync("finance", proofAge: TimeSpan.FromMinutes(16));
        var supplier = await SupplierAsync(owner);
        var payableId = (await PayableAsync(finance, supplier)).GetProperty("id").GetGuid();
        var bank = await BankAsync(owner);
        var order = await JsonAsync(await finance.SendAsync(Post(Base, Items((payableId, 950m)))));
        var orderId = order.GetProperty("summary").GetProperty("id").GetGuid();
        var approved = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{orderId}/approve", new { comment = (string?)null, version = Version(order) }));
        object Pay() => new { bankAccountId = bank, date = Iso(Today), items = Array.Empty<object>(), backdateReason = (string?)null, version = Version(approved) };

        // Sem a prova (o token não traz otp_at): recusa sem efeito; o usuário sem 2FA ativo recebe o texto de ativar.
        var key = Guid.NewGuid();
        var refused = await noProof.SendAsync(Post($"{Base}/{orderId}/pay", Pay(), key));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("two_factor_required");
        problem.GetProperty("enrolled").GetBoolean().Should().BeFalse();
        problem.GetProperty("detail").GetString().Should().Be("Para continuar, ative a verificação em duas etapas.");
        var tooOld = await stale.SendAsync(Post($"{Base}/{orderId}/pay", Pay()));
        var stale16 = await JsonAsync(tooOld);
        Code(stale16).Should().Be("two_factor_required", "prova de 16 minutos já venceu (janela de 15)");
        stale16.GetProperty("detail").GetString().Should().Be("Para continuar, confirme o código da verificação em duas etapas.");
        (await WithDbAsync(db => db.FinancialTransactions.CountAsync(t => t.PayableId == payableId))).Should().Be(0);
        (await WithDbAsync(db => db.PaymentOrders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync())).Should().Be(PaymentOrderStatus.Approved);
        (await WithDbAsync(db => db.IdempotencyRecords.CountAsync(r => r.Key == key))).Should().Be(0, "a recusa vem antes da chave de idempotência");

        // Com a prova recente, a mesma chave paga (a recusa não gastou a chave).
        var paid = await finance.SendAsync(Post($"{Base}/{orderId}/pay", Pay(), key));
        paid.StatusCode.Should().Be(HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());

        var current = await JsonAsync(await owner.GetAsync($"{Base}/{orderId}"));
        // BNK-02 CA 15 (P-20): estornar pede finance.approve; o Financeiro (finance.pay) recebe 403.
        var withoutApprove = await finance.PostAsJsonAsync($"{Base}/{orderId}/reverse", new { reason = "Pix devolvido", version = Version(current) });
        withoutApprove.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(withoutApprove)).Should().Be("forbidden");
        var (_, ownerNoProof) = await ActorAsync(Group.OwnerSeedKey, recentTwoFactor: false);
        var reverseRefused = await ownerNoProof.PostAsJsonAsync($"{Base}/{orderId}/reverse", new { reason = "Pix devolvido", version = Version(current) });
        Code(await JsonAsync(reverseRefused)).Should().Be("two_factor_required");
        (await WithDbAsync(db => db.PaymentOrders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync())).Should().Be(PaymentOrderStatus.Paid, "estorno recusado não grava");
        (await WithDbAsync(db => db.FinancialTransactions.CountAsync(t => t.PayableId == payableId))).Should().Be(1);

        var reversed = await owner.PostAsJsonAsync($"{Base}/{orderId}/reverse", new { reason = "Pix devolvido", version = Version(current) });
        reversed.StatusCode.Should().Be(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());

        // BNK-02 CA 15: o título volta a ficar disponível e, na nova OP, aparece "Pago e estornado em {data}" para o aprovador.
        var again = await finance.SendAsync(Post(Base, Items((payableId, 950m))));
        again.StatusCode.Should().Be(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
        (await JsonAsync(again)).GetProperty("items")[0].GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().Contain(w => w!.StartsWith("Pago e estornado em ", StringComparison.Ordinal));
        (await JsonAsync(reversed)).GetProperty("items")[0].GetProperty("warnings").EnumerateArray().Select(w => w.GetString())
            .Should().NotContain(w => w!.StartsWith("Pago e estornado", StringComparison.Ordinal), "a OP estornada não leva a marca");
    }
}
