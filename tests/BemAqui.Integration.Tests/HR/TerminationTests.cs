using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-05: rescisão com o pacote de 2026 e funcionários fictícios, cada teste num tenant próprio (o desligado sairia das folhas mensais dos
/// outros testes do TenantA). As datas são relativas a hoje: o desligamento é hoje e o espelho do mês é fechado direto no banco.
/// </summary>
public class TerminationTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Runs = "/api/v1/hr/payroll-runs";
    private const string Rh = "hr:view,create,edit,view_sensitive,approve;finance:view;parties:view,create";

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

    private async Task<T> InTenantAsync<T>(Tenant tenant, Func<IServiceProvider, Task<T>> action)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Tenant tenant, Func<TenantDbContext, Task<T>> action) => InTenantAsync(tenant, sp => action(sp.GetRequiredService<TenantDbContext>()));

    private Task<int> DbAsync(Tenant tenant, Func<TenantDbContext, Task> action) => DbAsync(tenant, async db =>
    {
        await action(db);
        await db.SaveChangesAsync();
        return 0;
    });

    /// <summary>Usuário num grupo próprio ("hr:view,edit"), com o 2FA ligado se pedir.</summary>
    private async Task<Actor> ActorAsync(Tenant tenant, string grants, bool totp = false)
    {
        var host = tenant.Domains.First().Host;
        var (id, _) = await tenancy.SeedUserAsync(tenant, "r" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        var secret = Totp.NewSecret();
        var stamp = await InTenantAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "Rescisão " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
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
                user.BeginTotpSetup(tenancy.Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenant.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
                user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            }

            await db.SaveChangesAsync();
            await sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
            return user.SecurityStamp;
        });
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp, otpAt: DateTimeOffset.UtcNow));
        return new Actor(id, client, totp ? new Authenticator(secret) : null);
    }

    private static HttpRequestMessage Post(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static DateOnly Month => new(Today.Year, Today.Month, 1);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Funcionário fictício admitido há 26 meses (aviso de 36 dias), salário 3.000.</summary>
    private static async Task<Guid> EmployeeAsync(HttpClient hr)
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var position = (await OkAsync(await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = "Montador " + Guid.NewGuid().ToString("N")[..6], cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = 220m }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var created = await OkAsync(await hr.PostAsJsonAsync("/api/v1/hr/employees", new
        {
            name = "Fictício " + Guid.NewGuid().ToString("N")[..6], cpf = NewCpf(), birthDate = "1990-05-10", admissionDate = Iso(Today.AddMonths(-26)),
            positionId = position, workScheduleId = (Guid?)null, baseSalary = 3000m, registration = "R" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant(),
        }), HttpStatusCode.Created);
        return created.GetProperty("employee").GetProperty("id").GetGuid();
    }

    private Task<int> CloseTimesheetAsync(Tenant tenant, Guid employee) => DbAsync(tenant, db =>
    {
        db.Timesheets.Add(new Timesheet
        {
            EmployeeId = employee, Period = Month, PeriodStart = Month, PeriodEnd = PayrollRules.PeriodEnd(Month), Status = TimesheetStatus.Closed, ClosedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return Task.CompletedTask;
    });

    private static object Body(decimal? fgts = 12345.67m) => new
    {
        kind = "WithoutCause", noticeKind = "Indemnified", terminationDate = Iso(Today), contractEndDate = (string?)null, fgtsBalance = fgts, fgtsStatementDate = fgts is null ? null : Iso(Today),
    };

    private static decimal Amount(JsonElement result, string code) =>
        result.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("code").GetString() == code).Sum(l => l.GetProperty("amount").GetDecimal());

    /// <summary>
    /// CA 1, 3, 4, 6, 7, 10, 11 e 12: a simulação sem gravar, as permissões, o desligamento antes do cálculo, o saldo do FGTS e o espelho
    /// obrigatórios, o cálculo em simulação (não fecha), o termo de conferência, o histórico, o estorno do desligamento e o canário no outbox.
    /// </summary>
    [Fact]
    public async Task Simula_sem_gravar_calcula_em_simulacao_e_estorna()
    {
        var tenant = await tenancy.ProvisionAsync("flh05-rescisao", "Rescisão Ltda", "rescisao.localhost", tenancy.Factory.Services);
        var hr = await ActorAsync(tenant, Rh, totp: true);
        var viewer = await ActorAsync(tenant, "hr:view");
        var noSensitive = await ActorAsync(tenant, "hr:view,edit");
        var employee = await EmployeeAsync(hr.Client);
        var url = $"/api/v1/hr/employees/{employee}/termination";

        // CA 1: a simulação mostra as verbas, a multa e o custo total, sem gravar nada; o padrão é hoje e o aviso do tipo.
        var view = await OkAsync(await viewer.Client.GetAsync(url));
        view.GetProperty("terminated").GetBoolean().Should().BeFalse();
        view.GetProperty("suggestedDate").GetString().Should().Be(Iso(Today));
        view.GetProperty("noticeDays").GetInt32().Should().Be(36, "2 anos completos: 30 + 3 × 2");
        view.GetProperty("kinds").EnumerateArray().Single(k => k.GetProperty("kind").GetString() == "WithoutCause").GetProperty("defaultNotice").GetString().Should().Be("Indemnified");
        (await viewer.Client.PostAsJsonAsync(url + "/simulate", Body())).StatusCode.Should().Be(HttpStatusCode.Forbidden, "simular exige hr.edit");
        (await noSensitive.Client.PostAsJsonAsync(url + "/simulate", Body())).StatusCode.Should().Be(HttpStatusCode.Forbidden, "e hr.view_sensitive");
        var empty = await hr.Client.PostAsJsonAsync(url + "/simulate", new { kind = (string?)null, terminationDate = (string?)null });
        empty.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(empty)).GetProperty("detail").GetString().Should().Be(TerminationPolicy.EmptySimulationMessage);
        var simulation = await OkAsync(await hr.Client.PostAsJsonAsync(url + "/simulate", Body()));
        Amount(simulation, "AVISOIND").Should().Be(3600m);
        simulation.GetProperty("fine").GetDecimal().Should().BeGreaterThan(4000m);
        simulation.GetProperty("totalCost").GetDecimal().Should().BeGreaterThan(simulation.GetProperty("net").GetDecimal());
        simulation.GetProperty("paymentDeadline").GetString().Should().Be(Iso(Today.AddDays(10)));
        (await DbAsync(tenant, db => db.EmployeeTerminations.CountAsync())).Should().Be(0, "a simulação não grava");
        (await DbAsync(tenant, db => db.PayrollRuns.CountAsync(r => r.Type == PayrollRunType.Termination))).Should().Be(0);

        // CA 3: calcular pede o desligamento registrado (CAD-04b); depois dele, o próximo passo é "Calcular rescisão".
        var early = await hr.Client.SendAsync(Post(url + "/calculate", Body()));
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(early)).GetProperty("code").GetString().Should().Be(TerminationPolicy.NotTerminatedCode);
        await OkAsync(await hr.Client.PostAsJsonAsync($"/api/v1/hr/employees/{employee}/termination", new { terminationDate = Iso(Today) }));
        (await OkAsync(await viewer.Client.GetAsync(url))).GetProperty("nextSteps")[0].GetProperty("label").GetString().Should().Be("Calcular rescisão");

        // CA 4: o saldo do FGTS é obrigatório nos tipos com multa; o espelho do mês precisa estar fechado.
        var noBalance = await hr.Client.SendAsync(Post(url + "/calculate", Body(fgts: null)));
        noBalance.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(noBalance)).GetProperty("detail").GetString().Should().Be("Informe o saldo do FGTS do extrato para calcular a multa.");
        var open = await hr.Client.SendAsync(Post(url + "/calculate", Body()));
        open.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(open)).GetProperty("code").GetString().Should().Be(TerminationPolicy.TimesheetOpenCode);
        await CloseTimesheetAsync(tenant, employee);

        // CA 3 e 7: a folha de rescisão nasce em simulação; o fechamento responde 409 com a mensagem.
        (await noSensitive.Client.SendAsync(Post(url + "/calculate", Body()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var calc = await OkAsync(await hr.Client.SendAsync(Post(url + "/calculate", Body())));
        var run = calc.GetProperty("run");
        var runId = run.GetProperty("id").GetGuid();
        run.GetProperty("type").GetString().Should().Be("Termination");
        run.GetProperty("mode").GetString().Should().Be("Calculated");
        run.GetProperty("isSimulation").GetBoolean().Should().BeTrue();
        calc.GetProperty("nextSteps")[0].GetProperty("label").GetString().Should().Be("Gerar termo para conferência");
        var termination = calc.GetProperty("termination");
        var terminationId = termination.GetProperty("id").GetGuid();
        termination.GetProperty("status").GetString().Should().Be("Calculated");
        termination.GetProperty("noticeDays").GetInt32().Should().Be(36);
        Amount(calc.GetProperty("result"), "AVISOIND").Should().Be(3600m);
        var slip = await OkAsync(await hr.Client.GetAsync($"{Runs}/{runId}/payslips/{calc.GetProperty("payslipId").GetGuid()}/sensitive"));
        slip.GetProperty("calculation").GetProperty("calculatorVersion").GetString().Should().Be(TerminationCalculator.Version);
        slip.GetProperty("calculation").GetProperty("inputs").EnumerateArray().Select(i => i.GetProperty("label").GetString()).Should().Contain("Tipo de desligamento");
        var version = (await JsonAsync(await hr.Client.GetAsync($"{Runs}/{runId}"))).GetProperty("version").GetUInt32();
        var close = await hr.Client.SendAsync(Post($"{Runs}/{runId}/close", new { paymentDate = (string?)null, twoFactorCode = hr.Codes!.Next(), version, acknowledgedChanges = true }));
        close.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await JsonAsync(close);
        body.GetProperty("code").GetString().Should().Be(TerminationPolicy.SimulationCode);
        body.GetProperty("detail").GetString().Should().Be("Rescisão calculada em conferência: feche a rescisão lançada.");

        // CA 6: o termo para conferência em PDF, só com hr.view_sensitive.
        var term = await hr.Client.GetAsync($"/api/v1/hr/terminations/{terminationId}/term");
        term.StatusCode.Should().Be(HttpStatusCode.OK);
        term.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await noSensitive.Client.GetAsync($"/api/v1/hr/terminations/{terminationId}/term")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CA 12: o histórico mostra o cálculo e o recálculo com a versão do motor.
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{runId}/recalculate", null));
        var history = await OkAsync(await viewer.Client.GetAsync($"{Runs}/{runId}/calculations"));
        history.EnumerateArray().Select(e => e.GetProperty("actionLabel").GetString()).Should().Equal("Recalculou", "Calculou");
        history[0].GetProperty("calculatorVersion").GetString().Should().Be(TerminationCalculator.Version);

        // CA 11: nenhum valor no outbox (o canário é o saldo do FGTS digitado).
        var outbox = await DbAsync(tenant, db => db.OutboxMessages.AsNoTracking().Select(m => m.Payload).ToListAsync());
        outbox.Should().NotContain(p => p.Contains("12345", StringComparison.Ordinal));

        // CA 10: o estorno do desligamento devolve o funcionário a ativo e tira a folha em rascunho, sem apagar a rescisão.
        var current = (await OkAsync(await viewer.Client.GetAsync(url))).GetProperty("termination").GetProperty("version").GetUInt32();
        (await hr.Client.PostAsJsonAsync($"/api/v1/hr/terminations/{terminationId}/cancel", new { reason = "x", version = current })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cancelled = await OkAsync(await hr.Client.PostAsJsonAsync($"/api/v1/hr/terminations/{terminationId}/cancel", new { reason = "Funcionário voltou antes do pagamento", version = current }));
        cancelled.GetProperty("terminated").GetBoolean().Should().BeFalse();
        cancelled.GetProperty("termination").ValueKind.Should().Be(JsonValueKind.Null);
        (await hr.Client.GetAsync($"{Runs}/{runId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DbAsync(tenant, db => db.EmployeeTerminations.AsNoTracking().SingleAsync(t => t.Id == terminationId))).Status.Should().Be(TerminationStatus.Cancelled);
        (await OkAsync(await viewer.Client.GetAsync($"/api/v1/hr/employees/{employee}"))).GetProperty("status").GetString().Should().Be("Active");
    }

    /// <summary>
    /// CA 8 e 9: com a virada e o caso de rescisão conferido (FLH-06, gravados direto), a rescisão fecha com 2FA: o título do funcionário e as
    /// guias do FGTS rescisório e da multa vencem no prazo de 10 dias, a rescisão fica fechada, as férias encerradas e o funcionário fora da
    /// Mensal calculada; o estorno pede reabrir e, com a folha paga, responde que a rescisão já foi paga.
    /// </summary>
    [Fact]
    public async Task Oficial_fecha_com_titulos_no_prazo_e_encerra()
    {
        var tenant = await tenancy.ProvisionAsync("flh05-oficial", "Rescisão Oficial Ltda", "rescisao-oficial.localhost", tenancy.Factory.Services);
        var hr = await ActorAsync(tenant, Rh, totp: true);
        var employee = await EmployeeAsync(hr.Client);
        var colleague = await EmployeeAsync(hr.Client);
        var organ = (await OkAsync(await hr.Client.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Caixa " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Supplier" } }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await DbAsync(tenant, async db =>
        {
            var e = await db.Employees.SingleAsync(x => x.Id == employee);
            e.BankCode = "077";
            e.BankAgency = "0001";
            e.BankAccount = "12345" + employee.ToString("N")[..3];
            e.BankAccountType = EmployeeBankAccountType.Checking;
            e.BankHolderName = e.Name;
            e.BankHolderCpf = e.Cpf;
            e.BankAccountVersion = 1;
            e.BankChangedAt = DateTimeOffset.UtcNow.AddDays(-60);
            foreach (var kind in new[] { PayrollChargeKind.Fgts, PayrollChargeKind.Inss, PayrollChargeKind.Irrf })
            {
                db.HrParameters.Add(new HrParameter { Key = HrParametersService.PayeeKey(kind), Text = organ.ToString(), UpdatedAt = DateTimeOffset.UtcNow });
            }

            db.HrParameters.Add(new HrParameter { Key = HrParametersService.CalculatedOfficialKey, Value = 1m, UpdatedAt = DateTimeOffset.UtcNow });
            db.HrParameters.Add(new HrParameter { Key = HrParametersService.ModeKey(PayrollParallelType.Termination), Value = 1m, UpdatedAt = DateTimeOffset.UtcNow });
        });
        foreach (var t in await DbAsync(tenant, db => db.PayrollTaxTables.AsNoTracking().Where(t => t.SeedPackage == 2026).ToListAsync()))
        {
            var d = await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/payroll-tax-tables/{t.Id}"));
            await OkAsync(await hr.Client.PostAsJsonAsync($"/api/v1/hr/payroll-tax-tables/{t.Id}/check", new { accountant = "Contadora Fictícia", checkedOn = "2026-10-01", dataset = (string?)null, version = d.GetProperty("version").GetUInt32() }));
        }

        await OkAsync(await hr.Client.PostAsJsonAsync($"/api/v1/hr/employees/{employee}/termination", new { terminationDate = Iso(Today) }));
        await CloseTimesheetAsync(tenant, employee);
        var calc = await OkAsync(await hr.Client.SendAsync(Post($"/api/v1/hr/employees/{employee}/termination/calculate", Body())));
        calc.GetProperty("run").GetProperty("isSimulation").GetBoolean().Should().BeFalse();
        calc.GetProperty("nextSteps")[0].GetProperty("label").GetString().Should().Be("Fechar folha de rescisão");
        var runId = calc.GetProperty("run").GetProperty("id").GetGuid();
        var terminationId = calc.GetProperty("termination").GetProperty("id").GetGuid();
        var deadline = Today.AddDays(10);

        // CA 8: o diálogo mostra o líquido, as guias, a multa e o prazo; o pagamento sugerido é o prazo legal.
        var preview = await OkAsync(await hr.Client.GetAsync($"{Runs}/{runId}/close/sensitive"));
        preview.GetProperty("blockers").GetArrayLength().Should().Be(0, preview.GetRawText());
        preview.GetProperty("legalPaymentDeadline").GetString().Should().Be(Iso(deadline));
        preview.GetProperty("suggestedPaymentDate").GetString().Should().Be(Iso(deadline));
        preview.GetProperty("fgtsFine").GetDecimal().Should().Be(calc.GetProperty("result").GetProperty("fine").GetDecimal());
        preview.GetProperty("latePaymentWarning").ValueKind.Should().Be(JsonValueKind.Null);
        preview.GetProperty("type").GetString().Should().Be("Termination");
        var version = (await JsonAsync(await hr.Client.GetAsync($"{Runs}/{runId}"))).GetProperty("version").GetUInt32();
        (await hr.Client.SendAsync(Post($"{Runs}/{runId}/close", new { paymentDate = (string?)null, twoFactorCode = "000000", version, acknowledgedChanges = true })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "o fechamento pede o 2FA de novo");
        var closed = await OkAsync(await hr.Client.SendAsync(Post($"{Runs}/{runId}/close", new { paymentDate = (string?)null, twoFactorCode = hr.Codes!.Next(), version, acknowledgedChanges = true })));
        closed.GetProperty("payables").GetInt32().Should().BeGreaterThanOrEqualTo(3, "o título do funcionário e as guias do FGTS rescisório e da multa");

        var slipId = await DbAsync(tenant, db => db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == runId).Select(p => p.Id).SingleAsync());
        var charges = await DbAsync(tenant, db => db.PayrollRunCharges.AsNoTracking().Where(c => c.PayrollRunId == runId && c.Kind == PayrollChargeKind.Fgts).Select(c => (Guid?)c.Id).ToListAsync());
        var salary = await DbAsync(tenant, db => db.Payables.AsNoTracking().SingleAsync(p => p.Origin == DocumentOrigin.Payroll && p.OriginId == slipId));
        var fgts = await DbAsync(tenant, db => db.Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.Payroll && charges.Contains(p.OriginId)).ToListAsync());
        salary.PayeeType.Should().Be(PayeeType.Employee);
        salary.PayeeId.Should().Be(employee);
        salary.DueDate.Should().Be(deadline);
        fgts.Should().HaveCount(2, "o FGTS rescisório e a multa");
        fgts.Should().OnlyContain(p => p.DueDate == deadline && p.PayeeType == PayeeType.Party && p.PayeeId == organ);

        // CA 9: a rescisão fechada, as férias encerradas e o funcionário fora da Mensal calculada da competência.
        (await DbAsync(tenant, db => db.EmployeeTerminations.AsNoTracking().SingleAsync(t => t.Id == terminationId))).Status.Should().Be(TerminationStatus.Closed);
        (await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/employees/{employee}/vacations"))).GetProperty("periods").EnumerateArray()
            .Should().OnlyContain(p => p.GetProperty("status").GetString() == "Closed");
        var monthly = await OkAsync(await hr.Client.SendAsync(Post(Runs, new { period = Month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), type = "Monthly", employeeIds = (Guid[]?)null, mode = "Calculated" })), HttpStatusCode.Created);
        var monthlyId = monthly.GetProperty("id").GetGuid();
        var people = await DbAsync(tenant, db => db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == monthlyId).Select(p => p.EmployeeId).ToListAsync());
        people.Should().Contain(colleague).And.NotContain(employee);

        // CA 10: com a folha fechada, o estorno pede reabrir; com a folha paga, a rescisão já foi paga.
        var current = (await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/employees/{employee}/termination"))).GetProperty("termination").GetProperty("version").GetUInt32();
        var reopenFirst = await hr.Client.PostAsJsonAsync($"/api/v1/hr/terminations/{terminationId}/cancel", new { reason = "Funcionário voltou", version = current });
        reopenFirst.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(reopenFirst)).GetProperty("detail").GetString().Should().Be(TerminationPolicy.ClosedMessage);
        await DbAsync(tenant, async db => (await db.PayrollRuns.SingleAsync(r => r.Id == runId)).Status = PayrollRunStatus.Paid);
        var paid = await hr.Client.PostAsJsonAsync($"/api/v1/hr/terminations/{terminationId}/cancel", new { reason = "Funcionário voltou", version = current });
        paid.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(paid)).GetProperty("detail").GetString().Should().Be("A rescisão já foi paga: fale com a contadora para o estorno.");
    }
}
