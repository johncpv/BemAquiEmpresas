using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Resumo diário dos lançamentos manuais ao Proprietário (EST-01a CA 12, D7): entradas, saídas e estornos manuais do dia, por autor,
/// com link para Movimentos filtrado pelo dia. Idempotente por tenant e dia (<c>inventory-manual-digest:{data}</c>); dia sem
/// lançamento manual não gera aviso; módulo não liberado, nada.
/// </summary>
public sealed class ManualDigestNotifier(IInventoryDb db, IUserDirectory users, INotificationService notifications, TenantContextHolder tenant, TimeProvider clock)
{
    public const string Type = "inventory.manual_digest";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Resume o dia <paramref name="day"/> (padrão: ontem em São Paulo); devolve quantos movimentos entraram no resumo.</summary>
    public async Task<int> NotifyAsync(DateOnly? day = null, CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased("inventory") != true)
        {
            return 0;
        }

        var date = day ?? BusinessTime.Today(clock).AddDays(-1);
        var start = BusinessTime.StartOfDayUtc(date);
        var end = BusinessTime.StartOfDayUtc(date.AddDays(1));
        var rows = await db.StockMovements.AsNoTracking()
            .Where(m => m.Origin == StockOrigins.Manual && m.PostedAt >= start && m.PostedAt < end
                && (m.ReversesMovementId != null || m.Type == StockMovementType.Receipt || m.Type == StockMovementType.Issue))
            .Select(m => new { m.Type, m.UserId, m.TotalCost, Reversal = m.ReversesMovementId != null })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return 0;
        }

        var receipts = rows.Where(r => !r.Reversal && r.Type == StockMovementType.Receipt).ToList();
        var issues = rows.Where(r => !r.Reversal && r.Type == StockMovementType.Issue).ToList();
        var reversals = rows.Where(r => r.Reversal).ToList();
        var authors = new List<string>();
        foreach (var group in rows.GroupBy(r => r.UserId).OrderByDescending(g => g.Count()))
        {
            var name = group.Key is { } user ? await users.DisplayNameAsync(user, cancellationToken) ?? "Usuário" : "Sistema";
            authors.Add($"{name} {group.Count()}");
        }

        var label = date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var title = $"Lançamentos manuais de {label}: {receipts.Count} entradas ({Sum(receipts.Select(r => r.TotalCost))}), {issues.Count} saídas ({Sum(issues.Select(r => r.TotalCost))}), "
            + $"{reversals.Count} estornos ({Sum(reversals.Select(r => r.TotalCost))}). Por autor: {string.Join(", ", authors)}.";
        var iso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var link = $"/app/estoque/movimentos?origem=Manual&de={iso}&ate={iso}";
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            await notifications.NotifyOnceAsync(owner, Type, title, link, $"inventory-manual-digest:{iso}", cancellationToken);
        }

        return rows.Count;
    }

    private static string Sum(IEnumerable<decimal> values) => ManualPostingRules.Money(values.Sum(Math.Abs));
}

public sealed record ManualDigestArgs;

/// <summary>Execução por tenant do resumo diário; o tenant chega pelo <see cref="TenantJob{TArgs}"/>.</summary>
public sealed class ManualDigestNoticeJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ManualDigestNoticeJob> logger)
    : TenantJob<ManualDigestArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, ManualDigestArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<ManualDigestNotifier>().NotifyAsync(null, cancellationToken);
}

/// <summary>Recorrente global diário (06:30 de São Paulo, valor inicial; <c>Jobs:Cron:inventory-manual-digest</c> muda) que enfileira o resumo por tenant.</summary>
public sealed class ManualDigestRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<ManualDigestNoticeJob, ManualDigestArgs>(directory, jobs)
{
    public const string Id = "inventory-manual-digest";
    public const string Cron = "30 6 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override ManualDigestArgs ArgsFor(TenantInfo tenant) => new();
}
