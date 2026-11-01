using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Production.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static BemAqui.Integration.Tests.Finance.BillingSlipFixture;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// FIN-09 (Testcontainers): régua pré-configurada e desligada e o "Salvar régua" (CA 1, 10), texto só com os campos da lista e a prévia
/// (CA 2), envio por passo uma vez só, no horário, execução dupla e dois tenants (CA 4), boleto anexado, pago entre enfileirar e enviar
/// vira <c>Skipped</c> pela guarda (CA 5), pausa do título e do cliente com IDOR (CA 6, 9), só o título e o limite diário (CA 7) e o bloco
/// "Lembretes" (CA 8). O calendário do tenant fica com todos os dias úteis, e a rodada roda às 20:30 de hoje (São Paulo).
/// </summary>
public class CollectionReminderTests(BillingSlipFixture fixture) : IClassFixture<BillingSlipFixture>
{
    private const string Rule = "/api/v1/settings/billing/collection-rule";
    private const string BillingSettings = "/api/v1/settings/billing";

    private static DateTimeOffset Evening => BusinessTime.StartOfDayUtc(Today).AddHours(20).AddMinutes(30);

    private static object RuleBody(bool active, string sendTime = "07:00", object[]? steps = null, string? replyTo = null) =>
        new { isActive = active, sendTime, steps = steps ?? CollectionRules.DefaultSteps.Select(s => new { days = s.Days, text = s.Text }).ToArray(), replyTo, version = (uint?)null };

    private static object LimitBody(int limit) =>
        new { finePct = 2m, interestMonthPct = 1m, defaultDiscountPct = 0m, autoEmail = true, dailyEmailLimit = limit, overdueFineFrom = "OriginalDueDate" };

