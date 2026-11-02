using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.HR;

public partial class PayrollClosingTests
{
    /// <summary>Todo funcionário do tenant com conta (a Mensal pega todos os ativos da competência).</summary>
    private Task<int> AccountsForAllAsync() =>
        DbAsync(async db =>
        {
            foreach (var e in await db.Employees.Where(x => x.BankAccountVersion == 0).ToListAsync())
            {
                e.BankCode = "077";
                e.BankAgency = "0001";
                e.BankAccount = AccountCanary + "1";
                e.BankAccountType = EmployeeBankAccountType.Checking;
                e.BankHolderName = e.Name;
                e.BankHolderCpf = e.Cpf;
                e.BankAccountVersion = 1;
                e.BankChangedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync();
            return 0;
        });

    /// <summary>Previsão ativa (linhas e títulos) do funcionário.</summary>
    private Task<List<(PayrollForecastKind Kind, DateOnly Period, decimal Amount, PayableStatus Status, bool Estimate)>> ForecastOfAsync(Guid employeeId) =>
        DbAsync(async db =>
        {
            var rows = await db.PayrollForecasts.AsNoTracking().Where(f => f.EmployeeId == employeeId && f.CancelledAt == null).ToListAsync();
            var ids = rows.Select(r => r.Id).ToList();
            var titles = await db.Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.PayrollForecast && p.OriginId != null && ids.Contains(p.OriginId.Value) && p.Status != PayableStatus.Cancelled).ToListAsync();
            return rows.Select(r => titles.Single(t => t.OriginId == r.Id) is var t ? (r.Kind, r.Period, t.Amount, t.Status, t.IsEstimate) : default).OrderBy(x => x.Period).ThenBy(x => x.Kind).ToList();
        });

    /// <summary>
    /// CA 14 e 15: a última Mensal fechada gera a folha prevista dos 3 meses (salário, guia, 13º em novembro e dezembro) como títulos Planned
    /// estimados; a folha real do mês substitui a previsão daquele mês e a janela anda; a do 13º some quando a folha do 13º fecha; o fluxo
    /// não conta duas vezes.
    /// </summary>
    [Fact]
    public async Task Folha_prevista_de_tres_meses_e_substituida_pela_folha_real()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var position = await PositionAsync(owner.Client);
        var e = await EmployeeAsync(owner.Client, position, 4_000m);
        await AccountsForAllAsync();
        var organ = await PartyAsync(owner.Client);
        var september = await RunAsync(owner.Client, "2040-09");
        await AddChargeAsync(owner.Client, september, organ, 900m);
        var closed = await CloseAsync(owner, september, null);
        closed.StatusCode.Should().Be(HttpStatusCode.OK, await closed.Content.ReadAsStringAsync());
        (await JsonAsync(closed)).GetProperty("run").GetProperty("paymentDate").GetString().Should().StartWith("2040-10", "Mensal: o 5º dia útil do mês seguinte");

        var forecast = await ForecastOfAsync(e.Id);
        forecast.Where(f => f.Kind == PayrollForecastKind.Salary).Select(f => f.Period).Should().Equal(new DateOnly(2040, 10, 1), new DateOnly(2040, 11, 1), new DateOnly(2040, 12, 1));
        forecast.Where(f => f.Kind == PayrollForecastKind.Salary).Should().OnlyContain(f => f.Amount == 4_000m && f.Status == PayableStatus.Planned && f.Estimate);
        forecast.Should().ContainSingle(f => f.Kind == PayrollForecastKind.Thirteenth1 && f.Period == new DateOnly(2040, 11, 1) && f.Amount == 2_000m);
        forecast.Should().ContainSingle(f => f.Kind == PayrollForecastKind.Thirteenth2 && f.Period == new DateOnly(2040, 12, 1) && f.Amount == 2_000m);
        var charges = await DbAsync(db => db.PayrollForecasts.AsNoTracking().Where(f => f.Kind == PayrollForecastKind.Charge && f.CancelledAt == null).Select(f => f.Period).ToListAsync());
        charges.Should().BeEquivalentTo([new DateOnly(2040, 10, 1), new DateOnly(2040, 11, 1), new DateOnly(2040, 12, 1)]);
        var forecastTitle = await DbAsync(db => db.Payables.AsNoTracking().FirstAsync(p => p.Origin == DocumentOrigin.PayrollForecast && p.PayeeId == e.Id && p.Status == PayableStatus.Planned));
        forecastTitle.Description.Should().StartWith("Salário previsto Mensal");
        forecastTitle.PayeeType.Should().Be(PayeeType.Employee);

