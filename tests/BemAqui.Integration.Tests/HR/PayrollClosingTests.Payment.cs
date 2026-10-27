using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Finance.Contracts;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

public partial class PayrollClosingTests
{
    private static object OrderOf(params (Guid Id, decimal Amount)[] items) =>
        new { items = items.Select(i => new { payableId = i.Id, amount = i.Amount, expectedCharges = 0m }), expectedDate = (string?)null };

    private static async Task<Guid> BankAsync(HttpClient owner)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Banco " + Guid.NewGuid().ToString("N")[..6], kind = "Bank", bankCode = "077", agency = "1234-5", account = "12345678-9", openingBalance = 50_000m, openingDate = Iso(Today.AddDays(-30)), isDefault = false });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private Task<int> ReconcileAsync() => InTenantAsync(tenancy.TenantA, async sp => { await sp.GetRequiredService<PayrollClosingService>().ReconcileAsync(); return 0; });

    /// <summary>
    /// CA 5, 7 a 12: aba Pagamentos com a pré-seleção sem a quarentena, salário agrupado para finance.view, retrato sem dado bancário,
    /// quarentena recusada na OP, conta lida só no "Pagar" (SensitiveRead com a OP, versão diferente 409), quem fecha não paga, folha paga
    /// pela fonte e volta a fechada no estorno.
    /// </summary>
    [Fact]
    public async Task Salarios_seguem_para_OP_com_quarentena_fora_e_conta_lida_so_no_pagar()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var approver = await ActorAsync(Group.OwnerSeedKey);
        var payer = await ActorAsync("finance:view,create,edit,pay");
        var viewer = await ActorAsync("finance:view");
        var (run, a, b, _) = await ReadyRunAsync(owner, "2036-01", quarantineB: true);
        (await CloseAsync(owner, run, "2036-01-29")).StatusCode.Should().Be(HttpStatusCode.OK);
        var titles = await PayrollPayablesAsync(run);
        var salaryA = titles.Single(t => t.PayeeId == a.Id);
        var salaryB = titles.Single(t => t.PayeeId == b.Id);
        var guide = titles.Single(t => t.PayeeType == PayeeType.Party);

        // CA 5: pré-seleção dos salários sem a conta em quarentena; das guias, a guia. Sem finance.view, só "Título gerado".
        var payments = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/payments/sensitive"));
        payments.GetProperty("salaryPreselected").EnumerateArray().Select(x => x.GetGuid()).Should().Equal(salaryA.Id);
        payments.GetProperty("quarantined").EnumerateArray().Select(x => x.GetProperty("employeeId").GetGuid()).Should().Equal(b.Id);
        payments.GetProperty("chargePreselected").EnumerateArray().Select(x => x.GetGuid()).Should().Equal(guide.Id);
        var hrOnly = await ActorAsync("hr:view,view_sensitive");
        var blind = await JsonAsync(await hrOnly.Client.GetAsync($"{Runs}/{run}/payments/sensitive"));
        blind.GetProperty("items").EnumerateArray().Should().OnlyContain(i => i.GetProperty("statusLabel").GetString() == "Título gerado");

