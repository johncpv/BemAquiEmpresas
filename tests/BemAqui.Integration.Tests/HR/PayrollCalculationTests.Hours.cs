using System.Net;
using System.Net.Http.Json;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.Platform.Domain;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-02b: horas extras, DSR e perda do DSR a partir do espelho fechado (com os dias congelados) e o banco de horas na folha calculada, com
/// a memória, o retrato, o aviso de parâmetro mudado e a baixa do saldo no fechamento. Funcionários fictícios, salário 2.200,00 ÷ 220 h.
/// </summary>
public partial class PayrollCalculationTests
{
    private static TimesheetDayResult Day(DateOnly day, TimesheetDayStatus status, int expected, int worked, int overtime = 0, int absence = 0, string? holiday = null) =>
        new(day, status, expected, worked, overtime, 0, absence, 0, 0, null, [], false, [], [], holiday, null);

    /// <summary>Feriados no calendário padrão (CAD-03), criando o calendário de segunda a sexta se o tenant ainda não abriu a tela.</summary>
    private Task<int> HolidaysAsync(Tenant tenant, params DateOnly[] days) => DbAsync(tenant, async db =>
    {
        var calendar = await db.Calendars.FirstOrDefaultAsync(c => c.IsDefault);
        if (calendar is null)
        {
            calendar = new Calendar { Name = "Padrão", IsDefault = true, WorkingDays = [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday], CreatedAt = DateTimeOffset.UtcNow };
            db.Calendars.Add(calendar);
        }

        foreach (var day in days)
        {
            if (!await db.CalendarExceptions.AnyAsync(e => e.CalendarId == calendar.Id && e.Date == day))
            {
                db.CalendarExceptions.Add(new CalendarExceptionDay { CalendarId = calendar.Id, Date = day, Type = CalendarExceptionType.Holiday, HoursAvailable = 0m, Description = "Feriado de teste", CreatedAt = DateTimeOffset.UtcNow });
            }
        }
    });

    private static Task<HttpResponseMessage> ParameterAsync(HttpClient client, string key, decimal value) =>
        client.PutAsJsonAsync("/api/v1/hr/parameters", new { changes = new[] { new { key, value = (decimal?)value, partyId = (Guid?)null, code = (string?)null } } });

    /// <summary>
    /// CA 1, 4, 5, 7 e 8 (Estados): extras em dia útil e no feriado do calendário, DSR sobre as variáveis, perda do DSR pela falta, a
    /// memória com os dias e o link para o espelho, as horas no retrato; percentual apagado trava o cálculo; parâmetro mudado depois do
    /// cálculo avisa e entra nas mudanças do fechamento.
    /// </summary>
    [Fact]
    public async Task Horas_extras_dsr_e_perda_com_memoria_retrato_e_parametro_mudado()
    {
        var a = tenancy.TenantA;
        var hr = await ActorAsync(a, Rh);
        var settings = await ActorAsync(a, "hr:view;settings:view,edit");
        var period = new DateOnly(2026, 11, 1);
        var employee = await EmployeeAsync(a, hr.Client, await PositionAsync(hr.Client), period, 2200m);
        await HolidaysAsync(a, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 20));
        await CloseTimesheetAsync(a, employee, period, absence: 528, days:
        [
            Day(new DateOnly(2026, 11, 3), TimesheetDayStatus.Overtime, 528, 648, overtime: 120),
            Day(new DateOnly(2026, 11, 11), TimesheetDayStatus.Absent, 528, 0, absence: 528),
            Day(new DateOnly(2026, 11, 20), TimesheetDayStatus.Overtime, 0, 60, overtime: 60, holiday: "Feriado de teste"),
        ]);
        var run = await CalculatedRunAsync(hr.Client, period);

