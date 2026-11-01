using System.Globalization;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>O que o lembrete fez num tenant.</summary>
public sealed record FiscalInvalidationReminderResult(int OldNumbers, int Notified);

/// <summary>
/// Lembrete semanal dos números a inutilizar (FIS-03b CA 12): com algum número gasto há mais de 30 dias, os membros do Proprietário recebem
/// o aviso com o link do filtro "A inutilizar", uma vez por semana (<c>dedup_key = fiscal-gaps:{ano}-W{semana}</c>: a execução dupla não
/// repete). Só com o módulo <c>fiscal</c> liberado.
/// </summary>
public sealed partial class FiscalInvalidationReminder(IFiscalDb db, IUserDirectory users, INotificationService notifications, TenantContextHolder tenant, TimeProvider clock,
    ILogger<FiscalInvalidationReminder> logger)
{
    public const string NotificationType = "fiscal.numbers_to_invalidate";

    public async Task<FiscalInvalidationReminderResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased(FiscalModuleInfo.Code) != true)
        {
            return new FiscalInvalidationReminderResult(0, 0);
        }

        var now = clock.GetUtcNow();
        var cutoff = now.AddDays(-NumberGapService.OldAfterDays);
        var old = await db.NumberGaps.AsNoTracking().CountAsync(g => g.Reason == NumberGapReasons.Rejected && g.Status == NumberGapStatus.Pending && g.CreatedAt <= cutoff, cancellationToken);
        if (old == 0)
        {
            return new FiscalInvalidationReminderResult(0, 0);
        }

        var today = BusinessTime.Today(clock).ToDateTime(TimeOnly.MinValue);
        var dedup = "fiscal-gaps:" + ISOWeek.GetYear(today).ToString(CultureInfo.InvariantCulture) + "-W" + ISOWeek.GetWeekOfYear(today).ToString("00", CultureInfo.InvariantCulture);
        var notified = 0;
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            if ((await notifications.NotifyOnceAsync(owner, NotificationType, Title(old), NumberGapService.Link, dedup, cancellationToken)).IsSuccess)
            {
                notified++;
            }
        }

        LogReminded(logger, old);
        return new FiscalInvalidationReminderResult(old, notified);
    }

    public static string Title(int count) => count == 1
        ? "1 número de NF-e espera a inutilização há mais de 30 dias. Regularize com a SEFAZ."
        : $"{count.ToString(CultureInfo.InvariantCulture)} números de NF-e esperam a inutilização há mais de 30 dias. Regularize com a SEFAZ.";

    [LoggerMessage(Level = LogLevel.Information, Message = "Números a inutilizar há mais de 30 dias: {Count}.")]
    private static partial void LogReminded(ILogger logger, int count);
}

public sealed record FiscalInvalidationReminderArgs;

/// <summary>Execução por tenant (só com o <c>tenantId</c>).</summary>
public sealed class FiscalInvalidationReminderJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<FiscalInvalidationReminderJob> logger)
    : TenantJob<FiscalInvalidationReminderArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, FiscalInvalidationReminderArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<FiscalInvalidationReminder>().CheckAsync(cancellationToken);
}

/// <summary>Recorrente global <c>fiscal-invalidation-reminder</c> (segunda-feira, 09:00 de São Paulo): um job por tenant com o módulo <c>fiscal</c> liberado.</summary>
public sealed class FiscalInvalidationReminderRecurringJob(ITenantDirectory directory, ITenantResolver resolver, IBackgroundJobClient jobs)
    : GlobalRecurringJob<FiscalInvalidationReminderJob, FiscalInvalidationReminderArgs>(directory, jobs)
{
    public const string Id = "fiscal-invalidation-reminder";
    public const string Cron = "0 9 * * 1";

    protected override string Queue => JobQueues.Fiscal;

    protected override FiscalInvalidationReminderArgs ArgsFor(TenantInfo tenant) => new();

    protected override async Task<bool> ShouldEnqueueAsync(TenantInfo tenant, CancellationToken cancellationToken) =>
        (await resolver.FindByIdAsync(tenant.Id, cancellationToken))?.IsReleased(FiscalModuleInfo.Code) == true;
}