        // CA 7: finance.view vê "Folha Férias janeiro de 2036: 2 salários", sem nome nem valor por funcionário; quem paga vê cada um.
        var list = await JsonAsync(await viewer.Client.GetAsync("/api/v1/finance/payables?situation=aberto&pageSize=100"));
        var listText = list.GetRawText();
        list.GetProperty("payrollGroups").EnumerateArray().Should().Contain(g => g.GetProperty("label").GetString() == "Folha Férias janeiro de 2036" && g.GetProperty("count").GetInt32() == 2);
        listText.Should().NotContain(a.Name).And.NotContain(salaryA.Id.ToString());
        (await viewer.Client.GetAsync($"/api/v1/finance/payables/{salaryA.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var detail = await JsonAsync(await payer.Client.GetAsync($"/api/v1/finance/payables/{salaryA.Id}"));
        detail.GetProperty("partyName").GetString().Should().Be($"Funcionário {a.Registration} — {a.Name}");
        detail.GetProperty("payeeType").GetString().Should().Be("Employee");

        // CA 9: o salário em quarentena não entra na OP (422) e nada muda.
        var quarantined = await payer.Client.SendAsync(Post(Orders, OrderOf((salaryB.Id, salaryB.Amount))));
        quarantined.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(quarantined)).Should().Be("payee_account_in_quarantine");
        (await DbAsync(db => db.Payables.AsNoTracking().Where(p => p.Id == salaryB.Id).Select(p => p.Status).SingleAsync())).Should().Be(PayableStatus.Open);

        // A guia precisa da forma de pagamento (título do órgão, Party).
        var guideVersion = (await JsonAsync(await payer.Client.GetAsync($"/api/v1/finance/payables/{guide.Id}"))).GetProperty("version").GetUInt32();
        (await payer.Client.PutAsJsonAsync($"/api/v1/finance/payables/{guide.Id}/payment-method", new { paymentDetails = new { kind = "Other", description = "Débito em conta da guia" }, version = guideVersion }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // CA 8: o retrato do salário guarda só o favorecido, a versão e o final mascarado.
        var submitted = await payer.Client.SendAsync(Post(Orders, OrderOf((salaryA.Id, salaryA.Amount), (guide.Id, guide.Amount))));
        submitted.StatusCode.Should().Be(HttpStatusCode.Created, await submitted.Content.ReadAsStringAsync());
        var order = await JsonAsync(submitted);
        var orderId = order.GetProperty("summary").GetProperty("id").GetGuid();
        var snapshot = await DbAsync(db => db.PaymentOrderItems.Where(i => i.PayableId == salaryA.Id).Select(i => i.PayeeSnapshot).SingleAsync());
        snapshot.Should().NotContain(AccountCanary).And.Contain("\"agency\": null").And.Contain("****-71").And.Contain("\"bankAccountVersion\":1");
        order.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("payableId").GetGuid() == salaryA.Id).GetProperty("warnings").EnumerateArray()
            .Select(w => w.GetString()).Should().Contain("Novo na folha.");

        var approved = await approver.Client.PostAsJsonAsync($"{Orders}/{orderId}/approve", new { comment = (string?)null, version = order.GetProperty("version").GetUInt32() });
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var version = (await JsonAsync(approved)).GetProperty("version").GetUInt32();
        var items = (await JsonAsync(approved)).GetProperty("items").EnumerateArray().Select(i => new { itemId = i.GetProperty("id").GetGuid(), interest = 0m, fine = 0m, discount = 0m }).ToList();
        var bank = await BankAsync(owner.Client);

        // CA 10: a conta em claro só no "Pagar", com no-store e SensitiveRead com o número da OP; só com finance.pay.
        (await viewer.Client.GetAsync($"{Orders}/{orderId}/payee-accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var accounts = await payer.Client.GetAsync($"{Orders}/{orderId}/payee-accounts");
        accounts.StatusCode.Should().Be(HttpStatusCode.OK, await accounts.Content.ReadAsStringAsync());
        accounts.Headers.CacheControl!.NoStore.Should().BeTrue();
        var read = await JsonAsync(accounts);
        read.EnumerateArray().Single().GetProperty("account").GetString().Should().Be(AccountCanary + "1");
        var number = order.GetProperty("summary").GetProperty("number").GetString()!;
        (await DbAsync(db => db.AuditEntries.Where(e => e.Action == AuditActions.SensitiveRead && e.EntityId == a.Id.ToString()).Select(e => e.Changes).ToListAsync()))
            .Should().Contain(c => c.Contains(number, StringComparison.Ordinal));
        (await DbAsync(async db => (await db.AuditEntries.Select(e => e.Changes).ToListAsync()).Count(c => c.Contains(AccountCanary, StringComparison.Ordinal)))).Should().Be(0, "a auditoria não guarda dado bancário");

        // A conta mudou depois do envio: 409 na leitura e no pagamento.
        await SetAccountAsync(a.Id, 2);
        (await CodeAsync(await payer.Client.GetAsync($"{Orders}/{orderId}/payee-accounts"))).Should().Be("payee_account_changed");
        (await CodeAsync(await payer.Client.SendAsync(Post($"{Orders}/{orderId}/pay", new { bankAccountId = bank, date = Iso(Today), items, backdateReason = (string?)null, version }))))
            .Should().Be("payee_account_changed");
        await SetAccountAsync(a.Id, 1);

        // CA 11: quem fechou a folha não paga (o Proprietário tem mais de um membro).
        var closerPay = await owner.Client.SendAsync(Post($"{Orders}/{orderId}/pay", new { bankAccountId = bank, date = Iso(Today), items, backdateReason = (string?)null, version }));
        closerPay.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(closerPay)).Should().Be("payroll_closer_cannot_pay");

        // CA 13: com título em OP, a folha não reabre.
        var reopenBusy = await owner.Client.PostAsJsonAsync($"{Runs}/{run}/reopen", new { reason = "Corrigir um holerite", version = await RunVersionAsync(owner.Client, run) });
        (await CodeAsync(reopenBusy)).Should().Be(PayrollClosingRules.ReopenInPaymentCode);

        var paid = await payer.Client.SendAsync(Post($"{Orders}/{orderId}/pay", new { bankAccountId = bank, date = Iso(Today), items, backdateReason = (string?)null, version }));
        paid.StatusCode.Should().Be(HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());

        // CA 12: com o salário em quarentena ainda aberto, a folha continua fechada; revisada a conta e paga, a folha fica paga.
        await ReconcileAsync();
        (await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}"))).GetProperty("status").GetString().Should().Be("Closed");
        await DbAsync(async db => { var e = await db.Employees.SingleAsync(x => x.Id == b.Id); e.BankReviewedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); return 0; });
        var second = await JsonAsync(await payer.Client.SendAsync(Post(Orders, OrderOf((salaryB.Id, salaryB.Amount)))));
        var secondId = second.GetProperty("summary").GetProperty("id").GetGuid();
        var secondApproved = await JsonAsync(await approver.Client.PostAsJsonAsync($"{Orders}/{secondId}/approve", new { comment = (string?)null, version = second.GetProperty("version").GetUInt32() }));
        var secondItems = secondApproved.GetProperty("items").EnumerateArray().Select(i => new { itemId = i.GetProperty("id").GetGuid(), interest = 0m, fine = 0m, discount = 0m }).ToList();
        (await payer.Client.SendAsync(Post($"{Orders}/{secondId}/pay", new { bankAccountId = bank, date = Iso(Today), items = secondItems, backdateReason = (string?)null, version = secondApproved.GetProperty("version").GetUInt32() })))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await ReconcileAsync();
        (await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}"))).GetProperty("status").GetString().Should().Be("Paid");
        (await DbAsync(db => OutboxCountAsync(db, nameof(PaymentOrderPaid), secondId))).Should().Be(1);

        // Estorno: PaymentOrderReversed e a folha volta a fechada.
        var current = await JsonAsync(await payer.Client.GetAsync($"{Orders}/{secondId}"));
        var reversed = await payer.Client.PostAsJsonAsync($"{Orders}/{secondId}/reverse", new { reason = "Pagamento em duplicidade", version = current.GetProperty("version").GetUInt32() });
        reversed.StatusCode.Should().Be(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());
        (await DbAsync(db => OutboxCountAsync(db, nameof(PaymentOrderReversed), secondId))).Should().Be(1);
        await ReconcileAsync();
        (await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}"))).GetProperty("status").GetString().Should().Be("Closed");
    }

    /// <summary>CA 13: sem título em OP, reabre com motivo: os títulos são cancelados pela origem e a folha volta a rascunho; fecha de novo sem duplicar.</summary>
    [Fact]
    public async Task Reabre_cancelando_os_titulos_e_fecha_de_novo_sem_duplicar()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var (run, _, _, _) = await ReadyRunAsync(owner, "2036-02");
        (await CloseAsync(owner, run, "2036-02-27")).StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await PayrollPayablesAsync(run);

        (await owner.Client.PostAsJsonAsync($"{Runs}/{run}/reopen", new { reason = "x", version = await RunVersionAsync(owner.Client, run) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var reopened = await owner.Client.PostAsJsonAsync($"{Runs}/{run}/reopen", new { reason = "Faltou a hora extra", version = await RunVersionAsync(owner.Client, run) });
        reopened.StatusCode.Should().Be(HttpStatusCode.OK, await reopened.Content.ReadAsStringAsync());
        var summary = await JsonAsync(reopened);
        summary.GetProperty("status").GetString().Should().Be("Draft");
        summary.GetProperty("reopenReason").GetString().Should().Be("Faltou a hora extra");
        (await PayrollPayablesAsync(run)).Should().BeEmpty();
        var cancelled = await DbAsync(db => db.Payables.AsNoTracking().Where(p => before.Select(x => x.Id).Contains(p.Id)).ToListAsync());
        cancelled.Should().OnlyContain(p => p.Status == PayableStatus.Cancelled && p.CancelReason!.Contains("Folha reaberta: Faltou a hora extra"));

        (await CloseAsync(owner, run, "2036-02-27")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PayrollPayablesAsync(run)).Should().HaveCount(before.Count);
    }
}
