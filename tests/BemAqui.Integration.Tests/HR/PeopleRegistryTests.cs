using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Infrastructure.Catalog;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// EXE-RH: Cargos e Funcionários do RH pelo recurso <c>hr.people_registry</c> (perfil Serviços) com o módulo <c>hr</c> fora do plano. Tenant
/// próprio: os testes ligam e desligam o perfil Serviços e o <c>hr</c> no tenant A (que nasce só com <c>factory</c>, como a Konebras).
/// </summary>
public class PeopleRegistryTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Hr = "/api/v1/hr";
    private const string HealthCanary = "CANARIO-SAUDE-EXERH";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Perfil Serviços ligado ou não e o <c>hr</c> liberado ou não no tenant A.</summary>
    private async Task StateAsync(bool services, bool hrReleased)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            var modules = scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Application.Modules.TenantModuleService>();
            var profiles = await modules.GetProfilesAsync(tenancy.TenantA.Id);
            var has = profiles.Any(p => p.Code == BusinessProfileCatalog.Services);
            if (services != has)
            {
                var result = services
                    ? await modules.ApplyProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Services, null, null)
                    : await modules.RemoveProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Services, null, null);
                result.IsSuccess.Should().BeTrue();
            }

            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "hr")
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, hrReleased));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    private async Task<List<(string Module, bool Enabled, bool Exception)>> TenantModulesAsync()
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.AsNoTracking()
                .Where(m => m.TenantId == tenancy.TenantA.Id).OrderBy(m => m.ModuleCode).ToListAsync())
            .Select(m => (m.ModuleCode, m.Enabled, m.IsException)).ToList();
    }

    /// <summary>Usuário no Proprietário ou num grupo com as ações de <c>hr</c> dadas ("hr:view,edit").</summary>
    private async Task<HttpClient> ActorAsync(string seedKeyOrGrants)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKeyOrGrants.StartsWith("hr:", StringComparison.Ordinal))
            {
                var group = new Group { Name = "Pessoas " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "hr", Enabled = true });
                foreach (var action in seedKeyOrGrants[3..].Split(','))
                {
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "hr", Action = action });
                }

                groupId = group.Id;
            }
            else
            {
                groupId = await db.Groups.Where(g => g.SeedKey == seedKeyOrGrants).Select(g => g.Id).SingleAsync();
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private static async Task<Guid> PositionAsync(HttpClient client, decimal? monthlyHours = 200m)
    {
        var created = await client.PostAsJsonAsync(Hr + "/positions",
            new { name = "Técnico " + Guid.NewGuid().ToString("N")[..6], cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = (Guid?)null, monthlyHours });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> EmployeeAsync(HttpClient client, Guid positionId)
    {
        var created = await client.PostAsJsonAsync(Hr + "/employees", new
        {
            name = "Pessoa " + Guid.NewGuid().ToString("N")[..6], cpf = NewCpf(), birthDate = "1990-05-10", admissionDate = "2024-03-01", positionId,
            workScheduleId = (Guid?)null, baseSalary = 3000m, registration = (string?)null,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("employee").GetProperty("id").GetGuid();
    }

    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    private static async Task<JsonElement> HrModuleAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/v1/me/permissions"))).GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("code").GetString() == "hr");

    [Fact]
    public async Task So_com_o_recurso_cargos_e_funcionarios_abrem_e_o_resto_do_RH_responde_nao_liberado()
    {
        try
        {
            await StateAsync(services: true, hrReleased: false);
            var before = await TenantModulesAsync();
            var owner = await ActorAsync(Group.OwnerSeedKey);

            // CA 1: Cargos e Funcionários (e as exportações da lista) funcionam como liberados.
            var position = await PositionAsync(owner);
            var employee = await EmployeeAsync(owner, position);
            foreach (var path in new[] { "/positions", "/positions/summary", $"/positions/{position}", "/employees", $"/employees/{employee}", $"/employees/{employee}/timeline", "/positions/export?format=xlsx", "/employees/export?format=xlsx" })
            {
                (await owner.GetAsync(Hr + path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
            }

            (await owner.PostAsJsonAsync($"{Hr}/employees/{employee}/termination", new { terminationDate = "2026-11-01" })).StatusCode.Should().Be(HttpStatusCode.OK, "desligamento desativa o login (USR-01)");

            // CA 1: o resto do RH continua 403 module_not_released, mesmo para o Proprietário.
            foreach (var path in new[]
                     {
                         "/schedules", "/unions", "/parameters", "/payroll-runs", "/payroll-items", "/vacations", "/time-entries", "/accountant-exports",
                         $"/employees/{employee}/leaves/sensitive", $"/employees/{employee}/vacations", $"/employees/{employee}/time-entries", $"/employees/{employee}/bank-account/status",
                     })
            {
                var response = await owner.GetAsync(Hr + path);
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
                (await CodeAsync(response)).Should().Be("module_not_released", path);
            }

            var salary = await owner.PostAsJsonAsync($"{Hr}/employees/{employee}/salary", new { newSalary = 3500m, effectiveDate = (string?)null, reason = "Reajuste" });
            (await CodeAsync(salary)).Should().Be("module_not_released", "reajuste é da folha");
            (await CodeAsync(await owner.GetAsync("/api/v1/me/hr/vacations"))).Should().Be("module_not_released");

            // CA 2: /me/permissions marca o RH como aberto em parte; a ajuda lista Cargos e Funcionários e esconde a folha.
            var module = await HrModuleAsync(owner);
            module.GetProperty("released").GetBoolean().Should().BeFalse();
            module.GetProperty("partial").GetBoolean().Should().BeTrue();
            module.GetProperty("hidden").GetBoolean().Should().BeFalse();
            module.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).Should().Contain(["view", "create", "edit", "view_sensitive", "view_health"]);
            (await owner.GetAsync("/api/v1/ajuda/rh-cargos")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await owner.GetAsync("/api/v1/ajuda/rh-funcionarios")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await owner.GetAsync("/api/v1/ajuda/rh-folha")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await owner.GetAsync("/api/v1/ajuda/rh-ponto-marcacoes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // O recurso não grava nada em tenant_modules.
            (await TenantModulesAsync()).Should().Equal(before);

            // CA 7: sem o recurso e sem o hr no plano, Cargos e Funcionários também respondem 403 module_not_released.
            await StateAsync(services: false, hrReleased: false);
            foreach (var path in new[] { "/positions", "/employees", "/positions/export?format=xlsx" })
            {
                var response = await owner.GetAsync(Hr + path);
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
                (await CodeAsync(response)).Should().Be("module_not_released", path);
            }

            (await HrModuleAsync(owner)).GetProperty("partial").GetBoolean().Should().BeFalse();
            (await owner.GetAsync("/api/v1/ajuda/rh-cargos")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await StateAsync(services: false, hrReleased: true);
        }
    }

    [Fact]
    public async Task Pelo_recurso_valem_as_camadas_2_e_3_o_salario_exige_view_sensitive_e_a_saude_view_health()
    {
        // Afastamento de saúde gravado antes, com o RH completo: pelo recurso, ele só pode sair pela exportação da lista.
        await StateAsync(services: false, hrReleased: true);
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner, monthlyHours: 220m);
        var sick = await EmployeeAsync(owner, position);
        var version = (await JsonAsync(await owner.GetAsync($"{Hr}/employees/{sick}"))).GetProperty("version").GetUInt32();
        var today = BemAqui.BuildingBlocks.Time.BusinessTime.Today(TimeProvider.System);
        var leave = await owner.PostAsJsonAsync($"{Hr}/employees/{sick}/leaves", new
        {
            type = "MedicalCertificate", startDate = today.ToString("yyyy-MM-dd"), expectedEndDate = today.AddDays(5).ToString("yyyy-MM-dd"), reason = HealthCanary, version,
        });
        leave.StatusCode.Should().Be(HttpStatusCode.OK, await leave.Content.ReadAsStringAsync());

        try
        {
            await StateAsync(services: true, hrReleased: false);

            // Camada 2: usuário sem grupo com o hr habilitado recebe 403 forbidden (não module_not_released).
            var outsider = await ActorAsync("hr:view");
            var nobody = tenancy.Factory.CreateClient();
            var (nobodyId, nobodyStamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "n" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
            nobody.DefaultRequestHeaders.Host = Host;
            nobody.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: nobodyId, stamp: nobodyStamp));
            var denied = await nobody.GetAsync(Hr + "/positions");
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeAsync(denied)).Should().Be("forbidden");

            // Camada 3: só hr.view lê, mas não cria nem mexe no custo-hora; o salário de referência exige hr.view_sensitive.
            (await outsider.GetAsync($"{Hr}/positions/{position}")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await CodeAsync(await outsider.PutAsJsonAsync($"{Hr}/positions/{position}/hourly-cost", new { value = 40m }))).Should().Be("forbidden");
            (await CodeAsync(await outsider.GetAsync($"{Hr}/positions/{position}/sensitive"))).Should().Be("forbidden");
            (await CodeAsync(await outsider.PostAsJsonAsync(Hr + "/positions", new { name = "X", cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = (Guid?)null, monthlyHours = 200m })))
                .Should().Be("forbidden");

            // CA 4, caminho manual: hr.edit ajusta o custo-hora sem ver salário.
            var editor = await ActorAsync("hr:view,edit");
            var manual = await editor.PutAsJsonAsync($"{Hr}/positions/{position}/hourly-cost", new { value = 41.5m });
            manual.StatusCode.Should().Be(HttpStatusCode.OK, await manual.Content.ReadAsStringAsync());
            (await JsonAsync(manual)).GetProperty("hourlyCost").GetDecimal().Should().Be(41.5m);
            (await editor.PutAsJsonAsync($"{Hr}/positions/{position}/salary", new { salaryMin = 2000m, salaryMax = 2000m, referenceSalary = (decimal?)null }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, "faixa e salário de referência exigem hr.view_sensitive");
            (await editor.DeleteAsync($"{Hr}/positions/{position}/hourly-cost")).StatusCode.Should().Be(HttpStatusCode.OK);

            // CA 4, caminho do salário: com hr.view_sensitive, salário de referência × (1 + encargos) ÷ horas; a leitura fica na auditoria.
            var sensitive = await ActorAsync("hr:view,edit,view_sensitive");
            var pct = (await JsonAsync(await sensitive.GetAsync(Hr + "/positions/summary"))).GetProperty("chargesPct").GetDecimal();
            var fromSalary = await sensitive.PutAsJsonAsync($"{Hr}/positions/{position}/salary", new { salaryMin = 2200m, salaryMax = 2200m, referenceSalary = 2200m });
            fromSalary.StatusCode.Should().Be(HttpStatusCode.OK, await fromSalary.Content.ReadAsStringAsync());
            (await JsonAsync(fromSalary)).GetProperty("hourlyCost").GetDecimal().Should().Be(BemAqui.HR.Domain.HourlyCostCalculator.Compute(2200m, pct, 220m));
            (await sensitive.GetAsync($"{Hr}/positions/{position}/sensitive")).StatusCode.Should().Be(HttpStatusCode.OK);
            await using (var scope = await ScopeAsync())
            {
                (await scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.BemAquiDbContext>().AuditEntries
                        .AnyAsync(a => a.Entity == "JobPosition" && a.EntityId == position.ToString() && a.Action == "SensitiveRead"))
                    .Should().BeTrue("leitura do salário de referência na auditoria");
            }

            // Saúde: pelo recurso, afastamento nem pela rota própria; na exportação, tipo e motivo só com hr.view_health.
            var health = await ActorAsync("hr:view,edit,view_sensitive,view_health");
            (await CodeAsync(await health.GetAsync($"{Hr}/employees/{sick}/leaves/sensitive"))).Should().Be("module_not_released");
            (await ExportTextAsync(sensitive)).Should().NotContain(HealthCanary, "sem hr.view_health");
            (await ExportTextAsync(health)).Should().Contain(HealthCanary, "com hr.view_health");
        }
        finally
        {
            await StateAsync(services: false, hrReleased: true);
        }
    }

    private static async Task<string> ExportTextAsync(HttpClient client)
    {
        var response = await client.GetAsync(Hr + "/employees/export?format=xlsx");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        return string.Join('\n', workbook.Worksheets.SelectMany(w => w.CellsUsed()).Select(c => c.GetString()));
    }

    /// <summary>CA 6: a Konebras (fábrica, hr pelo plano, sem o recurso) fica igual: mesmas rotas, menu, ajuda e <c>tenant_modules</c>.</summary>
    [Fact]
    public async Task Fabrica_com_o_RH_no_plano_fica_igual_e_tenant_modules_nao_muda()
    {
        await StateAsync(services: false, hrReleased: true);
        var before = await TenantModulesAsync();
        var owner = await ActorAsync(Group.OwnerSeedKey);

        var resolved = await tenancy.Factory.Services.GetRequiredService<ITenantResolver>().FindByIdAsync(tenancy.TenantA.Id);
        resolved!.HasFeature(BusinessProfileCatalog.PeopleRegistryFeature).Should().BeFalse();
        resolved.IsPartiallyReleased("hr").Should().BeFalse();
        foreach (var path in new[] { "/positions", "/employees", "/schedules", "/unions", "/parameters", "/payroll-runs", "/vacations" })
        {
            (await owner.GetAsync(Hr + path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
        }

        var module = await HrModuleAsync(owner);
        module.GetProperty("released").GetBoolean().Should().BeTrue();
        module.GetProperty("partial").GetBoolean().Should().BeFalse();
        (await owner.GetAsync("/api/v1/ajuda/rh-cargos")).StatusCode.Should().Be(HttpStatusCode.OK, "a ajuda com o recurso continua para quem tem o RH pelo plano");
        (await owner.GetAsync("/api/v1/ajuda/rh-folha")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TenantModulesAsync()).Should().Equal(before);
    }
}
