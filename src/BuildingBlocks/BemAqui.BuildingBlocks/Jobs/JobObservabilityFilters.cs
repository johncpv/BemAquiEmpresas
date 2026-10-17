using BemAqui.BuildingBlocks.Observability;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Serilog.Context;

namespace BemAqui.BuildingBlocks.Jobs;

/// <summary>Todo log de job leva JobId, tipo do job e TenantId (primeiro argumento do TenantJob) — FND-12 CA 2.</summary>
public sealed class JobLogContextFilter : JobFilterAttribute, IServerFilter
{
    private const string Key = "bemaqui.logcontext";

    public void OnPerforming(PerformingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var job = context.BackgroundJob.Job;
        var tenantId = job.Args.Count > 0 && job.Args[0] is Guid id ? id : (Guid?)null;
        var disposables = new List<IDisposable>
        {
            LogContext.PushProperty("JobId", context.BackgroundJob.Id),
            LogContext.PushProperty("JobType", job.Type.Name),
        };
        if (tenantId is not null)
        {
            disposables.Add(LogContext.PushProperty("TenantId", tenantId));
        }

        context.Items[Key] = disposables;
    }

    public void OnPerformed(PerformedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items.TryGetValue(Key, out var value) && value is List<IDisposable> disposables)
        {
            disposables.ForEach(d => d.Dispose());
        }
    }
}

/// <summary>Job que esgotou as tentativas (estado Failed) gera alerta aos super admins — FND-12 CA 6.</summary>
public sealed class JobFailureAlertFilter(IAlertSink alerts, AlertOptions options) : JobFilterAttribute, IElectStateFilter
{
    public void OnStateElection(ElectStateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CandidateState is not FailedState failed)
        {
            return;
        }

        var job = context.BackgroundJob.Job;
        var tenant = job.Args.Count > 0 && job.Args[0] is Guid id ? id.ToString() : null;
        var link = options.PlatformBaseUrl is { Length: > 0 } baseUrl ? baseUrl.TrimEnd('/') + "/hangfire/jobs/details/" + context.BackgroundJob.Id : null;
        var alert = new Alert(AlertKinds.JobFailed, tenant, "Job " + job.Type.Name + " falhou e esgotou as tentativas", failed.Exception?.Message, link);
        _ = Task.Run(() => alerts.RaiseAsync(alert));
    }
}
