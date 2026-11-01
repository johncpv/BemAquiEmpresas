using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.Host.Persistence;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-03b com o <see cref="FakeFiscalEmitter"/> e banco real: cancelar com <c>fiscal.approve</c> e o 2FA de novo, com o evento no outbox, a
/// DANFE com a marca, o XML do evento, os avisos e o e-mail, sem estoque, e a nota guardada (CA 1, 5, 6, 7 e 13); 403 para quem emitiu,
/// "Pedir cancelamento", pauta, cancelar pelo pedido, recusar e expirar (CA 2 e 3); fora do prazo (CA 4); recusa da SEFAZ e confirmação pelo
/// webhook (CA 1); reverso <c>InvoiceTaxReversal</c> idempotente (CA 5); carta de correção e as 20 (CA 8); inutilização com 2FA e recusa
/// local (CA 10 e 11); lembrete com execução dupla e dois tenants (CA 12); concorrência (CA 15); IDOR e auditoria (CA 14 e 16). O fake e o
/// tenant são da classe: contagens relativas ao início de cada teste.
/// </summary>
public partial class FiscalTests
{
    private const string CancelReason = "Cliente desistiu da compra do equipamento";
    private const string NumberGaps = "/api/v1/fiscal/number-gaps";

    /// <summary>Nota autorizada pela tela (rascunho do romaneio, salvo e emitido no fake).</summary>
    private async Task<(HttpClient Owner, Sale Sale, JsonElement Invoice)> AuthorizedAsync()
    {
        await ReadyToIssueAsync();
        var (owner, sale, draft) = await DraftAsync();
        var issued = await Ok(await IssueInvoiceAsync(owner, draft));
        issued.GetProperty("status").GetString().Should().Be("Authorized");
        return (owner, sale, issued.GetProperty("invoice"));
    }

    /// <summary>Proprietário com a prova recente do 2FA no token (D6).</summary>
    private async Task<HttpClient> ProvenOwnerAsync() => (await fixture.ProvenActorAsync(fixture.Tenancy.TenantA, FiscalFixture.HostA, true, DateTimeOffset.UtcNow)).Client;

    private static object CancelBody(JsonElement invoice, string reason = CancelReason, Guid? requestId = null) =>
        new { reason, requestId, notifyCustomer = true, version = invoice.GetProperty("version").GetUInt32() };

    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();

    private static async Task<JsonElement> ReloadAsync(HttpClient client, Guid id) => await Ok(await client.GetAsync($"{Invoices}/{id}"));

    // ---------- CA 1, 5, 6, 7, 13 e 16 ----------