/// <summary>
/// Expiração dos pedidos de cancelamento (FIS-03b CA 2): de hora em hora, o pedido pendente cuja nota passou do prazo vira <c>Expired</c> e
/// quem pediu recebe o aviso. Concorrência com a decisão do Proprietário: o <c>xmin</c> do pedido deixa só uma.
/// </summary>
public sealed class CancellationRequestExpiry(IFiscalDb db, INotificationService notifications, TenantContextHolder tenant, TimeProvider clock)
{
    public const string NotificationType = "fiscal.cancellation_request_expired";

    public async Task<int> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased(FiscalModuleInfo.Code) != true)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var due = await db.CancellationRequests.Where(r => r.Status == CancellationRequestStatus.Pending && r.ExpiresAt <= now).OrderBy(r => r.ExpiresAt).Take(500).ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var ids = due.Select(r => r.InvoiceId).Distinct().ToList();
        var labels = await db.OutgoingInvoices.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, OutgoingInvoiceEffects.Label, cancellationToken);
        foreach (var request in due.Where(r => r.Expire(now)))
        {
            if (request.RequestedBy is { } requester)
            {
                notifications.Notify(requester, NotificationType, $"O pedido de cancelamento da NF-e {labels.GetValueOrDefault(request.InvoiceId, "sem número")} expirou: o prazo de cancelamento terminou.",
                    OutgoingInvoiceEffects.Link(request.InvoiceId));
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return 0; // decidido ao mesmo tempo: vale a decisão; o resto fica para a próxima hora
        }

        return due.Count;
    }
}

public sealed record CancellationRequestExpiryArgs;

public sealed class CancellationRequestExpiryJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<CancellationRequestExpiryJob> logger)
    : TenantJob<CancellationRequestExpiryArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, CancellationRequestExpiryArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<CancellationRequestExpiry>().CheckAsync(cancellationToken);
}

/// <summary>Recorrente global <c>fiscal-cancellation-request-expiry</c> (de hora em hora).</summary>
public sealed class CancellationRequestExpiryRecurringJob(ITenantDirectory directory, ITenantResolver resolver, IBackgroundJobClient jobs)
    : GlobalRecurringJob<CancellationRequestExpiryJob, CancellationRequestExpiryArgs>(directory, jobs)
{
    public const string Id = "fiscal-cancellation-request-expiry";
    public const string Cron = "5 * * * *";

    protected override string Queue => JobQueues.Fiscal;

    protected override CancellationRequestExpiryArgs ArgsFor(TenantInfo tenant) => new();

    protected override async Task<bool> ShouldEnqueueAsync(TenantInfo tenant, CancellationToken cancellationToken) =>
        (await resolver.FindByIdAsync(tenant.Id, cancellationToken))?.IsReleased(FiscalModuleInfo.Code) == true;
}

/// <summary>
/// Pauta do Proprietário (FIS-03b CA 2 e 12, DSH-04): "Pedidos de cancelamento de NF-e", ordenados pelo prazo (quem vence primeiro, primeiro),
/// e "Números a inutilizar" (atrasado com algum número de mais de 30 dias). As duas com <c>fiscal.approve</c>, reservada ao Proprietário.
/// </summary>
public sealed class FiscalEventAgendaSource(IFiscalDb db, TimeProvider clock) : IAgendaSource
{
    public const string CancellationRequests = "fiscal.cancellation_requests";
    public const string NumbersToInvalidate = "fiscal.numbers_to_invalidate";

    private static readonly AgendaCategory[] Declared =
    [
        new(CancellationRequests, FiscalModuleInfo.Code, "fiscal.approve", AgendaTier.Overdue, "os pedidos de cancelamento de NF-e"),
        new(NumbersToInvalidate, FiscalModuleInfo.Code, "fiscal.approve", AgendaTier.DueSoon, "os números a inutilizar"),
    ];

