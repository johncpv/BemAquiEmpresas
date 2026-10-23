using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.HR.Application;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// CAD-04c1b: editar, encerrar antes e cancelar o afastamento, com rastro, eventos uma vez só e a hr.view_health (PO 22/10/2026).
/// Cada teste cria o próprio funcionário e filtra pelos ids que criou; as datas são relativas a hoje, sem sobreposição.
/// </summary>
public partial class EmployeesTests
{
    private static string LeaveUrl(Guid id, Guid leaveId, string suffix = "") => Emp(id, "/leaves/" + leaveId + suffix);

    private async Task<(Guid Id, HttpClient Owner)> OnLeaveEmployeeAsync(DateOnly start, DateOnly end, string type = "Other", string? reason = "Repouso")
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        (await owner.PostAsJsonAsync(Emp(id, "/leaves"), Leave(start, end, await VersionAsync(owner, id), type, reason))).EnsureSuccessStatusCode();
        return (id, owner);
    }

    private static async Task<List<JsonElement>> LeavesOfAsync(HttpClient client, Guid id) =>
        (await JsonAsync(await client.GetAsync(Emp(id, "/leaves/sensitive")))).EnumerateArray().ToList();

    private static async Task<Guid> OnlyLeaveIdAsync(HttpClient client, Guid id) => (await LeavesOfAsync(client, id)).Single().GetProperty("id").GetGuid();

    [Fact]
    public async Task Editar_afastamento_previsto_e_em_curso_com_as_validacoes_do_registro()
    {
        var (id, owner) = await OnLeaveEmployeeAsync(Today.AddDays(-2), Today.AddDays(5));
        var current = await OnlyLeaveIdAsync(owner, id);
        (await owner.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today.AddDays(20), Today.AddDays(25), await VersionAsync(owner, id), "Military"))).EnsureSuccessStatusCode();
        var future = (await LeavesOfAsync(owner, id)).Single(l => l.GetProperty("state").GetString() == "Scheduled").GetProperty("id").GetGuid();

        // Previsto: muda tipo, datas e motivo; o rastro guarda o antes e o depois (CA 1 e 7).
        var edited = await owner.PutAsJsonAsync(LeaveUrl(id, future), Leave(Today.AddDays(22), Today.AddDays(30), await VersionAsync(owner, id), "Paternity", "Nascimento"));
        edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        var leave = (await LeavesOfAsync(owner, id)).Single(l => l.GetProperty("id").GetGuid() == future);
        leave.GetProperty("type").GetString().Should().Be("Paternity");
        leave.GetProperty("startDate").GetString().Should().Be(Iso(Today.AddDays(22)));
        leave.GetProperty("reason").GetString().Should().Be("Nascimento");
        var change = leave.GetProperty("changes").EnumerateArray().Single();
        change.GetProperty("kind").GetString().Should().Be("Edited");
        change.GetProperty("oldStartDate").GetString().Should().Be(Iso(Today.AddDays(20)));
        change.GetProperty("oldExpectedEndDate").GetString().Should().Be(Iso(Today.AddDays(25)));
        change.GetProperty("newExpectedEndDate").GetString().Should().Be(Iso(Today.AddDays(30)));
        change.GetProperty("oldType").GetString().Should().Be("Military");
        change.GetProperty("newType").GetString().Should().Be("Paternity");

        var version = await VersionAsync(owner, id);
        var overlap = await owner.PutAsJsonAsync(LeaveUrl(id, future), Leave(Today.AddDays(4), Today.AddDays(30), version, "Paternity"));
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict, "sobrepõe o afastamento em curso");
        (await CodeAsync(overlap)).Should().Be(EmployeeService.LeaveOverlapCode);
        (await owner.PutAsJsonAsync(LeaveUrl(id, future), Leave(Today.AddDays(30), Today.AddDays(22), version, "Paternity"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "fim antes do início");
        (await owner.PutAsJsonAsync(LeaveUrl(id, future), Leave(Today.AddDays(22), Today.AddDays(30), version, "Paternity", new string('x', 201)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "motivo até 200");
        var stale = await owner.PutAsJsonAsync(LeaveUrl(id, future), Leave(Today.AddDays(23), Today.AddDays(30), version - 1, "Paternity"));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict, "versão antiga do funcionário (ADR 0019 §2)");
        (await CodeAsync(stale)).Should().Be("concurrency_conflict");

        // Em curso: o início fica hoje ou antes e o fim, hoje ou depois (CA 2).
        (await owner.PutAsJsonAsync(LeaveUrl(id, current), Leave(Today.AddDays(1), Today.AddDays(5), version))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o começo já saiu");
        (await owner.PutAsJsonAsync(LeaveUrl(id, current), Leave(Today.AddDays(-2), Today.AddDays(-1), version))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "terminar antes é pelo encerramento");
        var extended = await owner.PutAsJsonAsync(LeaveUrl(id, current), Leave(Today.AddDays(-3), Today.AddDays(8), version));
        extended.StatusCode.Should().Be(HttpStatusCode.OK, await extended.Content.ReadAsStringAsync());
        (await JsonAsync(extended)).GetProperty("status").GetString().Should().Be("OnLeave");

        // O afastamento precisa ser do funcionário da rota.
        var (other, _) = await OnLeaveEmployeeAsync(Today.AddDays(40), Today.AddDays(41));
        var missing = await owner.PutAsJsonAsync(LeaveUrl(other, current), Leave(Today.AddDays(-3), Today.AddDays(8), await VersionAsync(owner, other)));
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).Should().Be(EmployeeService.LeaveNotFoundCode);
    }

    [Fact]
    public async Task Desligado_nao_tem_afastamento_editado()
    {
        var (id, owner) = await OnLeaveEmployeeAsync(Today.AddDays(10), Today.AddDays(12));
        var leaveId = await OnlyLeaveIdAsync(owner, id);
        (await owner.PostAsJsonAsync(Emp(id, "/termination"), new { terminationDate = Iso(Today) })).EnsureSuccessStatusCode();
        var refused = await owner.PutAsJsonAsync(LeaveUrl(id, leaveId), Leave(Today.AddDays(11), Today.AddDays(12), await VersionAsync(owner, id), "Other"));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(refused)).Should().Be("employee_terminated");
    }

    [Fact]
    public async Task Encerrar_antes_recalcula_a_situacao_e_publica_o_fim_uma_vez()
    {
        var (id, owner) = await OnLeaveEmployeeAsync(Today.AddDays(-3), Today.AddDays(5));
        var leaveId = await OnlyLeaveIdAsync(owner, id);
        (await OutboxForAsync(id)).Should().Equal(nameof(EmployeeLeaveStarted));
        var version = await VersionAsync(owner, id);

        (await owner.PostAsJsonAsync(LeaveUrl(id, leaveId, "/end"), new { returnDate = Iso(Today.AddDays(1)), version })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "nunca depois de hoje (CA 4)");
        (await owner.PostAsJsonAsync(LeaveUrl(id, leaveId, "/end"), new { returnDate = Iso(Today.AddDays(-3)), version })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "depois do início");
        (await owner.PostAsJsonAsync(LeaveUrl(id, leaveId, "/end"), new { returnDate = (string?)null, version })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var ended = await owner.PostAsJsonAsync(LeaveUrl(id, leaveId, "/end"), new { returnDate = Iso(Today), version });
        ended.StatusCode.Should().Be(HttpStatusCode.OK, await ended.Content.ReadAsStringAsync());
        (await JsonAsync(ended)).GetProperty("status").GetString().Should().Be("Active", "a situação é recalculada na hora (CA 5)");
        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<IEmployees>().FindAsync([id])).Single().Status.Should().Be(EmployeeSituation.Active);
        }

        (await OutboxForAsync(id)).Should().Equal([nameof(EmployeeLeaveStarted), nameof(EmployeeLeaveEnded)], "o fim sai no encerramento");
        await RunLeaveEventsAsync(TimeSpan.FromDays(1));
        await RunLeaveEventsAsync(TimeSpan.FromDays(7));
        (await OutboxForAsync(id)).Should().Equal([nameof(EmployeeLeaveStarted), nameof(EmployeeLeaveEnded)], "o job diário não publica de novo");

        var leave = (await LeavesOfAsync(owner, id)).Single();
        leave.GetProperty("state").GetString().Should().Be("Ended");
        leave.GetProperty("expectedEndDate").GetString().Should().Be(Iso(Today.AddDays(-1)));
        var change = leave.GetProperty("changes").EnumerateArray().Single();
        change.GetProperty("kind").GetString().Should().Be("EndedEarly");
        change.GetProperty("oldExpectedEndDate").GetString().Should().Be(Iso(Today.AddDays(5)), "o previsto original fica no rastro");

        var again = await owner.PostAsJsonAsync(LeaveUrl(id, leaveId, "/end"), new { returnDate = Iso(Today), version = await VersionAsync(owner, id) });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(again)).Should().Be(EmployeeLeaveRules.EndedCode);
        (await owner.PutAsJsonAsync(LeaveUrl(id, leaveId), Leave(Today.AddDays(-3), Today.AddDays(5), await VersionAsync(owner, id), "Other"))).StatusCode.Should().Be(HttpStatusCode.Conflict, "encerrado não se edita");
    }

    [Fact]
    public async Task Cancelar_so_o_que_nao_comecou_com_motivo_e_sem_evento()
    {
        var (id, owner) = await OnLeaveEmployeeAsync(Today.AddDays(5), Today.AddDays(8));
        var leaveId = await OnlyLeaveIdAsync(owner, id);
        var version = await VersionAsync(owner, id);
        (await owner.PostAsJsonAsync(LeaveUrl(id, leaveId, "/cancel"), new { reason = " ", version })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "motivo obrigatório (CA 6)");
        var cancelled = await owner.PostAsJsonAsync(LeaveUrl(id, leaveId, "/cancel"), new { reason = "Lançado em duplicidade", version });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());

        var leave = (await LeavesOfAsync(owner, id)).Single();
        leave.GetProperty("state").GetString().Should().Be("Cancelled", "cancelamento lógico: o registro continua");
        leave.GetProperty("cancelledAt").ValueKind.Should().Be(JsonValueKind.String);
        leave.GetProperty("changes").EnumerateArray().Single().GetProperty("reason").GetString().Should().Be("Lançado em duplicidade");
        await RunLeaveEventsAsync(TimeSpan.FromDays(6));
        await RunLeaveEventsAsync(TimeSpan.FromDays(10));
        (await OutboxForAsync(id)).Should().BeEmpty("cancelado nunca publica");

        // O cancelado não bloqueia outro afastamento no mesmo período nem conta na situação.
        (await owner.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today.AddDays(5), Today.AddDays(8), await VersionAsync(owner, id), "Other"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await owner.PostAsJsonAsync(LeaveUrl(id, leaveId, "/cancel"), new { reason = "De novo", version = await VersionAsync(owner, id) });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(again)).Should().Be(EmployeeLeaveRules.CancelledCode);

        var (started, startedOwner) = await OnLeaveEmployeeAsync(Today.AddDays(-1), Today.AddDays(3));
        var refused = await startedOwner.PostAsJsonAsync(LeaveUrl(started, await OnlyLeaveIdAsync(startedOwner, started), "/cancel"), new { reason = "Errado", version = await VersionAsync(startedOwner, started) });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "já iniciado não se exclui, só se encerra");
        (await CodeAsync(refused)).Should().Be(EmployeeLeaveRules.StartedCode);
        (await JsonAsync(await startedOwner.GetAsync(Emp(started)))).GetProperty("status").GetString().Should().Be("OnLeave");
    }

    [Fact]
    public async Task Sem_view_health_nenhum_tipo_nem_motivo_so_afastado_e_as_datas()
    {
        var (id, owner) = await OnLeaveEmployeeAsync(Today.AddDays(-1), Today.AddDays(4), "Illness", Canary);
        (await owner.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today.AddDays(30), Today.AddDays(35), await VersionAsync(owner, id), "Maternity", "Licença"))).EnsureSuccessStatusCode();
        var (_, rh) = await ActorAsync("hr:view,edit,view_sensitive");

        var raw = await rh.GetStringAsync(Emp(id, "/leaves/sensitive"));
        raw.Should().NotContain(Canary).And.NotContain("Illness").And.NotContain("Maternity").And.NotContain("Licença", "sem hr.view_health, nenhum tipo nem motivo (CA 8, PO 22/10/2026)");
        var leaves = await LeavesOfAsync(rh, id);
        var illness = leaves.Single(l => l.GetProperty("startDate").GetString() == Iso(Today.AddDays(-1)));
        illness.GetProperty("healthProtected").GetBoolean().Should().BeTrue();
        illness.GetProperty("type").ValueKind.Should().Be(JsonValueKind.Null);
        illness.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        illness.GetProperty("expectedEndDate").GetString().Should().Be(Iso(Today.AddDays(4)), "as datas aparecem");
        var maternity = leaves.Single(l => l.GetProperty("startDate").GetString() == Iso(Today.AddDays(30)));
        maternity.GetProperty("type").ValueKind.Should().Be(JsonValueKind.Null, "nenhum tipo sem hr.view_health, nem os que não são de saúde");
        maternity.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        maternity.GetProperty("healthProtected").GetBoolean().Should().BeTrue();
        maternity.GetProperty("startDate").GetString().Should().Be(Iso(Today.AddDays(30)), "as datas aparecem");
        var ownerView = await LeavesOfAsync(owner, id);
        ownerView.Should().Contain(l => l.GetProperty("type").GetString() == "Illness", "o Proprietário tem a ação");
        ownerView.Should().Contain(l => l.GetProperty("type").GetString() == "Maternity" && l.GetProperty("reason").GetString() == "Licença");

        var illnessId = illness.GetProperty("id").GetGuid();
        var maternityId = maternity.GetProperty("id").GetGuid();
        (await rh.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today.AddDays(50), Today.AddDays(51), await VersionAsync(rh, id), "WorkAccident"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "registrar tipo de saúde exige hr.view_health");
        (await rh.PutAsJsonAsync(LeaveUrl(id, illnessId), Leave(Today.AddDays(-1), Today.AddDays(6), await VersionAsync(rh, id), "Other"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "alterar o de saúde exige hr.view_health");
        (await rh.PutAsJsonAsync(LeaveUrl(id, maternityId), Leave(Today.AddDays(30), Today.AddDays(35), await VersionAsync(rh, id), "Illness"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "passar a saúde exige hr.view_health");
        (await rh.PutAsJsonAsync(LeaveUrl(id, maternityId), Leave(Today.AddDays(31), Today.AddDays(36), await VersionAsync(rh, id), "Maternity", "Licença"))).StatusCode.Should().Be(HttpStatusCode.OK, "os demais tipos seguem com hr.view_sensitive");
        (await rh.PostAsJsonAsync(LeaveUrl(id, illnessId, "/end"), new { returnDate = Iso(Today), version = await VersionAsync(rh, id) })).StatusCode.Should().Be(HttpStatusCode.OK, "encerrar só mexe nas datas, que ele vê");
        (await rh.GetStringAsync(Emp(id, "/leaves/sensitive"))).Should().NotContain("\"oldType\":\"").And.NotContain("\"newType\":\"", "o rastro também não leva tipo");
        (await rh.PostAsJsonAsync(LeaveUrl(id, maternityId, "/cancel"), new { reason = "Lançado errado", version = await VersionAsync(rh, id) })).StatusCode.Should().Be(HttpStatusCode.OK, "cancelar não exige hr.view_health");
        (await rh.GetStringAsync(Emp(id, "/leaves/sensitive"))).Should().NotContain("Lançado errado", "o motivo do cancelamento segue a mesma regra");

        // Sem hr.view_sensitive, nada dos afastamentos, como na CAD-04c1.
        var (_, viewer) = await ActorAsync("hr:view,edit");
        (await viewer.PutAsJsonAsync(LeaveUrl(id, maternityId), Leave(Today.AddDays(31), Today.AddDays(36), await VersionAsync(viewer, id), "Maternity"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync(LeaveUrl(id, maternityId, "/cancel"), new { reason = "x", version = await VersionAsync(viewer, id) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Seed_patch_da_view_health_entra_no_RH_uma_vez()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        var hr = await db.Groups.AsNoTracking().SingleAsync(g => g.SeedKey == "hr");
        (await db.SeedPatches.AsNoTracking().CountAsync(p => p.Key == "hr-view-health")).Should().Be(1);
        (await db.GroupPermissions.AsNoTracking().CountAsync(p => p.GroupId == hr.Id && p.ModuleCode == "hr" && p.Action == "view_health")).Should().Be(1);
        await new BemAqui.Identity.Infrastructure.SeedPatchSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        (await db.GroupPermissions.AsNoTracking().CountAsync(p => p.GroupId == hr.Id && p.ModuleCode == "hr" && p.Action == "view_health")).Should().Be(1, "rodar de novo não duplica");
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        (await owner.GetStringAsync("/api/v1/me/permissions")).Should().Contain("hr.view_health", "o Proprietário recebe a ação");
    }

    [Fact]
    public async Task Mudancas_do_afastamento_sem_tipo_nem_motivo_na_auditoria_no_log_e_no_evento()
    {
        var sink = new MemorySink();
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)));
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        var position = await PositionAsync(client);
        var id = await CreatedAsync(await client.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        (await client.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today.AddDays(-2), Today.AddDays(6), await VersionAsync(client, id), "Paternity", "Primeiro"))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(Emp(id, "/leaves"), Leave(Today.AddDays(20), Today.AddDays(22), await VersionAsync(client, id), "Military", "Segundo"))).EnsureSuccessStatusCode();
        var leaves = await LeavesOfAsync(client, id);
        var current = leaves.Single(l => l.GetProperty("state").GetString() == "Current").GetProperty("id").GetGuid();
        var future = leaves.Single(l => l.GetProperty("state").GetString() == "Scheduled").GetProperty("id").GetGuid();

        (await client.PutAsJsonAsync(LeaveUrl(id, current), Leave(Today.AddDays(-2), Today.AddDays(7), await VersionAsync(client, id), "Maternity", Canary))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(LeaveUrl(id, current, "/end"), new { returnDate = Iso(Today), version = await VersionAsync(client, id) })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync(LeaveUrl(id, future, "/cancel"), new { reason = Canary, version = await VersionAsync(client, id) })).EnsureSuccessStatusCode();

        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>();
        var ids = (await scope.ServiceProvider.GetRequiredService<IHrDb>().EmployeeHistory.AsNoTracking().Where(h => h.EmployeeId == id).Select(h => h.Id).ToListAsync())
            .Select(g => g.ToString()).Append(id.ToString()).ToList();
        ids.Should().HaveCountGreaterThan(4, "dois afastamentos, três mudanças e o funcionário");
        var audit = string.Join("\n", await db.AuditEntries.AsNoTracking().Where(a => ids.Contains(a.EntityId)).Select(a => a.Changes).ToListAsync());
        audit.Should().Contain("***").And.NotContain(Canary).And.NotContain("Maternity").And.NotContain("Military", "tipo e motivo mascarados no antes e depois (CA 7)");
        var outbox = (await db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains("EmployeeLeave")).ToListAsync()).Where(m => m.Payload.Contains(id.ToString(), StringComparison.Ordinal)).ToList();
        outbox.Should().HaveCount(2).And.OnlyContain(m => !m.Payload.Contains(Canary, StringComparison.Ordinal) && !m.Payload.Contains("Maternity", StringComparison.Ordinal), "evento só com id e datas");
        string events;
        lock (sink.Events)
        {
            events = string.Join("\n", sink.Events.Select(e => e.RenderMessage(CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString()))));
        }

        events.Should().NotContain(Canary, "o motivo nunca vai para o log");
    }
}
