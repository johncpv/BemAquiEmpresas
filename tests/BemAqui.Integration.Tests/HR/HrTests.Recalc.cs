using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.HR.Application;
using BemAqui.Identity.Contracts;
using BemAqui.Integration.Tests.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.HR;

public partial class HrTests
{
    [Fact]
    public async Task Mudar_o_percentual_de_encargos_recalcula_os_calculados_e_nao_os_ajustados()
    {
        var owner = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var schedule = await SeedScheduleAsync(owner);
        var calculated = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/positions", Position(Unique("Eletricista"), schedule)));
        var adjusted = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/positions", Position(Unique("Supervisor"), schedule)));
        foreach (var id in new[] { calculated, adjusted })
        {
            (await owner.PutAsJsonAsync($"/api/v1/hr/positions/{id}/salary", new { salaryMin = 3300m, salaryMax = 3300m, referenceSalary = 3300m })).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await owner.PutAsJsonAsync($"/api/v1/hr/positions/{adjusted}/hourly-cost", new { value = 40m });
        var summary = await JsonAsync(await owner.GetAsync("/api/v1/hr/positions/summary"));
        summary.GetProperty("calculated").GetInt32().Should().BeLessThan(summary.GetProperty("total").GetInt32(), "o ajustado não conta (CA 7)");

        (await owner.PutAsJsonAsync("/api/v1/settings/parameters/payroll-charges", new { pct = 100m })).StatusCode.Should().Be(HttpStatusCode.OK);
        try
        {
            decimal cost = 0;
            for (var i = 0; i < 60 && cost != 30m; i++)
            {
                await Task.Delay(250);
                cost = (await JsonAsync(await owner.GetAsync($"/api/v1/hr/positions/{calculated}"))).GetProperty("hourlyCost").GetDecimal();
            }

            cost.Should().Be(30m, "3.300 × 2,00 ÷ 220, pelo evento PayrollChargesChanged (CA 6)");
            (await JsonAsync(await owner.GetAsync($"/api/v1/hr/positions/{adjusted}"))).GetProperty("hourlyCost").GetDecimal().Should().Be(40m, "o ajustado à mão não muda");

            // A mesma mensagem de novo não muda nada nem gera histórico novo.
            await using var scope = await ScopeAsync();
            var audit = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries;
            var before = await audit.CountAsync(a => a.EntityId == calculated.ToString());
            await scope.ServiceProvider.GetRequiredService<PayrollChargesChangedHandler>().HandleAsync(new PayrollChargesChanged(tenancy.TenantA.Id, 80m, 100m, DateTimeOffset.UtcNow), CancellationToken.None);
            (await audit.CountAsync(a => a.EntityId == calculated.ToString())).Should().Be(before);
        }
        finally
        {
            await owner.PutAsJsonAsync("/api/v1/settings/parameters/payroll-charges", new { pct = 80m });
        }
    }

    [Fact]
    public async Task Salario_canario_nao_aparece_na_auditoria_nem_no_log()
    {
        var sink = new MemorySink();
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)));
        var hr = await ActorAsync("hr");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = hr.DefaultRequestHeaders.Authorization;
        var id = await CreatedAsync(await client.PostAsJsonAsync("/api/v1/hr/positions", Position(Unique("Canário"), null, 220m)));
        await client.PutAsJsonAsync($"/api/v1/hr/positions/{id}/salary", new { salaryMin = 98765.43m, salaryMax = 98765.43m, referenceSalary = 98765.43m });
        await client.GetAsync($"/api/v1/hr/positions/{id}/sensitive");

        await using var scope = await ScopeAsync();
        var audit = string.Join("\n", await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.Where(a => a.EntityId == id.ToString()).Select(a => a.Changes).ToListAsync());
        audit.Should().Contain("***").And.NotContain("98765", "salário mascarado na auditoria (CA 14)");
        string events;
        lock (sink.Events)
        {
            events = string.Join("\n", sink.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString()))));
        }

        events.Should().NotContain("98765", "nem no log");
    }
}