    public string Module => FiscalModuleInfo.Code;

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = new List<AgendaItem>();
        var now = clock.GetUtcNow();
        if (request.Wants(CancellationRequests))
        {
            var pending = await db.CancellationRequests.AsNoTracking().Where(r => r.Status == CancellationRequestStatus.Pending && r.ExpiresAt > now).OrderBy(r => r.ExpiresAt)
                .Take(AgendaLimits.EntriesPerCategory).ToListAsync(cancellationToken);
            if (pending.Count > 0)
            {
                var count = await db.CancellationRequests.AsNoTracking().CountAsync(r => r.Status == CancellationRequestStatus.Pending && r.ExpiresAt > now, cancellationToken);
                var ids = pending.Select(r => r.InvoiceId).ToList();
                var invoices = await db.OutgoingInvoices.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);
                var entries = pending.Select(r => new AgendaEntry(
                    "NF-e " + (invoices.TryGetValue(r.InvoiceId, out var i) ? OutgoingInvoiceEffects.Label(i) : "sem número"),
                    r.Reason + " · restam " + OutgoingInvoiceService.Remaining(r.ExpiresAt - now), OutgoingInvoiceService.RequestLink(r.InvoiceId, r.Id),
                    DateOnly.FromDateTime(BusinessTime.ToBusiness(r.ExpiresAt).DateTime), invoices.TryGetValue(r.InvoiceId, out var v) ? v.TotalAmount : null)).ToList();
                var title = count == 1 ? "1 pedido de cancelamento de NF-e" : $"{count.ToString(CultureInfo.InvariantCulture)} pedidos de cancelamento de NF-e";
                items.Add(new AgendaItem(CancellationRequests, title, "Cancele ou recuse dentro do prazo: depois dele, a devolução fica com a contadora.",
                    OutgoingInvoiceService.QueueLink + "?aba=emitidas&situacao=cancelamento-pedido", entries[0].DueDate, null, AgendaTier.Overdue, count, "Decidir", entries));
            }
        }

        if (request.Wants(NumbersToInvalidate))
        {
            var gaps = await db.NumberGaps.AsNoTracking().Where(g => g.Reason == NumberGapReasons.Rejected && g.Status == NumberGapStatus.Pending)
                .Select(g => new { g.Series, g.Number, g.CreatedAt }).Take(5_000).ToListAsync(cancellationToken);
            if (gaps.Count > 0)
            {
                var oldest = gaps.Min(g => g.CreatedAt);
                var late = oldest <= now.AddDays(-NumberGapService.OldAfterDays);
                var entries = FiscalEventRules.Ranges(gaps.Select(g => (g.Series, g.Number))).Take(AgendaLimits.EntriesPerCategory)
                    .Select(r => new AgendaEntry($"Série {r.Series.ToString(CultureInfo.InvariantCulture)}: {r.Label}", r.Count == 1 ? "1 número" : $"{r.Count.ToString(CultureInfo.InvariantCulture)} números",
                        NumberGapService.Link, null, null)).ToList();
                var title = gaps.Count == 1 ? "1 número de NF-e a inutilizar" : $"{gaps.Count.ToString(CultureInfo.InvariantCulture)} números de NF-e a inutilizar";
                items.Add(new AgendaItem(NumbersToInvalidate, title,
                    late ? "Há número esperando há mais de 30 dias: a SEFAZ exige que a lacuna na numeração seja explicada." : "A SEFAZ exige que a lacuna na numeração seja explicada.",
                    NumberGapService.Link, DateOnly.FromDateTime(BusinessTime.ToBusiness(oldest).DateTime), null, late ? AgendaTier.Overdue : AgendaTier.DueSoon, gaps.Count, "Inutilizar números",
                    entries));
            }
        }

        return items;
    }
}
