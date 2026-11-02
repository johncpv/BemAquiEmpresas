using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-02a: folha mensal calculada com o pacote de 2026 (seed, "A confirmar com a contadora") e funcionários fictícios. Cada teste usa a
/// própria competência, e cada funcionário fica no vínculo só dentro dela (admitido no dia 1 e desligado no último dia), para a folha do
/// teste ter só os seus funcionários. A virada (FLH-06) é testada num tenant próprio, para não mudar a simulação dos outros testes.
/// </summary>
public partial class PayrollCalculationTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>, IDisposable
{
    private const string Runs = "/api/v1/hr/payroll-runs";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private const string Canary = "86420.13";
    private const string CanaryBr = "86.420,13";

    private readonly CapturingJobClient _jobs = new();
    private readonly MemorySink _sink = new();
    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddSingleton<ILogEventSink>(_sink);
        s.RemoveAll<IBackgroundJobClient>();
        s.AddSingleton<IBackgroundJobClient>(_jobs);
    }));

    public void Dispose()
    {
        _factory?.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class Authenticator(byte[] secret)
    {
        private long _last = long.MinValue;

        public string Next()
        {
            // O servidor aceita o passo anterior, o atual e o próximo; o código sai do passo anterior (sobram dois à frente). Nos últimos 10 s
            // do passo, ele venceria no meio da requisição quando o relógio vira (2FA "inválido" intermitente): espera o passo virar antes.
            var wait = Totp.StepSeconds - (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % (Totp.StepSeconds * 1000L)) / 1000.0;
            if (wait < 10)
            {
                Thread.Sleep(TimeSpan.FromSeconds(wait + 0.2));
            }

            var step = Math.Max(_last + 1, Totp.StepAt(DateTimeOffset.UtcNow) - 1);
            _last = step;
            return Totp.Code(secret, step);
        }
    }

    private sealed record Actor(Guid Id, HttpClient Client, Authenticator? Codes);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).TryGetProperty("code", out var c) ? c.GetString() : null;

    private async Task<T> InTenantAsync<T>(Tenant tenant, Func<IServiceProvider, Task<T>> action)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using var scope = await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Tenant tenant, Func<TenantDbContext, Task<T>> action) => InTenantAsync(tenant, sp => action(sp.GetRequiredService<TenantDbContext>()));

    private Task<int> DbAsync(Tenant tenant, Func<TenantDbContext, Task> action) => DbAsync(tenant, async db =>
    {
        await action(db);
        await db.SaveChangesAsync();
        return 0;
    });

    private static string HostOf(Tenant tenant) => tenant.Domains.First().Host;

    /// <summary>Usuário num grupo próprio ("hr:view,create;finance:view"), com o 2FA ligado se pedir.</summary>
    private async Task<Actor> ActorAsync(Tenant tenant, string grants, bool totp = false)
    {
        var host = HostOf(tenant);
        var (id, _) = await tenancy.SeedUserAsync(tenant, "f" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        var secret = Totp.NewSecret();
        var stamp = await InTenantAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "Calculada " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
            db.Groups.Add(group);
            foreach (var part in grants.Split(';'))
            {
                var module = part[..part.IndexOf(':', StringComparison.Ordinal)];
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                foreach (var action in part[(module.Length + 1)..].Split(','))
                {
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = action });
                }
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            var user = await db.Users.SingleAsync(u => u.Id == id);
            if (totp)
            {
                user.BeginTotpSetup(Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenant.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
                user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            }

            await db.SaveChangesAsync();
            await sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
            return user.SecurityStamp;
        });
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp, otpAt: DateTimeOffset.UtcNow));
        return new Actor(id, client, totp ? new Authenticator(secret) : null);
    }

    private const string Rh = "hr:view,create,edit,view_sensitive";

    private static HttpRequestMessage Post(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    private static async Task<Guid> PositionAsync(HttpClient hr)
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await OkAsync(await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = "Montador " + Guid.NewGuid().ToString("N")[..6], cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null }), HttpStatusCode.Created);
        return created.GetProperty("id").GetGuid();
    }

    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    /// <summary>Funcionário fictício só na competência: admitido no dia 1 e desligado no último dia (fica fora das folhas dos outros testes).</summary>
    private async Task<Guid> EmployeeAsync(Tenant tenant, HttpClient hr, Guid position, DateOnly period, decimal salary, Action<Employee>? change = null)
    {
        var created = await OkAsync(await hr.PostAsJsonAsync("/api/v1/hr/employees", new
        {
            name = "Fictício " + Guid.NewGuid().ToString("N")[..6], cpf = NewCpf(), birthDate = "1990-05-10", admissionDate = period.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            positionId = position, workScheduleId = (Guid?)null, baseSalary = salary, registration = "F" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant(),
        }), HttpStatusCode.Created);
        var id = created.GetProperty("employee").GetProperty("id").GetGuid();
        await DbAsync(tenant, async db =>
        {
            var e = await db.Employees.SingleAsync(x => x.Id == id);
            e.TerminationDate = PayrollRules.PeriodEnd(period);
            change?.Invoke(e);
        });
        return id;
    }

    /// <summary>O espelho do mês fechado (RH-01c) com as faltas e os atrasos em minutos, gravado direto (o fechamento tem os próprios testes).</summary>
    /// <summary>FLH-02b: com os dias congelados (<c>DaysSnapshot</c>) e o banco de horas (saldo do período e acumulado) quando o teste pede.</summary>
    private Task<int> CloseTimesheetAsync(Tenant tenant, Guid employee, DateOnly period, int absence = 0, int late = 0, IReadOnlyList<TimesheetDayResult>? days = null, int? bankMonth = null,
        int? bankBalance = null) => DbAsync(tenant, db =>
    {
        db.Timesheets.Add(new Timesheet
        {
            EmployeeId = employee, Period = period, PeriodStart = period, PeriodEnd = PayrollRules.PeriodEnd(period), Status = TimesheetStatus.Closed,
            AbsenceMinutes = absence, LateMinutes = late, ClosedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
            DaysSnapshot = days is null ? null : JsonSerializer.Serialize(days, WebJson),
            OvertimeMinutes = days?.Sum(d => d.OvertimeMinutes) ?? 0, BankMonthMinutes = bankMonth, BankBalanceMinutes = bankBalance,
        });
        return Task.CompletedTask;
    });

    private static async Task<Guid> CalculatedRunAsync(HttpClient hr, DateOnly period)
    {
        var run = await OkAsync(await hr.SendAsync(Post(Runs, new { period = period.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), type = "Monthly", employeeIds = (Guid[]?)null, mode = "Calculated" })), HttpStatusCode.Created);
        run.GetProperty("mode").GetString().Should().Be("Calculated");
        return run.GetProperty("id").GetGuid();
    }

    private static async Task<Dictionary<Guid, JsonElement>> PayslipsAsync(HttpClient hr, Guid run)
    {
        var rows = (await OkAsync(await hr.GetAsync($"{Runs}/{run}/payslips/sensitive"))).EnumerateArray().ToList();
        var result = new Dictionary<Guid, JsonElement>();
        foreach (var row in rows)
        {
            result[row.GetProperty("employeeId").GetGuid()] = await OkAsync(await hr.GetAsync($"{Runs}/{run}/payslips/{row.GetProperty("id").GetGuid()}/sensitive"));
        }

        return result;
    }

    private static decimal Amount(JsonElement payslip, string code) =>
        payslip.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("code").GetString() == code).Sum(l => l.GetProperty("amount").GetDecimal());

    /// <summary>CA 1 e 18: pré-condições com o link (ponto fechado, tabela da competência, Mensal), simulação, tabelas a confirmar e as ações.</summary>
    [Fact]
    public async Task Pre_condicoes_chips_e_permissoes()
    {
        var a = tenancy.TenantA;
        var hr = await ActorAsync(a, Rh);
        var editOnly = await ActorAsync(a, "hr:view,edit,view_sensitive");
        var noSensitive = await ActorAsync(a, "hr:view,create,edit");
        var period = new DateOnly(2026, 4, 1);
        var position = await PositionAsync(hr.Client);
        var employee = await EmployeeAsync(a, hr.Client, position, period, 2000m);
        var run = await CalculatedRunAsync(hr.Client, period);
        (await JsonAsync(await hr.Client.GetAsync($"{Runs}/{run}"))).GetProperty("isSimulation").GetBoolean().Should().BeTrue("antes da virada (FLH-06) a calculada é simulação");
        (await PayslipsAsync(hr.Client, run))[employee].GetProperty("lines").GetArrayLength().Should().Be(0, "a calculada nasce sem linha");

        var open = await hr.Client.PostAsync($"{Runs}/{run}/calculate", null);
        open.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await JsonAsync(open);
        body.GetProperty("code").GetString().Should().Be(PayrollCalculationRules.TimesheetOpenCode);
        body.GetProperty("detail").GetString().Should().Be("Feche o ponto de abril de 2026 antes de calcular.");
        body.GetProperty("link").GetString().Should().Be("/app/rh/ponto/espelho");

        await CloseTimesheetAsync(a, employee, period);
        (await OkAsync(await hr.Client.GetAsync($"{Runs}/suggested-period"))).GetProperty("period").GetString().Should().NotBeNull("a competência sugerida é a do último ponto fechado");
        (await noSensitive.Client.PostAsync($"{Runs}/{run}/calculate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "valores só com hr.view_sensitive");
        (await editOnly.Client.PostAsync($"{Runs}/{run}/calculate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "calcular é hr.create");
        var done = await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/calculate", null));
        done.GetProperty("queued").GetBoolean().Should().BeFalse();
        done.GetProperty("run").GetProperty("calculatorVersion").GetString().Should().Be(PayrollCalculator.Version);
        await OkAsync(await editOnly.Client.PostAsync($"{Runs}/{run}/recalculate", null));

        var status = await OkAsync(await noSensitive.Client.GetAsync($"{Runs}/{run}/calculation"));
        status.GetProperty("isSimulation").GetBoolean().Should().BeTrue();
        status.GetProperty("unconfirmedTables").GetArrayLength().Should().Be(7, "o pacote de 2026 está todo \"A confirmar com a contadora\"");
        status.GetProperty("nextStep").GetProperty("label").GetString().Should().Be("Comparar com a lançada");
        status.GetProperty("staleCount").GetInt32().Should().Be(0);

        // Sem tabela da competência: o 422 da FLH-01 (2025 não tem pacote no tenant).
        var june2025 = new DateOnly(2025, 6, 1);
        var oldEmployee = await EmployeeAsync(a, hr.Client, position, june2025, 2000m);
        var old = await CalculatedRunAsync(hr.Client, june2025);
        await CloseTimesheetAsync(a, oldEmployee, june2025);
        var missing = await hr.Client.PostAsync($"{Runs}/{old}/calculate", null);
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(missing)).GetProperty("detail").GetString().Should().Be("Falta a tabela INSS para junho/2025.");

        // Só a Mensal é calculada nesta story; a calculada não repete na competência; a lançada convive com ela.
        var advance = await hr.Client.SendAsync(Post(Runs, new { period = "2026-04", type = "Advance", employeeIds = (Guid[]?)null, mode = "Calculated" }));
        (await CodeAsync(advance)).Should().Be(PayrollCalculationRules.NotSupportedCode);
        (await hr.Client.SendAsync(Post(Runs, new { period = "2026-04", type = "Monthly", employeeIds = (Guid[]?)null, mode = "Calculated" }))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await OkAsync(await hr.Client.SendAsync(Post(Runs, new { period = "2026-04", type = "Monthly", employeeIds = (Guid[]?)null })), HttpStatusCode.Created);
    }

    /// <summary>
    /// CA 1, 3, 6, 8 a 12 e 19: linhas pelas rubricas, bases, FGTS, encargos e guias calculadas, a memória e o retrato gravados (o retrato
    /// mascarado na auditoria), os documentos do salário-família pelo formulário e o histórico com os totais só por /sensitive.
    /// </summary>
    [Fact]
    public async Task Calcula_grava_retrato_memoria_guias_e_historico()
    {
        var a = tenancy.TenantA;
        var hr = await ActorAsync(a, Rh);
        var viewer = await ActorAsync(a, "hr:view");
        var period = new DateOnly(2026, 5, 1);
        var position = await PositionAsync(hr.Client);
        var high = await EmployeeAsync(a, hr.Client, position, period, 6000m);
        var family = await EmployeeAsync(a, hr.Client, position, period, 1700m, e => e.UsesTransportVoucher = true);
        var sick = await EmployeeAsync(a, hr.Client, position, period, 3000m);
        foreach (var (name, docs) in new[] { ("Filho Um", true), ("Filho Dois", false) })
        {
            await OkAsync(await hr.Client.PostAsJsonAsync($"/api/v1/hr/employees/{family}/dependents",
                new { name, birthDate = "2018-05-10", kinship = "Child", irDependent = false, familyAllowance = true, cpf = (string?)null, version = (uint?)null, familyAllowanceDocsOk = docs }), HttpStatusCode.Created);
        }

        await DbAsync(a, db =>
        {
            db.EmployeeHistory.Add(new EmployeeHistoryEntry
            {
                EmployeeId = sick, Kind = EmployeeHistoryKind.Leave, EffectiveDate = new DateOnly(2026, 4, 24), LeaveEndDate = new DateOnly(2026, 5, 31), LeaveType = AbsenceReason.MedicalCertificate,
                NewValue = "Afastado", Reason = "Afastamento de teste", CreatedAt = DateTimeOffset.UtcNow,
            });
            return Task.CompletedTask;
        });
        foreach (var e in new[] { high, family, sick })
        {
            await CloseTimesheetAsync(a, e, period);
        }

        var run = await CalculatedRunAsync(hr.Client, period);
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/calculate", null));
        var slips = await PayslipsAsync(hr.Client, run);

        // 6.000: INSS 641,51 e IRRF 385,10 (dedução legal; o simplificado daria 394,54), FGTS 480 (informativo).
        var h = slips[high];
        Amount(h, "SAL").Should().Be(6000m);
        Amount(h, "INSS").Should().Be(641.51m);
        Amount(h, "IRRF").Should().Be(385.10m);
        Amount(h, "FGTS").Should().Be(480m);
        h.GetProperty("net").GetDecimal().Should().Be(4973.39m);
        h.GetProperty("inssBase").GetDecimal().Should().Be(6000m);
        h.GetProperty("fgtsAmount").GetDecimal().Should().Be(480m);
        var irrf = h.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("code").GetString() == "IRRF");
        irrf.GetProperty("calculated").GetBoolean().Should().BeTrue();
        irrf.GetProperty("memory").EnumerateArray().Select(m => m.GetString()).Should().Contain(m => m!.Contains("o imposto seria R$ 394,54", StringComparison.Ordinal));
        var info = h.GetProperty("calculation");
        info.GetProperty("calculatorVersion").GetString().Should().Be(PayrollCalculator.Version);
        info.GetProperty("tables").GetArrayLength().Should().Be(7);
        info.GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("label").GetString()).Should().Contain("INSS de janeiro/2026");
        info.GetProperty("employerCharges").GetDecimal().Should().Be(1728m, "28,8% de Lucro Presumido sobre 6.000");
        info.GetProperty("inputs").EnumerateArray().Select(i => i.GetProperty("label").GetString()).Should().Contain("Salário base").And.NotContain("Motivo do afastamento");

        // Salário-família só com os documentos entregues; o aviso fica na situação do cálculo, sem travar.
        Amount(slips[family], "SALFAM").Should().Be(67.54m);
        Amount(slips[family], "VT").Should().Be(102m);
        var status = await OkAsync(await viewer.Client.GetAsync($"{Runs}/{run}/calculation"));
        status.GetProperty("issues").EnumerateArray().Should().Contain(i => i.GetProperty("employeeId").GetGuid() == family
            && i.GetProperty("message").GetString() == "Documentos do salário-família pendentes." && !i.GetProperty("blocking").GetBoolean());

        // Doença desde 24/04: a empresa paga até 08/05; de 9 a 31/05, 22 dias pela previdência no mês comercial (o dia 31 conta como 30).
        Amount(slips[sick], "SAL").Should().Be(800m);
        Amount(slips[sick], "AFAST").Should().Be(2200m);
        slips[sick].GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("code").GetString() == "AFAST").GetProperty("memory")[0].GetString()
            .Should().StartWith("Afastamento: 22 dias pela previdência");

        // Gravado: a versão do motor, as tabelas, os parâmetros e o retrato; o retrato vai mascarado para a auditoria.
        var stored = await DbAsync(a, db => db.Payslips.AsNoTracking().SingleAsync(p => p.PayrollRunId == run && p.EmployeeId == high));
        stored.CalculatorVersion.Should().Be(PayrollCalculator.Version);
        stored.TaxTableIds.Should().HaveCount(7);
        stored.ParameterIds.Should().NotBeEmpty();
        PayrollCalculator.ReadSnapshot(stored.InputsSnapshot)!.BaseSalary.Should().Be(6000m);
        var audit = await OkAsync(await hr.Client.GetAsync($"/api/v1/audit/Payslip/{stored.Id}"));
        var changes = string.Join("\n", audit.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("changes").GetString()));
        changes.Should().Contain("inputsSnapshot").And.NotContain("6000.00");

        // Guias calculadas: FGTS, INSS (retido + encargos) e IRRF retido.
        var charges = await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/charges/sensitive"));
        var items = charges.GetProperty("items").EnumerateArray().ToList();
        items.Should().OnlyContain(c => c.GetProperty("calculated").GetBoolean());
        items.Select(c => c.GetProperty("kind").GetString()).Should().BeEquivalentTo(["Fgts", "Inss", "Irrf"]);

        // CA 19: quem, quando, a versão e as tabelas; os totais só por /sensitive.
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/recalculate", null));
        var history = await OkAsync(await viewer.Client.GetAsync($"{Runs}/{run}/calculations"));
        history.EnumerateArray().Select(e => e.GetProperty("actionLabel").GetString()).Should().Equal("Recalculou", "Calculou");
        history[0].GetProperty("calculatorVersion").GetString().Should().Be(PayrollCalculator.Version);
        history[0].GetProperty("tables").GetArrayLength().Should().Be(7);
        history[0].TryGetProperty("netAfter", out _).Should().BeFalse("sem valor na rota de hr.view");
        (await viewer.Client.GetAsync($"{Runs}/{run}/calculations/sensitive")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var totals = await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/calculations/sensitive"));
        totals[0].GetProperty("netBefore").GetDecimal().Should().Be(totals[0].GetProperty("netAfter").GetDecimal(), "recalcular o mesmo retrato dá o mesmo resultado");
    }

    /// <summary>CA 13 e 14: avulso com motivo entra nas bases e fica no recálculo; linha calculada não se edita; cadastro e tabela mudados avisam.</summary>
    [Fact]
    public async Task Avulsos_recalculo_e_aviso_de_mudanca()
    {
        var a = tenancy.TenantA;
        var hr = await ActorAsync(a, Rh);
        var period = new DateOnly(2026, 6, 1);
        var employee = await EmployeeAsync(a, hr.Client, await PositionAsync(hr.Client), period, 6000m);
        await CloseTimesheetAsync(a, employee, period, absence: 0, late: 30);
        var run = await CalculatedRunAsync(hr.Client, period);
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/calculate", null));
        var slip = (await PayslipsAsync(hr.Client, run))[employee];
        var slipId = slip.GetProperty("id").GetGuid();
        Amount(slip, "ATRASOS").Should().Be(13.64m, "6.000 ÷ 220 h × 0:30 h");
        var codes = (await OkAsync(await hr.Client.GetAsync("/api/v1/hr/payroll-items"))).EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("id").GetGuid());

        // Linha calculada não se edita à mão.
        var put = await hr.Client.PutAsJsonAsync($"{Runs}/{run}/payslips/{slipId}", new { lines = Array.Empty<object>(), inssBase = 0m, fgtsBase = 0m, irrfBase = 0m, fgtsAmount = 0m, version = slip.GetProperty("version").GetUInt32() });
        (await CodeAsync(put)).Should().Be(PayrollCalculationRules.CalculatedLineCode);

        // Avulso: motivo obrigatório, só rubrica sem regra; a pensão judicial deduz da base do IRRF.
        (await hr.Client.PostAsJsonAsync($"{Runs}/{run}/payslips/{slipId}/manual-lines", new { payrollItemId = codes["PENSAO"], reference = (string?)null, amount = 1000m, note = "" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await hr.Client.PostAsJsonAsync($"{Runs}/{run}/payslips/{slipId}/manual-lines", new { payrollItemId = codes["SAL"], reference = (string?)null, amount = 1000m, note = "Ajuste de salário" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a rubrica com regra não é avulsa");
        var withPension = await OkAsync(await hr.Client.PostAsJsonAsync($"{Runs}/{run}/payslips/{slipId}/manual-lines",
            new { payrollItemId = codes["PENSAO"], reference = (string?)null, amount = 1000m, note = "Pensão judicial, ofício 77/2026" }));
        Amount(withPension, "PENSAO").Should().Be(1000m);
        Amount(withPension, "IRRF").Should().BeLessThan(Amount(slip, "IRRF"), "a pensão entra na dedução legal");
        var pension = withPension.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("code").GetString() == "PENSAO");
        pension.GetProperty("calculated").GetBoolean().Should().BeFalse();
        pension.GetProperty("note").GetString().Should().Be("Pensão judicial, ofício 77/2026");
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/recalculate", null));
        Amount((await PayslipsAsync(hr.Client, run))[employee], "PENSAO").Should().Be(1000m, "o avulso fica no recálculo");

        // Mudou o cadastro depois do cálculo: aviso com "Recalcular"; recalculado, o aviso some.
        await DbAsync(a, async db => { (await db.Employees.SingleAsync(e => e.Id == employee)).BaseSalary = 6500m; });
        var stale = await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/calculation"));
        stale.GetProperty("staleCount").GetInt32().Should().Be(1);
        stale.GetProperty("staleMessage").GetString().Should().Be("Cadastro ou tabela mudou depois do cálculo de 1 funcionário.");
        stale.GetProperty("staleReasons").EnumerateArray().Select(r => r.GetString()).Should().Contain("Cadastro ou parâmetro mudou depois do cálculo");
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/recalculate", null));
        (await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/calculation"))).GetProperty("staleCount").GetInt32().Should().Be(0);

        // Tabela "A confirmar" usada editada depois do cálculo (FLH-01 CA 7): "Tabela alterada depois do cálculo".
        var vt = await DbAsync(a, db => db.PayrollTaxTables.AsNoTracking().SingleAsync(t => t.SeedPackage == 2026 && t.Kind == PayrollTaxTableKind.TransportVoucher));
        var detail = await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/payroll-tax-tables/{vt.Id}"));
        await OkAsync(await hr.Client.PutAsJsonAsync($"/api/v1/hr/payroll-tax-tables/{vt.Id}", new
        {
            kind = (string?)null, validFrom = "2026-01-01", rounding = "AwayFromZero", source = vt.Source + " Revisado.", content = new { transportVoucher = new { maxDiscountRate = 6m } },
            version = detail.GetProperty("version").GetUInt32(),
        }));
        (await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/calculation"))).GetProperty("staleReasons").EnumerateArray().Select(r => r.GetString()).Should().Contain("Tabela alterada depois do cálculo");
        var impact = await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/payroll-tax-tables/{vt.Id}/impact"));
        impact.GetProperty("draftRuns").EnumerateArray().Select(r => r.GetProperty("runId").GetGuid()).Should().Contain(run, "a folha calculada guarda as tabelas usadas");

        // Tirar o avulso recalcula o holerite (cada cálculo refaz as linhas: o id é o do holerite de agora).
        var lineId = (await PayslipsAsync(hr.Client, run))[employee].GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("code").GetString() == "PENSAO").GetProperty("lineId").GetGuid();
        Amount(await OkAsync(await hr.Client.DeleteAsync($"{Runs}/{run}/payslips/{slipId}/manual-lines/{lineId}")), "PENSAO").Should().Be(0m);
    }

    /// <summary>CA 15: antes da virada, o fechamento da calculada some (409 com "Comparar com a lançada") e a prévia mostra a pendência.</summary>
    [Fact]
    public async Task Simulacao_nao_fecha_antes_da_virada()
    {
        var a = tenancy.TenantA;
        var hr = await ActorAsync(a, Rh + ",approve", totp: true);
        var period = new DateOnly(2026, 7, 1);
        var employee = await EmployeeAsync(a, hr.Client, await PositionAsync(hr.Client), period, 2500m);
        await CloseTimesheetAsync(a, employee, period);
        await OkAsync(await hr.Client.SendAsync(Post(Runs, new { period = "2026-07", type = "Monthly", employeeIds = (Guid[]?)null })), HttpStatusCode.Created);
        var run = await CalculatedRunAsync(hr.Client, period);
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/calculate", null));
        var version = (await JsonAsync(await hr.Client.GetAsync($"{Runs}/{run}"))).GetProperty("version").GetUInt32();
        var close = await hr.Client.SendAsync(Post($"{Runs}/{run}/close", new { paymentDate = (string?)null, twoFactorCode = hr.Codes!.Next(), version, acknowledgedChanges = true }));
        close.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await JsonAsync(close);
        body.GetProperty("code").GetString().Should().Be(PayrollCalculationRules.SimulationCode);
        body.GetProperty("detail").GetString().Should().Be("Folha calculada em conferência paralela: feche a folha lançada.");
        body.GetProperty("nextStep").GetString().Should().Be("Comparar com a lançada");
        body.GetProperty("link").GetString().Should().Be(PayrollParallelService.Link(period), "FLH-06: \"Comparar com a lançada\" abre a conferência da competência");
        var preview = await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/close/sensitive"));
        preview.GetProperty("blockers").EnumerateArray().Select(b => b.GetProperty("code").GetString()).Should().Contain(PayrollCalculationRules.SimulationCode);
        (await DbAsync(a, db => db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == run))).Status.Should().Be(PayrollRunStatus.Draft);

        // A prévia do holerite sai (rascunho com a marca da simulação) e nunca vai para "Meus holerites" (só folha fechada).
        var slip = (await PayslipsAsync(hr.Client, run))[employee].GetProperty("id").GetGuid();
        var pdf = await hr.Client.GetAsync($"{Runs}/{run}/payslips/{slip}/pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync());
    }

    /// <summary>CA 17: acima de 20 funcionários, o cálculo vai para o job com só ids; nenhum salário ou líquido no log nem no argumento.</summary>
    [Fact]
    public async Task Acima_de_20_vai_para_o_job_sem_valor_no_log()
    {
        var a = tenancy.TenantA;
        var hr = await ActorAsync(a, Rh);
        var period = new DateOnly(2026, 8, 1);
        var position = await PositionAsync(hr.Client);
        var ids = new List<Guid> { await EmployeeAsync(a, hr.Client, position, period, decimal.Parse(Canary, System.Globalization.CultureInfo.InvariantCulture)) };
        for (var i = 0; i < PayrollCalculationRules.InlineLimit; i++)
        {
            ids.Add(await EmployeeAsync(a, hr.Client, position, period, 2000m + i));
        }

        foreach (var id in ids)
        {
            await CloseTimesheetAsync(a, id, period);
        }

        var run = await CalculatedRunAsync(hr.Client, period);
        var queued = await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/calculate", null));
        queued.GetProperty("queued").GetBoolean().Should().BeTrue();
        queued.GetProperty("run").GetProperty("calculating").GetBoolean().Should().BeTrue();
        (await hr.Client.PostAsync($"{Runs}/{run}/calculate", null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "o cálculo já está em andamento");
        var job = _jobs.Created.Last(j => j.Job.Type == typeof(PayrollCalculationJob)).Job;
        job.Args[1].Should().BeEquivalentTo(new PayrollCalculationArgs(run, hr.Id), "só os ids");
        var instance = ActivatorUtilities.CreateInstance<PayrollCalculationJob>(Factory.Services);
        await instance.ExecuteAsync(a.Id, (PayrollCalculationArgs)job.Args[1], CancellationToken.None);

        var summary = await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}"));
        summary.GetProperty("calculating").GetBoolean().Should().BeFalse();
        summary.GetProperty("calculatedAt").ValueKind.Should().Be(JsonValueKind.String);
        Amount((await PayslipsAsync(hr.Client, run))[ids[0]], "SAL").Should().Be(86420.13m);
        (await DbAsync(a, db => db.Notifications.AsNoTracking().CountAsync(n => n.UserId == hr.Id && n.Title.StartsWith("Folha de agosto de 2026 calculada")))).Should().Be(1);
        string logs;
        lock (_sink.Events)
        {
            logs = string.Join("\n", _sink.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Values)));
        }

        logs.Should().NotContain(Canary).And.NotContain(CanaryBr);
    }

    /// <summary>
    /// CA 14 e 16, depois da virada (tenant próprio): fecha só com as tabelas conferidas (422 com a lista), com 2FA, um título por
    /// funcionário e um por guia; a fechada nunca é recalculada: "Ver diferença" e "Gerar retificação" (complementar em rascunho).
    /// </summary>
    [Fact]
    public async Task Depois_da_virada_fecha_com_tabelas_conferidas_e_retifica_sem_recalcular_a_fechada()
    {
        var tenant = await tenancy.ProvisionAsync("flh02a-virada", "Virada Ltda", "v.localhost", Factory.Services);
        var hr = await ActorAsync(tenant, Rh + ",approve;finance:view;parties:view,create", totp: true);
        var period = new DateOnly(2026, 9, 1);
        var position = await PositionAsync(hr.Client);
        var one = await EmployeeAsync(tenant, hr.Client, position, period, 3000m);
        var two = await EmployeeAsync(tenant, hr.Client, position, period, 6000m);
        var organ = (await OkAsync(await hr.Client.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Órgão " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Supplier" } }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await DbAsync(tenant, async db =>
        {
            foreach (var id in new[] { one, two })
            {
                var e = await db.Employees.SingleAsync(x => x.Id == id);
                e.BankCode = "077";
                e.BankAgency = "0001";
                e.BankAccount = "12345" + id.ToString("N")[..3];
                e.BankAccountType = EmployeeBankAccountType.Checking;
                e.BankHolderName = e.Name;
                e.BankHolderCpf = e.Cpf;
                e.BankAccountVersion = 1;
                e.BankChangedAt = DateTimeOffset.UtcNow;
            }

            foreach (var kind in new[] { PayrollChargeKind.Fgts, PayrollChargeKind.Inss, PayrollChargeKind.Irrf })
            {
                db.HrParameters.Add(new HrParameter { Key = HrParametersService.PayeeKey(kind), Text = organ.ToString(), UpdatedAt = DateTimeOffset.UtcNow });
            }

            db.HrParameters.Add(new HrParameter { Key = HrParametersService.CalculatedOfficialKey, Value = 1m, UpdatedAt = DateTimeOffset.UtcNow });
        });
        foreach (var e in new[] { one, two })
        {
            await CloseTimesheetAsync(tenant, e, period);
        }

        var run = await CalculatedRunAsync(hr.Client, period);
        (await JsonAsync(await hr.Client.GetAsync($"{Runs}/{run}"))).GetProperty("isSimulation").GetBoolean().Should().BeFalse("a virada foi feita");
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/calculate", null));
        (await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/calculation"))).GetProperty("nextStep").GetProperty("label").GetString().Should().Be("Fechar folha");

        async Task<HttpResponseMessage> CloseAsync(Guid id) =>
            await hr.Client.SendAsync(Post($"{Runs}/{id}/close", new { paymentDate = (string?)null, twoFactorCode = hr.Codes!.Next(), version = (await JsonAsync(await hr.Client.GetAsync($"{Runs}/{id}"))).GetProperty("version").GetUInt32(), acknowledgedChanges = true }));

        // Tabelas "A confirmar": 422 com a lista.
        var pending = await CloseAsync(run);
        pending.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await JsonAsync(pending);
        body.GetProperty("code").GetString().Should().Be(PayrollCalculationRules.UnconfirmedTablesCode);
        body.GetProperty("detail").GetString().Should().StartWith("Confirme com a contadora as tabelas INSS de janeiro/2026, IRRF de janeiro/2026");

        // A contadora conferiu (seed é do sistema: quem aprova confere); a conferência entra nas mudanças desde o último fechamento.
        var tables = await DbAsync(tenant, db => db.PayrollTaxTables.AsNoTracking().Where(t => t.SeedPackage == 2026).ToListAsync());
        foreach (var t in tables)
        {
            var d = await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/payroll-tax-tables/{t.Id}"));
            await OkAsync(await hr.Client.PostAsJsonAsync($"/api/v1/hr/payroll-tax-tables/{t.Id}/check", new { accountant = "Contadora Fictícia", checkedOn = "2026-10-01", dataset = (string?)null, version = d.GetProperty("version").GetUInt32() }));
        }

        var preview = await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/close/sensitive"));
        preview.GetProperty("changes").GetArrayLength().Should().BeGreaterThan(0, "a conferência das tabelas aparece no diálogo (FLH-01 CA 12)");
        var closed = await OkAsync(await CloseAsync(run));
        closed.GetProperty("payables").GetInt32().Should().Be(5, "um título por funcionário e um por guia (FGTS, INSS, IRRF)");
        var payables = await DbAsync(tenant, async db =>
        {
            var origins = await db.Payslips.Where(p => p.PayrollRunId == run).Select(p => p.Id).Concat(db.PayrollRunCharges.Where(c => c.PayrollRunId == run).Select(c => c.Id)).ToListAsync();
            return await db.Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.Payroll && p.OriginId != null && origins.Contains(p.OriginId.Value)).ToListAsync();
        });
        payables.Should().HaveCount(5);

        // Fechada: nada é recalculado; "Ver diferença" mostra o que mudaria; "Gerar retificação" cria a complementar em rascunho.
        await DbAsync(tenant, async db => { (await db.Employees.SingleAsync(e => e.Id == one)).BaseSalary = 3300m; });
        (await hr.Client.PostAsync($"{Runs}/{run}/recalculate", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var diff = await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/difference/sensitive"));
        var changed = diff.GetProperty("employees").EnumerateArray().Single();
        changed.GetProperty("employeeId").GetGuid().Should().Be(one);
        changed.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("code").GetString() == "SAL").GetProperty("after").GetDecimal().Should().Be(3300m);
        var rectification = await OkAsync(await hr.Client.SendAsync(Post($"{Runs}/{run}/rectify", new { })), HttpStatusCode.Created);
        rectification.GetProperty("rectifiesRunId").GetGuid().Should().Be(run);
        rectification.GetProperty("status").GetString().Should().Be("Draft");
        var rectId = rectification.GetProperty("id").GetGuid();
        var slips = await PayslipsAsync(hr.Client, rectId);
        slips.Keys.Should().Equal([one], "só quem tem diferença");
        Amount(slips[one], "SAL").Should().Be(300m);
        slips[one].GetProperty("lines").EnumerateArray().First(l => l.GetProperty("code").GetString() == "SAL").GetProperty("description").GetString().Should().EndWith("(diferença)");
        (await hr.Client.SendAsync(Post($"{Runs}/{run}/rectify", new { }))).StatusCode.Should().Be(HttpStatusCode.Conflict, "uma retificação em rascunho por vez");
        var original = (await PayslipsAsync(hr.Client, run))[one];
        Amount(original, "SAL").Should().Be(3000m, "a folha fechada não muda");
        (await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/calculation"))).GetProperty("rectifications").EnumerateArray().Select(r => r.GetGuid()).Should().Contain(rectId);
    }
}