    /// <summary>Todos os dias úteis no calendário padrão do tenant (o teste roda em qualquer dia da semana).</summary>
    private async Task EveryDayAsync()
    {
        await using var scope = await fixture.ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var calendars = db.Set<BemAqui.Production.Domain.Calendar>();
        var all = Enum.GetValues<Weekday>().ToList();
        var calendar = await calendars.FirstOrDefaultAsync(c => c.IsDefault);
        if (calendar is null)
        {
            calendars.Add(new BemAqui.Production.Domain.Calendar { Name = "Padrão", IsDefault = true, WorkingDays = all, CreatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            calendar.WorkingDays = all;
        }

        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> ActiveRuleAsync(string? replyTo = null)
    {
        await EveryDayAsync();
        var (_, owner) = await fixture.ActorAsync(owner: true);
        var saved = await owner.PutAsJsonAsync(Rule, RuleBody(true, replyTo: replyTo));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        return owner;
    }

    private async Task<CollectionRunResult> RunAsync(DateTimeOffset? at = null)
    {
        await using var scope = await fixture.ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<CollectionReminderRunner>().RunAtAsync(at ?? Evening);
    }

    private async Task<(Guid Party, Guid Receivable, string Number)> TitleAsync(HttpClient owner, DateOnly due, Guid? party = null, decimal amount = 500m)
    {
        var p = party ?? await CustomerAsync(owner);
        var r = await fixture.ReceivableAsync(owner, p, amount, due);
        return (p, r.GetProperty("id").GetGuid(), r.GetProperty("number").GetString()!);
    }

    private Task<List<CollectionNotice>> NoticesAsync(Guid receivable) =>
        fixture.QueryAsync(db => db.CollectionNotices.AsNoTracking().Where(n => n.ReceivableId == receivable).ToListAsync());

    private Task<EmailLog> EmailAsync(Guid logId) => fixture.QueryAsync(db => db.EmailLogs.AsNoTracking().SingleAsync(l => l.Id == logId));

    [Fact]
    public async Task Regua_vem_desligada_com_os_passos_sugeridos_e_salvar_exige_settings_edit()
    {
        // CA 1: seed nos tenants (o B nunca foi mexido): desligada, −3, 0, +3 e +10, 09:00.
        var b = (await fixture.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(fixture.Tenancy.TenantB.Id))!;
        await using (var scope = await fixture.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(b))
        {
            var seeded = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().CollectionRules.AsNoTracking().SingleAsync();
            (seeded.IsActive, seeded.SendTime).Should().Be((false, new TimeOnly(9, 0)));
            seeded.Steps.Select(s => s.Days).Should().Equal(-3, 0, 3, 10);
        }

        var (_, owner) = await fixture.ActorAsync(owner: true);
        var (_, viewer) = await fixture.ActorAsync(false, "finance.view");
        var read = await JsonAsync(await viewer.GetAsync(Rule));
        read.GetProperty("canEdit").GetBoolean().Should().BeFalse();
        read.GetProperty("placeholders").EnumerateArray().Select(p => p.GetString()).Should().Equal(CollectionRules.Placeholders);
        (await viewer.PutAsJsonAsync(Rule, RuleBody(true))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var early = await owner.PutAsJsonAsync(Rule, RuleBody(true, sendTime: "06:30"));
        early.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(early)).GetProperty("errors").GetProperty("sendTime")[0].GetString().Should().Be("Escolha um horário de envio entre 07:00 e 20:00.");
        var repeated = await owner.PutAsJsonAsync(Rule, RuleBody(true, steps: [new { days = 3, text = "a" }, new { days = 3, text = "b" }]));
        repeated.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var saved = await owner.PutAsJsonAsync(Rule, RuleBody(true, sendTime: "08:30", steps: [new { days = 0, text = "Hoje vence o {numero}." }, new { days = -5, text = "Faltam 5 dias para o {numero}." }]));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var view = await JsonAsync(saved);
        (view.GetProperty("isActive").GetBoolean(), view.GetProperty("sendTime").GetString()).Should().Be((true, "08:30"));
        view.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("days").GetInt32()).Should().Equal(-5, 0);

        // CA 10: histórico com antes e depois.
        var id = view.GetProperty("id").GetGuid();
        var audit = await fixture.QueryAsync(db => db.AuditEntries.AsNoTracking().Where(e => e.Entity == nameof(CollectionRule) && e.EntityId == id.ToString()).Select(e => e.Changes).ToListAsync());
        audit.Should().Contain(c => c.Contains("08:30", StringComparison.Ordinal));
        (await owner.PutAsJsonAsync(Rule, RuleBody(false))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Texto_fora_da_lista_e_recusado_e_a_previa_mostra_o_titulo_de_exemplo()
    {
        var (_, owner) = await fixture.ActorAsync(owner: true);
        var outside = await owner.PutAsJsonAsync(Rule, RuleBody(false, steps: [new { days = 0, text = "Olá {nome}, {{ tenant_name }}" }]));
        outside.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(outside)).GetProperty("errors").GetProperty("steps[0].text")[0].GetString().Should().Be(CollectionRules.PlaceholderMessage);
        (await owner.PostAsJsonAsync(Rule + "/preview", new { days = 3, text = "{% raw %}" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var preview = await owner.PostAsJsonAsync(Rule + "/preview", new { days = 3, text = "Olá <b>{cliente}</b>, o {numero} de {valor} venceu em {vencimento} ({dias_de_atraso} dias)." });
        preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var body = await JsonAsync(preview);
        body.GetProperty("body").GetString().Should().Contain("Olá <b>Cliente Exemplo Ltda</b>, o REC-0001 de R$").And.Contain("(3 dias)");
        body.GetProperty("subject").GetString().Should().StartWith("Título REC-0001 está em atraso há 3 dias");
    }

    [Fact]
    public async Task Passo_do_dia_sai_uma_vez_no_horario_mesmo_com_execucao_dupla()
    {
        var owner = await ActiveRuleAsync(replyTo: "financeiro@konebras.com.br");
        var (_, receivable, number) = await TitleAsync(owner, Today.AddDays(3));
        var (_, other, otherNumber) = await TitleAsync(owner, Today.AddDays(20));

        // Antes do horário (06:30 < 07:00), nada sai.
        await RunAsync(BusinessTime.StartOfDayUtc(Today).AddHours(6).AddMinutes(30));
        (await NoticesAsync(receivable)).Should().BeEmpty();

        // Duas rodadas ao mesmo tempo e uma terceira depois: um lembrete só para o passo −3.
        await Task.WhenAll(RunAsync(), RunAsync());
        await RunAsync();
        var notice = (await NoticesAsync(receivable)).Should().ContainSingle().Subject;
        (notice.Step, notice.Status, notice.ScheduledFor).Should().Be((-3, CollectionNoticeStatus.Queued, Today));
        (await NoticesAsync(other)).Should().BeEmpty();

        // CA 7: só este título, para o contato que recebe cobrança, com o Reply-To do financeiro; sem boleto, sem anexo.
        var log = await EmailAsync(notice.EmailLogId!.Value);
        (log.Template, log.To, log.ReplyTo, log.RelatedType, log.RelatedId).Should().Be((EmailDispatchLimiter.CollectionReminderTemplate, "cobranca@cliente-boleto.com.br", "financeiro@konebras.com.br",
            CollectionReminderRunner.RelatedType, receivable));
        log.AttachmentIds.Should().BeEmpty();
        log.Model.Should().Contain(number).And.NotContain(otherNumber);

        // CA 8: o bloco "Lembretes" do título e o próximo previsto.
        var view = await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{receivable}/collection"));
        view.GetProperty("notices").EnumerateArray().Single().GetProperty("status").GetString().Should().Be("Queued");
        view.GetProperty("next").GetProperty("step").GetInt32().Should().Be(0);
        view.GetProperty("next").GetProperty("date").GetString().Should().Be(Iso(Today.AddDays(3)));
        (await owner.GetAsync($"/api/v1/finance/receivables/{Guid.CreateVersion7()}/collection")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Com_boleto_emitido_o_lembrete_leva_o_pdf_e_a_linha_digitavel()
    {
        await ActiveRuleAsync();
        var s = await fixture.ScenarioAsync(800m, due: Today);
        var slipId = await fixture.IssuedAsync(s);
        await RunAsync();

        var notice = (await NoticesAsync(s.ReceivableId)).Should().ContainSingle().Subject;
        notice.Step.Should().Be(0);
        var log = await EmailAsync(notice.EmailLogId!.Value);
        var slip = await fixture.QueryAsync(db => db.BillingSlips.AsNoTracking().SingleAsync(x => x.Id == slipId));
        (log.RelatedType, log.RelatedId).Should().Be((BillingSlipService.AttachmentOwner, slipId));
        log.AttachmentIds.Should().Equal(slip.PdfAttachmentId!.Value);
        JsonDocument.Parse(log.Model).RootElement.GetProperty("digitable_line").GetString().Should().Be(slip.DigitableLine);
    }

    [Fact]
    public async Task Pago_entre_enfileirar_e_enviar_a_guarda_marca_Skipped()
    {
        var owner = await ActiveRuleAsync();
        var (_, receivable, _) = await TitleAsync(owner, Today, amount: 300m);
        await RunAsync();
        var notice = (await NoticesAsync(receivable)).Single();

        var bank = await BankIntegrationFixture.BankAccountAsync(owner);
        var version = (await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{receivable}"))).GetProperty("version").GetUInt32();
        var settle = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{receivable}/settlements",
            new { bankAccountId = bank, date = Iso(Today), principal = 300m, interest = 0m, fine = 0m, discount = 0m, backdateReason = (string?)null, version });
        settle.StatusCode.Should().Be(HttpStatusCode.OK, await settle.Content.ReadAsStringAsync());

        var job = ActivatorUtilities.CreateInstance<SendEmailJob>(fixture.Factory.Services);
        await job.ExecuteAsync(fixture.TenantA.Id, new SendEmailArgs(notice.EmailLogId!.Value), CancellationToken.None);

        var log = await EmailAsync(notice.EmailLogId!.Value);
        (log.Status, log.Error, log.SentAt).Should().Be((EmailStatus.Skipped, CollectionEligibility.PaidReason, (DateTimeOffset?)null));
        var after = (await NoticesAsync(receivable)).Single();
        (after.Status, after.SkipReason).Should().Be((CollectionNoticeStatus.Skipped, CollectionEligibility.PaidReason));
        var view = await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{receivable}/collection"));
        var shown = view.GetProperty("notices").EnumerateArray().Single();
        (shown.GetProperty("status").GetString(), shown.GetProperty("reason").GetString()).Should().Be(("Skipped", CollectionEligibility.PaidReason));
    }

    [Fact]
    public async Task Pausa_do_titulo_e_do_cliente_barra_o_lembrete_e_fica_no_historico()
    {
        var owner = await ActiveRuleAsync();
        var (_, viewer) = await fixture.ActorAsync(false, "finance.view");
        var (_, paused, _) = await TitleAsync(owner, Today.AddDays(3));
        var (party, ofParty, _) = await TitleAsync(owner, Today.AddDays(3));
        var path = $"/api/v1/finance/receivables/{paused}/collection-pause";

        (await owner.PostAsJsonAsync(path, new { reason = " ", until = (string?)null })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await viewer.PostAsJsonAsync(path, new { reason = "Negociação", until = (string?)null })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{Guid.CreateVersion7()}/collection-pause", new { reason = "Negociação", until = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR: só o título da URL");
        (await owner.PostAsJsonAsync(path, new { reason = "Negociação", until = Iso(Today.AddDays(5)) })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync(path, new { reason = "De novo", until = (string?)null })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var partyPause = await owner.PostAsJsonAsync($"/api/v1/finance/parties/{party}/collection-pause", new { reason = "Acordo com o cliente", until = (string?)null });
        partyPause.StatusCode.Should().Be(HttpStatusCode.OK, await partyPause.Content.ReadAsStringAsync());

        await RunAsync();
        (await NoticesAsync(paused)).Single().SkipReason.Should().Be(CollectionEligibility.PausedReason);
        (await NoticesAsync(ofParty)).Single().SkipReason.Should().Be(CollectionEligibility.PausedReason);
        (await NoticesAsync(paused)).Single().EmailLogId.Should().BeNull();

        // Retomar: a pausa fica no histórico, com quem retomou; a auditoria guarda as duas mudanças.
        var resumed = await owner.DeleteAsync(path);
        resumed.StatusCode.Should().Be(HttpStatusCode.OK, await resumed.Content.ReadAsStringAsync());
        var view = await JsonAsync(resumed);
        view.GetProperty("activePause").ValueKind.Should().Be(JsonValueKind.Null);
        var history = view.GetProperty("pauses").EnumerateArray().Single();
        (history.GetProperty("reason").GetString(), history.GetProperty("resumedAt").ValueKind).Should().Be(("Negociação", JsonValueKind.String));
        var pauseId = history.GetProperty("id").GetGuid().ToString();
        (await fixture.QueryAsync(db => db.AuditEntries.CountAsync(e => e.Entity == nameof(CollectionPause) && e.EntityId == pauseId))).Should().BeGreaterThanOrEqualTo(2);
        (await owner.DeleteAsync(path)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var partyView = await JsonAsync(await viewer.GetAsync($"/api/v1/finance/parties/{party}/collection"));
        (partyView.GetProperty("canEdit").GetBoolean(), partyView.GetProperty("activePause").GetProperty("reason").GetString()).Should().Be((false, "Acordo com o cliente"));
        (await owner.DeleteAsync($"/api/v1/finance/parties/{party}/collection-pause")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Limite_diario_adia_o_excedente_sem_pular_o_passo()
    {
        var owner = await ActiveRuleAsync();
        await RunAsync(); // o que já estava pendente nesta classe sai antes
        var (party, first, firstNumber) = await TitleAsync(owner, Today.AddDays(3));
        var (_, second, secondNumber) = await TitleAsync(owner, Today.AddDays(3), party);
        var templates = EmailDispatchLimiter.BillingTemplates.ToList();
        var since = BusinessTime.StartOfDayUtc(Today);
        var used = await fixture.QueryAsync(db => db.EmailLogs.CountAsync(l => templates.Contains(l.Template) && l.CreatedAt >= since));
        (await owner.PutAsJsonAsync(BillingSettings, LimitBody(used + 1))).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            var run = await RunAsync();
            run.Queued.Should().Be(1);
            var a = (await NoticesAsync(first)).Single();
            var b = (await NoticesAsync(second)).Single();
            (a.Status, b.Status, b.SkipReason).Should().Be((CollectionNoticeStatus.Queued, CollectionNoticeStatus.Deferred, CollectionReminderRunner.LimitReason));
            // Mesmo cliente, um e-mail por título: nunca a lista dos outros.
            (await EmailAsync(a.EmailLogId!.Value)).Model.Should().Contain(firstNumber).And.NotContain(secondNumber);
        }
        finally
        {
            (await owner.PutAsJsonAsync(BillingSettings, LimitBody(200))).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Com vaga (o próximo dia útil), o adiado sai com o mesmo passo.
        await RunAsync();
        var later = (await NoticesAsync(second)).Single();
        (later.Status, later.Step).Should().Be((CollectionNoticeStatus.Queued, -3));
        later.EmailLogId.Should().NotBeNull();
    }

    [Fact]
    public async Task Recorrente_enfileira_por_tenant_com_o_financeiro_liberado()
    {
        var before = fixture.Jobs.Created.Count;
        var recurring = ActivatorUtilities.CreateInstance<CollectionRemindersRecurringJob>(fixture.Factory.Services, fixture.Jobs);
        await recurring.ExecuteAsync(CancellationToken.None);
        var tenants = fixture.Jobs.Created.Skip(before).Where(c => c.Job.Type == typeof(CollectionRemindersJob)).Select(c => (Guid)c.Job.Args[0]).ToList();
        tenants.Should().Contain([fixture.TenantA.Id, fixture.Tenancy.TenantB.Id]).And.OnlyHaveUniqueItems();

        await fixture.SetModuleAsync("finance", false);
        try
        {
            var again = fixture.Jobs.Created.Count;
            await recurring.ExecuteAsync(CancellationToken.None);
            fixture.Jobs.Created.Skip(again).Where(c => c.Job.Type == typeof(CollectionRemindersJob)).Select(c => (Guid)c.Job.Args[0]).Should().NotContain(fixture.TenantA.Id);
        }
        finally
        {
            await fixture.SetModuleAsync("finance", true);
        }

        // O tenant B, com a régua desligada, não grava lembrete.
        var job = ActivatorUtilities.CreateInstance<CollectionRemindersJob>(fixture.Factory.Services);
        await job.ExecuteAsync(fixture.Tenancy.TenantB.Id, new CollectionRemindersArgs(), CancellationToken.None);
        var b = (await fixture.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(fixture.Tenancy.TenantB.Id))!;
        await using var scope = await fixture.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(b);
        (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().CollectionNotices.CountAsync()).Should().Be(0);
    }
}
