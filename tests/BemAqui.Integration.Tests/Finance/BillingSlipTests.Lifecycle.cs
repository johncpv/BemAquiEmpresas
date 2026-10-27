using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Email;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using static BemAqui.Integration.Tests.Finance.BillingSlipFixture;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-01b: Configurações → Cobrança (CA 1 a 3), cancelar manual e automático (CA 16, 17), expirar (CA 18), lote (CA 19), e-mail e
/// limite diário (CA 13), três camadas e IDOR (CA 14, 21) e auditoria (CA 23).
/// </summary>
public partial class BillingSlipTests
{
    private const string Settings = "/api/v1/settings/billing";

    private static object SettingsBody(decimal fine = 2m, decimal interest = 1m, decimal discount = 0m, bool autoEmail = true, int limit = 200, string from = "OriginalDueDate") =>
        new { finePct = fine, interestMonthPct = interest, defaultDiscountPct = discount, autoEmail, dailyEmailLimit = limit, overdueFineFrom = from };

    [Fact]
    public async Task Configuracoes_de_cobranca_seed_conferencia_e_validacao()
    {
        var (_, owner) = await fixture.ActorAsync(owner: true);
        var (_, viewer) = await fixture.ActorAsync(false, "finance.view");
        try
        {
            // CA 1: valores do seed; CA 2: multa, juros e a regra do vencido "A confirmar com a contadora".
            var seeded = await JsonAsync(await viewer.GetAsync(Settings));
            seeded.GetProperty("finePct").GetDecimal().Should().Be(2m);
            seeded.GetProperty("interestMonthPct").GetDecimal().Should().Be(1m);
            seeded.GetProperty("defaultDiscountPct").GetDecimal().Should().Be(0m);
            seeded.GetProperty("autoEmail").GetBoolean().Should().BeTrue();
            seeded.GetProperty("dailyEmailLimit").GetInt32().Should().Be(200);
            seeded.GetProperty("overdueFineFrom").GetString().Should().Be("OriginalDueDate");
            seeded.GetProperty("pendingCount").GetInt32().Should().Be(3);
            seeded.GetProperty("canEdit").GetBoolean().Should().BeFalse();

            // CA 3: sem settings.edit, 403; com, valida em PT-BR.
            (await viewer.PutAsJsonAsync(Settings, SettingsBody())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await viewer.PostAsJsonAsync(Settings + "/confirm", new { key = BillingSettingsService.FinePct })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var tooHigh = await owner.PutAsJsonAsync(Settings, SettingsBody(fine: 2.5m));
            tooHigh.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await JsonAsync(tooHigh)).GetProperty("errors").GetProperty("finePct")[0].GetString().Should().Be("A multa não pode passar de 2,00%.");
            (await owner.PutAsJsonAsync(Settings, SettingsBody(limit: 0))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await owner.PutAsJsonAsync(Settings, SettingsBody(interest: 1.5m))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            // "Marcar como conferido" só para o campo; mudar o valor conferido volta o selo.
            var confirmed = await JsonAsync(await owner.PostAsJsonAsync(Settings + "/confirm", new { key = BillingSettingsService.FinePct }));
            confirmed.GetProperty("pendingCount").GetInt32().Should().Be(2);
            var fine = confirmed.GetProperty("reviews").EnumerateArray().Single(r => r.GetProperty("key").GetString() == BillingSettingsService.FinePct);
            fine.GetProperty("reviewState").GetString().Should().Be("Confirmed");
            fine.GetProperty("reviewedByName").ValueKind.Should().Be(JsonValueKind.String);
            var changed = await owner.PutAsJsonAsync(Settings, SettingsBody(fine: 1.5m, limit: 150));
            changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
            var saved = await JsonAsync(changed);
            saved.GetProperty("finePct").GetDecimal().Should().Be(1.5m);
            saved.GetProperty("dailyEmailLimit").GetInt32().Should().Be(150);
            saved.GetProperty("pendingCount").GetInt32().Should().Be(3);

            // CA 23: auditado com antes e depois.
            var id = await fixture.QueryAsync(db => db.FinanceParameters.Where(p => p.Key == BillingSettingsService.FinePct).Select(p => p.Id).SingleAsync());
            var audit = await fixture.QueryAsync(db => db.AuditEntries.AsNoTracking().Where(e => e.Entity == nameof(FinanceParameter) && e.EntityId == id.ToString()).Select(e => e.Changes).ToListAsync());
            audit.Should().Contain(c => c.Contains("1.5", StringComparison.Ordinal) || c.Contains("1,5", StringComparison.Ordinal));
        }
        finally
        {
            (await owner.PutAsJsonAsync(Settings, SettingsBody())).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Cancelar_manual_vai_ao_Inter_e_libera_o_titulo()
    {
        var s = await fixture.ScenarioAsync(900m);
        var slipId = await fixture.IssuedAsync(s);
        var slips = Slips(s.ReceivableId);

        // Motivo fora da lista manual: 422.
        (await s.Owner.PostAsJsonAsync($"{slips}/{slipId}/cancel", new { reason = "DueDateChanged" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // CA 16: o cancelamento vai ao Inter pelo job; o boleto só fica Cancelled depois da confirmação do banco.
        var requested = await s.Owner.PostAsJsonAsync($"{slips}/{slipId}/cancel", new { reason = "CustomerRequest" });
        requested.StatusCode.Should().Be(HttpStatusCode.OK, await requested.Content.ReadAsStringAsync());
        (await JsonAsync(requested)).GetProperty("slip").GetProperty("cancelPending").GetBoolean().Should().BeTrue();
        (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Issued);
        fixture.JobsFor(slipId).Should().Contain(j => j.Type == typeof(CancelBillingSlipJob));
        (await s.Owner.PostAsJsonAsync($"{slips}/{slipId}/cancel", new { reason = "CustomerRequest" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await fixture.CancelJobAsync(slipId);
        var cancelled = await SlipAsync(slipId);
        cancelled.Status.Should().Be(BillingSlipStatus.Cancelled);
        cancelled.CancelReason.Should().Be(BillingSlipCancelReason.CustomerRequest);
        cancelled.CancelledBy.Should().Be(s.OwnerId);
        fixture.Inter.Billing.ByReference(cancelled.OurReference)!.CancelReason.Should().Be("APEDIDODOCLIENTE");
        var receivable = await ReceivableAsync(s.ReceivableId);
        receivable.BillingSlipId.Should().BeNull();
        receivable.Status.Should().Be(BemAqui.Finance.Contracts.ReceivableStatus.Open, "o título não é cancelado");

        // Pode receber outro boleto; o histórico preserva o anterior.
        var again = await IssueAsync(s.Owner, s.ReceivableId, s.Account, receivable.Version);
        again.StatusCode.Should().Be(HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
        (await JsonAsync(await s.Owner.GetAsync(slips))).GetArrayLength().Should().Be(2);

        // Recusa do banco: o boleto continua emitido, com o motivo.
        var otherId = await fixture.IssuedAsync(await fixture.ScenarioAsync(100m));
        var other = await SlipAsync(otherId);
        fixture.Inter.Billing.RejectCancel = true;
        (await fixture.QueryAsync(db => db.BillingSlips.Where(x => x.Id == otherId).ExecuteUpdateAsync(u => u.SetProperty(x => x.CancelRequestedAt, DateTimeOffset.UtcNow).SetProperty(x => x.CancelReason, BillingSlipCancelReason.IssueError))))
            .Should().Be(1);
        await fixture.CancelJobAsync(otherId);
        var refused = await SlipAsync(otherId);
        refused.Status.Should().Be(BillingSlipStatus.Issued);
        refused.CancelRequestedAt.Should().BeNull();
        refused.CancelFailureReason.Should().NotBeNullOrEmpty();
        other.ReceivableId.Should().NotBe(s.ReceivableId);
    }

    [Fact]
    public async Task Mudanca_no_titulo_cancela_o_boleto_e_a_baixa_manual_pede_confirmacao()
    {
        // CA 17: vencimento alterado (D14) → cancelamento automático no Inter e o aviso a quem tem finance.edit.
        var s = await fixture.ScenarioAsync(450m);
        var slipId = await fixture.IssuedAsync(s);
        var current = await ReceivableAsync(s.ReceivableId);
        var due = await s.Owner.PostAsJsonAsync($"/api/v1/finance/receivables/{s.ReceivableId}/due-date", new { dueDate = Iso(Today.AddDays(20)), reason = "Pedido do cliente", version = current.Version });
        due.StatusCode.Should().Be(HttpStatusCode.OK, await due.Content.ReadAsStringAsync());
        var pending = await SlipAsync(slipId);
        pending.CancelReason.Should().Be(BillingSlipCancelReason.DueDateChanged);
        pending.CancelRequestedAt.Should().NotBeNull();
        fixture.JobsFor(slipId).Should().Contain(j => j.Type == typeof(CancelBillingSlipJob));
        await fixture.CancelJobAsync(slipId);
        (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Cancelled);
        var number = current.Number;
        (await fixture.QueryAsync(db => db.Notifications.AnyAsync(n => n.Title == $"O boleto do título {number} foi cancelado porque o vencimento do título mudou. Emita outro se precisar.")))
            .Should().BeTrue();
        fixture.Inter.Billing.ByReference(pending.OurReference)!.CancelReason.Should().Be("SUBSTITUICAO");

        // Baixa manual de título com boleto aberto: sem confirmar, 409 com a pergunta; confirmando, baixa e pede o cancelamento.
        var t = await fixture.ScenarioAsync(300m);
        var openSlip = await fixture.IssuedAsync(t);
        var receivable = await ReceivableAsync(t.ReceivableId);
        object Settle(bool confirm) => new { bankAccountId = t.Account, date = Iso(Today), principal = 300m, interest = 0m, fine = 0m, discount = 0m, backdateReason = (string?)null, version = receivable.Version, cancelBillingSlip = confirm };
        var ask = await t.Owner.PostAsJsonAsync($"/api/v1/finance/receivables/{t.ReceivableId}/settlements", Settle(false));
        ask.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(ask);
        Code(problem).Should().Be(BillingSlipReactions.OpenCode);
        problem.GetProperty("detail").GetString().Should().Be(BillingSlipReactions.OpenMessage);
        (await ReceivableAsync(t.ReceivableId)).PaidAmount.Should().Be(0m);
        var settled = await t.Owner.PostAsJsonAsync($"/api/v1/finance/receivables/{t.ReceivableId}/settlements", Settle(true));
        settled.StatusCode.Should().Be(HttpStatusCode.OK, await settled.Content.ReadAsStringAsync());
        (await SlipAsync(openSlip)).CancelReason.Should().Be(BillingSlipCancelReason.SettledOtherMeans);

        // Título cancelado também cancela o boleto.
        var c = await fixture.ScenarioAsync(200m);
        var cSlip = await fixture.IssuedAsync(c);
        var cr = await ReceivableAsync(c.ReceivableId);
        (await c.Owner.PostAsJsonAsync($"/api/v1/finance/receivables/{c.ReceivableId}/cancel", new { reason = "Venda desfeita", version = cr.Version })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SlipAsync(cSlip)).CancelReason.Should().Be(BillingSlipCancelReason.ReceivableCancelled);
    }

    [Fact]
    public async Task Boleto_expirado_no_banco_libera_o_titulo_vencido()
    {
        var s = await fixture.ScenarioAsync(250m);
        var slipId = await fixture.IssuedAsync(s);
        var slip = await SlipAsync(slipId);

        // CA 18: a reconsulta (BNK-02) vê "Expirado": o boleto fica Expired e o título continua em aberto.
        fixture.Inter.Billing.ByReference(slip.OurReference)!.Situation = "EXPIRADO";
        await fixture.ProcessAsync(p => p.RefreshAsync(slipId));
        (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Expired);
        var receivable = await ReceivableAsync(s.ReceivableId);
        receivable.Status.Should().Be(BemAqui.Finance.Contracts.ReceivableStatus.Open);
        receivable.BillingSlipId.Should().BeNull();
        (await JsonAsync(await s.Owner.GetAsync(Slips(s.ReceivableId) + "/draft"))).GetProperty("preconditions").EnumerateArray()
            .Where(p => p.GetProperty("blocking").GetBoolean()).Should().BeEmpty("pode emitir um novo boleto");
    }

    [Fact]
    public async Task Lote_com_impacto_em_lista()
    {
        var s = await fixture.ScenarioAsync(100m);
        var second = await fixture.ReceivableAsync(s.Owner, s.Party, 200m);
        var noAddress = await CustomerAsync(s.Owner, address: false);
        var refusedReceivable = await fixture.ReceivableAsync(s.Owner, noAddress, 300m);
        var ids = new[] { s.ReceivableId, second.GetProperty("id").GetGuid(), refusedReceivable.GetProperty("id").GetGuid() };

        // CA 19: elegíveis (título, cliente, valor, vencimento, e-mail), recusados com o motivo e o total.
        var preview = await JsonAsync(await s.Owner.PostAsJsonAsync("/api/v1/finance/receivables/billing-slips/batch-preview", new { receivableIds = ids }));
        preview.GetProperty("eligible").GetArrayLength().Should().Be(2);
        preview.GetProperty("total").GetDecimal().Should().Be(300m);
        var refusal = preview.GetProperty("refused").EnumerateArray().Single();
        refusal.GetProperty("receivableId").GetGuid().Should().Be(ids[2]);
        refusal.GetProperty("reason").GetString().Should().Contain("endereço de cobrança");

        var items = ids.Select(id => new { receivableId = id, clientRequestId = Guid.NewGuid() }).ToArray();
        var result = await JsonAsync(await s.Owner.PostAsJsonAsync("/api/v1/finance/receivables/billing-slips/batch", new { items, sendEmail = false }));
        result.GetProperty("requested").GetInt32().Should().Be(2);
        result.GetProperty("message").GetString().Should().Be("2 boletos solicitados");
        result.GetProperty("refused").GetArrayLength().Should().Be(1);

        // Cada um com o seu Idempotency-Key: repetir o lote não duplica.
        var replay = await JsonAsync(await s.Owner.PostAsJsonAsync("/api/v1/finance/receivables/billing-slips/batch", new { items, sendEmail = false }));
        replay.GetProperty("requested").GetInt32().Should().Be(2);
        (await fixture.QueryAsync(db => db.BillingSlips.CountAsync(x => ids.Contains(x.ReceivableId)))).Should().Be(2);
        (await s.Owner.PostAsJsonAsync("/api/v1/finance/receivables/billing-slips/batch-preview", new { receivableIds = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()) }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Enviar_ao_cliente_e_o_limite_diario()
    {
        var s = await fixture.ScenarioAsync(150m);
        var slipId = await fixture.IssuedAsync(s, sendEmail: false);
        var slips = Slips(s.ReceivableId);
        (await s.Owner.PostAsJsonAsync($"{slips}/{slipId}/send", new { to = "Nome <x@y.com>", cc = Array.Empty<string>() })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await s.Owner.PostAsJsonAsync($"{slips}/{slipId}/send", new { to = (string?)null, cc = new[] { "a@a.com", "b@b.com", "c@c.com", "d@d.com" } })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // CA 13: "Enviar ao cliente" para o contato de cobrança, com cópia e o PDF.
        var sent = await s.Owner.PostAsJsonAsync($"{slips}/{slipId}/send", new { to = (string?)null, cc = new[] { "copia@cliente-boleto.com.br" } });
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        (await JsonAsync(sent)).GetProperty("message").GetString().Should().Be("Boleto enviado");
        var log = await fixture.QueryAsync(db => db.EmailLogs.AsNoTracking().SingleAsync(l => l.RelatedId == slipId));
        log.Cc.Should().Be("copia@cliente-boleto.com.br");
        log.RequestedBy.Should().Be(s.OwnerId);
        var view = await JsonAsync(await s.Owner.GetAsync($"{slips}/{slipId}"));
        view.GetProperty("emails").EnumerateArray().Single().GetProperty("to").GetString().Should().Be("c***@cliente-boleto.com.br");

        // Acima do limite (somando orçamentos, pedidos e boletos), o envio fica para amanhã às 08:00.
        var (_, owner) = await fixture.ActorAsync(owner: true);
        (await owner.PutAsJsonAsync(Settings, SettingsBody(limit: 1))).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            var later = await s.Owner.PostAsJsonAsync($"{slips}/{slipId}/send", new { to = (string?)null, cc = Array.Empty<string>() });
            later.StatusCode.Should().Be(HttpStatusCode.OK);
            (await JsonAsync(later)).GetProperty("message").GetString().Should().Be(BillingSlipMailer.LimitMessage);
            var scheduled = await SlipAsync(slipId);
            scheduled.ScheduledEmailAt.Should().Be(BillingSlipMailer.NextMorning(TimeProvider.System));
            fixture.JobsFor(slipId).Should().Contain(j => j.Type == typeof(SendBillingSlipEmailJob) && j.State == "Scheduled");
            (await fixture.QueryAsync(db => db.EmailLogs.CountAsync(l => l.RelatedId == slipId))).Should().Be(1);

            await fixture.ProcessAsync(p => p.SendScheduledEmailAsync(slipId));
            (await fixture.QueryAsync(db => db.EmailLogs.CountAsync(l => l.RelatedId == slipId && l.Template == EmailDispatchLimiter.BillingSlipTemplate))).Should().Be(2);
            (await SlipAsync(slipId)).ScheduledEmailAt.Should().BeNull();
        }
        finally
        {
            (await owner.PutAsJsonAsync(Settings, SettingsBody())).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Tres_camadas_IDOR_e_auditoria()
    {
        var s = await fixture.ScenarioAsync(120m);
        var slipId = await fixture.IssuedAsync(s);
        var other = await fixture.ScenarioAsync(130m);
        var slips = Slips(s.ReceivableId);

        // CA 21: sem grupo, 403 forbidden; só ver: lê, mas não emite, envia nem cancela.
        var (_, nobody) = await fixture.ActorAsync(false);
        var forbidden = await nobody.GetAsync(slips);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(forbidden)).Should().Be("forbidden");
        var (_, viewer) = await fixture.ActorAsync(false, "finance.view");
        (await viewer.GetAsync($"{slips}/{slipId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await IssueAsync(viewer, other.ReceivableId, other.Account, other.Version)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"{slips}/{slipId}/send", new { to = (string?)null })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"{slips}/{slipId}/cancel", new { reason = "CustomerRequest" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsync($"{slips}/{slipId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // IDOR: o boleto pelo título errado é 404.
        (await s.Owner.GetAsync($"{Slips(other.ReceivableId)}/{slipId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.Owner.PostAsJsonAsync($"{Slips(other.ReceivableId)}/{slipId}/cancel", new { reason = "CustomerRequest" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Módulo finance não liberado: 403 module_not_released, também em Configurações → Cobrança.
        await fixture.SetModuleAsync("finance", false);
        try
        {
            var locked = await s.Owner.GetAsync(slips);
            locked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(locked)).Should().Be("module_not_released");
            Code(await JsonAsync(await s.Owner.GetAsync(Settings))).Should().Be("module_not_released");
        }
        finally
        {
            await fixture.SetModuleAsync("finance", true);
        }

        // CA 23: o boleto tem histórico (pedido, emissão) na auditoria; o título também registra o vínculo.
        (await fixture.QueryAsync(db => db.AuditEntries.CountAsync(e => e.Entity == nameof(BillingSlip) && e.EntityId == slipId.ToString()))).Should().BeGreaterThanOrEqualTo(2);
        (await fixture.QueryAsync(db => db.AuditEntries.AnyAsync(e => e.Entity == nameof(Receivable) && e.EntityId == s.ReceivableId.ToString() && e.Changes.Contains(slipId.ToString())))).Should().BeTrue();
    }
}
