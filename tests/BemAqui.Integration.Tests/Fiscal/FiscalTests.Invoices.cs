using System.Net;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Domain;
using BemAqui.Platform.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-01 CA 3, 5, 6, 11 a 14 e 16 com o <see cref="FakeFiscalEmitter"/>: chave e hash do payload, reenvio com a mesma chave, contingência
/// sem reemissão, webhook assinado só como gatilho da reconsulta, XML guardado com hash e prefixo do tenant, conferência de pendentes pela
/// consulta forçada (webhook perdido, corrida e dois tenants), alertas e canários de log, outbox e auditoria.
/// </summary>
public partial class FiscalTests
{
    /// <summary>Linha da auditoria em que a situação virou Authorized (o diff guarda o valor do enum).</summary>
    private static bool BecameAuthorized(string changes) =>
        System.Text.Json.JsonDocument.Parse(changes).RootElement.EnumerateObject()
            .Any(p => p.Name.Equals("status", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.Object
                && p.Value.TryGetProperty("new", out var v) && (v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetInt32() == (int)OutgoingInvoiceStatus.Authorized : v.GetString() == nameof(OutgoingInvoiceStatus.Authorized)));

    private async Task<(Guid Invoice, string Key, NfeDocument Doc)> PendingAsync(FakeIssue issue = FakeIssue.Delay)
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.ConfigureAsync(tenant);
        fixture.Fake(tenant).Next = issue;
        var id = await fixture.InvoiceAsync(tenant);
        var submitted = await fixture.SubmitAsync(tenant, id);
        submitted.IsSuccess.Should().BeTrue(submitted.Error?.Message);
        var invoice = await fixture.InvoiceOfAsync(tenant, id);
        return (id, invoice.AccessKey!, fixture.Fake(tenant).Document(invoice.AccessKey!)!);
    }

    // ---------- CA 3, 5 e 6 ----------

