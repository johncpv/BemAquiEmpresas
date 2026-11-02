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
/// FLH-03: férias com o pacote de 2026 e funcionários fictícios, cada teste num tenant próprio (o funcionário com mais de um ano de casa
/// entraria nas folhas mensais dos outros testes do TenantA). As datas são relativas a hoje: o primeiro período aquisitivo está completo.
/// </summary>
public class VacationTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
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

    /// <summary>Usuário num grupo próprio ("hr:view,edit"; vazio é sem grupo), com o 2FA ligado se pedir.</summary>
    private async Task<Actor> ActorAsync(Tenant tenant, string grants, bool totp = false, Guid? employee = null)
    {
        var host = tenant.Domains.First().Host;
        var (id, _) = await tenancy.SeedUserAsync(tenant, "v" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        var secret = Totp.NewSecret();
        var stamp = await InTenantAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            if (grants.Length > 0)
            {
                var group = new Group { Name = "Férias " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
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
            }

            var user = await db.Users.SingleAsync(u => u.Id == id);
            user.EmployeeId = employee;
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

    /// <summary>Funcionário fictício admitido há 14 meses (o primeiro período aquisitivo completo e a gozar), salário 3.000.</summary>
    private static async Task<Guid> EmployeeAsync(HttpClient hr)
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var position = (await OkAsync(await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = "Montador " + Guid.NewGuid().ToString("N")[..6], cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var created = await OkAsync(await hr.PostAsJsonAsync("/api/v1/hr/employees", new
        {
            name = "Fictício " + Guid.NewGuid().ToString("N")[..6], cpf = NewCpf(), birthDate = "1990-05-10", admissionDate = Today.AddMonths(-14).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            positionId = position, workScheduleId = (Guid?)null, baseSalary = 3000m, registration = "V" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant(),
        }), HttpStatusCode.Created);
        return created.GetProperty("employee").GetProperty("id").GetGuid();
    }

    private static decimal Amount(JsonElement payslip, string code) =>
        payslip.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("code").GetString() == code).Sum(l => l.GetProperty("amount").GetDecimal());

    /// <summary>
    /// CA 1, 4, 5, 6, 8, 11 e 12: períodos com os chips, a validação com a mensagem do parâmetro, a programação no espelho, no "Meu ponto" e no
    /// outbox, o cálculo (simulação: não fecha), o cancelamento com o saldo de volta, as permissões e o IDOR do autoatendimento.
    /// </summary>
    [Fact]
    public async Task Programa_calcula_em_simulacao_e_cancela()
    {
        var tenant = await tenancy.ProvisionAsync("flh03-ferias", "Férias Ltda", "ferias.localhost", tenancy.Factory.Services);
        var hr = await ActorAsync(tenant, Rh, totp: true);
        var viewer = await ActorAsync(tenant, "hr:view");
        var noSensitive = await ActorAsync(tenant, "hr:view,edit");
        var employee = await EmployeeAsync(hr.Client);
        var other = await EmployeeAsync(hr.Client);

        var tab = await OkAsync(await viewer.Client.GetAsync($"/api/v1/hr/employees/{employee}/vacations"));
        var periods = tab.GetProperty("periods").EnumerateArray().ToList();
        periods.Should().HaveCount(2);
        var first = periods.Single(p => p.GetProperty("sequence").GetInt32() == 1);
        first.GetProperty("status").GetString().Should().Be("Available");
        first.GetProperty("statusLabel").GetString().Should().Be("A gozar");
        first.GetProperty("entitledDays").GetInt32().Should().Be(30);
        periods.Single(p => p.GetProperty("sequence").GetInt32() == 2).GetProperty("statusLabel").GetString().Should().Be("Em aquisição");
        var suggestion = tab.GetProperty("suggestion");
        var periodId = suggestion.GetProperty("periodId").GetGuid();
        periodId.Should().Be(first.GetProperty("id").GetGuid());
        var start = DateOnly.Parse(suggestion.GetProperty("startsOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        start.Should().BeOnOrAfter(Today.AddDays(30), "o início sugerido respeita o aviso de 30 dias");
        suggestion.GetProperty("maxSoldDays").GetInt32().Should().Be(10);

        // CA 4: 422 com a mensagem e o valor do parâmetro; programar exige hr.edit.
        var invalid = await hr.Client.SendAsync(Post($"/api/v1/hr/employees/{employee}/vacations", new { periodId, startsOn = start, days = 10, soldDays = 0, advance13th = false }));
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(invalid)).GetProperty("detail").GetString().Should().Be("O primeiro período precisa ter pelo menos 14 dias.");
        (await viewer.Client.SendAsync(Post($"/api/v1/hr/employees/{employee}/vacations", new { periodId, startsOn = start, days = 20, soldDays = 10, advance13th = false })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CA 5: programadas; o próximo passo é o aviso; o espelho mostra "Férias"; o evento sai pelo outbox só com ids e datas.
        var scheduled = await OkAsync(await hr.Client.SendAsync(Post($"/api/v1/hr/employees/{employee}/vacations", new { periodId, startsOn = start, days = 20, soldDays = 10, advance13th = false })), HttpStatusCode.Created);
        scheduled.GetProperty("message").GetString().Should().Be("Férias programadas.");
        scheduled.GetProperty("nextSteps")[0].GetProperty("label").GetString().Should().Be("Gerar aviso de férias");
        var schedule = scheduled.GetProperty("schedule");
        var scheduleId = schedule.GetProperty("id").GetGuid();
        var due = DateOnly.Parse(schedule.GetProperty("paymentDueOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        due.Should().BeOnOrBefore(start.AddDays(-2), "vence até 2 dias antes do início (hr.vacation_payment_days_before)");
        (await OkAsync(await viewer.Client.GetAsync($"/api/v1/hr/employees/{employee}/vacations"))).GetProperty("periods").EnumerateArray()
            .Single(p => p.GetProperty("sequence").GetInt32() == 1).GetProperty("balance").GetInt32().Should().Be(0);
        var sheet = await OkAsync(await viewer.Client.GetAsync($"/api/v1/hr/employees/{employee}/timesheets/{start:yyyy-MM}"));
        sheet.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("day").GetString() == start.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .GetProperty("statusLabel").GetString().Should().Be("Férias");
        var outbox = await DbAsync(tenant, db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains("EmployeeVacationScheduled")).Select(m => m.Payload).ToListAsync());
        outbox.Should().ContainSingle(p => p.Contains(employee.ToString(), StringComparison.Ordinal)).Which.Should().NotContain("3000");
        var notice = await viewer.Client.GetAsync($"/api/v1/hr/vacations/{scheduleId}/notice");
        notice.StatusCode.Should().Be(HttpStatusCode.OK);
        notice.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        // CA 12: o funcionário vê as datas e o saldo, sem valor, só os dele.
        var me = await ActorAsync(tenant, string.Empty, employee: employee);
        var mine = await OkAsync(await me.Client.GetAsync("/api/v1/me/hr/vacations"));
        mine.GetProperty("schedules").EnumerateArray().Should().ContainSingle().Which.GetProperty("days").GetInt32().Should().Be(20);
        mine.GetRawText().Should().NotContain("paymentDueOn").And.NotContain("amount");
        var otherMe = await ActorAsync(tenant, string.Empty, employee: other);
        (await OkAsync(await otherMe.Client.GetAsync("/api/v1/me/hr/vacations"))).GetProperty("schedules").GetArrayLength().Should().Be(0, "só a própria programação");
        (await me.Client.GetAsync($"/api/v1/me/hr/vacations?employeeId={other}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // CA 6 e 8: calcular exige hr.view_sensitive; a calculada é simulação, não fecha e a prévia sai.
        (await noSensitive.Client.SendAsync(Post($"/api/v1/hr/vacations/{scheduleId}/calculate", new { }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var calc = await OkAsync(await hr.Client.SendAsync(Post($"/api/v1/hr/vacations/{scheduleId}/calculate", new { })));
        var run = calc.GetProperty("run");
        var runId = run.GetProperty("id").GetGuid();
        run.GetProperty("type").GetString().Should().Be("Vacation");
        run.GetProperty("mode").GetString().Should().Be("Calculated");
        run.GetProperty("isSimulation").GetBoolean().Should().BeTrue();
        calc.GetProperty("nextSteps")[0].GetProperty("label").GetString().Should().Be("Baixar prévia do recibo");
        var payslipId = calc.GetProperty("payslipId").GetGuid();
        var slip = await OkAsync(await hr.Client.GetAsync($"{Runs}/{runId}/payslips/{payslipId}/sensitive"));
        Amount(slip, "FERIAS").Should().Be(2000m);
        Amount(slip, "FER13").Should().Be(666.67m);
        Amount(slip, "ABONO").Should().Be(1000m);
        Amount(slip, "ABONO13").Should().Be(333.33m);
        Amount(slip, "INSS").Should().BeGreaterThan(0m);
        slip.GetProperty("inssBase").GetDecimal().Should().Be(2666.67m, "o abono e o seu 1/3 ficam fora do INSS");
        slip.GetProperty("calculation").GetProperty("calculatorVersion").GetString().Should().Be(PayrollCalculator.Version);
        slip.GetProperty("calculation").GetProperty("inputs").EnumerateArray().Select(i => i.GetProperty("label").GetString()).Should().Contain("Período aquisitivo");
        (await hr.Client.GetAsync($"{Runs}/{runId}/payslips/{payslipId}/pdf")).StatusCode.Should().Be(HttpStatusCode.OK, "a prévia do recibo sai com a marca \"Simulação\"");
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{runId}/recalculate", null));
        var history = await OkAsync(await viewer.Client.GetAsync($"{Runs}/{runId}/calculations"));
        history.EnumerateArray().Select(e => e.GetProperty("actionLabel").GetString()).Should().Equal("Recalculou", "Calculou");
        var version = (await JsonAsync(await hr.Client.GetAsync($"{Runs}/{runId}"))).GetProperty("version").GetUInt32();
        var close = await hr.Client.SendAsync(Post($"{Runs}/{runId}/close", new { paymentDate = (string?)null, twoFactorCode = hr.Codes!.Next(), version, acknowledgedChanges = true }));
        close.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await JsonAsync(close);
        body.GetProperty("code").GetString().Should().Be(VacationPolicy.SimulationCode);
        body.GetProperty("detail").GetString().Should().Be("Férias calculadas em conferência: feche a folha de férias lançada.");

        // CA 11: cancelar com motivo devolve o saldo, tira a folha em rascunho e publica o cancelamento.
        var current = (await OkAsync(await viewer.Client.GetAsync($"/api/v1/hr/employees/{employee}/vacations"))).GetProperty("schedules")[0].GetProperty("version").GetUInt32();
        (await hr.Client.PostAsJsonAsync($"/api/v1/hr/vacations/{scheduleId}/cancel", new { reason = "x", version = current })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cancelled = await OkAsync(await hr.Client.PostAsJsonAsync($"/api/v1/hr/vacations/{scheduleId}/cancel", new { reason = "Mudança de planejamento", version = current }));
        cancelled.GetProperty("status").GetString().Should().Be("Cancelled");
        (await hr.Client.GetAsync($"{Runs}/{runId}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "a folha de férias em rascunho sai com a programação");
        (await OkAsync(await viewer.Client.GetAsync($"/api/v1/hr/employees/{employee}/vacations"))).GetProperty("periods").EnumerateArray()
            .Single(p => p.GetProperty("sequence").GetInt32() == 1).GetProperty("balance").GetInt32().Should().Be(30);
        (await DbAsync(tenant, db => db.OutboxMessages.AsNoTracking().CountAsync(m => m.Type.Contains("EmployeeVacationCancelled")))).Should().Be(1);
        var audit = await OkAsync(await hr.Client.GetAsync($"/api/v1/audit/VacationSchedule/{scheduleId}"));
        string.Join("\n", audit.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("changes").GetString())).Should().NotContain("Mudança de planejamento", "o motivo sai mascarado");
    }

    /// <summary>
    /// CA 9: depois da virada e do primeiro caso de férias conferido (FLH-06, gravados direto), a folha de férias fecha com 2FA, o título do
    /// funcionário vence na data legal e entra no fluxo de caixa, a programação fica paga e o cancelamento pede reabrir a folha antes.
    /// </summary>
    [Fact]
    public async Task Oficial_fecha_com_titulo_do_funcionario_no_vencimento_legal()
    {
        var tenant = await tenancy.ProvisionAsync("flh03-oficial", "Férias Oficial Ltda", "ferias-oficial.localhost", tenancy.Factory.Services);
        var hr = await ActorAsync(tenant, Rh, totp: true);
        var employee = await EmployeeAsync(hr.Client);
        var organ = (await OkAsync(await hr.Client.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Receita " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Supplier" } }), HttpStatusCode.Created))
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
            db.HrParameters.Add(new HrParameter { Key = HrParametersService.ModeKey(PayrollParallelType.Vacation), Value = 1m, UpdatedAt = DateTimeOffset.UtcNow });
        });
        foreach (var t in await DbAsync(tenant, db => db.PayrollTaxTables.AsNoTracking().Where(t => t.SeedPackage == 2026).ToListAsync()))
        {
            var d = await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/payroll-tax-tables/{t.Id}"));
            await OkAsync(await hr.Client.PostAsJsonAsync($"/api/v1/hr/payroll-tax-tables/{t.Id}/check", new { accountant = "Contadora Fictícia", checkedOn = "2026-10-01", dataset = (string?)null, version = d.GetProperty("version").GetUInt32() }));
        }

        var tab = await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/employees/{employee}/vacations"));
        var suggestion = tab.GetProperty("suggestion");
        var scheduled = await OkAsync(await hr.Client.SendAsync(Post($"/api/v1/hr/employees/{employee}/vacations",
            new { periodId = suggestion.GetProperty("periodId").GetGuid(), startsOn = suggestion.GetProperty("startsOn").GetString(), days = 30, soldDays = 0, advance13th = false })), HttpStatusCode.Created);
        var scheduleId = scheduled.GetProperty("schedule").GetProperty("id").GetGuid();
        var due = DateOnly.Parse(scheduled.GetProperty("schedule").GetProperty("paymentDueOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var calc = await OkAsync(await hr.Client.SendAsync(Post($"/api/v1/hr/vacations/{scheduleId}/calculate", new { })));
        calc.GetProperty("run").GetProperty("isSimulation").GetBoolean().Should().BeFalse();
        calc.GetProperty("nextSteps")[0].GetProperty("label").GetString().Should().Be("Fechar folha de férias");
        var runId = calc.GetProperty("run").GetProperty("id").GetGuid();

        var preview = await OkAsync(await hr.Client.GetAsync($"{Runs}/{runId}/close/sensitive"));
        preview.GetProperty("blockers").GetArrayLength().Should().Be(0, preview.GetRawText());
        preview.GetProperty("suggestedPaymentDate").GetString().Should().Be(due.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        preview.GetProperty("legalPaymentDeadline").GetString().Should().Be(due.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        preview.GetProperty("latePaymentWarning").ValueKind.Should().Be(JsonValueKind.Null);
        var version = (await JsonAsync(await hr.Client.GetAsync($"{Runs}/{runId}"))).GetProperty("version").GetUInt32();
        var closed = await OkAsync(await hr.Client.SendAsync(Post($"{Runs}/{runId}/close", new { paymentDate = (string?)null, twoFactorCode = hr.Codes!.Next(), version, acknowledgedChanges = true })));
        closed.GetProperty("payables").GetInt32().Should().BeGreaterThanOrEqualTo(2, "o título do funcionário e a guia do INSS retido");

        var payable = await DbAsync(tenant, async db =>
        {
            var slip = await db.Payslips.AsNoTracking().SingleAsync(p => p.PayrollRunId == runId);
            return await db.Payables.AsNoTracking().SingleAsync(p => p.Origin == DocumentOrigin.Payroll && p.OriginId == slip.Id);
        });
        payable.PayeeType.Should().Be(PayeeType.Employee);
        payable.PayeeId.Should().Be(employee);
        payable.DueDate.Should().Be(due);
        payable.Status.Should().Be(PayableStatus.Open, "entra no fluxo de caixa");
        var schedule = (await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/employees/{employee}/vacations"))).GetProperty("schedules").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == scheduleId);
        schedule.GetProperty("status").GetString().Should().Be("Paid");
        schedule.GetProperty("runClosed").GetBoolean().Should().BeTrue();

        // CA 11: com a folha de férias fechada, cancelar pede reabrir antes; recalcular também.
        var cancel = await hr.Client.PostAsJsonAsync($"/api/v1/hr/vacations/{scheduleId}/cancel", new { reason = "Funcionário pediu para adiar", version = schedule.GetProperty("version").GetUInt32() });
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(cancel)).GetProperty("detail").GetString().Should().Be("Reabra a folha de férias antes.");
        (await hr.Client.SendAsync(Post($"/api/v1/hr/vacations/{scheduleId}/calculate", new { }))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
