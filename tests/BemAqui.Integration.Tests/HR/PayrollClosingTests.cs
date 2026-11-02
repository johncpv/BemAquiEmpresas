using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Host.Persistence;
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
/// RH-02c: fechamento da folha com 2FA de novo, títulos a pagar por funcionário (favorecido <c>Employee</c>) e por guia na mesma transação,
/// salário no Financeiro por forma de permissão, quarentena e conta lida só no "Pagar", quem fecha não paga, folha paga pela fonte,
/// reabertura e folha prevista. Cada teste usa competências próprias e crescentes (a previsão parte da última Mensal fechada do tenant).
/// </summary>
public partial class PayrollClosingTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string HostA = "a.localhost";
    private const string HostB = "b.localhost";
    private const string Runs = "/api/v1/hr/payroll-runs";
    private const string Orders = "/api/v1/finance/payment-orders";
    private const string AccountCanary = "CANARIO-99887";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

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

    private async Task<T> InTenantAsync<T>(Tenant tenant, Func<IServiceProvider, Task<T>> action)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Func<TenantDbContext, Task<T>> action, Tenant? tenant = null) =>
        InTenantAsync(tenant ?? tenancy.TenantA, sp => action(sp.GetRequiredService<TenantDbContext>()));

    /// <summary>Usuário num grupo do seed ou num grupo próprio ("hr:view,approve;finance:view"), com o 2FA ligado se pedir.</summary>
    private async Task<Actor> ActorAsync(string? grants, bool totp = false, Tenant? tenant = null)
    {
        var target = tenant ?? tenancy.TenantA;
        var host = target.Id == tenancy.TenantB.Id ? HostB : HostA;
        var (id, stamp) = await tenancy.SeedUserAsync(target, "c" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        var secret = Totp.NewSecret();
        stamp = await InTenantAsync(target, async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            if (grants is not null)
            {
                Guid groupId;
                if (grants.Contains(':', StringComparison.Ordinal))
                {
                    var group = new Group { Name = "Folha " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
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

                    groupId = group.Id;
                }
                else
                {
                    groupId = await db.Groups.Where(g => g.SeedKey == grants).Select(g => g.Id).SingleAsync();
                }

                db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            }

            var user = await db.Users.SingleAsync(u => u.Id == id);
            if (totp)
            {
                user.BeginTotpSetup(tenancy.Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(target.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
                user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            }

            await db.SaveChangesAsync();
            await sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
            return user.SecurityStamp;
        });
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(target.Id, host, userId: id, stamp: stamp, otpAt: DateTimeOffset.UtcNow));
        return new Actor(id, client, totp ? new Authenticator(secret) : null);
    }

    /// <summary>Mensagens do outbox do tipo com o id no payload (o payload é jsonb: o filtro roda em memória).</summary>
    private static async Task<int> OutboxCountAsync(TenantDbContext db, string type, Guid id) =>
        (await db.OutboxMessages.AsNoTracking().Where(m => m.Type.EndsWith(type)).Select(m => m.Payload).ToListAsync()).Count(p => p.Contains(id.ToString(), StringComparison.Ordinal));

    private static HttpRequestMessage Post(string url, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    private static async Task<Guid> PositionAsync(HttpClient hr)
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = "Auxiliar " + Guid.NewGuid().ToString("N")[..6], cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    /// <summary>Funcionário admitido há tempo, com salário e matrícula próprios.</summary>
    private static async Task<(Guid Id, string Registration, string Name)> EmployeeAsync(HttpClient owner, Guid position, decimal salary = 3_000m)
    {
        var cpf = NewCpf();
        var registration = "R" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var name = "Pessoa " + Guid.NewGuid().ToString("N")[..6];
        var created = await owner.PostAsJsonAsync("/api/v1/hr/employees", new { name, cpf, birthDate = "1990-05-10", admissionDate = "2020-01-02", positionId = position, workScheduleId = (Guid?)null, baseSalary = salary, registration });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return ((await JsonAsync(created)).GetProperty("employee").GetProperty("id").GetGuid(), registration, name);
    }

    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    /// <summary>Conta para pagamento gravada direto (o fluxo da CAD-04c2 tem os próprios testes): a versão e, se pedir, a quarentena.</summary>
    private Task<int> SetAccountAsync(Guid employeeId, int version = 1, bool quarantine = false, Tenant? tenant = null) =>
        DbAsync(async db =>
        {
            var e = await db.Employees.SingleAsync(x => x.Id == employeeId);
            e.BankCode = "077";
            e.BankAgency = "0001";
            e.BankAccount = AccountCanary + version;
            e.BankAccountType = EmployeeBankAccountType.Checking;
            e.BankHolderName = e.Name;
            e.BankHolderCpf = e.Cpf;
            e.BankAccountVersion = version;
            e.BankChangedAt = DateTimeOffset.UtcNow;
            e.BankQuarantineUntil = quarantine ? DateTimeOffset.UtcNow.AddDays(30) : null;
            e.BankReviewedAt = null;
            await db.SaveChangesAsync();
            return 0;
        }, tenant);

    private static async Task<Guid> PartyAsync(HttpClient owner)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Órgão " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Supplier" } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> RunAsync(HttpClient owner, string period, string type = "Monthly", Guid[]? employees = null)
    {
        var created = await owner.SendAsync(Post(Runs, new { period, type, employeeIds = employees }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task AddChargeAsync(HttpClient owner, Guid run, Guid? payee, decimal amount = 480m, string kind = "Fgts")
    {
        var added = await owner.PostAsJsonAsync($"{Runs}/{run}/charges", new { kind, description = (string?)null, amount, dueDate = (string?)null, payeePartyId = payee });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
    }

    private static async Task<uint> RunVersionAsync(HttpClient client, Guid run) => (await JsonAsync(await client.GetAsync($"{Runs}/{run}"))).GetProperty("version").GetUInt32();

    private static async Task<HttpResponseMessage> CloseAsync(Actor actor, Guid run, string? paymentDate, Guid? key = null, bool code = true, bool ack = true) =>
        await actor.Client.SendAsync(Post($"{Runs}/{run}/close", new { paymentDate, twoFactorCode = code ? actor.Codes?.Next() : null, version = await RunVersionAsync(actor.Client, run), acknowledgedChanges = ack }, key));

    /// <summary>Títulos ativos da folha (salários e guias) no Finance.</summary>
    private Task<List<BemAqui.Finance.Domain.Payable>> PayrollPayablesAsync(Guid run, Tenant? tenant = null) =>
        DbAsync(async db =>
        {
            var origins = await db.Payslips.Where(p => p.PayrollRunId == run).Select(p => p.Id).Concat(db.PayrollRunCharges.Where(c => c.PayrollRunId == run).Select(c => c.Id)).ToListAsync();
            return await db.Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.Payroll && p.OriginId != null && origins.Contains(p.OriginId.Value) && p.Status != PayableStatus.Cancelled).ToListAsync();
        }, tenant);

    /// <summary>Folha pronta: dois funcionários com conta (um em quarentena, se pedir) e a guia do FGTS com o órgão.</summary>
    private async Task<(Guid Run, (Guid Id, string Registration, string Name) A, (Guid Id, string Registration, string Name) B, Guid Organ)> ReadyRunAsync(
        Actor owner, string period, bool quarantineB = false)
    {
        var position = await PositionAsync(owner.Client);
        var a = await EmployeeAsync(owner.Client, position, 3_000m);
        var b = await EmployeeAsync(owner.Client, position, 2_000m);
        await SetAccountAsync(a.Id);
        await SetAccountAsync(b.Id, quarantineB ? 2 : 1, quarantineB);
        var run = await RunAsync(owner.Client, period, "Vacation", [a.Id, b.Id]);
        await FillAsync(owner.Client, run);
        var organ = await PartyAsync(owner.Client);
        await AddChargeAsync(owner.Client, run, organ);
        return (run, a, b, organ);
    }

    /// <summary>Holerites das Férias com uma linha de salário (o líquido = o valor).</summary>
    private static async Task FillAsync(HttpClient owner, Guid run, decimal? amount = null)
    {
        var items = (await JsonAsync(await owner.GetAsync("/api/v1/hr/payroll-items"))).EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("id").GetGuid());
        var salary = items.First(i => i.Key == "SAL").Value;
        foreach (var slip in (await JsonAsync(await owner.GetAsync($"{Runs}/{run}/payslips/sensitive"))).EnumerateArray().ToList())
        {
            var value = amount ?? 1_000m + slip.GetProperty("employeeName").GetString()!.Length;
            var saved = await owner.PutAsJsonAsync($"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}",
                new { lines = new[] { new { payrollItemId = salary, reference = "30 dias", amount = value } }, inssBase = 0m, fgtsBase = value, irrfBase = 0m, fgtsAmount = 0m, version = slip.GetProperty("version").GetUInt32() });
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }
    }
}