        // Estados: o percentual das extras apagado trava o cálculo com o link para os parâmetros.
        var holidayRate = await DbAsync(a, db => db.HrParameters.AsNoTracking().SingleAsync(p => p.Key == HrParametersService.OvertimeHolidayPct));
        await DbAsync(a, async db => { db.HrParameters.Remove(await db.HrParameters.SingleAsync(p => p.Key == HrParametersService.OvertimeHolidayPct)); });
        var missing = await hr.Client.PostAsync($"{Runs}/{run}/calculate", null);
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await JsonAsync(missing);
        body.GetProperty("code").GetString().Should().Be(PayrollCalculationRules.OvertimeRatesMissingCode);
        body.GetProperty("detail").GetString().Should().Be("Cadastre os percentuais de horas extras em Configurações → Parâmetros → RH.");
        body.GetProperty("link").GetString().Should().Be("/app/configuracoes/parametros?secao=rh");
        await DbAsync(a, db =>
        {
            db.HrParameters.Add(new HrParameter { Key = HrParametersService.OvertimeHolidayPct, Value = holidayRate.Value, UpdatedAt = DateTimeOffset.UtcNow });
            return Task.CompletedTask;
        });

        // Novembro: 5 domingos + feriados de 02 e 20/11 = 7 de descanso, 23 úteis. 2 h × 10 × 1,5 = 30; 1 h no feriado × 10 × 2 = 20 (HE100);
        // DSR 50 ÷ 23 × 7 = 15,22; falta de 8:48 h = 88; perda do DSR da semana de 09/11 = 2.200 ÷ 30 = 73,33.
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/calculate", null));
        var slip = (await PayslipsAsync(hr.Client, run))[employee];
        Amount(slip, "HE50").Should().Be(30m);
        Amount(slip, "HE100").Should().Be(20m);
        Amount(slip, "DSR").Should().Be(15.22m);
        Amount(slip, "FALTAS").Should().Be(88m);
        Amount(slip, "DSRFALTA").Should().Be(73.33m);
        var lines = slip.GetProperty("lines").EnumerateArray().ToList();
        lines.Single(l => l.GetProperty("code").GetString() == "DSRFALTA").GetProperty("description").GetString().Should().Be("DSR da semana de 09/11/2026");
        var he50 = lines.Single(l => l.GetProperty("code").GetString() == "HE50");
        he50.GetProperty("description").GetString().Should().Be("Horas extras 50%");
        he50.GetProperty("reference").GetString().Should().Be("2:00 h");
        he50.GetProperty("memory").EnumerateArray().Select(m => m.GetString()).Should().Contain(m => m!.Contains("03/11 (2:00 h) dia útil", StringComparison.Ordinal));
        lines.Single(l => l.GetProperty("code").GetString() == "DSR").GetProperty("memory").EnumerateArray().Select(m => m.GetString())
            .Should().Contain("Dias do mês: 23 úteis e 7 de descanso (dias úteis = dias do mês − domingos − feriados, parâmetro hr.dsr_formula).");
        var info = slip.GetProperty("calculation");
        info.GetProperty("calculatorVersion").GetString().Should().Be("1.1.0");
        info.GetProperty("timesheetLink").GetString().Should().Be($"/app/rh/ponto/espelho/{employee}?mes=2026-11");
        info.GetProperty("inputs").EnumerateArray().Should().Contain(i => i.GetProperty("label").GetString() == "Horas extras em dia útil" && i.GetProperty("value").GetString() == "2:00 h");
        var stored = await DbAsync(a, db => db.Payslips.AsNoTracking().SingleAsync(p => p.PayrollRunId == run && p.EmployeeId == employee));
        var snapshot = PayrollCalculator.ReadSnapshot(stored.InputsSnapshot)!;
        snapshot.HourDays.Should().HaveCount(3, "as horas do espelho entram no retrato");
        snapshot.Calendar!.Holidays.Should().Equal(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 20));

        // CA 8: parâmetro das horas mudado com settings.edit depois do cálculo: aviso, "Recalcular" e a mudança no diálogo de fechamento.
        try
        {
            await OkAsync(await ParameterAsync(settings.Client, HrParametersService.OvertimeWorkdayPct, 60m));
            var status = await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/calculation"));
            status.GetProperty("staleCount").GetInt32().Should().Be(1);
            status.GetProperty("staleReasons").EnumerateArray().Select(r => r.GetString()).Should().Contain("Parâmetro mudou depois do cálculo");
            var preview = await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/close/sensitive"));
            preview.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("what").GetString()).Should().Contain("Parâmetro " + HrParametersService.OvertimeWorkdayPct);
            await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/recalculate", null));
            Amount((await PayslipsAsync(hr.Client, run))[employee], "HE50").Should().Be(32m, "2 h × 10 × 1,6");
        }
        finally
        {
            await ParameterAsync(settings.Client, HrParametersService.OvertimeWorkdayPct, 50m);
        }
    }

    /// <summary>
    /// CA 2 e 9: banco de horas sem vencimento na folha: a seção com o saldo e a sugestão acima de 40 h, "Pagar" e "Descontar" com motivo
    /// (hr.edit + hr.view_sensitive), as linhas e o DSR do banco pago; no fechamento (depois da virada, tenant próprio) a baixa do saldo no
    /// espelho e no espelho seguinte já fechado; na reabertura, desfeita.
    /// </summary>
    [Fact]
    public async Task Banco_de_horas_pagar_descontar_e_baixa_no_fechamento()
    {
        var tenant = await tenancy.ProvisionAsync("flh02b-banco", "Banco Horas Ltda", "bh.localhost", Factory.Services);
        var hr = await ActorAsync(tenant, Rh + ",approve;finance:view;parties:view,create", totp: true);
        var noSensitive = await ActorAsync(tenant, "hr:view,create,edit");
        var noEdit = await ActorAsync(tenant, "hr:view,view_sensitive");
        var period = new DateOnly(2026, 9, 1);
        var position = await PositionAsync(hr.Client);
        var one = await EmployeeAsync(tenant, hr.Client, position, period, 2200m);
        var two = await EmployeeAsync(tenant, hr.Client, position, period, 2200m);
        var three = await EmployeeAsync(tenant, hr.Client, position, period, 2200m);
        var organ = (await OkAsync(await hr.Client.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Órgão " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Supplier" } }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await DbAsync(tenant, async db =>
        {
            foreach (var id in new[] { one, two, three })
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
        });
        await CloseTimesheetAsync(tenant, one, period, bankMonth: 600, bankBalance: 3000);
        await CloseTimesheetAsync(tenant, two, period, bankMonth: -600, bankBalance: -600);
        await CloseTimesheetAsync(tenant, three, period);
        await CloseTimesheetAsync(tenant, one, period.AddMonths(1), bankMonth: 0, bankBalance: 3000);
        var run = await CalculatedRunAsync(hr.Client, period);
        await OkAsync(await hr.Client.PostAsync($"{Runs}/{run}/calculate", null));
        var slips = await PayslipsAsync(hr.Client, run);
        string Slip(Guid employee) => $"{Runs}/{run}/payslips/{slips[employee].GetProperty("id").GetGuid()}/hour-bank";

        // A seção: só quem tem banco; acima de 40 h, a sugestão "Pagar 50 h do banco" (sem decidir).
        (await noSensitive.Client.GetAsync($"{Runs}/{run}/hour-bank/sensitive")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var section = await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/hour-bank/sensitive"));
        section.GetProperty("editable").GetBoolean().Should().BeTrue();
        var rows = section.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("employeeId").GetGuid());
        rows.Keys.Should().BeEquivalentTo([one, two]);
        rows[one].GetProperty("suggestedPayMinutes").GetInt32().Should().Be(3000);
        rows[two].GetProperty("suggestedPayMinutes").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        // CA 9: "Pagar" e "Descontar" exigem hr.edit (rota) e hr.view_sensitive (serviço).
        object Pay(int minutes, string note = "Saldo acima de 40 h") => new { action = "Pay", minutes, note };
        (await noSensitive.Client.PutAsJsonAsync(Slip(one), Pay(600))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await noEdit.Client.PutAsJsonAsync(Slip(one), Pay(600))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Até o saldo, com motivo, só com banco.
        (await hr.Client.PutAsJsonAsync(Slip(one), Pay(3060))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "mais que o saldo positivo");
        (await hr.Client.PutAsJsonAsync(Slip(one), new { action = "Discount", minutes = 60, note = "Desconto indevido" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o saldo é positivo");
        (await hr.Client.PutAsJsonAsync(Slip(one), Pay(600, ""))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o motivo é obrigatório");
        var noBank = await hr.Client.PutAsJsonAsync(Slip(three), Pay(60));
        (await CodeAsync(noBank)).Should().Be(PayrollCalculationRules.HourBankInvalidCode);

        // 10 h pagas como extra de dia útil: 10 × 10 × 1,5 = 150; DSR 150 ÷ 26 × 4 = 23,08 (setembro: 4 domingos, sem feriado). Descontar 5 h = 50.
        var paid = await OkAsync(await hr.Client.PutAsJsonAsync(Slip(one), Pay(600)));
        Amount(paid, "BHPAGO").Should().Be(150m);
        Amount(paid, "DSR").Should().Be(23.08m);
        paid.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("code").GetString() == "BHPAGO").GetProperty("memory")[0].GetString()
            .Should().Be("Decisão do RH na folha: pagar 10:00 h do banco de horas (saldo do espelho fechado +50 h). Motivo: Saldo acima de 40 h");
        Amount(await OkAsync(await hr.Client.PutAsJsonAsync(Slip(two), new { action = "Discount", minutes = 300, note = "Acerto do saldo negativo" })), "BHDESC").Should().Be(50m);
        var after = (await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/hour-bank/sensitive"))).GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("employeeId").GetGuid());
        after[one].GetProperty("balanceAfterMinutes").GetInt32().Should().Be(2400);
        after[two].GetProperty("balanceAfterMinutes").GetInt32().Should().Be(-300);
        after[one].GetProperty("suggestedPayMinutes").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "já decidido");
        var cleared = await OkAsync(await hr.Client.DeleteAsync(Slip(two)));
        Amount(cleared, "BHDESC").Should().Be(0m, "tirar a decisão recalcula sem a linha");
        await OkAsync(await hr.Client.PutAsJsonAsync(Slip(two), new { action = "Discount", minutes = 300, note = "Acerto do saldo negativo" }));

        // Fechar (as tabelas conferidas pela contadora): a baixa no espelho de setembro e no de outubro já fechado.
        foreach (var t in await DbAsync(tenant, db => db.PayrollTaxTables.AsNoTracking().Where(x => x.SeedPackage == 2026).ToListAsync()))
        {
            var d = await OkAsync(await hr.Client.GetAsync($"/api/v1/hr/payroll-tax-tables/{t.Id}"));
            await OkAsync(await hr.Client.PostAsJsonAsync($"/api/v1/hr/payroll-tax-tables/{t.Id}/check", new { accountant = "Contadora Fictícia", checkedOn = "2026-10-01", dataset = (string?)null, version = d.GetProperty("version").GetUInt32() }));
        }

        uint Version(System.Text.Json.JsonElement summary) => summary.GetProperty("version").GetUInt32();
        await OkAsync(await hr.Client.SendAsync(Post($"{Runs}/{run}/close", new { paymentDate = (string?)null, twoFactorCode = hr.Codes!.Next(), version = Version(await JsonAsync(await hr.Client.GetAsync($"{Runs}/{run}"))), acknowledgedChanges = true })));
        var sheets = await DbAsync(tenant, db => db.Timesheets.AsNoTracking().Where(t => t.EmployeeId == one || t.EmployeeId == two).ToListAsync());
        sheets.Single(t => t.EmployeeId == one && t.Period == period).BankPayoutMinutes.Should().Be(600);
        sheets.Single(t => t.EmployeeId == one && t.Period == period.AddMonths(1)).BankBalanceMinutes.Should().Be(2400, "o espelho seguinte já fechado parte do saldo depois da folha");
        sheets.Single(t => t.EmployeeId == two && t.Period == period).BankPayoutMinutes.Should().Be(-300);
        (await OkAsync(await hr.Client.GetAsync($"{Runs}/{run}/hour-bank/sensitive"))).GetProperty("editable").GetBoolean().Should().BeFalse();

        // Reabrir a folha desfaz a baixa.
        await OkAsync(await hr.Client.PostAsJsonAsync($"{Runs}/{run}/reopen", new { reason = "Conferência do banco de horas", version = Version(await JsonAsync(await hr.Client.GetAsync($"{Runs}/{run}"))) }));
        var reverted = await DbAsync(tenant, db => db.Timesheets.AsNoTracking().Where(t => t.EmployeeId == one).ToListAsync());
        reverted.Single(t => t.Period == period).BankPayoutMinutes.Should().Be(0);
        reverted.Single(t => t.Period == period.AddMonths(1)).BankBalanceMinutes.Should().Be(3000);
    }
}
