using System.Diagnostics;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>Seeder que falha na primeira execução e conta quantas vezes gravou (idempotência e retomada).</summary>
public sealed class FlakySeeder : ITenantSeeder
{
    private static int _calls;

    public static int Calls { get => _calls; set => _calls = value; }
    public static bool FailNext { get; set; }

    public int Order => 0;

    public Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("seed indisponível (falha proposital)");
        }

        Interlocked.Increment(ref _calls);
        return Task.CompletedTask;
    }
}

/// <summary>FND-03a CA 1 a 8: provisionamento em menos de 1 min, logs por etapa, retomada idempotente, roles e segredo.</summary>
public partial class ProvisioningTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private async Task<ProvisioningResult> ProvisionAsync(ProvisionRequest request, IServiceProvider? services = null)
    {
        await using var scope = (services ?? tenancy.Factory.Services).CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProvisioningService>().ProvisionAsync(request);
    }

    private async Task<List<ProvisioningLog>> LogsAsync(Guid tenantId)
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ProvisioningLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId).OrderBy(l => l.At).ThenBy(l => l.Id).ToListAsync();
    }

    [Fact]
    public async Task Provisiona_em_menos_de_1_minuto_com_log_de_cada_etapa()
    {
        var watch = Stopwatch.StartNew();
        var result = await ProvisionAsync(new ProvisionRequest("empresa-c", "Empresa C Ltda", "11.222.333/0001-81", "c.localhost", "dono@c.localhost", "Piloto"));
        watch.Stop();

        result.Success.Should().BeTrue(result.Error);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(1));
        result.NextStep.Should().Contain("dono@c.localhost").And.Contain("https://c.localhost");

        var tenant = await tenancy.FindAsync("empresa-c");
        tenant.Status.Should().Be(TenantLifecycle.Active);
        tenant.SchemaVersion.Should().NotBeNullOrEmpty();
        var logs = await LogsAsync(tenant.Id);
        logs.Where(l => l.Status == ProvisioningStatus.Succeeded).Select(l => l.Step).Should().Equal(ProvisioningSteps.Ordered);
        logs.Should().OnlyContain(l => l.At.Offset == TimeSpan.Zero);
    }

    [Fact]
    public async Task Chave_ou_host_ja_usados_sao_recusados_antes_de_criar_qualquer_coisa()
    {
        var mesmaChave = await ProvisionAsync(new ProvisionRequest("empresa-a", "Outra", null, "outra.localhost", "x@x.com", null));
        var mesmoHost = await ProvisionAsync(new ProvisionRequest("empresa-z", "Outra", null, "a.localhost", "x@x.com", null));

        mesmaChave.Success.Should().BeFalse();
        mesmaChave.Error.Should().Contain("Já existe");
        mesmoHost.Error.Should().Contain("já está em uso");
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants.AnyAsync(t => t.Key == "empresa-z")).Should().BeFalse();
    }

    [Theory]
    [InlineData("A", "Nome", "h.localhost", "a@a.com", "Chave inválida")]
    [InlineData("valida", "", "h.localhost", "a@a.com", "razão social")]
    [InlineData("valida", "Nome", "nao e host", "a@a.com", "Endereço inválido")]
    [InlineData("valida", "Nome", "h.localhost", "sem-arroba", "e-mail")]
    public async Task Dados_invalidos_sao_recusados(string key, string name, string host, string email, string expected) =>
        (await ProvisionAsync(new ProvisionRequest(key, name, null, host, email, null))).Error.Should().Contain(expected);
}
