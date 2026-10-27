using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.HR.Application.Documents;

/// <summary>Argumentos do lote (CA 2, §4.3 da revisão): só ids, nunca valor nem dado pessoal.</summary>
public sealed record PayslipBatchArgs(Guid PayrollRunId, Guid? RequestedBy);

/// <summary>"Baixar todos os holerites" acima de 20 (RH-02b CA 2): gera o ZIP no escopo do tenant e avisa quem pediu no sino.</summary>
public sealed class PayslipBatchJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<PayslipBatchJob> logger)
    : TenantJob<PayslipBatchArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, PayslipBatchArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        return services.GetRequiredService<PayslipDocumentService>().CompleteBatchAsync(args.PayrollRunId, args.RequestedBy, cancellationToken);
    }
}
