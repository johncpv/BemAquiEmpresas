using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BemAqui.Integration.Tests.Finance.BillingSlipFixture;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// BNK-02, integração (Testcontainers + fake do Inter): cadastro do aviso com 2FA e URL mostrada uma vez (CA 1), troca do endereço (CA 3),
/// produção sem a flag (CA 4), corpo falso com a reconsulta verdadeira (CA 5), deduplicação (CA 6), baixa automática com a transação e o
/// actor de sistema (CA 7, 13, 17), repetição e 10 processamentos paralelos (CA 8), divergência e conferência (CA 9), cobrança desconhecida
/// (CA 10), Inter fora do ar (CA 12) e estorno da baixa do banco com <c>finance.approve</c> (CA 14).
/// </summary>
public partial class BnkPaymentTests(BillingSlipFixture fixture) : IClassFixture<BillingSlipFixture>
{
    public const string PlatformHost = "plataforma.bemaquiapp.com.br";

    private string? _url;

    private static string Notice(string base_, Guid account) => $"/api/v1/settings/integrations/inter/{account}/payment-notice{base_}";

    private Task<BillingSlip> SlipAsync(Guid id) => fixture.QueryAsync(db => db.BillingSlips.AsNoTracking().SingleAsync(s => s.Id == id));

    private Task<Receivable> ReceivableAsync(Guid id) => fixture.QueryAsync(db => db.Receivables.AsNoTracking().SingleAsync(r => r.Id == id));

    /// <summary>Aviso ativo na conta da classe (uma vez), com a URL completa guardada para os POSTs do "Inter".</summary>
    private async Task<string> ActiveUrlAsync(BillingScenario s)
    {
        if (_url is not null)
        {
            return _url;
        }

        var current = await JsonAsync(await s.Owner.GetAsync(Notice(string.Empty, s.Account)));
        var response = current.GetProperty("active").GetBoolean()
            ? await s.Owner.PostAsync(Notice("/rotate", s.Account), null)
            : await s.Owner.PostAsync(Notice(string.Empty, s.Account), null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _url = (await JsonAsync(response)).GetProperty("url").GetString()!;
        return _url;
    }

    private static string Body(FakeCharge charge, string situation = "RECEBIDO", string? at = null, decimal? value = null) =>
        JsonSerializer.Serialize(new[]
        {
            new Dictionary<string, object?>
            {
                ["codigoSolicitacao"] = charge.Code, ["seuNumero"] = charge.OurReference, ["situacao"] = situation, ["dataHoraSituacao"] = at ?? "2026-10-20T10:00:00Z",
                ["valorTotalRecebido"] = (value ?? 1000m).ToString("0.00", CultureInfo.InvariantCulture), ["origemRecebimento"] = "PIX",
            },
        });

    private async Task<HttpResponseMessage> PostAsync(string url, string body)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = PlatformHost;
        return await client.PostAsync(new Uri(url).PathAndQuery, new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private Task<WebhookEvent?> EventAsync(string body)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return fixture.QueryAsync(db => db.WebhookEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Provider == InterWebhook.ProviderCode && e.BodySha256 == hash));
    }

    /// <summary>Processa o evento como o Hangfire faria (o job com o tenantId e o id do evento).</summary>
    private async Task<WebhookEvent> ProcessAsync(Guid eventId)
    {
        var job = ActivatorUtilities.CreateInstance<ProcessWebhookEventJob>(fixture.Factory.Services);
        try
        {
            await job.ExecuteAsync(fixture.TenantA.Id, new WebhookEventJobArgs(eventId), CancellationToken.None);
        }
        catch (WebhookProcessingFailedException)
        {
            // falha registrada no evento; o teste confere a situação
        }

        return await fixture.QueryAsync(db => db.WebhookEvents.AsNoTracking().SingleAsync(e => e.Id == eventId));
    }

