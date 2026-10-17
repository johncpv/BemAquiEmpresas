using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BemAqui.Architecture.Tests;

public class JobRulesTests
{
    private sealed class SendRemindersJob
    {
        public int Runs { get; private set; }

        public Task RunAsync()
        {
            Runs++;
            return Task.CompletedTask;
        }
    }

    private sealed class GoodJob(ITenantDirectory d, ITenantScopeFactory s) : TenantJob<int>(d, s, NullLogger.Instance)
    {
        protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, int args, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void Nenhum_job_da_solucao_foge_da_base() =>
        JobRules.JobsOutsideBase(Solution.Assemblies.SelectMany(a => a.GetTypes())).Should().BeEmpty();

    [Fact]
    public void Regra_acusa_job_sem_tenant_e_aceita_TenantJob()
    {
        JobRules.JobsOutsideBase([typeof(SendRemindersJob)]).Should().ContainSingle();
        JobRules.JobsOutsideBase([typeof(GoodJob)]).Should().BeEmpty();
    }
}
