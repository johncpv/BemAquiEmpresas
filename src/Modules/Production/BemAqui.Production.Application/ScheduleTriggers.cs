using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Contracts;
using BemAqui.Production.Contracts;
using Hangfire;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Production.Application;

/// <summary>
/// Pedido de recálculo do plano por evento (PLN-01b CA 1 a 3, ADR 0006: reação dentro do módulo, síncrona): os comandos do Production que
/// mudam as datas chamam antes do <c>SaveChanges</c> (ou dentro da transação); o pedido sai só depois do commit e nunca desfaz o comando.
/// </summary>
public interface IScheduleTrigger
{
    /// <param name="reason">Quem pediu (só para o log: "apontamento", "calendário", "aditivo"…), sem dado pessoal.</param>
    Task RequestAsync(string reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementação do <see cref="IScheduleTrigger"/> (PLN-01b regras, "Pedido de recálculo"): depois do commit, num escopo novo do mesmo
/// tenant (o DbContext do comando não é tocado), grava a marca "pendente" do tenant e, se ela não existia, enfileira o
/// <see cref="PlanningRecalculateJob"/> com o gatilho <c>Event</c> e só o <c>tenantId</c>. Com a marca já lá, o pedido se junta a ela
/// (no máximo 1 rodando e 1 pendente, CA 3). Módulo <c>production</c> não liberado: nada é pedido (CA 14). Falha no pedido só vai para o
/// log; o recorrente de hora em hora é a reserva. Sem Hangfire no processo (migrator e CLI de provisionamento rodam o seed sem servidor
/// de jobs), o <see cref="IBackgroundJobClient"/> é resolvido só na hora do pedido e, ausente, nada é marcado nem enfileirado: o
/// recálculo de hora em hora da API cobre o tenant.
/// </summary>
public sealed partial class ScheduleRecalculationTrigger(
    IAfterCommit afterCommit, TenantContextHolder tenant, ITenantScopeFactory scopes, IServiceProvider services, ILogger<ScheduleRecalculationTrigger> logger) : IScheduleTrigger
{
    public Task RequestAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved is not { } resolved || !resolved.IsReleased(PlanningService.Module))
        {
            return Task.CompletedTask;
        }

        if (services.GetService<IBackgroundJobClient>() is not { } jobs)
        {
            LogNoJobs(logger, reason, resolved.Tenant.Id);
            return Task.CompletedTask;
        }

        var info = resolved.Tenant;
        return afterCommit.RunAsync(async ct =>
        {
            try
            {
                await using var scope = await scopes.CreateScopeAsync(info, ct);
                if (await scope.ServiceProvider.GetRequiredService<ScheduleService>().TryMarkPendingAsync(ScheduleTrigger.Event, ct))
                {
                    var args = new PlanningRecalculateArgs(ScheduleTrigger.Event);
                    jobs.Create<PlanningRecalculateJob>(j => j.ExecuteAsync(info.Id, args, CancellationToken.None), new EnqueuedState(JobQueues.Default));
                }

                LogRequested(logger, reason, info.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, reason, info.Id);
            }
        }, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recálculo do plano pedido ({Reason}) no tenant {TenantId}.")]
    private static partial void LogRequested(ILogger logger, string reason, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recálculo do plano ({Reason}) não pedido no tenant {TenantId}: processo sem Hangfire (migrator).")]
    private static partial void LogNoJobs(ILogger logger, string reason, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "O pedido de recálculo do plano ({Reason}) falhou no tenant {TenantId}; o recorrente de hora em hora recalcula.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string reason, Guid tenantId);
}

/// <summary>
/// Eventos de outros módulos que mudam a data prometida ou a existência do projeto (PLN-01b CA 2): aditivo aprovado (COM-07, prazo) e
/// contrato cancelado. Idempotente pela inbox; o pedido é agrupado com os outros (a marca "pendente" é uma só), e a data prometida é lida
/// pelo <c>IProjects</c> no cálculo.
/// </summary>
public sealed class PlanningContractConsumers(IScheduleTrigger trigger) : IIntegrationEventConsumer<ContractAmended>, IIntegrationEventConsumer<ContractCancelled>
{
    public Task HandleAsync(ContractAmended integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return trigger.RequestAsync("aditivo", cancellationToken);
    }

    public Task HandleAsync(ContractCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return trigger.RequestAsync("contrato cancelado", cancellationToken);
    }
}