    private async Task<WebhookEvent> DeliverAsync(BillingScenario s, string body)
    {
        var response = await PostAsync(await ActiveUrlAsync(s), body);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        var evt = await EventAsync(body);
        evt.Should().NotBeNull();
        return await ProcessAsync(evt!.Id);
    }

    private async Task<(BillingScenario S, Guid SlipId, FakeCharge Charge)> IssuedAsync(decimal amount = 1000m)
    {
        var s = await fixture.ScenarioAsync(amount);
        var slipId = await fixture.IssuedAsync(s);
        var slip = await SlipAsync(slipId);
        return (s, slipId, fixture.Inter.Billing.ByReference(slip.OurReference)!);
    }

    [Fact]
    public async Task Ativar_o_aviso_cadastra_a_URL_no_Inter_mostra_uma_vez_e_guarda_so_o_hash()
    {
        var (_, ownerNoProof) = await fixture.ActorAsync(owner: false, "settings.view", "settings.edit", "finance.view");
        var s = await fixture.ScenarioAsync();

        // 2FA de novo (D6): sem a prova recente, 403 e nada vai ao Inter.
        var calls = fixture.Inter.Billing.WebhookRegistrations;
        var refused = await ownerNoProof.PostAsync(Notice(string.Empty, s.Account), null);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(refused)).Should().Be("two_factor_required");
        fixture.Inter.Billing.WebhookRegistrations.Should().Be(calls);

        var url = await ActiveUrlAsync(s);
        var key = await fixture.Factory.Services.GetRequiredService<IWebhookTenantKeys>().EnsureAsync(fixture.TenantA.Id);
        url.Should().StartWith("https://plataforma.bemaquiapp.com.br/webhooks/inter/" + key + "?t=").And.NotContain(fixture.TenantA.Key);
        fixture.Inter.Billing.WebhookUrl.Should().Be(url, "o mesmo endereço vai ao Inter");

