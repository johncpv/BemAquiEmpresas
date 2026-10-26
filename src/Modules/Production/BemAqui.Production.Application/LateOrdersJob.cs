using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Production.Application;

/// <summary>
/// "OS atrasada" (PRD-02 CA 11, R-COM-2): confere o módulo <c>production</c> liberado no tenant, avalia as OS de fabricação abertas
/// pela regra única (<see cref="ProductionOrderRisk"/>) e avisa o responsável do projeto e quem tem <c>production.approve</c>, uma vez
/// por OS e data prometida (<c>os-late:{id}:{prometido}</c>): rodar de novo no mesmo dia não duplica.
/// </summary>
public sealed class LateOrdersNotifier(
    IProductionDb db, IProjects projects, IUserDirectory users, INotificationService notifications, TenantContextHolder tenant, TimeProvider clock)
{
    public const string Type = "production.order_late";

    private static readonly ProductionOrderStatus[] OpenStatuses =
        [ProductionOrderStatus.Open, ProductionOrderStatus.InProgress, ProductionOrderStatus.Paused, ProductionOrderStatus.Quality, ProductionOrderStatus.ReadyToComplete];

    /// <summary>Quantas OS atrasadas foram avaliadas (0 com o módulo não liberado).</summary>
    public async Task<int> NotifyAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased("production") != true)
        {
            return 0;
        }

        var orders = await db.ProductionOrders.AsNoTracking()
            .Where(o => o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status))
            .Select(o => new { o.Id, o.Number, o.Status, o.ProjectId })
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
        {
            return 0;
        }

        var byProject = (await projects.FindManyAsync(orders.Select(o => o.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var today = BusinessTime.Today(clock);
        var late = orders
            .Select(o => (Order: o, Project: byProject.GetValueOrDefault(o.ProjectId)))
            .Select(x => (x.Order, x.Project, Risk: ProductionOrderRisk.Evaluate(x.Project?.PromisedDate, null, today, x.Order.Status)))
            .Where(x => x.Project is not null && x.Risk.Risk == DeliveryRisk.Late)
            .ToList();
        if (late.Count == 0)
        {
            return 0;
        }

        var approvers = await users.UsersWithPermissionAsync("production.approve", cancellationToken);
        foreach (var (order, project, risk) in late)
        {
            var recipients = approvers.Concat(project!.ManagerUserId is { } manager ? [manager] : Array.Empty<Guid>()).Distinct();
            var promised = project.PromisedDate!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var user in recipients)
            {
                await notifications.NotifyOnceAsync(user, Type, $"OS {order.Number} de {project.Number} está atrasada {risk.DelayDays} d.",
                    FabricationOrderService.Link + order.Id, $"os-late:{order.Id}:{promised}", cancellationToken);
            }
        }

        return late.Count;
    }
}

public sealed record LateOrdersArgs;

/// <summary>Execução por tenant do aviso "OS atrasada"; argumentos só de ids (o tenant).</summary>
public sealed class LateOrdersNoticeJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<LateOrdersNoticeJob> logger)
    : TenantJob<LateOrdersArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, LateOrdersArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<LateOrdersNotifier>().NotifyAsync(cancellationToken);
}

/// <summary>Recorrente global diário (07:00 de São Paulo, valor inicial; <c>Jobs:Cron:production-late-orders</c> muda) que enfileira o aviso por tenant ativo.</summary>
public sealed class LateOrdersRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<LateOrdersNoticeJob, LateOrdersArgs>(directory, jobs)
{
    public const string Id = "production-late-orders";
    public const string Cron = "0 7 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override LateOrdersArgs ArgsFor(TenantInfo tenant) => new();
}