        // A folha real de outubro substitui a previsão de outubro, e a janela anda para janeiro.
        var october = await RunAsync(owner.Client, "2040-10");
        await AddChargeAsync(owner.Client, october, organ, 910m);
        (await CloseAsync(owner, october, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        forecast = await ForecastOfAsync(e.Id);
        forecast.Where(f => f.Kind == PayrollForecastKind.Salary).Select(f => f.Period).Should().Equal(new DateOnly(2040, 11, 1), new DateOnly(2040, 12, 1), new DateOnly(2041, 1, 1));
        var replaced = await DbAsync(db => db.PayrollForecasts.AsNoTracking().SingleAsync(f => f.EmployeeId == e.Id && f.Kind == PayrollForecastKind.Salary && f.Period == new DateOnly(2040, 10, 1)));
        replaced.CancelReason.Should().Be(PayrollForecastRules.ReplacedReason);
        var octoberSalaries = await DbAsync(db => db.Payables.AsNoTracking()
            .CountAsync(p => p.PayeeId == e.Id && p.Status != PayableStatus.Cancelled && p.Description.Contains("Mensal outubro de 2040")));
        octoberSalaries.Should().Be(1, "o fluxo não conta outubro duas vezes");

        // A folha do 13º (1ª parcela) fecha: a previsão do 13º de novembro some.
        var thirteenth = await RunAsync(owner.Client, "2040-11", "Thirteenth1");
        await FillAsync(owner.Client, thirteenth, 2_000m);
        (await CloseAsync(owner, thirteenth, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ForecastOfAsync(e.Id)).Should().NotContain(f => f.Kind == PayrollForecastKind.Thirteenth1 && f.Period.Year == 2040, "a 1ª parcela de 2040 fechou (a de 2041 já aparece: a janela chegou a janeiro)");
        (await ForecastOfAsync(e.Id)).Should().ContainSingle(f => f.Kind == PayrollForecastKind.Thirteenth2 && f.Period.Year == 2040);

        // "Recalcular previsão" (hr.edit) não duplica; sem hr.edit, 403.
        var recalc = await owner.Client.PostAsync($"{Runs}/forecast/recalculate", null);
        recalc.StatusCode.Should().Be(HttpStatusCode.OK, await recalc.Content.ReadAsStringAsync());
        (await ForecastOfAsync(e.Id)).Where(f => f.Kind == PayrollForecastKind.Salary).Should().HaveCount(3);
        var viewer = await ActorAsync("hr:view");
        (await viewer.Client.PostAsync($"{Runs}/forecast/recalculate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Menos meses no parâmetro (settings.edit): a previsão encolhe.
        (await owner.Client.PutAsJsonAsync("/api/v1/hr/parameters", new { changes = new[] { new { key = "hr.payroll_forecast_months", value = 1m, partyId = (Guid?)null, code = (string?)null } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await ForecastOfAsync(e.Id)).Where(f => f.Kind == PayrollForecastKind.Salary).Select(f => f.Period).Should().Equal(new DateOnly(2040, 11, 1));
        (await owner.Client.PutAsJsonAsync("/api/v1/hr/parameters", new { changes = new[] { new { key = "hr.payroll_forecast_months", value = 3m, partyId = (Guid?)null, code = (string?)null } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
