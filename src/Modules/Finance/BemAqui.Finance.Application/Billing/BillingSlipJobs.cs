using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Finance.Application;

/// <summary>Argumentos dos jobs do boleto: só o id (ADR 0006); o pagador é lido do Parties no momento, nunca guardado no job.</summary>
public sealed record BillingSlipJobArgs(Guid BillingSlipId);

/// <summary>
/// Enfileira os jobs do boleto depois do commit (o pedido só sai se o comando gravou), sempre com o <c>tenantId</c> (<c>TenantJob</c>).
/// Sem Hangfire no processo (migrator), nada é enfileirado: "Tentar de novo" e a reconsulta cobrem.
/// </summary>
public interface IBillingSlipJobs
{
    Task EnqueueIssueAsync(Guid slipId, CancellationToken cancellationToken = default);

    /// <summary>Nova consulta da emissão assíncrona, depois de <paramref name="delay"/> (padrão 6: "Demorando").</summary>
    Task SchedulePollAsync(Guid slipId, TimeSpan delay, CancellationToken cancellationToken = default);

    Task EnqueueCancelAsync(Guid slipId, CancellationToken cancellationToken = default);

    /// <summary>Envio adiado pelo limite diário (CA 13).</summary>
    Task ScheduleEmailAsync(Guid slipId, DateTimeOffset at, CancellationToken cancellationToken = default);
}

public sealed partial class HangfireBillingSlipJobs(IAfterCommit afterCommit, TenantContextHolder tenant, IServiceProvider services, ILogger<HangfireBillingSlipJobs> logger) : IBillingSlipJobs
{
    public Task EnqueueIssueAsync(Guid slipId, CancellationToken cancellationToken = default) =>
        CreateAsync<IssueBillingSlipJob>(slipId, new EnqueuedState(JobQueues.Default), cancellationToken);

    public Task SchedulePollAsync(Guid slipId, TimeSpan delay, CancellationToken cancellationToken = default) =>
        CreateAsync<IssueBillingSlipJob>(slipId, new ScheduledState(delay), cancellationToken);

    public Task EnqueueCancelAsync(Guid slipId, CancellationToken cancellationToken = default) =>
        CreateAsync<CancelBillingSlipJob>(slipId, new EnqueuedState(JobQueues.Default), cancellationToken);

    public Task ScheduleEmailAsync(Guid slipId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
        CreateAsync<SendBillingSlipEmailJob>(slipId, new ScheduledState(at.UtcDateTime), cancellationToken);

    private Task CreateAsync<TJob>(Guid slipId, IState state, CancellationToken cancellationToken)
        where TJob : TenantJob<BillingSlipJobArgs>
    {
        if (tenant.Tenant is not { } info || services.GetService<IBackgroundJobClient>() is not { } jobs)
        {
            LogNoJobs(logger, typeof(TJob).Name, slipId);
            return Task.CompletedTask;
        }

        var tenantId = info.Id;
        var args = new BillingSlipJobArgs(slipId);
        return afterCommit.RunAsync(ct =>
        {
            jobs.Create<TJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), state);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {Job} do boleto {SlipId} não enfileirado: processo sem Hangfire ou fora de um tenant.")]
    private static partial void LogNoJobs(ILogger logger, string job, Guid slipId);
}

/// <summary>
/// Emissão no banco (CA 7 e 8): confere o módulo e a integração, cria a cobrança (ou acha a da tentativa anterior pelo "seu número"),
/// consulta até ter linha digitável e Pix, guarda o PDF e passa o boleto a <c>Issued</c>. O título não muda.
/// </summary>
public sealed class IssueBillingSlipJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<IssueBillingSlipJob> logger)
    : TenantJob<BillingSlipJobArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, BillingSlipJobArgs args, CancellationToken cancellationToken) =>
        services.GetRequiredService<BillingSlipProcessor>().IssueAsync(args.BillingSlipId, cancellationToken);
}

/// <summary>Cancelamento no banco (CA 16 e 17): o boleto fica <c>Cancelled</c> só depois da confirmação do Inter.</summary>
public sealed class CancelBillingSlipJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<CancelBillingSlipJob> logger)
    : TenantJob<BillingSlipJobArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, BillingSlipJobArgs args, CancellationToken cancellationToken) =>
        services.GetRequiredService<BillingSlipProcessor>().CancelAsync(args.BillingSlipId, cancellationToken);
}

/// <summary>Envio adiado pelo limite diário (CA 13), às 08:00 do dia seguinte.</summary>
public sealed class SendBillingSlipEmailJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<SendBillingSlipEmailJob> logger)
    : TenantJob<BillingSlipJobArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, BillingSlipJobArgs args, CancellationToken cancellationToken) =>
        services.GetRequiredService<BillingSlipProcessor>().SendScheduledEmailAsync(args.BillingSlipId, cancellationToken);
}