        // Depois, só a máscara; o token não fica em lugar nenhum (só o SHA-256).
        var view = await JsonAsync(await s.Owner.GetAsync(Notice(string.Empty, s.Account)));
        view.GetProperty("active").GetBoolean().Should().BeTrue();
        view.GetProperty("mode").GetString().Should().Be("Query");
        view.GetProperty("maskedUrl").GetString().Should().Be("…/webhooks/inter/••••?t=••••");
        view.GetRawText().Should().NotContain(url[(url.IndexOf("?t=", StringComparison.Ordinal) + 3)..]);
        var token = url[(url.IndexOf("?t=", StringComparison.Ordinal) + 3)..];
        var credential = await fixture.QueryAsync(db => db.WebhookCredentials.AsNoTracking().SingleAsync(c => c.Provider == InterWebhook.ProviderCode));
        credential.TokenHash.Should().Be(Convert.ToHexStringLower(UrlTokenValidator.Hash(token)));
        (await fixture.QueryAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(WebhookCredential)).Select(a => a.Changes).ToListAsync()))
            .Should().NotBeEmpty().And.OnlyContain(c => !c.Contains(token));

        // Ativar de novo é recusado (para outro endereço, "Trocar endereço do aviso").
        var again = await s.Owner.PostAsync(Notice(string.Empty, s.Account), null);
        Code(await JsonAsync(again)).Should().Be(InterWebhookService.AlreadyActiveCode);
    }

    [Fact]
    public async Task Trocar_o_endereco_mantem_o_antigo_por_24_horas_e_depois_recusa()
    {
        var (s, _, charge) = await IssuedAsync();
        var old = await ActiveUrlAsync(s);

        var rotated = await s.Owner.PostAsync(Notice("/rotate", s.Account), null);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK, await rotated.Content.ReadAsStringAsync());
        var result = await JsonAsync(rotated);
        result.GetProperty("message").GetString().Should().Be(InterWebhookService.RotatedMessage);
        var fresh = result.GetProperty("url").GetString()!;
        _url = fresh;
        fresh.Should().NotBe(old);
        fixture.Inter.Billing.WebhookUrl.Should().Be(fresh);
        (await fixture.QueryAsync(db => db.Notifications.AnyAsync(n => n.UserId == s.OwnerId && n.Type == InterWebhookService.RotatedNotification))).Should().BeTrue();

        (await PostAsync(old, Body(charge, situation: "A_RECEBER", at: "antigo-1"))).StatusCode.Should().Be(HttpStatusCode.Accepted, "o antigo vale pelas 24 h de sobreposição");
        (await PostAsync(fresh, Body(charge, situation: "A_RECEBER", at: "novo-1"))).StatusCode.Should().Be(HttpStatusCode.Accepted);

        await fixture.QueryAsync(db => db.WebhookCredentials.Where(c => c.Provider == InterWebhook.ProviderCode)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.PreviousValidUntil, DateTimeOffset.UtcNow.AddMinutes(-1))));
        (await PostAsync(old, Body(charge, situation: "A_RECEBER", at: "antigo-2"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "passadas as 24 h");
    }

    [Fact]
    public async Task Producao_sem_a_flag_recusa_ativar_o_aviso()
    {
        var s = await fixture.ScenarioAsync();
        await fixture.QueryAsync(db => db.BankIntegrations.Where(i => i.BankAccountId == s.Account).ExecuteUpdateAsync(u => u.SetProperty(i => i.Environment, BankEnvironment.Production)));
        try
        {
            var response = await s.Owner.PostAsync(Notice("/rotate", s.Account), null);
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            Code(await JsonAsync(response)).Should().Be(BankEffectGuard.ProductionNotAllowed);
        }
        finally
        {
            await fixture.QueryAsync(db => db.BankIntegrations.Where(i => i.BankAccountId == s.Account).ExecuteUpdateAsync(u => u.SetProperty(i => i.Environment, BankEnvironment.Sandbox)));
        }
    }

    [Fact]
    public async Task Corpo_falso_nao_tem_efeito_a_situacao_vem_da_reconsulta()
    {
        var (s, slipId, charge) = await IssuedAsync();
        var before = await ReceivableAsync(s.ReceivableId);

        // O corpo diz "RECEBIDO" com R$ 1.000,00; o banco, na reconsulta, diz "A_RECEBER".
        var evt = await DeliverAsync(s, Body(charge, at: "falso-" + Guid.NewGuid().ToString("N")));

        evt.Status.Should().Be(WebhookEventStatus.Ignored);
        evt.LastError.Should().Be("Situação no banco: a receber");
        (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Issued);
        var after = await ReceivableAsync(s.ReceivableId);
        (after.Status, after.PaidAmount).Should().Be((before.Status, before.PaidAmount));
        (await fixture.QueryAsync(db => db.FinancialTransactions.CountAsync(t => t.ReceivableId == s.ReceivableId))).Should().Be(0);
    }

    [Fact]
    public async Task Pagamento_confirmado_baixa_o_titulo_com_a_transacao_o_evento_e_o_actor_de_sistema_uma_vez_so()
    {
        var (financeId, _) = await fixture.ActorAsync(owner: false, "finance.view");
        var (s, slipId, charge) = await IssuedAsync();
        charge.Pay(1000m, Today);
        var body = Body(charge, at: "pago-" + Guid.NewGuid().ToString("N"));

        var evt = await DeliverAsync(s, body);

        evt.Status.Should().Be(WebhookEventStatus.Processed, evt.LastError);
        evt.ExternalId.Should().StartWith("cobranca:" + charge.Code + ":RECEBIDO:", "chave composta (CA 6)");
        var slip = await SlipAsync(slipId);
        (slip.Status, slip.PaidAmount, slip.CreditDate, slip.PaidEventId).Should().Be((BillingSlipStatus.Paid, 1000m, Today, evt.Id));
        var receivable = await ReceivableAsync(s.ReceivableId);
        (receivable.Status, receivable.PaidAmount).Should().Be((BemAqui.Finance.Contracts.ReceivableStatus.Paid, 1000m));
        var settlement = await fixture.QueryAsync(db => db.ReceivableSettlements.AsNoTracking().SingleAsync(x => x.ReceivableId == s.ReceivableId));
        (settlement.Source, settlement.WebhookEventId, settlement.BillingSlipId, settlement.Date, settlement.BankAccountId, settlement.CreatedBy)
            .Should().Be((SettlementSource.Bank, evt.Id, slipId, Today, s.Account, (Guid?)null));
        var transaction = await fixture.QueryAsync(db => db.FinancialTransactions.AsNoTracking().SingleAsync(t => t.ReceivableId == s.ReceivableId));
        (transaction.Amount, transaction.Type, transaction.PendingReview, transaction.SettlementId).Should().Be((1000m, BemAqui.Finance.Contracts.TransactionType.Receipt, false, settlement.Id));
        (await fixture.QueryAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.EndsWith("ReceivableSettled")).Select(m => m.Payload).ToListAsync()))
            .Count(p => p.Contains(settlement.Id.ToString(), StringComparison.Ordinal)).Should().Be(1);

        // CA 17: o histórico do título e do boleto mostra o actor de sistema com o id do evento.
        var origins = await fixture.QueryAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => (a.EntityId == s.ReceivableId.ToString() || a.EntityId == slipId.ToString()) && a.Origin != null).Select(a => a.Origin!).ToListAsync());
        origins.Should().NotBeEmpty().And.OnlyContain(o => o == BankPaymentService.WebhookActor + " · evento " + evt.Id);

        // CA 13: quem tem finance.view recebe "Recebido R$ … de … · título …", uma vez.
        var paid = await fixture.QueryAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == financeId && n.Type == BankPaymentService.PaidNotification).ToListAsync());
        // O tenant é da classe: se os outros testes já pagaram mais de 5 boletos nesta hora, vem o resumo por hora.
        var title = paid.Should().ContainSingle().Which.Title;
        if (title.Contains("nesta hora", StringComparison.Ordinal))
        {
            title.Should().MatchRegex(@"^\d+ boletos pagos nesta hora · R\$ ");
        }
        else
        {
            title.Should().StartWith("Recebido R$ 1.000,00 de ").And.EndWith("· título " + receivable.Number);
        }

        // CA 6 e 8: a mesma entrega de novo não grava outro evento; outro aviso da mesma cobrança termina Ignored.
        (await PostAsync(await ActiveUrlAsync(s), body)).StatusCode.Should().Be(HttpStatusCode.OK, "entrega repetida responde 200");
        (await fixture.QueryAsync(db => db.WebhookEvents.CountAsync(e => e.Provider == InterWebhook.ProviderCode && e.ExternalId == evt.ExternalId))).Should().Be(1);
        var second = await DeliverAsync(s, Body(charge, at: "outra-entrega-" + Guid.NewGuid().ToString("N")));
        (second.Status, second.LastError).Should().Be((WebhookEventStatus.Ignored, BankPaymentService.AlreadyPaid));
        (await fixture.QueryAsync(db => db.ReceivableSettlements.CountAsync(x => x.ReceivableId == s.ReceivableId))).Should().Be(1);
    }

    [Fact]
    public async Task Dez_avisos_processados_em_paralelo_baixam_uma_vez_so()
    {
        var (s, slipId, charge) = await IssuedAsync();
        charge.Pay(1000m, Today);
        var url = await ActiveUrlAsync(s);
        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            var body = Body(charge, at: $"paralelo-{i}-" + Guid.NewGuid().ToString("N"));
            (await PostAsync(url, body)).StatusCode.Should().Be(HttpStatusCode.Accepted);
            ids.Add((await EventAsync(body))!.Id);
        }

        var events = await Task.WhenAll(ids.Select(ProcessAsync));

        (await fixture.QueryAsync(db => db.ReceivableSettlements.CountAsync(x => x.ReceivableId == s.ReceivableId))).Should().Be(1);
        (await fixture.QueryAsync(db => db.FinancialTransactions.CountAsync(t => t.ReceivableId == s.ReceivableId))).Should().Be(1);
        (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Paid);
        events.Count(e => e.Status == WebhookEventStatus.Processed).Should().Be(1);
        events.Where(e => e.Status != WebhookEventStatus.Processed).Should().OnlyContain(e => e.Status == WebhookEventStatus.Ignored && e.LastError == BankPaymentService.AlreadyPaid);
    }

    [Fact]
    public async Task Pago_a_menor_fora_da_data_vai_para_a_conferencia_com_a_entrada_na_conta()
    {
        var (editorId, editor) = await fixture.ActorAsync(owner: false, "finance.view", "finance.edit");
        var (s, slipId, charge) = await IssuedAsync();
        charge.Pay(900m, Today);

        var evt = await DeliverAsync(s, Body(charge, at: "menor-" + Guid.NewGuid().ToString("N")));

        evt.Status.Should().Be(WebhookEventStatus.Processed, evt.LastError);
        var slip = await SlipAsync(slipId);
        (slip.Status, slip.PaidAmount, slip.ReviewReason).Should().Be((BillingSlipStatus.PaidPendingReview, 900m, BillingSlipSettlement.Underpaid));
        var pending = await fixture.QueryAsync(db => db.FinancialTransactions.AsNoTracking().SingleAsync(t => t.ReceivableId == s.ReceivableId));
        (pending.Amount, pending.PendingReview, pending.SettlementId, pending.BillingSlipId).Should().Be((900m, true, (Guid?)null, slipId));
        pending.Description.Should().StartWith("Recebido do Inter, aguardando conferência");
        (await ReceivableAsync(s.ReceivableId)).PaidAmount.Should().Be(0m, "a baixa espera a conferência (R-PRP-7)");
        (await fixture.QueryAsync(db => db.Notifications.AsNoTracking().SingleAsync(n => n.UserId == editorId && n.Type == BankPaymentService.ReviewNotification)))
            .Link.Should().Be(BankPaymentService.ReviewLink(slipId));

        // Filtro "Pagamentos a conferir" e o diálogo com o impacto; a opção sugerida (fora da data do desconto) é deixar o saldo em aberto.
        var list = await JsonAsync(await editor.GetAsync("/api/v1/finance/billing-slips/pending-review"));
        list.GetProperty("items").EnumerateArray().Should().Contain(r => r.GetProperty("slipId").GetGuid() == slipId && r.GetProperty("difference").GetDecimal() == -100m);
        var draft = await JsonAsync(await editor.GetAsync($"/api/v1/finance/billing-slips/{slipId}/review"));
        draft.GetProperty("suggestedDecision").GetString().Should().Be(BankPaymentDecision.KeepOpen);
        var keepOpen = draft.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("decision").GetString() == BankPaymentDecision.KeepOpen);
        (keepOpen.GetProperty("balanceBefore").GetDecimal(), keepOpen.GetProperty("balanceAfter").GetDecimal()).Should().Be((1000m, 100m));

        // IDOR / boleto que não está em conferência: 404.
        (await editor.GetAsync($"/api/v1/finance/billing-slips/{Guid.NewGuid()}/review")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var reviewed = await editor.PostAsJsonAsync($"/api/v1/finance/billing-slips/{slipId}/review", new { decision = BankPaymentDecision.KeepOpen, version = slip.Version });
        reviewed.StatusCode.Should().Be(HttpStatusCode.OK, await reviewed.Content.ReadAsStringAsync());
        (await JsonAsync(reviewed)).GetProperty("message").GetString().Should().Be(BankPaymentService.ReviewedMessage);
        var after = await SlipAsync(slipId);
        (after.Status, after.ReviewDecision, after.ReviewedBy).Should().Be((BillingSlipStatus.Paid, BankPaymentDecision.KeepOpen, editorId));
        var receivable = await ReceivableAsync(s.ReceivableId);
        (receivable.Status, receivable.PaidAmount).Should().Be((BemAqui.Finance.Contracts.ReceivableStatus.PartiallyPaid, 900m));
        var linked = await fixture.QueryAsync(db => db.FinancialTransactions.AsNoTracking().SingleAsync(t => t.ReceivableId == s.ReceivableId));
        (linked.Id, linked.Amount, linked.PendingReview).Should().Be((pending.Id, 900m, false), "a mesma entrada, agora ligada à baixa");
        linked.SettlementId.Should().Be(after.SettlementId);
    }

    [Fact]
    public async Task Desconto_acima_do_limite_na_conferencia_so_com_finance_approve()
    {
        var (_, editor) = await fixture.ActorAsync(owner: false, "finance.view", "finance.edit");
        var (s, slipId, charge) = await IssuedAsync();
        charge.Pay(500m, Today); // 50% de desconto: acima do limite da FIN-01
        await DeliverAsync(s, Body(charge, at: "desconto-" + Guid.NewGuid().ToString("N")));
        var version = (await SlipAsync(slipId)).Version;

        var draft = await JsonAsync(await editor.GetAsync($"/api/v1/finance/billing-slips/{slipId}/review"));
        var discount = draft.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("decision").GetString() == BankPaymentDecision.Discount);
        (discount.GetProperty("requiresApproval").GetBoolean(), discount.GetProperty("allowed").GetBoolean()).Should().Be((true, false));
        (await editor.PostAsJsonAsync($"/api/v1/finance/billing-slips/{slipId}/review", new { decision = BankPaymentDecision.Discount, version }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var approved = await s.Owner.PostAsJsonAsync($"/api/v1/finance/billing-slips/{slipId}/review", new { decision = BankPaymentDecision.Discount, version });
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var receivable = await ReceivableAsync(s.ReceivableId);
        (receivable.Status, receivable.Discount).Should().Be((BemAqui.Finance.Contracts.ReceivableStatus.Paid, 500m));
    }

    [Fact]
    public async Task Pago_a_maior_antes_do_vencimento_confere_como_juros()
    {
        var (s, slipId, charge) = await IssuedAsync();
        charge.Pay(1015m, Today);
        await DeliverAsync(s, Body(charge, at: "maior-" + Guid.NewGuid().ToString("N")));
        var slip = await SlipAsync(slipId);
        (slip.Status, slip.ReviewReason).Should().Be((BillingSlipStatus.PaidPendingReview, BillingSlipSettlement.OverpaidOnTime));

        var draft = await JsonAsync(await s.Owner.GetAsync($"/api/v1/finance/billing-slips/{slipId}/review"));
        draft.GetProperty("suggestedDecision").GetString().Should().Be(BankPaymentDecision.Interest);
        (await s.Owner.PostAsJsonAsync($"/api/v1/finance/billing-slips/{slipId}/review", new { decision = BankPaymentDecision.Interest, version = slip.Version }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var receivable = await ReceivableAsync(s.ReceivableId);
        (receivable.Status, receivable.Interest).Should().Be((BemAqui.Finance.Contracts.ReceivableStatus.Paid, 15m));
    }

    [Fact]
    public async Task Boleto_cancelado_que_aparece_pago_vai_para_a_conferencia()
    {
        var (s, slipId, charge) = await IssuedAsync();
        await fixture.QueryAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE finance.billing_slips SET status = 'Cancelled' WHERE id = {slipId}"));
        charge.Pay(1000m, Today);

        await DeliverAsync(s, Body(charge, at: "cancelado-" + Guid.NewGuid().ToString("N")));

        var slip = await SlipAsync(slipId);
        slip.Status.Should().Be(BillingSlipStatus.PaidPendingReview);
        slip.ReviewReason.Should().Contain("cancelado");
        (await ReceivableAsync(s.ReceivableId)).PaidAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Cobranca_fora_do_sistema_nao_baixa_nada()
    {
        var (s, _, _) = await IssuedAsync();
        var outside = new FakeCharge { Code = Guid.NewGuid().ToString(), OurReference = "IB" + Guid.NewGuid().ToString("N")[..8], Body = new System.Text.Json.Nodes.JsonObject
        {
            ["dataVencimento"] = Iso(Today), ["valorNominal"] = 300m, ["pagador"] = new System.Text.Json.Nodes.JsonObject(),
        } }.Pay(300m, Today);
        fixture.Inter.Billing.Charges[outside.Code] = outside;
        var settlements = await fixture.QueryAsync(db => db.ReceivableSettlements.CountAsync());

        var evt = await DeliverAsync(s, Body(outside, at: "fora-" + Guid.NewGuid().ToString("N"), value: 300m));

        (evt.Status, evt.LastError).Should().Be((WebhookEventStatus.Ignored, InterWebhookHandler.Unknown));
        (await fixture.QueryAsync(db => db.ReceivableSettlements.CountAsync())).Should().Be(settlements);
    }

    [Fact]
    public async Task Inter_fora_do_ar_na_reconsulta_falha_sem_alterar_o_titulo()
    {
        var (s, slipId, charge) = await IssuedAsync();
        charge.Pay(1000m, Today);
        fixture.Inter.Billing.Unavailable = true;
        try
        {
            var evt = await DeliverAsync(s, Body(charge, at: "fora-do-ar-" + Guid.NewGuid().ToString("N")));

            evt.Status.Should().Be(WebhookEventStatus.Failed);
            evt.LastError.Should().StartWith("O Inter não respondeu à reconsulta");
            (await SlipAsync(slipId)).Status.Should().Be(BillingSlipStatus.Issued);
            (await ReceivableAsync(s.ReceivableId)).PaidAmount.Should().Be(0m);

            // Depois que o Inter volta, a nova tentativa baixa.
            fixture.Inter.Billing.Unavailable = false;
            (await ProcessAsync(evt.Id)).Status.Should().Be(WebhookEventStatus.Processed);
            (await ReceivableAsync(s.ReceivableId)).PaidAmount.Should().Be(1000m);
        }
        finally
        {
            fixture.Inter.Billing.Unavailable = false;
        }
    }

    [Fact]
    public async Task Estorno_da_baixa_do_banco_pede_finance_approve_e_o_2FA()
    {
        var (_, editor) = await fixture.ActorAsync(owner: false, "finance.view", "finance.edit");
        var (s, _, charge) = await IssuedAsync();
        charge.Pay(1000m, Today);
        await DeliverAsync(s, Body(charge, at: "estorno-" + Guid.NewGuid().ToString("N")));
        var settlement = await fixture.QueryAsync(db => db.ReceivableSettlements.AsNoTracking().SingleAsync(x => x.ReceivableId == s.ReceivableId));
        var version = (await ReceivableAsync(s.ReceivableId)).Version;
        var path = $"/api/v1/finance/receivables/{s.ReceivableId}/settlements/{settlement.Id}";

        var common = await s.Owner.PostAsJsonAsync(path + "/reverse", new { reason = "Banco devolveu", version });
        common.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(common)).Should().Be(ReceivableService.BankSettlementRequiresApproval);
        (await editor.PostAsJsonAsync(path + "/reverse-bank", new { reason = "Banco devolveu", version })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem finance.approve");

        var reversed = await s.Owner.PostAsJsonAsync(path + "/reverse-bank", new { reason = "Banco devolveu", version });
        reversed.StatusCode.Should().Be(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());
        (await ReceivableAsync(s.ReceivableId)).PaidAmount.Should().Be(0m);
        (await fixture.QueryAsync(db => db.Notifications.AnyAsync(n => n.UserId == s.OwnerId && n.Title.Contains("estornada")))).Should().BeTrue("o estorno é avisado ao Proprietário");
    }
}
