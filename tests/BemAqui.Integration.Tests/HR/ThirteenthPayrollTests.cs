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
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-04: folha de 13º calculada com o pacote de 2026 e funcionários fictícios, cada teste no próprio tenant (a folha de 13º pega todos os
/// ativos do ano, e a virada não pode vazar para os outros testes): prévia dos avos, simulação até a conferência (CA 5), duplicidade e a
/// 2ª sem a 1ª (CA 8), médias das horas extras das Mensais fechadas, e, depois da virada e do caso conferido, o fechamento com um título por
/// funcionário e por guia vencendo na data limite (CA 6).
/// </summary>
public class ThirteenthPayrollTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Runs = "/api/v1/hr/payroll-runs";
    private const string Rh = "hr:view,create,edit,approve,view_sensitive";

    private sealed class Authenticator(byte[] secret)
    {
        private long _last = long.MinValue;

        public string Next()
        {
            var step = Math.Max(_last + 1, Totp.StepAt(DateTimeOffset.UtcNow) - 1);
            _last = step;
            return Totp.Code(secret, step);
        }
    }

    private sealed record Actor(Guid Id, HttpClient Client, Authenticator Codes);

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

    private async Task<Actor> ActorAsync(Tenant tenant, string grants)
    {
        var host = tenant.Domains.First().Host;
        var (id, _) = await tenancy.SeedUserAsync(tenant, "t" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        var secret = Totp.NewSecret();
        var stamp = await InTenantAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "13º " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
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
            user.BeginTotpSetup(tenancy.Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenant.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
            user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            await db.SaveChangesAsync();
            await sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
            return user.SecurityStamp;
        });
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp, otpAt: DateTimeOffset.UtcNow));
        return new Actor(id, client, new Authenticator(secret));
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

    private static async Task<Guid> EmployeeAsync(HttpClient hr, string admission, decimal salary)
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var position = (await OkAsync(await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = "Montador " + Guid.NewGuid().ToString("N")[..6], cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var created = await OkAsync(await hr.PostAsJsonAsync("/api/v1/hr/employees", new
        {
            name = "Fictício " + Guid.NewGuid().ToString("N")[..6], cpf = NewCpf(), birthDate = "1990-05-10", admissionDate = admission, positionId = position, workScheduleId = (Guid?)null,
            baseSalary = salary, registration = "T" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant(),
        }), HttpStatusCode.Created);
        return created.GetProperty("employee").GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient hr, string period, string type, bool? confirm = null) =>
        hr.SendAsync(Post(Runs, new { period, type, employeeIds = (Guid[]?)null, mode = "Calculated", confirmWithoutAdvance = confirm }));

    private static async Task<Dictionary<Guid, JsonElement>> PayslipsAsync(HttpClient hr, Guid run)
    {
        var result = new Dictionary<Guid, JsonElement>();
        foreach (var row in (await OkAsync(await hr.GetAsync($"{Runs}/{run}/payslips/sensitive"))).EnumerateArray())
        {
            result[row.GetProperty("employeeId").GetGuid()] = await OkAsync(await hr.GetAsync($"{Runs}/{run}/payslips/{row.GetProperty("id").GetGuid()}/sensitive"));
        }

        return result;
    }

    private static decimal Amount(JsonElement payslip, string code) =>
        payslip.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("code").GetString() == code).Sum(l => l.GetProperty("amount").GetDecimal());

    private static async Task<HttpResponseMessage> CloseAsync(Actor hr, Guid run) =>
        await hr.Client.SendAsync(Post($"{Runs}/{run}/close", new
        {
            paymentDate = (string?)null, twoFactorCode = hr.Codes.Next(), version = (await JsonAsync(await hr.Client.GetAsync($"{Runs}/{run}"))).GetProperty("version").GetUInt32(), acknowledgedChanges = true,
        }));

    /// <summary>CA 1, 3, 5 e 8 e as médias: prévia dos avos, 1ª parcela sem INSS e IRRF, simulação que não fecha, duplicidade e a 2ª sem a 1ª.</summary>
    [Fact]
    public async Task Primeira_e_segunda_parcela_em_simulacao()
    {
        var tenant = await tenancy.ProvisionAsync("flh04-sim", "Treze Simulação Ltda", "t13s.localhost", tenancy.Factory.Services);
        var hr = await ActorAsync(tenant, Rh);
        var viewer = await ActorAsync(tenant, "hr:view");
        var mid = await EmployeeAsync(hr.Client, "2026-06-10", 3000m);
        var absent = await EmployeeAsync(hr.Client, "2020-01-02", 2400m);
        var he50 = await DbAsync(tenant, db => db.PayrollItems.AsNoTracking().Where(i => i.Code == "HE50").Select(i => i.Id).SingleAsync());
        await DbAsync(tenant, db =>
        {
            // Março com 17 faltas (espelho fechado): o avo de março cai. Julho com horas extras numa Mensal lançada fechada (variável das médias).
            db.Timesheets.Add(new Timesheet
            {
                EmployeeId = absent, Period = new DateOnly(2026, 3, 1), PeriodStart = new DateOnly(2026, 3, 1), PeriodEnd = new DateOnly(2026, 3, 31), Status = TimesheetStatus.Closed,
                AbsenceDays = 17, ClosedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow,
            });
            var july = new PayrollRun { Period = new DateOnly(2026, 7, 1), Type = PayrollRunType.Monthly, Mode = PayrollRunMode.Entered, Status = PayrollRunStatus.Closed, CreatedAt = DateTimeOffset.UtcNow, ClosedAt = DateTimeOffset.UtcNow };
            db.PayrollRuns.Add(july);
            var slip = new Payslip { PayrollRunId = july.Id, EmployeeId = mid, Gross = 200m, Net = 200m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.Payslips.Add(slip);
            db.PayslipLines.Add(new PayslipLine { PayslipId = slip.Id, PayrollItemId = he50, Code = "HE50", Description = "Horas extras 50%", Type = PayrollItemKind.Earning, Amount = 200m });
            return Task.CompletedTask;
        });

        // Prévia dos avos (sem valor), só com hr.view_sensitive.
        (await viewer.Client.GetAsync($"{Runs}/thirteenth-preview/sensitive?year=2026&type=Thirteenth1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var preview = await OkAsync(await hr.Client.GetAsync($"{Runs}/thirteenth-preview/sensitive?year=2026&type=Thirteenth1"));
        preview.GetProperty("period").GetString().Should().Be("2026-11");
        preview.GetProperty("firstClosed").GetBoolean().Should().BeFalse();
        var avos = preview.GetProperty("employees").EnumerateArray().ToDictionary(e => e.GetProperty("employeeId").GetGuid(), e => e.GetProperty("avos").GetInt32());
        avos[mid].Should().Be(7, "junho (21 dias) a novembro e dezembro projetado");
        avos[absent].Should().Be(11, "o ano projetado, menos março (14 dias trabalhados)");

        // 1ª parcela: a competência vira a da data limite; sem INSS e IRRF, com FGTS; é simulação.
        var first = await OkAsync(await CreateAsync(hr.Client, "2026-10", "Thirteenth1"), HttpStatusCode.Created);
        first.GetProperty("period").GetString().Should().Be("2026-11-01");
        first.GetProperty("isSimulation").GetBoolean().Should().BeTrue();
        var firstId = first.GetProperty("id").GetGuid();
        (await CreateAsync(hr.Client, "2026-11", "Thirteenth1")).StatusCode.Should().Be(HttpStatusCode.Conflict, "uma calculada por tipo e ano (CA 8)");
        var done = await OkAsync(await hr.Client.PostAsync($"{Runs}/{firstId}/calculate", null));
        done.GetProperty("run").GetProperty("calculatorVersion").GetString().Should().Be(ThirteenthCalculator.Version);
        var slips = await PayslipsAsync(hr.Client, firstId);
        Amount(slips[mid], "13PARC1").Should().Be(875m);
        Amount(slips[mid], "FGTS").Should().Be(70m);
        Amount(slips[mid], "INSS").Should().Be(0m);
        Amount(slips[absent], "13PARC1").Should().Be(1100m, "2.400 × 11/12 ÷ 2");
        slips[mid].GetProperty("calculation").GetProperty("inputs").EnumerateArray().Should().Contain(i => i.GetProperty("label").GetString() == "Avos" && i.GetProperty("value").GetString() == "7/12");
        var charges = await OkAsync(await hr.Client.GetAsync($"{Runs}/{firstId}/charges/sensitive"));
        charges.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("kind").GetString()).Should().Equal("Fgts");

        // CA 5: antes da virada e do caso de 13º conferido, não fecha.
        var close = await CloseAsync(hr, firstId);
        close.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(close)).GetProperty("detail").GetString().Should().Be(ThirteenthCalculator.SimulationMessage);
        (await OkAsync(await viewer.Client.GetAsync($"{Runs}/{firstId}/calculation"))).GetProperty("nextStep").GetProperty("label").GetString().Should().Be("Comparar com a lançada");

        // CA 8: a 2ª sem a 1ª fechada pede a confirmação; confirmada, sai sem adiantamento e com as médias das horas extras.
        var unconfirmed = await CreateAsync(hr.Client, "2026-12", "Thirteenth2");
        unconfirmed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(unconfirmed)).GetProperty("detail").GetString().Should().Be(ThirteenthCalculator.FirstOpenMessage);
        var secondId = (await OkAsync(await CreateAsync(hr.Client, "2026-12", "Thirteenth2", confirm: true), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{secondId}/calculate", null));
        var second = (await PayslipsAsync(hr.Client, secondId))[mid];
        Amount(second, "13SAL").Should().Be(1750m, "3.000 × 7/12");
        Amount(second, "13MED").Should().Be(16.67m, "200 de horas extras ÷ 7 meses × 7/12");
        Amount(second, "13ADIANT").Should().Be(0m);
        (await OkAsync(await hr.Client.GetAsync($"{Runs}/{secondId}/calculation"))).GetProperty("staleCount").GetInt32().Should().Be(0);
        (await hr.Client.SendAsync(Post($"{Runs}/{secondId}/rectify", new { }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>CA 6: depois da virada e do primeiro caso de 13º conferido, cada parcela fecha com um título por funcionário e por guia, na data limite.</summary>
    [Fact]
    public async Task Parcelas_oficiais_geram_titulos_na_data_limite()
    {
        var tenant = await tenancy.ProvisionAsync("flh04-oficial", "Treze Oficial Ltda", "t13o.localhost", tenancy.Factory.Services);
        var hr = await ActorAsync(tenant, Rh + ";finance:view;parties:view,create");
        var low = await EmployeeAsync(hr.Client, "2020-01-02", 3000m);
        var high = await EmployeeAsync(hr.Client, "2020-01-02", 6000m);
        var organ = (await OkAsync(await hr.Client.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Órgão " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Supplier" } }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await DbAsync(tenant, async db =>
        {
            foreach (var id in new[] { low, high })
            {
                var e = await db.Employees.SingleAsync(x => x.Id == id);
                e.BankCode = "077";
                e.BankAgency = "0001";
                e.BankAccount = "54321" + id.ToString("N")[..3];
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
            db.HrParameters.Add(new HrParameter { Key = HrParametersService.ModeKey(PayrollParallelType.Thirteenth), Value = 1m, UpdatedAt = DateTimeOffset.UtcNow });
        });
        foreach (var t in await DbAsync(tenant, db => db.PayrollTaxTables.AsNoTracking().Where(x => x.SeedPackage == 2026).ToListAsync()))
        {
            var d = await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/payroll-tax-tables/{t.Id}"));
            await OkAsync(await hr.Client.PostAsJsonAsync($"/api/v1/hr/payroll-tax-tables/{t.Id}/check", new { accountant = "Contadora Fictícia", checkedOn = "2026-10-01", dataset = (string?)null, version = d.GetProperty("version").GetUInt32() }));
        }

        async Task<(JsonElement Closed, List<BemAqui.Finance.Domain.Payable> Payables)> CloseRunAsync(Guid run)
        {
            var closed = await OkAsync(await CloseAsync(hr, run));
            var payables = await DbAsync(tenant, async db =>
            {
                var origins = await db.Payslips.Where(p => p.PayrollRunId == run).Select(p => p.Id).Concat(db.PayrollRunCharges.Where(c => c.PayrollRunId == run).Select(c => c.Id)).ToListAsync();
                return await db.Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.Payroll && p.OriginId != null && origins.Contains(p.OriginId.Value)).ToListAsync();
            });
            return (closed, payables);
        }

        // 1ª parcela: 1.500 e 3.000, só a guia do FGTS; os títulos de salário vencem em 30/11 (data limite, segunda-feira).
        var first = (await OkAsync(await CreateAsync(hr.Client, "2026-11", "Thirteenth1"), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        (await JsonAsync(await hr.Client.GetAsync($"{Runs}/{first}"))).GetProperty("isSimulation").GetBoolean().Should().BeFalse();
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{first}/calculate", null));
        var preview = await OkAsync(await hr.Client.GetAsync($"{Runs}/{first}/close/sensitive"));
        preview.GetProperty("suggestedPaymentDate").GetString().Should().Be("2026-11-30");
        var (closed1, payables1) = await CloseRunAsync(first);
        closed1.GetProperty("payables").GetInt32().Should().Be(3, "um título por funcionário e o da guia do FGTS");
        payables1.Where(p => p.PayeeType == PayeeType.Employee).Should().HaveCount(2).And.OnlyContain(p => p.Status == PayableStatus.Open);
        payables1.Where(p => p.PayeeType == PayeeType.Employee).Select(p => p.Amount).Should().BeEquivalentTo([1500m, 3000m]);

        // 2ª parcela (a 1ª fechada, sem confirmação): INSS e IRRF do 13º inteiro, o adiantamento descontado, as três guias.
        var second = (await OkAsync(await CreateAsync(hr.Client, "2026-12", "Thirteenth2"), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{second}/calculate", null));
        var slip = (await PayslipsAsync(hr.Client, second))[high];
        Amount(slip, "13SAL").Should().Be(6000m);
        Amount(slip, "13ADIANT").Should().Be(3000m);
        Amount(slip, "INSS").Should().Be(641.51m);
        Amount(slip, "IRRF").Should().Be(385.10m);
        slip.GetProperty("net").GetDecimal().Should().Be(1973.39m);
        var (closed2, payables2) = await CloseRunAsync(second);
        closed2.GetProperty("payables").GetInt32().Should().Be(5, "dois funcionários e as guias do FGTS, do INSS e do IRRF");
        payables2.Where(p => p.PayeeType == PayeeType.Employee).Should().OnlyContain(p => p.DueDate <= new DateOnly(2026, 12, 20) && p.DueDate >= new DateOnly(2026, 12, 14), "a data limite de 20/12, antecipada");
        payables2.Single(p => p.PayeeType == PayeeType.Employee && p.Amount == 1973.39m).Should().NotBeNull();
    }
}
