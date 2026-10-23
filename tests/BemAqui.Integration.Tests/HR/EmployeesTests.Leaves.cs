using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.HR;

/// <summary>CAD-04c1: afastamentos (LGPD art. 11), situação calculada, <c>Status</c> no <c>IEmployees</c> e job diário.</summary>
public partial class EmployeesTests
{
    private const string Canary = "CANARIO-AFASTAMENTO-7731";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Emp(Guid id, string suffix = "") => "/api/v1/hr/employees/" + id + suffix;

    private static object Leave(DateOnly? start, DateOnly? end, uint version, string? type = "Illness", string? reason = "Repouso") =>
        new { type, startDate = start is { } s ? Iso(s) : null, expectedEndDate = end is { } e ? Iso(e) : null, reason, version };

    private static async Task<uint> VersionAsync(HttpClient client, Guid id) => (await JsonAsync(await client.GetAsync(Emp(id)))).GetProperty("version").GetUInt32();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).GetProperty("code").GetString();

    private sealed class ShiftedClock(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + offset;
    }

    [Fact]
    public async Task Registrar_afastamento_valida_datas_sobreposicao_e_versao()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        var version = await VersionAsync(owner, id);
        var leaves = Emp(id, "/leaves");

        (await owner.PostAsJsonAsync(leaves, Leave(Today, Today.AddDays(3), version, type: null))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "tipo obrigatório");
        (await owner.PostAsJsonAsync(leaves, Leave(Today, Today.AddDays(-1), version))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "fim previsto antes do início (CA 1)");
        (await owner.PostAsJsonAsync(leaves, Leave(Today, Today.AddDays(3), version, reason: new string('x', 201)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "motivo até 200 (CA 5)");
        (await owner.PostAsJsonAsync(leaves, Leave(Today, Today.AddDays(3), version, type: "Vacation"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "férias não são afastamento");

        var created = await owner.PostAsJsonAsync(leaves, Leave(Today.AddDays(-2), Today.AddDays(5), version));
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var detail = await JsonAsync(created);
        detail.GetProperty("status").GetString().Should().Be("OnLeave", "hoje está entre o início e o fim previsto (CA 2)");
        var current = detail.GetProperty("version").GetUInt32();

        var overlap = await owner.PostAsJsonAsync(leaves, Leave(Today.AddDays(5), Today.AddDays(9), current, type: "Other"));
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict, "sobreposto (CA 1)");
        (await CodeAsync(overlap)).Should().Be(EmployeeService.LeaveOverlapCode);

        var stale = await owner.PostAsJsonAsync(leaves, Leave(Today.AddDays(30), Today.AddDays(40), version, type: "Military"));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict, "versão antiga do funcionário (ADR 0019 §2)");
        (await CodeAsync(stale)).Should().Be("concurrency_conflict");

        (await owner.PostAsJsonAsync(leaves, Leave(Today.AddDays(30), Today.AddDays(40), current, type: "Military"))).StatusCode.Should().Be(HttpStatusCode.OK, "período sem sobreposição");
        (await JsonAsync(await owner.GetAsync(Emp(id)))).GetProperty("status").GetString().Should().Be("OnLeave");
    }

    [Fact]
    public async Task Situacao_afastado_sem_detalhes_para_quem_nao_tem_a_permissao_sensivel()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("hr:view,edit");
        var position = await PositionAsync(owner);
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf(), name: "Paula Afastada " + Guid.NewGuid().ToString("N")[..5])));
        var future = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));

        (await viewer.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today, Today.AddDays(3), await VersionAsync(viewer, id)))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "registrar exige hr.view_sensitive");
        (await owner.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today.AddDays(-1), Today.AddDays(10), await VersionAsync(owner, id), "WorkAccident", Canary))).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync(Emp(future, "/leaves"), Leave(Today.AddDays(3), Today.AddDays(10), await VersionAsync(owner, future)))).EnsureSuccessStatusCode();

        var seen = await viewer.GetStringAsync(Emp(id));
        seen.Should().Contain("\"status\":\"OnLeave\"").And.NotContain("WorkAccident").And.NotContain(Canary).And.NotContain(Iso(Today.AddDays(10)), "só a situação, sem tipo, motivo nem datas (CA 4)");
        (await viewer.GetStringAsync(Emp(future))).Should().Contain("\"status\":\"Active\"", "antes do início a situação é Ativo (CA 2)");
        (await viewer.GetStringAsync("/api/v1/hr/employees?status=OnLeave&q=Paula")).Should().Contain("Paula Afastada");
        (await viewer.GetStringAsync("/api/v1/hr/employees?status=Active&q=Paula")).Should().NotContain("Paula Afastada");
        var timeline = await viewer.GetStringAsync(Emp(id, "/timeline"));
        timeline.Should().NotContain("Leave").And.NotContain(Canary, "a linha do tempo comum não traz afastamento");
        (await viewer.GetAsync(Emp(id, "/leaves/sensitive"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var before = await SensitiveReadsAsync(id, ownerId);
        var sensitive = await owner.GetAsync(Emp(id, "/leaves/sensitive"));
        sensitive.Headers.CacheControl!.NoStore.Should().BeTrue("CA 4");
        var leave = (await JsonAsync(sensitive)).EnumerateArray().Single();
        leave.GetProperty("type").GetString().Should().Be("WorkAccident");
        leave.GetProperty("reason").GetString().Should().Be(Canary);
        leave.GetProperty("expectedReturnDate").GetString().Should().Be(Iso(Today.AddDays(11)), "a volta prevista é o dia seguinte ao fim");
        (await SensitiveReadsAsync(id, ownerId)).Should().Be(before + 1, "a leitura sensível vai para a auditoria");

        await using var scope = await ScopeAsync();
        var summaries = await scope.ServiceProvider.GetRequiredService<IEmployees>().FindAsync([id, future]);
        summaries.Single(s => s.Id == id).Status.Should().Be(EmployeeSituation.OnLeave, "CA 3");
        summaries.Single(s => s.Id == id).IsActive.Should().BeTrue("afastado continua no quadro");
        summaries.Single(s => s.Id == future).Status.Should().Be(EmployeeSituation.Active);
        (await scope.ServiceProvider.GetRequiredService<IEmployees>().ActiveAsync()).Should().Contain(s => s.Id == id && s.Status == EmployeeSituation.OnLeave);
    }

    [Fact]
    public async Task Desligado_prevalece_e_nao_recebe_afastamento_novo()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        (await owner.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today.AddDays(-1), Today.AddDays(10), await VersionAsync(owner, id)))).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync(Emp(id, "/termination"), new { terminationDate = Iso(Today) })).EnsureSuccessStatusCode();

        (await JsonAsync(await owner.GetAsync(Emp(id)))).GetProperty("status").GetString().Should().Be("Terminated", "Desligado prevalece sobre Afastado (CA 2)");
        var refused = await owner.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today.AddDays(20), Today.AddDays(25), await VersionAsync(owner, id)));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var scope = await ScopeAsync();
        (await scope.ServiceProvider.GetRequiredService<IEmployees>().FindAsync([id])).Single().Status.Should().Be(EmployeeSituation.Terminated);
    }

    [Fact]
    public async Task Tipo_e_motivo_nao_vao_para_auditoria_log_nem_evento()
    {
        var sink = new MemorySink();
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)));
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        var position = await PositionAsync(client);
        var id = await CreatedAsync(await client.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        (await client.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today, Today.AddDays(4), await VersionAsync(client, id), "Maternity", Canary))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today, Today.AddDays(2), await VersionAsync(client, id), "Paternity", Canary))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await client.GetAsync(Emp(id, "/leaves/sensitive"));

        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>();
        var leaveId = (await scope.ServiceProvider.GetRequiredService<IHrDb>().EmployeeHistory.SingleAsync(h => h.EmployeeId == id && h.Kind == EmployeeHistoryKind.Leave)).Id;
        var audit = string.Join("\n", await db.AuditEntries.Where(a => a.EntityId == leaveId.ToString() || a.EntityId == id.ToString()).Select(a => a.Changes).ToListAsync());
        audit.Should().Contain("***").And.NotContain(Canary).And.NotContain("Maternity", "tipo e motivo mascarados no diff (CA 9)");
        var outbox = (await db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains("EmployeeLeave")).ToListAsync()).Where(m => m.Payload.Contains(id.ToString(), StringComparison.Ordinal)).ToList();
        outbox.Should().ContainSingle(m => m.Type.Contains(nameof(EmployeeLeaveStarted), StringComparison.Ordinal), "início hoje: evento na mesma transação (CA 7)");
        outbox.Should().OnlyContain(m => !m.Payload.Contains(Canary, StringComparison.Ordinal) && !m.Payload.Contains("Maternity", StringComparison.Ordinal), "evento só com id e datas (CA 6)");
        string events;
        lock (sink.Events)
        {
            events = string.Join("\n", sink.Events.Select(e => e.RenderMessage(CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString()))));
        }

        events.Should().NotContain(Canary, "o motivo nunca vai para o log (CA 5)");
    }

    [Fact]
    public async Task Job_diario_publica_inicio_e_fim_uma_vez_e_pula_tenant_sem_hr()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        (await owner.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today.AddDays(2), Today.AddDays(4), await VersionAsync(owner, id)))).EnsureSuccessStatusCode();
        (await OutboxForAsync(id)).Should().BeEmpty("início futuro não publica no registro (CA 7)");

        await RunLeaveEventsAsync(TimeSpan.FromDays(1));
        (await OutboxForAsync(id)).Should().BeEmpty("ainda antes do início");
        await RunLeaveEventsAsync(TimeSpan.FromDays(2));
        (await OutboxForAsync(id)).Should().Equal(nameof(EmployeeLeaveStarted));
        await RunLeaveEventsAsync(TimeSpan.FromDays(4));
        (await OutboxForAsync(id)).Should().Equal([nameof(EmployeeLeaveStarted)], "no último dia ainda está afastado");
        await RunLeaveEventsAsync(TimeSpan.FromDays(5));
        await RunLeaveEventsAsync(TimeSpan.FromDays(6));
        (await OutboxForAsync(id)).Should().Equal([nameof(EmployeeLeaveStarted), nameof(EmployeeLeaveEnded)], "no dia seguinte ao fim, uma vez só");

        // Tenant com o hr não liberado: o job do tenant não publica nada.
        var other = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        await using (var scope = await ScopeAsync())
        {
            var hr = scope.ServiceProvider.GetRequiredService<IHrDb>();
            hr.EmployeeHistory.Add(new EmployeeHistoryEntry { EmployeeId = other, Kind = EmployeeHistoryKind.Leave, EffectiveDate = Today.AddDays(-1), LeaveEndDate = Today.AddDays(3), LeaveType = EmployeeLeaveType.Other, CreatedAt = DateTimeOffset.UtcNow });
            await hr.SaveChangesAsync();
        }

        var job = ActivatorUtilities.CreateInstance<EmployeeLeaveEventsJob>(tenancy.Factory.Services);
        await SetHrReleasedAsync(false);
        try
        {
            await job.ExecuteAsync(tenancy.TenantA.Id, new EmployeeLeaveEventsArgs(), CancellationToken.None);
            (await OutboxForAsync(other)).Should().BeEmpty("módulo hr não liberado: o job pula o tenant");
        }
        finally
        {
            await SetHrReleasedAsync(true);
        }

        await job.ExecuteAsync(tenancy.TenantA.Id, new EmployeeLeaveEventsArgs(), CancellationToken.None);
        (await OutboxForAsync(other)).Should().Equal(nameof(EmployeeLeaveStarted));
    }

    [Fact]
    public async Task Tres_camadas_nos_endpoints_de_afastamento()
    {
        var (_, nobody) = await ActorAsync();
        var noGroup = await nobody.GetAsync(Emp(Guid.NewGuid(), "/leaves/sensitive"));
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(noGroup)).Should().Be("forbidden");
        var (_, viewer) = await ActorAsync("hr:view");
        (await viewer.PostAsJsonAsync(Emp(Guid.NewGuid(), "/leaves"), Leave(Today, Today, 0))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "registrar exige hr.edit");
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await SetHrReleasedAsync(false);
        try
        {
            var notReleased = await owner.GetAsync(Emp(Guid.NewGuid(), "/leaves/sensitive"));
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetHrReleasedAsync(true);
        }

        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/hr", StringComparison.Ordinal) == true && e.RoutePattern.RawText.Contains("/leaves", StringComparison.Ordinal)).ToList();
        // Registrar e ler (CAD-04c1); editar, encerrar antes e cancelar (CAD-04c1b).
        endpoints.Should().HaveCount(5);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("hr", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix + "hr.", StringComparison.Ordinal), endpoint.DisplayName);
        }
    }

    private async Task<int> SensitiveReadsAsync(Guid id, Guid userId)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.CountAsync(a => a.EntityId == id.ToString() && a.Action == AuditActions.SensitiveRead && a.UserId == userId);
    }

    /// <summary>Nomes dos eventos de afastamento do funcionário no outbox, em ordem.</summary>
    private async Task<List<string>> OutboxForAsync(Guid employeeId)
    {
        await using var scope = await ScopeAsync();
        var messages = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().OutboxMessages.AsNoTracking().Where(m => m.Type.Contains("EmployeeLeave")).OrderBy(m => m.OccurredAt).ToListAsync();
        return messages.Where(m => m.Payload.Contains(employeeId.ToString(), StringComparison.Ordinal))
            .Select(m => m.Type.Contains(nameof(EmployeeLeaveStarted), StringComparison.Ordinal) ? nameof(EmployeeLeaveStarted) : nameof(EmployeeLeaveEnded)).ToList();
    }

    private async Task RunLeaveEventsAsync(TimeSpan offset)
    {
        await using var scope = await ScopeAsync();
        await ActivatorUtilities.CreateInstance<EmployeeLeaveEvents>(scope.ServiceProvider, new ShiftedClock(offset)).RunAsync();
    }

    private async Task SetHrReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "hr").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }
}