    [Fact]
    public async Task Envio_grava_o_hash_em_Processing_e_usa_a_chave_nfe_id_tentativa()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, key, _) = await PendingAsync();
        var invoice = await fixture.InvoiceOfAsync(tenant, id);

        invoice.Status.Should().Be(OutgoingInvoiceStatus.Processing);
        invoice.PayloadHash.Should().Be(NfePayload.Hash(FiscalSamples.Sample()));
        invoice.Attempt.Should().Be(1);
        key.Should().HaveLength(44);
        fixture.Fake(tenant).Calls.Should().Contain(c => c.Operation == "emitir" && c.IdempotencyKey == "nfe-" + id.ToString("D") + "-1");
    }

    [Fact]
    public async Task Payload_diferente_com_a_mesma_chave_e_recusado_sem_chamar_o_Emissor()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, _, _) = await PendingAsync();
        var before = fixture.Fake(tenant).Count("emitir");

        var changed = await fixture.SubmitAsync(tenant, id, FiscalSamples.Sample(total: 1001m));

        changed.Error!.Code.Should().Be(FiscalErrorCodes.PayloadChanged);
        changed.Error.Type.Should().Be(BemAqui.SharedKernel.ErrorType.Conflict);
        fixture.Fake(tenant).Count("emitir").Should().Be(before);
    }

    [Fact]
    public async Task Queda_depois_de_criar_fica_Processing_e_o_reenvio_com_a_mesma_chave_devolve_a_nota_existente()
    {
        var tenant = fixture.Tenancy.TenantA;
        await fixture.ConfigureAsync(tenant);
        var fake = fixture.Fake(tenant);
        fake.Next = FakeIssue.CrashAfterCreate;
        var id = await fixture.InvoiceAsync(tenant);

        var first = await fixture.SubmitAsync(tenant, id);
        first.Value.Outcome.Should().Be(SubmissionOutcome.Uncertain);
        first.Value.Status.Should().Be(OutgoingInvoiceStatus.Processing);
        (await fixture.InvoiceOfAsync(tenant, id)).AccessKey.Should().BeNull("o adaptador não sabe se a nota saiu");

        var second = await fixture.SubmitAsync(tenant, id);
        second.Value.IdempotencyKey.Should().Be(first.Value.IdempotencyKey, "nunca chave nova");
        second.Value.Outcome.Should().Be(SubmissionOutcome.Answered);
        var invoice = await fixture.InvoiceOfAsync(tenant, id);
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Authorized);
        invoice.Attempt.Should().Be(1);
        fake.Calls.Where(c => c.Operation == "emitir" && c.IdempotencyKey == first.Value.IdempotencyKey).Should().HaveCount(2);
    }

    [Fact]
    public async Task Contingencia_nunca_dispara_reemissao()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, _, _) = await PendingAsync(FakeIssue.Contingency);
        var before = fixture.Fake(tenant).Count("emitir");

        (await fixture.InvoiceOfAsync(tenant, id)).Status.Should().Be(OutgoingInvoiceStatus.ContingencyPending);
        (await fixture.SubmitAsync(tenant, id)).Error!.Code.Should().Be(FiscalErrorCodes.ContingencyPending);
        fixture.Fake(tenant).Count("emitir").Should().Be(before);
    }

    [Fact]
    public async Task Rejeicao_consome_o_numero_e_a_correcao_e_nova_emissao_com_a_tentativa_seguinte()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, _, _) = await PendingAsync(FakeIssue.Reject);
        var rejected = await fixture.InvoiceOfAsync(tenant, id);
        rejected.Status.Should().Be(OutgoingInvoiceStatus.Rejected);
        rejected.RejectionCode.Should().Be(225);
        rejected.SuggestedAction.Should().NotBeNullOrEmpty();

        fixture.Fake(tenant).Next = FakeIssue.Authorize;
        var corrected = await fixture.SubmitAsync(tenant, id, FiscalSamples.Sample(total: 1001m));

        corrected.Value.IdempotencyKey.Should().Be("nfe-" + id.ToString("D") + "-2");
        corrected.Value.Status.Should().Be(OutgoingInvoiceStatus.Authorized);
    }

    [Fact]
    public async Task Denegacao_e_recusa_antes_de_criar()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (denied, _, _) = await PendingAsync(FakeIssue.Deny);
        (await fixture.InvoiceOfAsync(tenant, denied)).Status.Should().Be(OutgoingInvoiceStatus.Denied);

        fixture.Fake(tenant).Next = FakeIssue.Invalid;
        var id = await fixture.InvoiceAsync(tenant);
        var refused = await fixture.SubmitAsync(tenant, id);
        refused.Value.Outcome.Should().Be(SubmissionOutcome.NotCreated);
        refused.Value.EmitterError!.Fields!.Keys.Should().Contain("itens");
        var draft = await fixture.InvoiceOfAsync(tenant, id);
        draft.Status.Should().Be(OutgoingInvoiceStatus.Draft);
        draft.PayloadHash.Should().BeNull();
    }

    // ---------- CA 11, 13 e 16 ----------

    [Fact]
    public async Task Webhook_assinado_dispara_a_reconsulta_e_a_transicao_vem_dela_com_o_XML_guardado()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, key, doc) = await PendingAsync();
        var fake = fixture.Fake(tenant);
        fake.Settle(key, FiscalDocumentStatus.Authorized);
        // O corpo "mente" (rejeitada): a situação vem da reconsulta, nunca do corpo.
        var body = fake.WebhookBody(doc with { Status = FiscalDocumentStatus.Rejected }, "nfe.autorizada");

        var (response, delivery) = await fixture.PostWebhookAsync(tenant, body, "nfe.autorizada");
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var consults = fake.Count("consultar");
        var evt = await fixture.ProcessWebhookAsync(tenant, delivery);

        evt.Status.Should().Be(WebhookEventStatus.Processed, evt.LastError);
        fake.Count("consultar").Should().Be(consults + 1, "o corpo só dispara a reconsulta");
        var invoice = await fixture.InvoiceOfAsync(tenant, id);
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Authorized);
        invoice.Protocol.Should().NotBeNullOrEmpty();
        invoice.XmlAttachmentId.Should().NotBeNull();
        var xml = await fixture.QueryAsync(tenant, db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == invoice.XmlAttachmentId));
        xml.OwnerType.Should().Be(OutgoingInvoiceReconciler.AttachmentOwner);
        xml.OwnerId.Should().Be(id);
        xml.Origin.Should().Be(AttachmentOrigin.System);
        xml.Sha256.Should().Be(invoice.XmlSha256);
        xml.StoragePath.Should().StartWith(tenant.Id.ToString("N") + "/", "o storage tem o prefixo do tenant");
        await using var stored = await fixture.Factory.Services.GetRequiredService<IFileStorage>().OpenReadAsync(xml.StoragePath);
        Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(stored)).Should().Be(xml.Sha256);
    }

    [Fact]
    public async Task Webhook_de_nota_desconhecida_ou_de_outro_tenant_do_Emissor_fica_Ignored_sem_efeito()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, key, doc) = await PendingAsync();
        var fake = fixture.Fake(tenant);
        fake.Settle(key, FiscalDocumentStatus.Authorized);

        var unknown = fake.WebhookBody(doc with { DocumentId = Guid.CreateVersion7(), AccessKey = "35261047225005000186550020000000999999999999" }, "nfe.autorizada");
        var (r1, d1) = await fixture.PostWebhookAsync(tenant, unknown, "nfe.autorizada");
        r1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var e1 = await fixture.ProcessWebhookAsync(tenant, d1);
        e1.Status.Should().Be(WebhookEventStatus.Ignored);
        e1.LastError.Should().Be(EmissorFiscalWebhookHandler.Unknown);

        var other = fake.WebhookBody(doc, "nfe.autorizada", tenantId: Guid.CreateVersion7());
        var (_, d2) = await fixture.PostWebhookAsync(tenant, other, "nfe.autorizada");
        var e2 = await fixture.ProcessWebhookAsync(tenant, d2);
        e2.Status.Should().Be(WebhookEventStatus.Ignored);
        e2.LastError.Should().Be(EmissorFiscalWebhookHandler.OtherTenant);
        (await fixture.InvoiceOfAsync(tenant, id)).Status.Should().Be(OutgoingInvoiceStatus.Processing);
    }

    [Fact]
    public async Task Webhook_com_assinatura_errada_ou_timestamp_fora_da_janela_da_401_e_nada_e_gravado()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (_, _, doc) = await PendingAsync();
        var body = fixture.Fake(tenant).WebhookBody(doc, "nfe.autorizada");
        var before = await fixture.QueryAsync(tenant, db => db.WebhookEvents.CountAsync(e => e.Provider == EmissorFiscalWebhook.ProviderCode));

        (await fixture.PostWebhookAsync(tenant, body, "nfe.autorizada", secret: "segredo-errado-do-webhook-000000")).Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.PostWebhookAsync(tenant, body, "nfe.autorizada", at: DateTimeOffset.UtcNow.AddMinutes(-6))).Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.PostWebhookAsync(tenant, body, "nfe.autorizada", host: FiscalFixture.HostA)).Response.StatusCode.Should().Be(HttpStatusCode.NotFound, "no host do tenant a rota não existe");

        (await fixture.QueryAsync(tenant, db => db.WebhookEvents.CountAsync(e => e.Provider == EmissorFiscalWebhook.ProviderCode))).Should().Be(before);
    }

    [Fact]
    public async Task Destinatario_chave_e_XML_nunca_vao_para_log_outbox_ou_auditoria()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, key, doc) = await PendingAsync();
        fixture.Fake(tenant).Settle(key, FiscalDocumentStatus.Authorized);
        var (_, delivery) = await fixture.PostWebhookAsync(tenant, fixture.Fake(tenant).WebhookBody(doc, "nfe.autorizada"), "nfe.autorizada");
        await fixture.ProcessWebhookAsync(tenant, delivery);
        (await fixture.InvoiceOfAsync(tenant, id)).XmlAttachmentId.Should().NotBeNull();

        var logs = fixture.AllLogs();
        logs.Should().NotContain(FiscalSamples.RecipientName).And.NotContain(FiscalSamples.RecipientEmail).And.NotContain(FiscalFixture.ApiKey)
            .And.NotContain("CLIENTE CANARIO XML").And.NotContain("<nfeProc");
        var outbox = await fixture.QueryAsync(tenant, db => db.OutboxMessages.AsNoTracking().Select(m => m.Payload).ToListAsync());
        string.Join("\n", outbox).Should().NotContain(FiscalSamples.RecipientName).And.NotContain(FiscalSamples.RecipientEmail);
        var audit = await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(OutgoingInvoice)).Select(a => a.Changes).ToListAsync());
        audit.Should().NotBeEmpty();
        string.Join("\n", audit).Should().NotContain(FiscalSamples.RecipientName).And.NotContain(FiscalSamples.RecipientEmail).And.NotContain("CLIENTE CANARIO XML");
    }

    // ---------- CA 12 e 14 ----------

    [Fact]
    public async Task Webhook_perdido_e_resolvido_pela_consulta_forcada_da_conferencia()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, key, _) = await PendingAsync();
        var fake = fixture.Fake(tenant);
        fake.Resolve(key, FiscalDocumentStatus.Authorized);

        await fixture.RunPendingJobAsync(tenant);
        (await fixture.InvoiceOfAsync(tenant, id)).Status.Should().Be(OutgoingInvoiceStatus.Processing, "pendente há menos de 5 min não entra");

        await fixture.AgeAsync(tenant, id, TimeSpan.FromMinutes(6));
        await fixture.RunPendingJobAsync(tenant);

        var invoice = await fixture.InvoiceOfAsync(tenant, id);
        invoice.Status.Should().Be(OutgoingInvoiceStatus.Authorized);
        invoice.XmlAttachmentId.Should().NotBeNull();
        fake.Calls.Should().Contain(c => c.Operation == "consulta_forcada");

        await fixture.RunPendingJobAsync(tenant);
        (await fixture.QueryAsync(tenant, db => db.Attachments.CountAsync(a => a.OwnerId == id))).Should().Be(1, "idempotente: rodar de novo não duplica");
    }

    [Fact]
    public async Task Webhook_e_conferencia_ao_mesmo_tempo_aplicam_a_transicao_uma_vez()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, key, doc) = await PendingAsync();
        var fake = fixture.Fake(tenant);
        fake.Settle(key, FiscalDocumentStatus.Authorized);
        await fixture.AgeAsync(tenant, id, TimeSpan.FromMinutes(6));
        var (_, delivery) = await fixture.PostWebhookAsync(tenant, fake.WebhookBody(doc, "nfe.autorizada"), "nfe.autorizada");

        await Task.WhenAll(fixture.ProcessWebhookAsync(tenant, delivery), fixture.CheckPendingAsync(tenant));
        await fixture.ProcessWebhookAsync(tenant, delivery); // se a primeira perdeu a corrida, a reentrega conclui

        (await fixture.InvoiceOfAsync(tenant, id)).Status.Should().Be(OutgoingInvoiceStatus.Authorized);
        (await fixture.QueryAsync(tenant, db => db.Attachments.CountAsync(a => a.OwnerId == id && a.OwnerType == OutgoingInvoiceReconciler.AttachmentOwner))).Should().Be(1);
        var audits = await fixture.QueryAsync(tenant, db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(OutgoingInvoice) && a.EntityId == id.ToString()).Select(a => a.Changes).ToListAsync());
        audits.Count(BecameAuthorized).Should().Be(1, "a transição para Authorized acontece uma vez");
    }

    [Fact]
    public async Task Conferencia_roda_por_tenant_com_o_tenantId()
    {
        var a = fixture.Tenancy.TenantA;
        var b = fixture.Tenancy.TenantB;
        await fixture.SetAllowanceAsync(b, FiscalEnvironmentAllowance.Homologation);
        try
        {
            var (idA, keyA, _) = await PendingAsync();
            await fixture.ConfigureAsync(b);
            fixture.Fake(b).Next = FakeIssue.Delay;
            var idB = await fixture.InvoiceAsync(b);
            (await fixture.SubmitAsync(b, idB)).IsSuccess.Should().BeTrue();
            var keyB = (await fixture.InvoiceOfAsync(b, idB)).AccessKey!;
            await fixture.AgeAsync(a, idA, TimeSpan.FromMinutes(6));
            await fixture.AgeAsync(b, idB, TimeSpan.FromMinutes(6));
            fixture.Fake(a).Resolve(keyA, FiscalDocumentStatus.Authorized);
            fixture.Fake(b).Resolve(keyB, FiscalDocumentStatus.Rejected);

            await fixture.RunPendingJobAsync(a);
            (await fixture.InvoiceOfAsync(a, idA)).Status.Should().Be(OutgoingInvoiceStatus.Authorized);
            (await fixture.InvoiceOfAsync(b, idB)).Status.Should().Be(OutgoingInvoiceStatus.Processing, "o job do tenant A não toca no B");

            await fixture.RunPendingJobAsync(b);
            (await fixture.InvoiceOfAsync(b, idB)).Status.Should().Be(OutgoingInvoiceStatus.Rejected);
        }
        finally
        {
            await fixture.SetAllowanceAsync(b, FiscalEnvironmentAllowance.None);
        }
    }

    [Fact]
    public async Task Sem_a_consulta_forcada_no_Emissor_a_nota_fica_demorando_com_alerta_e_nunca_e_reemitida()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, _, _) = await PendingAsync();
        var fake = fixture.Fake(tenant);
        fake.NoForceConsult = true;
        await fixture.AgeAsync(tenant, id, TimeSpan.FromMinutes(6));
        var emitted = fake.Count("emitir");

        await fixture.RunPendingJobAsync(tenant);

        (await fixture.InvoiceOfAsync(tenant, id)).Status.Should().Be(OutgoingInvoiceStatus.Processing);
        fake.Count("emitir").Should().Be(emitted);
        fixture.Alerts.Raised.Should().Contain(x => x.Kind == FiscalAlerts.Kind && x.Details == nameof(FiscalAlertReason.ForceConsultMissing) && x.Link == FiscalLinks.Invoice(id));
    }

    [Fact]
    public async Task Contingencia_ha_mais_de_2_horas_alerta_quem_edita_o_fiscal_com_link_para_a_nota()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, _, _) = await PendingAsync(FakeIssue.Contingency);
        var (editorId, _) = await fixture.ActorAsync(tenant, FiscalFixture.HostA, grants: ["fiscal.view", "fiscal.edit"]);
        await fixture.AgeAsync(tenant, id, TimeSpan.FromHours(3), contingency: true);
        var emitted = fixture.Fake(tenant).Count("emitir");

        await fixture.RunPendingJobAsync(tenant);

        (await fixture.InvoiceOfAsync(tenant, id)).Status.Should().Be(OutgoingInvoiceStatus.ContingencyPending);
        fixture.Fake(tenant).Count("emitir").Should().Be(emitted, "contingência nunca reemite");
        fixture.Alerts.Raised.Should().Contain(x => x.Kind == FiscalAlerts.Kind && x.Details == nameof(FiscalAlertReason.ContingencyTooLong));
        (await fixture.QueryAsync(tenant, db => db.Notifications.AnyAsync(n => n.UserId == editorId && n.Link == FiscalLinks.Invoice(id)))).Should().BeTrue();
    }

    [Fact]
    public async Task SEFAZ_fora_do_ar_alerta_e_modulo_nao_liberado_nao_confere()
    {
        var tenant = fixture.Tenancy.TenantA;
        var (id, _, _) = await PendingAsync();
        await fixture.AgeAsync(tenant, id, TimeSpan.FromMinutes(6));
        fixture.Fake(tenant).SefazOnline = false;
        await fixture.RunPendingJobAsync(tenant);
        fixture.Alerts.Raised.Should().Contain(x => x.Kind == FiscalAlerts.Kind && x.Details == nameof(FiscalAlertReason.SefazDown));

        fixture.Fake(tenant).Calls.Clear();
        await fixture.SetModuleAsync(tenant, "fiscal", released: false);
        try
        {
            await fixture.RunPendingJobAsync(tenant);
            fixture.Fake(tenant).Calls.Should().BeEmpty("sem o módulo liberado a conferência não roda");
        }
        finally
        {
            await fixture.SetModuleAsync(tenant, "fiscal", released: true);
        }
    }
}
