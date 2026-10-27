using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Finance.Application.Statements;

public sealed record InterStatementImportArgs;

/// <summary>
/// Importação diária do extrato pela API (BNK-03a CA 1): por tenant, só com o módulo <c>finance</c> liberado (conferido de novo aqui,
/// porque a liberação pode mudar entre o enfileiramento e a execução); cada conta com integração ativa é importada e o que falhar fica
/// no log. Executar duas vezes não duplica (índice único e lock da conta, CA 2).
/// </summary>
public sealed class InterStatementImportJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<InterStatementImportJob> logger)
    : TenantJob<InterStatementImportArgs>(directory, scopes, logger)
{
    public const string Module = "finance";

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, InterStatementImportArgs args, CancellationToken cancellationToken)
    {
        if (services.GetRequiredService<TenantContextHolder>().Resolved?.IsReleased(Module) != true)
        {
            return;
        }

        await services.GetRequiredService<BankStatementImportService>().RunScheduledAsync(cancellationToken);
    }
}

/// <summary>Recorrente global <c>inter-statement-import</c>: 06:30 de São Paulo, um job por tenant ativo com o <c>finance</c> liberado.</summary>
public sealed class InterStatementImportRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs, ITenantResolver resolver)
    : GlobalRecurringJob<InterStatementImportJob, InterStatementImportArgs>(directory, jobs)
{
    public const string Id = "inter-statement-import";
    public const string Cron = "30 6 * * *";

    protected override string Queue => JobQueues.Default;

    protected override InterStatementImportArgs ArgsFor(TenantInfo tenant) => new();

    protected override async Task<bool> ShouldEnqueueAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return (await resolver.FindByIdAsync(tenant.Id, cancellationToken))?.IsReleased(InterStatementImportJob.Module) == true;
    }
}
