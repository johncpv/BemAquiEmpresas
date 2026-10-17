using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BemAqui.Integration.Tests.Messaging;

public sealed record ExpireQuotesArgs(DateOnly ReferenceDate);

/// <summary>Job de exemplo: registra em qual tenant e escopo rodou.</summary>
public sealed class ExpireQuotesJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ExpireQuotesJob> logger)
    : TenantJob<ExpireQuotesArgs>(directory, scopes, logger)
{
    public List<(Guid TenantId, DateOnly Date, IServiceProvider Services)> Runs { get; } = [];

    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, ExpireQuotesArgs args, CancellationToken cancellationToken)
    {
        Runs.Add((tenant.Id, args.ReferenceDate, services));
        return Task.CompletedTask;
    }
}

public sealed class ExpireQuotesEveryDay(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<ExpireQuotesJob, ExpireQuotesArgs>(directory, jobs)
{
    protected override string Queue => JobQueues.Maintenance;

    protected override ExpireQuotesArgs ArgsFor(TenantInfo tenant) => new(new DateOnly(2026, 10, 17));
}

public sealed class CapturingJobClient : IBackgroundJobClient
{
    public List<(Job Job, IState State)> Created { get; } = [];

    public string Create(Job job, IState state)
    {
        Created.Add((job, state));
        return Created.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}

/// <summary>FND-07 CA 15 a 17: TenantJob com escopo do tenant, tenant suspenso não roda, recorrente global por tenant ativo.</summary>
public class JobsTests
{
    private static readonly TenantInfo Active = new(Guid.CreateVersion7(), "konebras", TenantStatus.Active);
    private static readonly TenantInfo Other = new(Guid.CreateVersion7(), "outra", TenantStatus.Active);
    private static readonly TenantInfo Suspended = new(Guid.CreateVersion7(), "suspensa", TenantStatus.Suspended);

    private static (ExpireQuotesJob Job, FakeTenantDirectory Directory) CreateJob()
    {
        var directory = new FakeTenantDirectory();
        directory.Tenants.AddRange([Active, Other, Suspended]);
        var root = new ServiceCollection().BuildServiceProvider();
        return (new ExpireQuotesJob(directory, new SameDatabaseScopeFactory(root), NullLogger<ExpireQuotesJob>.Instance), directory);
    }

    [Fact]
    public async Task Job_roda_no_escopo_do_tenant_recebido()
    {
        var (job, _) = CreateJob();

        await job.ExecuteAsync(Active.Id, new ExpireQuotesArgs(new DateOnly(2026, 10, 17)), CancellationToken.None);
        await job.ExecuteAsync(Other.Id, new ExpireQuotesArgs(new DateOnly(2026, 10, 17)), CancellationToken.None);

        job.Runs.Select(r => r.TenantId).Should().Equal(Active.Id, Other.Id);
        job.Runs[0].Services.Should().NotBeSameAs(job.Runs[1].Services, "cada execução abre escopo próprio");
    }

    [Fact]
    public async Task Tenant_suspenso_ou_inexistente_nao_roda()
    {
        var (job, _) = CreateJob();

        await job.ExecuteAsync(Suspended.Id, new ExpireQuotesArgs(default), CancellationToken.None);
        await job.ExecuteAsync(Guid.CreateVersion7(), new ExpireQuotesArgs(default), CancellationToken.None);

        job.Runs.Should().BeEmpty();
    }

    [Fact]
    public async Task Job_sem_tenantId_falha()
    {
        var (job, _) = CreateJob();

        var executar = () => job.ExecuteAsync(Guid.Empty, new ExpireQuotesArgs(default), CancellationToken.None);

        await executar.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Recorrente_global_enfileira_um_por_tenant_ativo_na_fila_certa()
    {
        var (_, directory) = CreateJob();
        var client = new CapturingJobClient();

        var enqueued = await new ExpireQuotesEveryDay(directory, client).ExecuteAsync(CancellationToken.None);

        enqueued.Should().Be(2);
        client.Created.Should().AllSatisfy(c =>
        {
            c.Job.Type.Should().Be<ExpireQuotesJob>();
            c.Job.Method.Name.Should().Be(nameof(ExpireQuotesJob.ExecuteAsync));
            ((EnqueuedState)c.State).Queue.Should().Be(JobQueues.Maintenance);
        });
        client.Created.Select(c => (Guid)c.Job.Args[0]).Should().BeEquivalentTo([Active.Id, Other.Id]);
    }
}
