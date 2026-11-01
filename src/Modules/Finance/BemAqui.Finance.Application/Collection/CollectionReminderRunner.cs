using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Finance.Application;

/// <summary>Resultado de uma rodada da régua num tenant (para o log e os testes).</summary>
public sealed record CollectionRunResult(int Queued, int Deferred, int Skipped);

/// <summary>
/// Rodada da régua num tenant (FIN-09 CA 4 a 7): com a régua ligada, no dia útil e a partir do horário configurado, enfileira o
/// <c>collection-reminder</c> de cada título cujo passo cai hoje, um por título e passo (<c>collection_notices</c> único). Roda sob a trava
/// do tenant: a segunda execução simultânea espera e acha os lembretes já gravados. O limite diário de Configurações → Cobrança vale; o
/// excedente fica <c>Deferred</c> e sai na próxima rodada de um dia útil seguinte, sem pular o passo.
/// </summary>
public sealed partial class CollectionReminderRunner(
    IFinanceDb db,
    CollectionPlanner planner,
    CollectionEligibility eligibility,
    CollectionReminderMailer mailer,
    EmailDispatchLimiter limiter,
    BillingSettingsService settings,
    IParties parties,
    ICompanyIdentity company,
    IBusinessCalendar calendar,
    TimeProvider clock,
    ILogger<CollectionReminderRunner> logger)
{
    public const string LockKey = "finance.collection_reminders";
    public const string RelatedType = "Receivable";
    public const string LimitReason = "Limite diário de e-mails atingido: sai no próximo dia útil.";
    public const string NoContactReason = "Cliente sem contato que recebe cobrança";

    public Task<CollectionRunResult> RunAsync(CancellationToken cancellationToken = default) => RunAtAsync(clock.GetUtcNow(), cancellationToken);

    /// <summary>A rodada no instante <paramref name="at"/> (o horário da régua é conferido contra ele).</summary>
    public async Task<CollectionRunResult> RunAtAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var rule = await db.CollectionRules.AsNoTracking().OrderBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        var now = BusinessTime.ToBusiness(at);
        var today = DateOnly.FromDateTime(now.DateTime);
        if (rule is not { IsActive: true } || TimeOnly.FromDateTime(now.DateTime) < rule.SendTime || !await calendar.IsBusinessDayAsync(today, cancellationToken))
        {
            return new CollectionRunResult(0, 0, 0);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({LockKey}, 0))) AS l").ToListAsync(cancellationToken);
        var remaining = await limiter.BillingRemainingAsync((await settings.DefaultsAsync(cancellationToken)).DailyEmailLimit, cancellationToken);

        // Adiados pelo limite (CA 7) primeiro: o passo não se perde.
        var deferred = await db.CollectionNotices.Where(n => n.Status == CollectionNoticeStatus.Deferred).OrderBy(n => n.ScheduledFor).ThenBy(n => n.CreatedAt).ToListAsync(cancellationToken);
        var deferredIds = deferred.Select(n => n.ReceivableId).Distinct().ToList();
        var deferredReceivables = await db.Receivables.AsNoTracking().Where(r => deferredIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var due = await planner.DueOnAsync(rule.Steps, today, tracking: false, cancellationToken);
        var blocked = await eligibility.BlockedAsync([.. deferredReceivables.Values, .. due.Select(d => d.Receivable).Where(r => !deferredReceivables.ContainsKey(r.Id))], cancellationToken);

        var info = await company.GetAsync(cancellationToken);
        var name = EmailAddresses.OneLine(info.TradeName ?? info.LegalName);
        var replyTo = rule.ReplyTo ?? (info.Email is { Length: > 0 } e && EmailAddresses.IsPlain(e) ? e : null);
        var contacts = new Dictionary<Guid, (string? To, string Customer)>();
        var (queued, postponed, skipped) = (0, 0, 0);

        async Task<bool> TryQueueAsync(CollectionNotice notice, Receivable receivable)
        {
            if (blocked.TryGetValue(receivable.Id, out var reason))
            {
                notice.Skip(reason);
                skipped++;
                return false;
            }

            var (to, customer) = await ContactAsync(contacts, receivable.PartyId, cancellationToken);
            if (to is null)
            {
                notice.Skip(NoContactReason);
                skipped++;
                return false;
            }

            if (remaining <= 0)
            {
                notice.Defer(LimitReason);
                postponed++;
                return false;
            }

            var text = rule.Steps.FirstOrDefault(s => s.Days == notice.Step)?.Text ?? CollectionRules.DefaultSteps.FirstOrDefault(s => s.Days == notice.Step)?.Text;
            if (text is null)
            {
                notice.Skip("Passo removido da régua");
                skipped++;
                return false;
            }

            var slip = await db.BillingSlips.AsNoTracking().Where(s => s.ReceivableId == receivable.Id && s.Status == BillingSlipStatus.Issued && s.PdfAttachmentId != null)
                .OrderByDescending(s => s.IssuedAt).FirstOrDefaultAsync(cancellationToken);
            var id = mailer.Enqueue(new CollectionReminderMail(receivable, text, to, name, customer, replyTo, slip, today));
            notice.Queue(id, clock.GetUtcNow());
            remaining--;
            queued++;
            return true;
        }

        foreach (var notice in deferred)
        {
            if (deferredReceivables.TryGetValue(notice.ReceivableId, out var receivable))
            {
                await TryQueueAsync(notice, receivable);
            }
            else
            {
                notice.Skip(CollectionEligibility.NotOpenReason);
                skipped++;
            }
        }

        foreach (var item in due)
        {
            var notice = new CollectionNotice { ReceivableId = item.Receivable.Id, PartyId = item.Receivable.PartyId, Step = item.Step, ScheduledFor = today, CreatedAt = clock.GetUtcNow() };
            db.CollectionNotices.Add(notice);
            await TryQueueAsync(notice, item.Receivable);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogRun(logger, queued, postponed, skipped);
        return new CollectionRunResult(queued, postponed, skipped);
    }

    /// <summary>O contato do cliente marcado "recebe cobrança" (só ele) e o nome do cliente; um por cliente na rodada.</summary>
    private async Task<(string? To, string Customer)> ContactAsync(Dictionary<Guid, (string? To, string Customer)> cache, Guid partyId, CancellationToken ct)
    {
        if (cache.TryGetValue(partyId, out var hit))
        {
            return hit;
        }

        var party = await parties.FindAsync(partyId, ct);
        var contact = (await parties.ContactsAsync(partyId, ct)).FirstOrDefault(c => c.ReceivesBilling && c.Email is { Length: > 0 } e && EmailAddresses.IsPlain(e.Trim()));
        var result = (contact?.Email?.Trim(), EmailAddresses.OneLine(party is null ? string.Empty : party.TradeName ?? party.LegalName));
        cache[partyId] = result;
        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Régua de cobrança: {Queued} lembretes na fila, {Deferred} adiados pelo limite, {Skipped} ignorados.")]
    private static partial void LogRun(ILogger logger, int queued, int deferred, int skipped);
}

public sealed record CollectionRemindersArgs;

/// <summary>Rodada por tenant (ADR 0006): só com o módulo <c>finance</c> liberado, conferido de novo aqui.</summary>
public sealed class CollectionRemindersJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<CollectionRemindersJob> logger)
    : TenantJob<CollectionRemindersArgs>(directory, scopes, logger)
{
    public const string Module = "finance";

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, CollectionRemindersArgs args, CancellationToken cancellationToken)
    {
        if (services.GetRequiredService<TenantContextHolder>().Resolved?.IsReleased(Module) != true)
        {
            return;
        }

        await services.GetRequiredService<CollectionReminderRunner>().RunAsync(cancellationToken);
    }
}

/// <summary>
/// Recorrente global <c>collection-reminders</c> (FIN-09 CA 4): a cada 15 min, das 07:00 às 20:45 de São Paulo, um job por tenant com o
/// <c>finance</c> liberado; cada tenant só envia a partir do horário da régua dele (a rodada seguinte não repete o que já saiu).
/// </summary>
public sealed class CollectionRemindersRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs, ITenantResolver resolver)
    : GlobalRecurringJob<CollectionRemindersJob, CollectionRemindersArgs>(directory, jobs)
{
    public const string Id = "collection-reminders";
    public const string Cron = "*/15 7-20 * * *";

    protected override string Queue => JobQueues.Default;

    protected override CollectionRemindersArgs ArgsFor(TenantInfo tenant) => new();

    protected override async Task<bool> ShouldEnqueueAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return (await resolver.FindByIdAsync(tenant.Id, cancellationToken))?.IsReleased(CollectionRemindersJob.Module) == true;
    }
}
