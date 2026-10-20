using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Expiração e aviso de vencimento (COM-02 CA 10 a 13): enviado vencido vira Expirado e avisa o autor uma vez; vence em até 3
/// dias avisa uma vez por data de validade (a prorrogação gera aviso novo). Rascunho não muda. Por tenant, só ids.
/// </summary>
public sealed class QuoteExpiry(ICommercialDb db, INotificationService notifications, IEventPublisher events, TimeProvider clock)
{
    public const string NotificationType = "quotes.validity";

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var expired = await db.Quotes.Where(q => q.IsCurrent && q.Status == QuoteStatus.Sent && q.ValidUntil < today).ToListAsync(cancellationToken);
        foreach (var quote in expired)
        {
            quote.Status = QuoteStatus.Expired;
            quote.ExpiredAt = clock.GetUtcNow();
            events.Publish(new QuoteExpired(quote.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var quote in expired.Where(q => q.CreatedBy != null))
        {
            await notifications.NotifyOnceAsync(quote.CreatedBy!.Value, NotificationType, $"{quote.Number} rev. {quote.Revision} expirou. Reabra com nova revisão se o cliente ainda quiser.",
                $"/app/orcamentos/{quote.Id}?acao=revisao", $"quote-expired:{quote.Id}", cancellationToken);
        }

        var limit = today.AddDays(3);
        var expiring = await db.Quotes.AsNoTracking().Where(q => q.IsCurrent && q.Status == QuoteStatus.Sent && q.ValidUntil >= today && q.ValidUntil <= limit && q.CreatedBy != null)
            .ToListAsync(cancellationToken);
        foreach (var quote in expiring)
        {
            await notifications.NotifyOnceAsync(quote.CreatedBy!.Value, NotificationType, $"{quote.Number} vence em {quote.ValidUntil:dd/MM/yyyy}.",
                $"/app/orcamentos/{quote.Id}", $"quote-expiring:{quote.Id}:{quote.ValidUntil:yyyy-MM-dd}", cancellationToken);
        }

        return expired.Count;
    }
}

public sealed record ExpireQuotesArgs;

public sealed class ExpireQuotesJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ExpireQuotesJob> logger) : TenantJob<ExpireQuotesArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, ExpireQuotesArgs args, CancellationToken cancellationToken) =>
        services.GetRequiredService<QuoteExpiry>().RunAsync(cancellationToken);
}

/// <summary>Recorrente global diário (07:00 de São Paulo) que enfileira a expiração por tenant ativo.</summary>
public sealed class ExpireQuotesRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs) : GlobalRecurringJob<ExpireQuotesJob, ExpireQuotesArgs>(directory, jobs)
{
    public const string Id = "quotes-expire";
    public const string Cron = "0 7 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override ExpireQuotesArgs ArgsFor(TenantInfo tenant) => new();
}