    [Fact]
    public async Task Cancelar_com_approve_e_2FA_cancela_guarda_tudo_publica_o_evento_avisa_e_nao_mexe_no_estoque()
    {
        var tenant = fixture.Tenancy.TenantA;
        var fake = fixture.Fake(tenant);
        var (owner, sale, invoice) = await AuthorizedAsync();
        var id = IdOf(invoice);
        var movements = await fixture.QueryAsync(tenant, db => db.Set<BemAqui.Inventory.Domain.StockMovement>().CountAsync());
        var calls = fake.Count("cancelar");

        var view = invoice.GetProperty("cancellation");
        view.GetProperty("canCancel").GetBoolean().Should().BeTrue("o Proprietário tem fiscal.approve e está no prazo");
        view.GetProperty("deadline").GetDateTimeOffset().Should().BeCloseTo(invoice.GetProperty("authorizedAt").GetDateTimeOffset().AddHours(24), TimeSpan.FromSeconds(1));

        // D6: sem a prova recente do 2FA, 403 two_factor_required e nada vai ao Emissor.
        var noProof = await owner.PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(invoice));
        noProof.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(noProof)).GetProperty("code").GetString().Should().Be("two_factor_required");
        var proven = await ProvenOwnerAsync();
        var shortReason = await proven.PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(invoice, "curto"));
        shortReason.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(shortReason)).GetProperty("errors").GetProperty("reason")[0].GetString().Should().Be("Escreva o motivo com 15 a 255 caracteres.");
        fake.Count("cancelar").Should().Be(calls);

        var cancelled = await Ok(await proven.PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(invoice)));

        cancelled.GetProperty("status").GetString().Should().Be("Registered");
        cancelled.GetProperty("message").GetString().Should().Be(OutgoingInvoiceService.CancelledMessage);
        cancelled.GetProperty("invoice").GetProperty("status").GetString().Should().Be("Cancelled");
        fake.Justifications.Should().Contain(CancelReason);
        var after = await fixture.InvoiceOfAsync(tenant, id);
        (after.Status, after.CancellationReason, after.CancellationSentAt).Should().Be((OutgoingInvoiceStatus.Cancelled, CancelReason, (DateTimeOffset?)null));
        after.CancellationProtocol.Should().NotBeNullOrEmpty();
        after.CancelledOn.Should().NotBeNull();
        after.XmlAttachmentId.Should().NotBeNull("o XML autorizado continua guardado (CA 13)");
        after.DanfeAttachmentId.Should().NotBeNull("o DANFE original continua guardado");
        after.CancelledDanfeAttachmentId.Should().NotBeNull().And.NotBe(after.DanfeAttachmentId!.Value, "o DANFE com a marca fica ao lado do original (CA 6)");
        var events = await fixture.FiscalAsync(tenant, db => db.FiscalEvents.AsNoTracking().Where(e => e.InvoiceId == id).ToListAsync());
        var cancel = events.Should().ContainSingle().Subject;
        (cancel.Type, cancel.Status, cancel.Sequence, cancel.Text).Should().Be((FiscalEventType.Cancel, FiscalEventStatus.Registered, 1, CancelReason));
        cancel.Protocol.Should().Be(after.CancellationProtocol);
        cancel.XmlAttachmentId.Should().NotBeNull("o XML do evento é guardado como anexo de sistema");

        var outbox = await fixture.QueryAsync(tenant, db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains(nameof(InvoiceCancelled))).Select(m => m.Payload).ToListAsync());
        var published = outbox.Should().ContainSingle(p => p.Contains(id.ToString())).Subject;
        published.Should().NotContain(RecipientCanary).And.NotContain(CancelReason, "o evento leva só ids, datas e valores");
        (await fixture.QueryAsync(tenant, db => db.Notifications.AsNoTracking().Where(n => n.Type == OutgoingInvoiceEffects.CancelledType && n.Link == OutgoingInvoiceEffects.Link(id)).ToListAsync()))
            .Should().NotBeEmpty().And.OnlyContain(n => n.Title.StartsWith("NF-e ", StringComparison.Ordinal) && n.Title.EndsWith(": " + CancelReason, StringComparison.Ordinal));
        var mail = await fixture.QueryAsync(tenant, db => db.EmailLogs.AsNoTracking().SingleAsync(e => e.RelatedId == id && e.Template == OutgoingInvoiceEffects.CancelledEmailTemplate));
        mail.AttachmentIds.Should().BeEquivalentTo([cancel.XmlAttachmentId!.Value, after.CancelledDanfeAttachmentId!.Value]);
        (await fixture.QueryAsync(tenant, db => db.Set<BemAqui.Inventory.Domain.StockMovement>().CountAsync())).Should().Be(movements, "cancelar não mexe no estoque (D12)");

        // CA 5 e PRD-06b CA 11: o romaneio continua expedido e volta a poder receber outra nota; o faturamento deixa de contar a nota.
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            (await scope.ServiceProvider.GetRequiredService<IOutgoingInvoices>().ActiveForShipmentAsync(sale.Shipment)).Should().BeNull();
        }

        (await owner.SendAsync(CreateInvoice(sale.Shipment))).StatusCode.Should().Be(HttpStatusCode.Created, "o romaneio pode receber outra nota");

        // CA 13 e 16: eventos, XML do evento e DANFE da cancelada só pela própria nota; nada é apagado; auditoria.
        var list = await Ok(await owner.GetAsync($"{Invoices}/{id}/events"));
        list.EnumerateArray().Should().ContainSingle().Which.GetProperty("typeLabel").GetString().Should().Be("Cancelamento");
        (await owner.GetAsync($"{Invoices}/{id}/events/{cancel.Id}/xml")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync($"{Invoices}/{id}/cancelled-danfe")).StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, _, other) = await DraftAsync();
        (await owner.GetAsync($"{Invoices}/{IdOf(other)}/events/{cancel.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR");
        (await owner.GetAsync($"{Invoices}/{IdOf(other)}/events/{cancel.Id}/xml")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR");
        (await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().AnyAsync(a => a.Entity == nameof(FiscalEvent) && a.EntityId == cancel.Id.ToString()))).Should().BeTrue();
        var again = await proven.PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(await ReloadAsync(owner, id)));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        fake.Count("cancelar").Should().Be(calls + 1);
    }

    // ---------- CA 2 e 3 ----------

    [Fact]
    public async Task Quem_emitiu_sem_approve_recebe_403_pede_o_cancelamento_e_o_Proprietario_cancela_pelo_pedido()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (owner, _, invoice) = await AuthorizedAsync();
        var id = IdOf(invoice);
        var (financeId, finance) = await fixture.ProvenActorAsync(tenant, FiscalFixture.HostA, false, DateTimeOffset.UtcNow, "fiscal.view", "fiscal.create", "fiscal.edit");

        var denied = await finance.PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(invoice));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(denied)).GetProperty("code").GetString().Should().Be("forbidden", "mesmo com o 2FA recente, cancelar é do fiscal.approve (D12)");
        var seen = (await ReloadAsync(finance, id)).GetProperty("cancellation");
        (seen.GetProperty("canCancel").GetBoolean(), seen.GetProperty("canRequest").GetBoolean()).Should().Be((false, true));

        var requested = await Ok(await finance.PostAsJsonAsync($"{Invoices}/{id}/cancellation-requests", new { reason = CancelReason }), HttpStatusCode.Created);
        var requestId = IdOf(requested);
        requested.GetProperty("status").GetString().Should().Be("Pending");
        (await finance.PostAsJsonAsync($"{Invoices}/{id}/cancellation-requests", new { reason = CancelReason })).StatusCode.Should().Be(HttpStatusCode.Conflict, "um pendente por nota");
        (await fixture.InvoiceOfAsync(tenant, id)).Status.Should().Be(OutgoingInvoiceStatus.Authorized, "o pedido não tem efeito na nota");
        var asked = await fixture.QueryAsync(tenant, db => db.Notifications.AsNoTracking().Where(n => n.Type == OutgoingInvoiceService.RequestType && n.Link == OutgoingInvoiceService.RequestLink(id, requestId)).ToListAsync());
        asked.Should().NotBeEmpty().And.OnlyContain(n => n.Title.Contains(CancelReason, StringComparison.Ordinal) && n.Title.Contains("Restam ", StringComparison.Ordinal));
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var agenda = scope.ServiceProvider.GetServices<IAgendaSource>().OfType<FiscalEventAgendaSource>().Single();
            var items = await agenda.ItemsAsync(new AgendaRequest(Guid.Empty, null!, new HashSet<string> { FiscalEventAgendaSource.CancellationRequests }, DateOnly.FromDateTime(DateTime.UtcNow)));
            items.Should().ContainSingle().Which.Entries.Should().Contain(e => e.Link == OutgoingInvoiceService.RequestLink(id, requestId));
        }

        var filtered = await Ok(await owner.GetAsync($"{Invoices}?cancellationRequested=true&pageSize=100"));
        filtered.GetProperty("items").EnumerateArray().Should().Contain(r => IdOf(r) == id);
        var current = await ReloadAsync(owner, id);
        current.GetProperty("cancellation").GetProperty("canDecline").GetBoolean().Should().BeTrue();
        current.GetProperty("cancellation").GetProperty("pendingRequest").GetProperty("reason").GetString().Should().Be(CancelReason);

        var proven = await ProvenOwnerAsync();
        await Ok(await proven.PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(current, CancelReason + " (pedido do Financeiro)", requestId)));

        var request = await fixture.FiscalAsync(tenant, db => db.CancellationRequests.AsNoTracking().SingleAsync(r => r.Id == requestId));
        request.Status.Should().Be(CancellationRequestStatus.Done, "ao cancelar, o pedido fica atendido (CA 3)");
        (await fixture.FiscalAsync(tenant, db => db.FiscalEvents.AsNoTracking().SingleAsync(e => e.InvoiceId == id))).CancellationRequestId.Should().Be(requestId);
        (await fixture.QueryAsync(tenant, db => db.Notifications.AnyAsync(n => n.UserId == financeId && n.Type == OutgoingInvoiceEffects.RequestDoneType))).Should().BeTrue();
    }

    [Fact]
    public async Task Recusar_pedido_avisa_quem_pediu_o_pedido_de_outra_nota_da_404_e_o_pendente_expira_no_fim_do_prazo()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (owner, _, invoice) = await AuthorizedAsync();
        var (_, _, other) = await AuthorizedAsync();
        var id = IdOf(invoice);
        var (financeId, finance) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, false, "fiscal.view");
        var requestId = IdOf(await Ok(await finance.PostAsJsonAsync($"{Invoices}/{id}/cancellation-requests", new { reason = CancelReason }), HttpStatusCode.Created));

        (await finance.PostAsJsonAsync($"{Invoices}/{id}/cancellation-requests/{requestId}/decline", new { reason = "A nota está correta, mantenha" })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "recusar é do fiscal.approve");
        (await owner.PostAsJsonAsync($"{Invoices}/{IdOf(other)}/cancellation-requests/{requestId}/decline", new { reason = "A nota está correta, mantenha" })).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "IDOR: o pedido é de outra nota");
        var declined = await Ok(await owner.PostAsJsonAsync($"{Invoices}/{id}/cancellation-requests/{requestId}/decline", new { reason = "A nota está correta, mantenha" }));
        declined.GetProperty("status").GetString().Should().Be("Declined");
        (await fixture.QueryAsync(tenant, db => db.Notifications.AnyAsync(n => n.UserId == financeId && n.Type == OutgoingInvoiceService.RequestDeclinedType
            && n.Title.EndsWith("A nota está correta, mantenha")))).Should().BeTrue();

        var second = IdOf(await Ok(await finance.PostAsJsonAsync($"{Invoices}/{id}/cancellation-requests", new { reason = CancelReason }), HttpStatusCode.Created));
        await fixture.QueryAsync(tenant, db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fiscal.cancellation_requests SET expires_at = now() - interval '1 minute' WHERE id = {second}"));
        var expired = await fixture.WithAsync<int, CancellationRequestExpiry>(tenant, e => e.CheckAsync());
        var twice = await fixture.WithAsync<int, CancellationRequestExpiry>(tenant, e => e.CheckAsync());

        expired.Should().BeGreaterThanOrEqualTo(1);
        twice.Should().Be(0, "execução dupla não repete");
        (await fixture.FiscalAsync(tenant, db => db.CancellationRequests.AsNoTracking().SingleAsync(r => r.Id == second))).Status.Should().Be(CancellationRequestStatus.Expired);
        (await fixture.QueryAsync(tenant, db => db.Notifications.CountAsync(n => n.UserId == financeId && n.Type == CancellationRequestExpiry.NotificationType))).Should().Be(1);
    }

    // ---------- CA 4 ----------

    [Fact]
    public async Task Fora_do_prazo_nao_cancela_nem_pede_e_o_servidor_confere_o_prazo_no_envio()
    {
        var tenant = fixture.Tenancy.TenantA;
        var fake = fixture.Fake(tenant);
        var (owner, _, invoice) = await AuthorizedAsync();
        var id = IdOf(invoice);
        var calls = fake.Count("cancelar");
        await fixture.QueryAsync(tenant, db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE fiscal.outgoing_invoices SET authorized_at = authorized_at - interval '25 hours' WHERE id = {id}"));

        var reloaded = await ReloadAsync(owner, id);
        var view = reloaded.GetProperty("cancellation");
        (view.GetProperty("withinWindow").GetBoolean(), view.GetProperty("canCancel").GetBoolean(), view.GetProperty("canRequest").GetBoolean()).Should().Be((false, false, false));
        var refused = await (await ProvenOwnerAsync()).PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(reloaded));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "o diálogo aberto antes do fim não vale: o servidor confere de novo");
        var problem = await JsonAsync(refused);
        problem.GetProperty("code").GetString().Should().Be(FiscalErrorCodes.CancellationWindowClosed);
        problem.GetProperty("detail").GetString().Should().Be(FiscalEventRules.OutOfWindow);
        var (_, finance) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, false, "fiscal.view");
        (await finance.PostAsJsonAsync($"{Invoices}/{id}/cancellation-requests", new { reason = CancelReason })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        fake.Count("cancelar").Should().Be(calls);
    }

    // ---------- CA 1: recusa da SEFAZ e confirmação pelo webhook ----------

    [Fact]
    public async Task SEFAZ_recusa_mantem_autorizada_e_sem_resposta_o_webhook_confirma_uma_vez_so()
    {
        var tenant = fixture.Tenancy.TenantA;
        var fake = fixture.Fake(tenant);
        var (owner, _, invoice) = await AuthorizedAsync();
        var id = IdOf(invoice);
        var proven = await ProvenOwnerAsync();
        fake.NextEvent = FakeEvent.Reject;

        var rejected = await proven.PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(invoice));
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(rejected)).GetProperty("code").GetString().Should().Be(FiscalErrorCodes.EventRejected);
        var still = await fixture.InvoiceOfAsync(tenant, id);
        (still.Status, still.CancellationSentAt).Should().Be((OutgoingInvoiceStatus.Authorized, (DateTimeOffset?)null));
        (await fixture.FiscalAsync(tenant, db => db.FiscalEvents.AsNoTracking().SingleAsync(e => e.InvoiceId == id))).Should()
            .Match<FiscalEvent>(e => e.Status == FiscalEventStatus.Rejected && e.RejectionReason!.StartsWith("Rejeicao", StringComparison.Ordinal));

        fake.NextEvent = FakeEvent.DownAfterRegister;
        var sent = await Ok(await proven.PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(await ReloadAsync(owner, id))), HttpStatusCode.Accepted);
        fake.NextEvent = FakeEvent.Accept;
        sent.GetProperty("message").GetString().Should().Be(OutgoingInvoiceService.CancelSentMessage);
        sent.GetProperty("invoice").GetProperty("cancellation").GetProperty("cancellationSentAt").ValueKind.Should().Be(JsonValueKind.String, "\"Cancelamento enviado\"");
        (await fixture.InvoiceOfAsync(tenant, id)).Status.Should().Be(OutgoingInvoiceStatus.Authorized);

        var doc = fake.Document(still.AccessKey!)!;
        for (var i = 0; i < 2; i++)
        {
            var (_, delivery) = await fixture.PostWebhookAsync(tenant, fake.WebhookBody(doc, "nfe.cancelada"), "nfe.cancelada");
            await fixture.ProcessWebhookAsync(tenant, delivery);
        }

        var after = await fixture.InvoiceOfAsync(tenant, id);
        after.Status.Should().Be(OutgoingInvoiceStatus.Cancelled);
        after.CancellationProtocol.Should().NotBeNullOrEmpty("o protocolo vem do evento homologado na reconsulta");
        var events = await fixture.FiscalAsync(tenant, db => db.FiscalEvents.AsNoTracking().Where(e => e.InvoiceId == id).OrderBy(e => e.Sequence).ToListAsync());
        events.Select(e => e.Status).Should().Equal(FiscalEventStatus.Rejected, FiscalEventStatus.Registered);
        (await fixture.QueryAsync(tenant, db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains(nameof(InvoiceCancelled))).Select(m => m.Payload).ToListAsync())).Count(p => p.Contains(id.ToString(), StringComparison.Ordinal))
            .Should().Be(1, "webhook repetido não publica de novo");
    }

    // ---------- CA 5 ----------

    [Fact]
    public async Task Reverso_do_imposto_InvoiceTaxReversal_e_idempotente_e_so_da_nota_que_entrou_no_custo()
    {
        await ReadyToIssueAsync();
        var sale = await SaleAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var invoiceId = Guid.CreateVersion7();
        var late = Guid.CreateVersion7();
        var homologation = Guid.CreateVersion7();
        var authorized = new InvoiceAuthorized(invoiceId, InvoiceType.NFe, sale.Project, sale.Shipment, sale.Party, today.AddDays(-1), 105_000m, [new InvoiceTaxAmount("ICMS", 18_000m, true)], 22_650m, true);
        await using (var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA))
        {
            await ActivatorUtilities.CreateInstance<InvoiceTaxConsumer>(scope.ServiceProvider).HandleAsync(authorized, CancellationToken.None);
            var reversal = ActivatorUtilities.CreateInstance<InvoiceTaxReversalConsumer>(scope.ServiceProvider);
            var cancelled = new InvoiceCancelled(invoiceId, InvoiceType.NFe, sale.Project, sale.Shipment, today, 22_650m, 105_000m, true);
            await reversal.HandleAsync(cancelled, CancellationToken.None);
            await reversal.HandleAsync(cancelled, CancellationToken.None);
            await reversal.HandleAsync(cancelled with { InvoiceId = homologation, ProjectTaxApplies = false }, CancellationToken.None);
            // Fora de ordem: o cancelamento chega antes da autorização e a soma fecha em zero do mesmo jeito.
            await reversal.HandleAsync(cancelled with { InvoiceId = late, CostTaxAmount = 1_000m, TotalAmount = 5_000m }, CancellationToken.None);
            await ActivatorUtilities.CreateInstance<InvoiceTaxConsumer>(scope.ServiceProvider)
                .HandleAsync(authorized with { InvoiceId = late, CostTaxAmount = 1_000m, TotalAmount = 5_000m }, CancellationToken.None);
        }

        var lines = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.Set<ProjectCost>().AsNoTracking().Where(c => c.ProjectId == sale.Project).ToListAsync());
        var reverse = lines.Should().ContainSingle(l => l.SourceId == invoiceId && l.Source == ProjectCostSource.InvoiceTaxReversal, "idempotente por (InvoiceTaxReversal, nota, seq)").Subject;
        (reverse.CostType, reverse.Seq, reverse.Amount, reverse.InvoiceAmount, reverse.OccurredOn).Should().Be((CostType.Tax, 1, -22_650m, (decimal?)-105_000m, today));
        lines.Where(l => l.SourceId == invoiceId).Sum(l => l.Amount).Should().Be(0m, "a linha original fica; o reverso a anula");
        lines.Should().NotContain(l => l.SourceId == homologation, "nota que não entrou no custo não tem reverso");
        lines.Where(l => l.SourceId == late).Sum(l => l.Amount).Should().Be(0m);
    }

    // ---------- CA 8 e 9 ----------

    [Fact]
    public async Task Carta_de_correcao_com_edit_registra_com_sequencia_guarda_o_XML_envia_o_email_e_para_na_vigesima()
    {
        var tenant = fixture.Tenancy.TenantA;
        var fake = fixture.Fake(tenant);
        var (_, _, invoice) = await AuthorizedAsync();
        var id = IdOf(invoice);
        var (_, viewer) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, false, "fiscal.view");
        var (_, editor) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, false, "fiscal.view", "fiscal.edit");
        const string Text = "Corrige o volume transportado: 3 volumes de madeira em vez de 2.";
        object Body(JsonElement v, string text = Text) => new { text, notifyCustomer = true, version = v.GetProperty("version").GetUInt32() };

        (await viewer.PostAsJsonAsync($"{Invoices}/{id}/corrections", Body(invoice))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "carta é do fiscal.edit");
        var tooShort = await editor.PostAsJsonAsync($"{Invoices}/{id}/corrections", Body(invoice, "Curto"));
        tooShort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(tooShort)).GetProperty("errors").GetProperty("text")[0].GetString().Should().Be("Escreva a correção com 15 a 1000 caracteres.");

        var first = await Ok(await editor.PostAsJsonAsync($"{Invoices}/{id}/corrections", Body(invoice)));

        first.GetProperty("message").GetString().Should().Be(OutgoingInvoiceService.CorrectionMessage);
        var evt = await fixture.FiscalAsync(tenant, db => db.FiscalEvents.AsNoTracking().SingleAsync(e => e.InvoiceId == id && e.Type == FiscalEventType.Correction));
        (evt.Sequence, evt.Status, evt.Text).Should().Be((1, FiscalEventStatus.Registered, Text));
        evt.Protocol.Should().NotBeNullOrEmpty();
        evt.XmlAttachmentId.Should().NotBeNull();
        var mail = await fixture.QueryAsync(tenant, db => db.EmailLogs.AsNoTracking().SingleAsync(e => e.RelatedId == id && e.Template == OutgoingInvoiceEffects.CorrectionEmailTemplate));
        mail.AttachmentIds.Should().Equal(evt.XmlAttachmentId!.Value);
        (await fixture.InvoiceOfAsync(tenant, id)).Status.Should().Be(OutgoingInvoiceStatus.Authorized, "a carta não muda a nota");

        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            for (var seq = 2; seq <= 19; seq++)
            {
                var old = new FiscalEvent { InvoiceId = id, Type = FiscalEventType.Correction, Sequence = seq, Text = Text, RequestedAt = DateTimeOffset.UtcNow };
                old.Register("1352600000" + seq.ToString("00000", System.Globalization.CultureInfo.InvariantCulture), Guid.CreateVersion7(), DateTimeOffset.UtcNow);
                db.FiscalEvents.Add(old);
            }

            await db.SaveChangesAsync();
        }

        var twentieth = await Ok(await editor.PostAsJsonAsync($"{Invoices}/{id}/corrections", Body(await ReloadAsync(editor, id))));
        twentieth.GetProperty("invoice").GetProperty("cancellation").GetProperty("correctionsLeft").GetInt32().Should().Be(0);
        var calls = fake.Count("carta_correcao");
        var refused = await editor.PostAsJsonAsync($"{Invoices}/{id}/corrections", Body(await ReloadAsync(editor, id)));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(refused)).GetProperty("detail").GetString().Should().Be(FiscalEventRules.TooManyCorrections);
        fake.Count("carta_correcao").Should().Be(calls, "a 21ª nem vai ao Emissor");
    }

    // ---------- CA 15 ----------

    [Fact]
    public async Task Dois_cancelamentos_ou_carta_e_cancelamento_juntos_so_um_vai_ao_Emissor()
    {
        var tenant = fixture.Tenancy.TenantA;
        var fake = fixture.Fake(tenant);
        var proven = await ProvenOwnerAsync();
        var (_, _, invoice) = await AuthorizedAsync();
        var id = IdOf(invoice);
        var before = fake.Count("cancelar");

        var results = await Task.WhenAll(proven.PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(invoice)), proven.PostAsJsonAsync($"{Invoices}/{id}/cancel", CancelBody(invoice)));

        results.Count(r => r.IsSuccessStatusCode).Should().Be(1, string.Join(" | ", await Task.WhenAll(results.Select(r => r.Content.ReadAsStringAsync()))));
        var loser = results.Single(r => !r.IsSuccessStatusCode);
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(loser)).GetProperty("code").GetString().Should().Be("concurrency_conflict", "o diálogo oferece \"Recarregar\"");
        fake.Count("cancelar").Should().Be(before + 1);

        var (_, _, second) = await AuthorizedAsync();
        var mixed = await Task.WhenAll(
            proven.PostAsJsonAsync($"{Invoices}/{IdOf(second)}/corrections", new { text = "Corrige o peso bruto para 851 kg na nota.", notifyCustomer = false, version = second.GetProperty("version").GetUInt32() }),
            proven.PostAsJsonAsync($"{Invoices}/{IdOf(second)}/cancel", CancelBody(second)));
        mixed.Count(r => r.IsSuccessStatusCode).Should().Be(1, string.Join(" | ", await Task.WhenAll(mixed.Select(r => r.Content.ReadAsStringAsync()))));
        mixed.Should().Contain(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    // ---------- CA 10, 11 e 12 ----------

    [Fact]
    public async Task Inutilizar_faixa_com_2FA_recusa_numero_com_nota_e_o_lembrete_avisa_uma_vez_por_semana_por_tenant()
    {
        var tenant = fixture.Tenancy.TenantA;
        var fake = fixture.Fake(tenant);
        await ReadyToIssueAsync();
        var series = Random.Shared.Next(500, 999);
        var now = DateTimeOffset.UtcNow;
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            foreach (var (n, days) in new[] { (10L, 1), (11L, 2), (12L, 3), (20L, 40) })
            {
                db.NumberGaps.Add(new NumberGap { Series = series, Number = n, InvoiceId = Guid.CreateVersion7(), Attempt = 1, Reason = NumberGapReasons.Rejected, CreatedAt = now.AddDays(-days) });
            }

            var used = OutgoingInvoice.NewDraft(Guid.CreateVersion7(), "ROM-INUT", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, now);
            used.BeginSubmission(new string('c', 64), now);
            used.Apply(new EmitterNfeState(Guid.CreateVersion7(), FiscalDocumentStatus.Authorized, Random.Shared.NextInt64(1, long.MaxValue / 2).ToString("D44", System.Globalization.CultureInfo.InvariantCulture)[..44],
                series, 21, "1", now, null, null, null), now);
            used.AttachAuthorizedXml(Guid.CreateVersion7(), new string('b', 64));
            used.AttachDanfe(Guid.CreateVersion7());
            used.MarkAuthorizationPublished(DateOnly.FromDateTime(now.UtcDateTime), now);
            db.OutgoingInvoices.Add(used);
            await db.SaveChangesAsync();
        }

        var (_, viewer) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, false, "fiscal.view");
        var list = await Ok(await viewer.GetAsync(NumberGaps));
        list.GetProperty("canInvalidate").GetBoolean().Should().BeFalse();
        var mine = list.GetProperty("ranges").EnumerateArray().Where(r => r.GetProperty("series").GetInt32() == series).ToList();
        mine.Select(r => r.GetProperty("label").GetString()).Should().Equal("10 a 12", "20");
        mine[1].GetProperty("old").GetBoolean().Should().BeTrue();
        object Body(long first, long last, string justification = "Números gastos por rejeição de schema na emissão") => new { series, firstNumber = first, lastNumber = last, justification };

        (await viewer.PostAsJsonAsync($"{NumberGaps}/invalidate", Body(10, 12))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "inutilizar é do fiscal.approve");
        var (_, owner) = await OwnerAsync();
        var noProof = await owner.PostAsJsonAsync($"{NumberGaps}/invalidate", Body(10, 12));
        (await JsonAsync(noProof)).GetProperty("code").GetString().Should().Be("two_factor_required", "D6");
        var proven = await ProvenOwnerAsync();
        var calls = fake.Count("inutilizar");
        var withInvoice = await proven.PostAsJsonAsync($"{NumberGaps}/invalidate", Body(20, 21));
        withInvoice.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(withInvoice)).GetProperty("detail").GetString().Should().Be("O número 21 tem nota autorizada e não pode ser inutilizado.");
        (await JsonAsync(await proven.PostAsJsonAsync($"{NumberGaps}/invalidate", Body(10, 13)))).GetProperty("detail").GetString().Should().Be("O número 13 não está em Números a inutilizar.");
        (await proven.PostAsJsonAsync($"{NumberGaps}/invalidate", Body(10, 12, "curta"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        fake.Count("inutilizar").Should().Be(calls, "a recusa vem antes do Emissor (CA 11)");

        var done = await Ok(await proven.PostAsJsonAsync($"{NumberGaps}/invalidate", Body(10, 12)));

        done.GetProperty("message").GetString().Should().Be(NumberGapService.InvalidatedMessage);
        done.GetProperty("protocol").GetString().Should().NotBeNullOrEmpty();
        fake.Invalidations.Last().Should().Match<BemAqui.Fiscal.Application.Emitter.NumberRangeInvalidation>(r => r.Series == series && r.FirstNumber == 10 && r.LastNumber == 12);
        var gaps = await fixture.FiscalAsync(tenant, db => db.NumberGaps.AsNoTracking().Where(g => g.Series == series).OrderBy(g => g.Number).ToListAsync());
        gaps.Take(3).Should().OnlyContain(g => g.Status == NumberGapStatus.Invalidated && g.InvalidationEventId == done.GetProperty("eventId").GetGuid() && g.InvalidatedAt != null);
        gaps[3].Status.Should().Be(NumberGapStatus.Pending);
        (await Ok(await viewer.GetAsync(NumberGaps))).GetProperty("ranges").EnumerateArray().Where(r => r.GetProperty("series").GetInt32() == series)
            .Select(r => r.GetProperty("label").GetString()).Should().Equal("20");

        // CA 12: o lembrete semanal por tenant (job com o tenantId), execução dupla sem repetir; o outro tenant não recebe nada.
        var before = await fixture.QueryAsync(tenant, db => db.Notifications.CountAsync(n => n.Type == FiscalInvalidationReminder.NotificationType));
        var otherBefore = await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.Notifications.CountAsync(n => n.Type == FiscalInvalidationReminder.NotificationType));
        foreach (var t in new[] { tenant, fixture.Tenancy.TenantB, tenant })
        {
            var job = ActivatorUtilities.CreateInstance<FiscalInvalidationReminderJob>(fixture.Factory.Services);
            await job.ExecuteAsync(t.Id, new FiscalInvalidationReminderArgs(), CancellationToken.None);
        }

        var reminded = await fixture.QueryAsync(tenant, db => db.Notifications.AsNoTracking().Where(n => n.Type == FiscalInvalidationReminder.NotificationType).ToListAsync());
        reminded.Count.Should().BeGreaterThan(before);
        reminded.GroupBy(n => n.UserId).Should().OnlyContain(g => g.Count() == 1, "uma vez por semana, mesmo rodando duas vezes");
        reminded.Should().OnlyContain(n => n.Link == NumberGapService.Link);
        (await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.Notifications.CountAsync(n => n.Type == FiscalInvalidationReminder.NotificationType))).Should().Be(otherBefore);
        await using (var scope = await fixture.ScopeAsync(tenant))
        {
            var agenda = scope.ServiceProvider.GetServices<IAgendaSource>().OfType<FiscalEventAgendaSource>().Single();
            var items = await agenda.ItemsAsync(new AgendaRequest(Guid.Empty, null!, new HashSet<string> { FiscalEventAgendaSource.NumbersToInvalidate }, DateOnly.FromDateTime(DateTime.UtcNow)));
            items.Should().ContainSingle().Which.Tier.Should().Be(AgendaTier.Overdue, "há número de mais de 30 dias");
        }
    }
}
