using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// RH-02a: folha lançada (rubricas, parâmetros do RH, folha da competência, holerites, guias, importação do CSV v1 e conferência),
/// com os valores só para quem tem <c>hr.view_sensitive</c> (LGPD). Cada teste usa competências próprias (o tenant é da classe).
/// </summary>
public partial class PayrollTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Runs = "/api/v1/hr/payroll-runs";
    private const string Items = "/api/v1/hr/payroll-items";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).GetProperty("code").GetString();

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> query)
    {
        await using var scope = await ScopeAsync();
        return await query(scope.ServiceProvider);
    }

    /// <summary>Usuário num grupo do seed, num grupo com as ações de <c>hr</c> dadas ("hr:view,edit") ou sem grupo.</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "f" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey.StartsWith("hr:", StringComparison.Ordinal))
            {
                var group = new Group { Name = "Folha parcial " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "hr", Enabled = true });
                foreach (var action in seedKey[3..].Split(','))
                {
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "hr", Action = action });
                }

                groupId = group.Id;
            }
            else
            {
                groupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync();
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    private static async Task<Guid> PositionAsync(HttpClient hr)
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = "Montador " + Guid.NewGuid().ToString("N")[..6], cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null });
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    /// <summary>Funcionário admitido em <paramref name="admission"/>, com salário, CPF e matrícula próprios.</summary>
    private static async Task<(Guid Id, string Cpf, string Registration)> EmployeeAsync(HttpClient owner, Guid position, string admission, decimal salary = 3300m, string? name = null)
    {
        var cpf = NewCpf();
        var registration = "M" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var created = await owner.PostAsJsonAsync("/api/v1/hr/employees", new { name = name ?? "Folha " + Guid.NewGuid().ToString("N")[..6], cpf, birthDate = "1990-05-10", admissionDate = admission, positionId = position, workScheduleId = (Guid?)null, baseSalary = salary, registration });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return ((await JsonAsync(created)).GetProperty("employee").GetProperty("id").GetGuid(), cpf, registration);
    }

    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    private static async Task<HttpResponseMessage> CreateRunAsync(HttpClient client, string period, string type, Guid[]? employees = null, Guid? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Runs) { Content = JsonContent.Create(new { period, type, employeeIds = employees }) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return await client.SendAsync(request);
    }

    private static async Task<Guid> RunAsync(HttpClient client, string period, string type = "Monthly", Guid[]? employees = null)
    {
        var created = await CreateRunAsync(client, period, type, employees);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    /// <summary>O holerite do funcionário na folha (lista sensível).</summary>
    private static async Task<JsonElement> PayslipRowAsync(HttpClient client, Guid run, Guid employee) =>
        (await JsonAsync(await client.GetAsync($"{Runs}/{run}/payslips/sensitive"))).EnumerateArray().Single(p => p.GetProperty("employeeId").GetGuid() == employee);

    private static async Task<JsonElement> PayslipAsync(HttpClient client, Guid run, Guid payslip) =>
        await JsonAsync(await client.GetAsync($"{Runs}/{run}/payslips/{payslip}/sensitive"));

    private static async Task<Dictionary<string, Guid>> ItemIdsAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync(Items))).EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("id").GetGuid());

    private static object Line(Guid item, decimal amount, string? reference = null) => new { payrollItemId = item, reference, amount };

    private static object Slip(uint version, object[] lines, decimal inss = 0m, decimal fgts = 0m, decimal irrf = 0m, decimal fgtsAmount = 0m) =>
        new { lines, inssBase = inss, fgtsBase = fgts, irrfBase = irrf, fgtsAmount, version };

    private static MultipartFormDataContent File(string text)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "lancamentos.csv");
        return content;
    }

    private async Task SetHrReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "hr").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    private Task<int> AuditCountAsync(string entity, string id, string action) =>
        InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.CountAsync(a => a.Entity == entity && a.EntityId == id && a.Action == action));

    [Fact]
    public async Task Seed_das_rubricas_idempotente_com_o_chip_e_Marcar_como_conferido()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var list = (await JsonAsync(await owner.GetAsync(Items))).EnumerateArray().ToList();
        list.Select(i => i.GetProperty("code").GetString()).Should().Contain(PayrollItemService.Standards.Select(s => s.Code), "a lista padrão do CA 1");
        list.Where(i => i.GetProperty("isStandard").GetBoolean()).Should().OnlyContain(i => !i.GetProperty("confirmed").GetBoolean(), "A confirmar com a contadora");
        list.Single(i => i.GetProperty("code").GetString() == "FGTS").GetProperty("kind").GetString().Should().Be("Information");
        var inss = list.Single(i => i.GetProperty("code").GetString() == "INSS");
        inss.GetProperty("kind").GetString().Should().Be("Deduction");
        inss.GetProperty("irrfBase").GetBoolean().Should().BeTrue();

        // Excluir uma padrão sem uso e rodar o seed de novo: não volta (seed_key com IgnoreQueryFilters).
        var pension = list.Single(i => i.GetProperty("code").GetString() == "PENSAO").GetProperty("id").GetGuid();
        (await owner.DeleteAsync($"{Items}/{pension}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await InTenantAsync(async sp => { await PayrollItemService.SeedAsync(sp.GetRequiredService<IHrDb>(), TimeProvider.System); return 0; });
        var again = (await JsonAsync(await owner.GetAsync(Items))).EnumerateArray().ToList();
        again.Should().HaveCount(list.Count - 1, "o seed é idempotente e não recria a excluída");

        var salary = again.Single(i => i.GetProperty("code").GetString() == "SAL");
        var id = salary.GetProperty("id").GetGuid();
        var confirmed = await JsonAsync(await owner.PostAsync($"{Items}/{id}/confirm", null));
        confirmed.GetProperty("confirmed").GetBoolean().Should().BeTrue();
        confirmed.GetProperty("confirmedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        confirmed.GetProperty("confirmedByName").GetString().Should().NotBeNullOrEmpty("quem conferiu");
        (await AuditCountAsync(nameof(PayrollItem), id.ToString(), AuditActions.Updated)).Should().BeGreaterThan(0, "a conferência é auditada");

        // Trocar o código pelo da contadora volta o chip.
        var renamed = await owner.PutAsJsonAsync($"{Items}/{id}", new { code = "001", name = "Salário", kind = "Earning", inssBase = true, fgtsBase = true, irrfBase = true, version = confirmed.GetProperty("version").GetUInt32() });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        var after = await JsonAsync(renamed);
        after.GetProperty("code").GetString().Should().Be("001");
        after.GetProperty("confirmed").GetBoolean().Should().BeFalse("o código mudou: a conferência valia para o de antes");
        (await owner.PutAsJsonAsync($"{Items}/{id}", new { code = "SAL", name = "Salário", kind = "Earning", inssBase = true, fgtsBase = true, irrfBase = true, version = after.GetProperty("version").GetUInt32() })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Rubrica_em_uso_so_inativa_e_cadastro_auditado()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("hr:view");
        var code = "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        (await viewer.PostAsJsonAsync(Items, new { code, name = "Prêmio", kind = "Earning", inssBase = true, fgtsBase = true, irrfBase = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "rubricas com hr.edit");
        (await owner.PostAsJsonAsync(Items, new { code = "=HE", name = "X", kind = "Earning", inssBase = false, fgtsBase = false, irrfBase = false })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var created = await owner.PostAsJsonAsync(Items, new { code, name = "Prêmio", kind = "Earning", inssBase = true, fgtsBase = true, irrfBase = true });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var item = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var duplicate = await owner.PostAsJsonAsync(Items, new { code = code.ToLowerInvariant(), name = "Outro", kind = "Earning", inssBase = false, fgtsBase = false, irrfBase = false });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(duplicate)).Should().Be(PayrollRules.ItemCodeTakenCode);

        var position = await PositionAsync(owner);
        var (employee, _, _) = await EmployeeAsync(owner, position, "2033-01-05");
        var run = await RunAsync(owner, "2033-01", "Vacation", [employee]);
        var payslip = await PayslipRowAsync(owner, run, employee);
        (await owner.PutAsJsonAsync($"{Runs}/{run}/payslips/{payslip.GetProperty("id").GetGuid()}", Slip(payslip.GetProperty("version").GetUInt32(), [Line(item, 100m)]))).EnsureSuccessStatusCode();

        var inUse = await owner.DeleteAsync($"{Items}/{item}");
        inUse.StatusCode.Should().Be(HttpStatusCode.Conflict, "rubrica em uso só inativa (CA 2)");
        (await JsonAsync(inUse)).GetProperty("detail").GetString().Should().Contain("Inative");
        var inactive = await JsonAsync(await owner.PostAsync($"{Items}/{item}/inactivate", null));
        inactive.GetProperty("isActive").GetBoolean().Should().BeFalse();
        inactive.GetProperty("inUse").GetBoolean().Should().BeTrue();
        (await AuditCountAsync(nameof(PayrollItem), item.ToString(), AuditActions.Created)).Should().Be(1);
        (await AuditCountAsync(nameof(PayrollItem), item.ToString(), AuditActions.Updated)).Should().BeGreaterThan(0);

        var unused = (await JsonAsync(await owner.PostAsJsonAsync(Items, new { code = code + "B", name = "Sem uso", kind = "Deduction", inssBase = false, fgtsBase = false, irrfBase = false }))).GetProperty("id").GetGuid();
        (await owner.DeleteAsync($"{Items}/{unused}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AuditCountAsync(nameof(PayrollItem), unused.ToString(), AuditActions.Deleted)).Should().Be(1);
    }

    [Fact]
    public async Task Parametros_do_RH_pre_configurados_editaveis_conferidos_e_auditados()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, hrOnly) = await ActorAsync("hr:view,edit");
        var view = await JsonAsync(await owner.GetAsync("/api/v1/hr/parameters"));
        var items = view.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("key").GetString()!);
        items[HrParametersService.NetVariationAlertPct].GetProperty("value").GetDecimal().Should().Be(10m);
        items[HrParametersService.NetVariationAlertPct].GetProperty("legal").GetBoolean().Should().BeFalse();
        var fgtsKey = HrParametersService.DueDayKey(PayrollChargeKind.Fgts);
        items[fgtsKey].GetProperty("value").GetDecimal().Should().Be(20m);
        items[fgtsKey].GetProperty("legal").GetBoolean().Should().BeTrue("vencimento legal: A confirmar com a contadora");
        items[HrParametersService.PayeeKey(PayrollChargeKind.Fgts)].GetProperty("partyId").ValueKind.Should().Be(JsonValueKind.Null, "o órgão fica vazio até alguém escolher");
        items[HrParametersService.BankAccountQuarantineDays].GetProperty("value").GetDecimal().Should().Be(30m);
        view.GetProperty("pendingCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        object Change(string key, decimal? value = null, Guid? party = null) => new { changes = new[] { new { key, value, partyId = party } } };
        (await hrOnly.PutAsJsonAsync("/api/v1/hr/parameters", Change(fgtsKey, 15m))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "parâmetros com settings.edit");
        (await owner.PutAsJsonAsync("/api/v1/hr/parameters", Change(fgtsKey, 32m))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.PutAsJsonAsync("/api/v1/hr/parameters", Change(HrParametersService.PayeeKey(PayrollChargeKind.Fgts), party: Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o órgão é validado pelo IParties");

        var confirmed = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/hr/parameters/confirm", new { key = fgtsKey }));
        var fgts = confirmed.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == fgtsKey);
        fgts.GetProperty("confirmed").GetBoolean().Should().BeTrue();
        fgts.GetProperty("confirmedByName").GetString().Should().NotBeNullOrEmpty();
        var changed = await JsonAsync(await owner.PutAsJsonAsync("/api/v1/hr/parameters", Change(fgtsKey, 7m)));
        fgts = changed.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == fgtsKey);
        fgts.GetProperty("value").GetDecimal().Should().Be(7m);
        fgts.GetProperty("confirmed").GetBoolean().Should().BeFalse("valor novo volta para A confirmar (ADR 0042)");
        var parameterId = fgts.GetProperty("parameterId").GetGuid().ToString();
        var audit = await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(HrParameter) && a.EntityId == parameterId && a.Action == AuditActions.Updated).Select(a => a.Changes).ToListAsync());
        string.Join("\n", audit).Should().Contain("20").And.Contain("7", "antes e depois na auditoria");
        (await owner.PutAsJsonAsync("/api/v1/hr/parameters", Change(fgtsKey, 20m))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Calendario_padrao_antecipa_o_vencimento_que_cai_em_dia_nao_util()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await using var scope = await ScopeAsync();
        var calendar = scope.ServiceProvider.GetRequiredService<BemAqui.Production.Contracts.IBusinessCalendar>();
        var production = scope.ServiceProvider.GetRequiredService<BemAqui.Production.Application.IProductionDb>();
        var defaultCalendar = await scope.ServiceProvider.GetRequiredService<BemAqui.Production.Application.CalendarService>().DefaultEntityAsync();
        var holiday = new DateOnly(2034, 12, 20);
        production.CalendarExceptions.Add(new BemAqui.Production.Domain.CalendarExceptionDay { CalendarId = defaultCalendar.Id, Date = holiday, Type = BemAqui.Production.Contracts.CalendarExceptionType.Holiday, HoursAvailable = 0m, Description = "Feriado de teste", CreatedAt = DateTimeOffset.UtcNow });
        await production.SaveChangesAsync();

        (await calendar.IsBusinessDayAsync(new DateOnly(2026, 12, 20))).Should().BeFalse("domingo");
        (await calendar.PreviousBusinessDayAsync(new DateOnly(2026, 12, 20))).Should().Be(new DateOnly(2026, 12, 18), "domingo antecipa para sexta");
        (await calendar.PreviousBusinessDayAsync(new DateOnly(2026, 12, 18))).Should().Be(new DateOnly(2026, 12, 18), "o próprio dia útil");
        (await calendar.NthBusinessDayOfMonthAsync(2026, 11, 5)).Should().Be(new DateOnly(2026, 11, 6));
        (await calendar.ExceptionsAsync(holiday.AddDays(-1), holiday.AddDays(1))).Should().ContainSingle().Which.Description.Should().Be("Feriado de teste");
        (await calendar.IsBusinessDayAsync(holiday)).Should().BeFalse("feriado (quarta-feira)");

        var defaults = await scope.ServiceProvider.GetRequiredService<HrParametersService>().ChargeDefaultsAsync(new DateOnly(2034, 11, 1));
        defaults.Single(d => d.Kind == PayrollChargeKind.Fgts).DueDate.Should().Be(new DateOnly(2034, 12, 19), "dia 20 é feriado: antecipa (CA 3)");
        defaults.Single(d => d.Kind == PayrollChargeKind.Union).DueDate.Should().BeNull("contribuição sindical: informada na folha");
    }

    [Fact]
    public async Task Criar_folha_com_Idempotency_Key_sem_repetir_e_os_seis_tipos()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var (inside, _, _) = await EmployeeAsync(owner, position, "2031-01-31");
        var (later, _, _) = await EmployeeAsync(owner, position, "2031-02-01");

        using (var request = new HttpRequestMessage(HttpMethod.Post, Runs) { Content = JsonContent.Create(new { period = "2031-01", type = "Monthly" }) })
        {
            var noKey = await owner.SendAsync(request);
            noKey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await CodeAsync(noKey)).Should().Be("idempotency_key_required");
        }

        var key = Guid.NewGuid();
        var first = await CreateRunAsync(owner, "2031-01", "Monthly", key: key);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var run = await JsonAsync(first);
        run.GetProperty("status").GetString().Should().Be("Draft");
        run.GetProperty("mode").GetString().Should().Be("Entered");
        var replay = await CreateRunAsync(owner, "2031-01", "Monthly", key: key);
        replay.StatusCode.Should().Be(HttpStatusCode.Created, "a mesma chave devolve a resposta original");
        (await JsonAsync(replay)).GetProperty("id").GetGuid().Should().Be(run.GetProperty("id").GetGuid());
        var duplicate = await CreateRunAsync(owner, "2031-01", "Monthly");
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(duplicate)).GetProperty("detail").GetString().Should().Be("Já existe a folha Mensal de janeiro de 2031.");

        var payslips = (await JsonAsync(await owner.GetAsync($"{Runs}/{run.GetProperty("id").GetGuid()}/payslips/sensitive"))).EnumerateArray().Select(p => p.GetProperty("employeeId").GetGuid()).ToList();
        payslips.Should().Contain(inside, "admitido até o último dia").And.NotContain(later, "admitido depois da competência");

        foreach (var type in new[] { "Advance", "Thirteenth1", "Thirteenth2" })
        {
            (await CreateRunAsync(owner, "2031-01", type)).StatusCode.Should().Be(HttpStatusCode.Created, type);
        }

        var noChoice = await CreateRunAsync(owner, "2031-01", "Vacation");
        noChoice.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "em Férias, quem lança escolhe os funcionários");
        (await JsonAsync(noChoice)).GetProperty("errors").TryGetProperty("employeeIds", out _).Should().BeTrue();
        (await CreateRunAsync(owner, "2031-01", "Termination", [later])).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "fora da competência");
        var vacation = await RunAsync(owner, "2031-01", "Vacation", [inside]);
        (await JsonAsync(await owner.GetAsync($"{Runs}/{vacation}"))).GetProperty("payslipCount").GetInt32().Should().Be(1);
        (await CreateRunAsync(owner, "2031-01", "Termination", [inside])).StatusCode.Should().Be(HttpStatusCode.Created);

        var eligible = (await JsonAsync(await owner.GetAsync($"{Runs}/eligible?period=2031-01"))).EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        eligible.Should().Contain(inside).And.NotContain(later);
    }

    [Fact]
    public async Task Holerite_vem_com_o_salario_vigente_e_copia_do_mes_anterior()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var (employee, _, _) = await EmployeeAsync(owner, position, "2032-01-02", 4000m);
        // Reajuste com vigência depois da competência de fevereiro: a folha de fevereiro usa o salário de antes.
        (await owner.PostAsJsonAsync($"/api/v1/hr/employees/{employee}/salary", new { newSalary = 4500m, effectiveDate = "2032-03-01", reason = "Dissídio" })).EnsureSuccessStatusCode();
        var items = await ItemIdsAsync(owner);

        var january = await RunAsync(owner, "2032-01");
        var slip = await PayslipRowAsync(owner, january, employee);
        var detail = await PayslipAsync(owner, january, slip.GetProperty("id").GetGuid());
        var line = detail.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("code").GetString().Should().Be("SAL");
        line.GetProperty("reference").GetString().Should().Be("30 dias");
        line.GetProperty("amount").GetDecimal().Should().Be(4000m, "o salário vigente na competência (CA 5)");
        detail.GetProperty("net").GetDecimal().Should().Be(4000m);

        (await owner.PutAsJsonAsync($"{Runs}/{january}/payslips/{slip.GetProperty("id").GetGuid()}", Slip(detail.GetProperty("version").GetUInt32(),
            [Line(items["SAL"], 4000m, "30 dias"), Line(items["HE50"], 250m, "10:00 h"), Line(items["INSS"], 380m)], inss: 4250m, fgts: 4250m, irrf: 3870m, fgtsAmount: 340m))).EnsureSuccessStatusCode();

        var february = await RunAsync(owner, "2032-02");
        var next = await PayslipRowAsync(owner, february, employee);
        next.GetProperty("net").GetDecimal().Should().Be(4000m, "o reajuste de março não entra em fevereiro");
        var previous = await JsonAsync(await owner.GetAsync($"{Runs}/{february}/payslips/{next.GetProperty("id").GetGuid()}/previous/sensitive"));
        previous.GetProperty("period").GetString().Should().Be("2032-01-01");
        previous.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("code").GetString()).Should().Equal("SAL", "HE50", "INSS");
        previous.GetProperty("fgtsAmount").GetDecimal().Should().Be(340m);
        (await PayslipAsync(owner, february, next.GetProperty("id").GetGuid())).GetProperty("lines").GetArrayLength().Should().Be(1, "copiar não grava: é rascunho editável");
        (await AuditCountAsync(nameof(Payslip) + ".Previous", next.GetProperty("id").GetGuid().ToString(), AuditActions.SensitiveRead)).Should().Be(1);
    }

    [Fact]
    public async Task Salvar_holerite_recalcula_os_totais_recusa_liquido_negativo_e_versao_antiga()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var (employee, _, _) = await EmployeeAsync(owner, position, "2033-03-01");
        var items = await ItemIdsAsync(owner);
        var run = await RunAsync(owner, "2033-03", "Vacation", [employee]);
        var slip = await PayslipRowAsync(owner, run, employee);
        var url = $"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}";
        var version = slip.GetProperty("version").GetUInt32();

        var negative = await owner.PutAsJsonAsync(url, Slip(version, [Line(items["FERIAS"], 100m), Line(items["IRRF"], 150m)]));
        negative.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(negative)).GetProperty("detail").GetString().Should().Be("O líquido não pode ser negativo.");
        (await owner.PutAsJsonAsync(url, Slip(version, [Line(items["FERIAS"], 0m)]))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "valor > 0");
        var longRef = await owner.PutAsJsonAsync(url, Slip(version, [Line(items["FERIAS"], 10m, new string('x', 21))]));
        longRef.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "referência até 20");
        (await JsonAsync(longRef)).GetProperty("errors").TryGetProperty("lines[0]", out _).Should().BeTrue("o erro na linha");

        var saved = await owner.PutAsJsonAsync(url, Slip(version, [Line(items["FERIAS"], 3000m, "30 dias"), Line(items["FER13"], 1000.55m), Line(items["INSS"], 400.25m), Line(items["FGTS"], 320.04m)], inss: 4000.55m, fgts: 4000.55m, irrf: 3600.30m, fgtsAmount: 320.04m));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var detail = await JsonAsync(saved);
        detail.GetProperty("gross").GetDecimal().Should().Be(4000.55m);
        detail.GetProperty("deductions").GetDecimal().Should().Be(400.25m);
        detail.GetProperty("net").GetDecimal().Should().Be(3600.30m, "a informativa (FGTS) não entra no líquido");
        detail.GetProperty("inssBase").GetDecimal().Should().Be(4000.55m);

        var stale = await owner.PutAsJsonAsync(url, Slip(version, [Line(items["FERIAS"], 1m)]));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict, "dois usuários no mesmo holerite (CA 15)");
        (await CodeAsync(stale)).Should().Be("concurrency_conflict");
        (await PayslipAsync(owner, run, slip.GetProperty("id").GetGuid())).GetProperty("lines").GetArrayLength().Should().Be(4, "o conflito não grava nada");
    }

    [Fact]
    public async Task Guias_com_vencimento_e_orgao_padrao_aviso_do_FGTS_e_IDOR()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var (employee, _, _) = await EmployeeAsync(owner, position, "2034-05-01");
        var items = await ItemIdsAsync(owner);
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Caixa Econômica Federal " + Guid.NewGuid().ToString("N")[..5], roles = new[] { "Supplier" } });
        var caixa = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var payeeKey = HrParametersService.PayeeKey(PayrollChargeKind.Fgts);
        (await owner.PutAsJsonAsync("/api/v1/hr/parameters", new { changes = new[] { new { key = payeeKey, value = (decimal?)null, partyId = (Guid?)caixa } } })).EnsureSuccessStatusCode();
        try
        {
            var run = await RunAsync(owner, "2034-05", "Vacation", [employee]);
            var slip = await PayslipRowAsync(owner, run, employee);
            (await owner.PutAsJsonAsync($"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}", Slip(slip.GetProperty("version").GetUInt32(), [Line(items["FERIAS"], 3000m)], fgtsAmount: 240m))).EnsureSuccessStatusCode();

            var added = await owner.PostAsJsonAsync($"{Runs}/{run}/charges", new { kind = "Fgts", description = (string?)null, amount = 250m, dueDate = (string?)null, payeePartyId = (Guid?)null });
            added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
            var view = await JsonAsync(added);
            var charge = view.GetProperty("items").EnumerateArray().Single();
            charge.GetProperty("dueDate").GetString().Should().Be("2034-06-20", "dia 20 do mês seguinte, terça-feira (CA 3)");
            charge.GetProperty("payeePartyId").GetGuid().Should().Be(caixa, "o órgão padrão do CA 3");
            charge.GetProperty("description").GetString().Should().Be("FGTS maio de 2034");
            view.GetProperty("payslipsFgts").GetDecimal().Should().Be(240m);
            view.GetProperty("fgtsMismatch").GetBoolean().Should().BeTrue("a soma do FGTS dos holerites difere da guia (aviso âmbar)");

            (await owner.PostAsJsonAsync($"{Runs}/{run}/charges", new { kind = "Union", description = (string?)null, amount = 50m, dueDate = (string?)null, payeePartyId = (Guid?)null }))
                .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "contribuição sindical: vencimento informado na folha");
            (await owner.PostAsJsonAsync($"{Runs}/{run}/charges", new { kind = "Inss", description = (string?)null, amount = 50m, dueDate = "2034-06-20", payeePartyId = Guid.NewGuid() }))
                .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o favorecido é validado pelo IParties");

            var chargeId = charge.GetProperty("id").GetGuid();
            var fixedView = await JsonAsync(await owner.PutAsJsonAsync($"{Runs}/{run}/charges/{chargeId}", new { kind = "Fgts", description = "FGTS maio", amount = 240m, dueDate = "2034-06-20", payeePartyId = caixa, version = charge.GetProperty("version").GetUInt32() }));
            fixedView.GetProperty("fgtsMismatch").GetBoolean().Should().BeFalse();

            var other = await RunAsync(owner, "2034-05", "Termination", [employee]);
            (await owner.PutAsJsonAsync($"{Runs}/{other}/charges/{chargeId}", new { kind = "Fgts", description = "x", amount = 1m, dueDate = "2034-06-20", payeePartyId = caixa })).StatusCode.Should().Be(HttpStatusCode.NotFound, "guia de outra folha (IDOR)");
            (await owner.DeleteAsync($"{Runs}/{other}/charges/{chargeId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.GetAsync($"{Runs}/{other}/payslips/{slip.GetProperty("id").GetGuid()}/sensitive")).StatusCode.Should().Be(HttpStatusCode.NotFound, "holerite de outra folha (IDOR)");
        }
        finally
        {
            await owner.PutAsJsonAsync("/api/v1/hr/parameters", new { changes = new[] { new { key = payeeKey, value = (decimal?)null, partyId = (Guid?)null } } });
        }
    }
}
