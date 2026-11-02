using System.Globalization;
using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Assets.Application;

/// <summary>
/// Job diário da manutenção (PAT-03a CA 3): só com o módulo <c>assets</c> liberado no tenant. Para cada plano ativo de bem não baixado: na
/// antecedência, avisa quem tem <c>assets.edit</c> ("Preventiva {plano} de {bem} vence em {data}.", <c>maint-due:{planId}:{data}</c>); no vencimento,
/// abre a OS preventiva <c>Open</c> com o checklist do plano, uma única vez por vencimento (índice único <c>(plan_id, due_key)</c>). Rodar de novo,
/// ou em paralelo, não duplica nem o aviso nem a OS.
/// </summary>
public sealed class MaintenanceDueNotifier(
    IAssetsDb db, IUserDirectory users, INotificationService notifications, TenantContextHolder tenant, ITenantScopeFactory scopes, TimeProvider clock)
{
    public const string DueType = "assets.maintenance_due";
    public const string OpenedType = "assets.maintenance_opened";

    /// <summary>Quantas OS preventivas foram abertas agora (0 com o módulo não liberado).</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased(AssetService.Module) != true)
        {
            return 0;
        }

        var plans = await (from p in db.MaintenancePlans.AsNoTracking()
                           join a in db.Assets.AsNoTracking() on p.AssetId equals a.Id
                           where p.IsActive && a.Status != AssetStatus.Disposed
                           select new { p.Id, p.AssetId, p.Description, p.FrequencyType, p.NextDueAt, p.NextDueMeter, p.LeadDays, a.Tag, a.Name })
            .ToListAsync(cancellationToken);
        if (plans.Count == 0)
        {
            return 0;
        }

        var assetIds = plans.Select(p => p.AssetId).Distinct().ToList();
        var meters = await db.AssetMeterReadings.AsNoTracking().Where(r => assetIds.Contains(r.AssetId)).GroupBy(r => r.AssetId)
            .Select(g => new { g.Key, Max = g.Max(r => r.Value) }).ToDictionaryAsync(x => x.Key, x => x.Max, cancellationToken);
        var today = BusinessTime.Today(clock);
        IReadOnlyList<Guid>? editors = null;
        var opened = 0;
        foreach (var plan in plans)
        {
            decimal? meter = meters.TryGetValue(plan.AssetId, out var m) ? m : null;
            if (MaintenanceRules.IsDue(plan.FrequencyType, plan.NextDueAt, plan.NextDueMeter, meter, today))
            {
                var key = MaintenanceRules.DueKey(plan.FrequencyType, plan.NextDueAt, plan.NextDueMeter)!;
                if (await OpenAsync(plan.Id, key, cancellationToken) is { } order)
                {
                    opened++;
                    editors ??= await users.UsersWithPermissionAsync("assets.edit", cancellationToken);
                    foreach (var user in editors)
                    {
                        await notifications.NotifyOnceAsync(user, OpenedType, $"OS {order.Number} aberta: preventiva {plan.Description} de {plan.Tag} {plan.Name}.",
                            MaintenanceService.OrderLink + order.Id, "maint-open:" + order.Id, cancellationToken);
                    }
                }

                continue;
            }

            if (plan.NextDueAt is { } due && MaintenanceRules.InLead(due, plan.LeadDays, today))
            {
                editors ??= await users.UsersWithPermissionAsync("assets.edit", cancellationToken);
                var day = due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                foreach (var user in editors)
                {
                    await notifications.NotifyOnceAsync(user, DueType, $"Preventiva {plan.Description} de {plan.Tag} {plan.Name} vence em {due.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.",
                        $"/app/patrimonio/bens/{plan.AssetId}?aba=manutencao&plano={plan.Id}", $"maint-due:{plan.Id}:{day}", cancellationToken);
                }
            }
        }

        return opened;
    }

    /// <summary>
    /// Abre a OS do vencimento num escopo próprio (transação, número <c>MAN</c> e checklist): se outra execução já abriu (a OS existe ou o índice
    /// único recusa), nada muda e devolve null.
    /// </summary>
    private async Task<MaintenanceOrder?> OpenAsync(Guid planId, string dueKey, CancellationToken ct)
    {
        await using var scope = await scopes.CreateScopeAsync(tenant.Tenant!, ct);
        var services = scope.ServiceProvider;
        var scoped = services.GetRequiredService<IAssetsDb>();
        if (await scoped.MaintenanceOrders.AsNoTracking().AnyAsync(o => o.PlanId == planId && o.DueKey == dueKey, ct))
        {
            return null;
        }

        var plan = await scoped.MaintenancePlans.AsNoTracking().FirstAsync(p => p.Id == planId, ct);
        var asset = await scoped.Assets.FirstAsync(a => a.Id == plan.AssetId, ct);
        try
        {
            // Sem valor combinado: o título do prestador é decisão de quem edita o patrimônio (efeito financeiro com confirmação, R-PRP-7);
            // a parada do centro sai com as horas sugeridas (PAT-03b CA 8).
            var opened = await services.GetRequiredService<MaintenanceService>().AddOrderAsync(
                asset, plan, dueKey, MaintenanceOrderType.Preventive, plan.Description, false, plan.NextDueAt ?? BusinessTime.Today(clock), plan.DefaultSupplierPartyId,
                plan.Checklist, null, ct);
            return opened.IsSuccess ? opened.Value : null;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, MaintenanceService.DueIndex))
        {
            // Outra execução abriu a OS deste vencimento primeiro: nada a fazer.
            return null;
        }
    }
}

public sealed record MaintenanceDueArgs;

/// <summary>Execução por tenant do job da manutenção; argumentos só de ids (o tenant).</summary>
public sealed class MaintenanceDueJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<MaintenanceDueJob> logger)
    : TenantJob<MaintenanceDueArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, MaintenanceDueArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<MaintenanceDueNotifier>().RunAsync(cancellationToken);
}

/// <summary>Recorrente global diário (06:00 de São Paulo) que enfileira o job da manutenção por tenant ativo.</summary>
public sealed class MaintenanceDueRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<MaintenanceDueJob, MaintenanceDueArgs>(directory, jobs)
{
    public const string Id = "assets-maintenance-due";
    public const string Cron = "0 6 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override MaintenanceDueArgs ArgsFor(TenantInfo tenant) => new();
}
