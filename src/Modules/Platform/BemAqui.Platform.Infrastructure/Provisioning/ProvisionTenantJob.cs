using BemAqui.BuildingBlocks.Jobs;
using BemAqui.Platform.Application.Provisioning;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace BemAqui.Platform.Infrastructure.Provisioning;

/// <summary>
/// Provisionamento pela tela da plataforma (FND-03b CA 2 e 4): o mesmo serviço da CLI, em segundo plano, na fila
/// <c>maintenance</c>. O progresso fica em <c>provisioning_logs</c>, que a tela consulta.
/// </summary>
public sealed partial class ProvisionTenantJob(ProvisioningService provisioning, ILogger<ProvisionTenantJob> logger) : PlatformJob<ProvisionRequest>
{
    [Queue(JobQueues.Maintenance)]
    [AutomaticRetry(Attempts = 0)] // falha fica no log da etapa; o super admin decide "Retomar provisionamento"
    public override async Task ExecuteAsync(ProvisionRequest args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var result = await provisioning.ProvisionAsync(args, cancellationToken);
        if (!result.Success)
        {
            LogFailed(logger, args.Key, result.Error ?? string.Empty);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provisionamento do tenant {Tenant} não concluído: {Erro}")]
    private static partial void LogFailed(ILogger logger, string tenant, string erro);
}
