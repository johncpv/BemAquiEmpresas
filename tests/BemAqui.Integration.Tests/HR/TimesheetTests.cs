using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Application;
using BemAqui.HR.Application.TimeClock;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// RH-01c: espelho de ponto, banco de horas e fechamento com a trava real (os espelhos fechados). Os testes da classe dividem o tenant: cada
/// um usa o próprio período e funcionários admitidos para ele (o fechamento é do período inteiro); o cenário de fechamento usa maio e junho
/// de 2025, o espelho por permissão fevereiro de 2026 e a carga março de 2026.
/// </summary>
public class TimesheetTests(TenancyFixture tenancy) : TimeAdjustmentTestBase(tenancy), IClassFixture<TenancyFixture>
{
    private const string Sheets = "/api/v1/hr/timesheets";
    private const string MySheets = "/api/v1/me/hr/timesheets";

    protected override bool FakePeriods => false;

    private static string Sheet(Guid employeeId, string period) => $"/api/v1/hr/employees/{employeeId}/timesheets/{period}";

    /// <summary>Marcações de todos os dias úteis do intervalo (08:00 12:00 13:00 17:48), menos os dias trocados em <paramref name="special"/>.</summary>
    private Task<int> WorkdaysAsync(Guid employeeId, DateOnly from, DateOnly to, Dictionary<DateOnly, string[]>? special = null) => DbAsync(async db =>
    {
        TimeEntryType[] cycle = [TimeEntryType.In, TimeEntryType.BreakStart, TimeEntryType.BreakEnd, TimeEntryType.Out];
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var times = special is not null && special.TryGetValue(day, out var s) ? s : ["08:00", "12:00", "13:00", "17:48"];
            for (var i = 0; i < times.Length; i++)
            {
                var local = day.ToDateTime(TimeOnly.Parse(times[i], System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
                db.TimeEntries.Add(new TimeEntry
                {
                    EmployeeId = employeeId, Day = day, At = new DateTimeOffset(local, BusinessTime.Zone.GetUtcOffset(local)).ToUniversalTime(), Type = cycle[i % 4],
                    Source = TimeEntrySource.App, CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        return await db.SaveChangesAsync();
    });

    private Task<int> AdmitAsync(Guid employeeId, DateOnly admission, DateOnly? agreement = null) => DbAsync(async db =>
    {
        var e = await db.Employees.SingleAsync(x => x.Id == employeeId);
        e.AdmissionDate = admission;
        e.HourBankAgreementOn = agreement;
        return await db.SaveChangesAsync();
    });

    private Task<int> ApprovedAbsenceAsync(Guid employeeId, DateOnly day, int? minutes, AbsenceReason reason) => DbAsync(async db =>
    {
        db.TimeAdjustments.Add(new TimeAdjustment
        {
            EmployeeId = employeeId, Kind = TimeAdjustmentKind.Absence, Day = day, AbsenceMinutes = minutes, AbsenceReason = reason, Justification = "Abono aprovado no teste",
            Source = TimeAdjustmentSource.HR, RequestedAt = DateTimeOffset.UtcNow, Status = TimeAdjustmentStatus.Approved, DecidedAt = DateTimeOffset.UtcNow,
        });
        return await db.SaveChangesAsync();
    });

    private static JsonElement Day(JsonElement sheet, string day) => sheet.GetProperty("days").EnumerateArray().Single(d => d.GetProperty("day").GetString() == day);

    /// <summary>CA 3, 6, 7, 8 e 20: o espelho por permissão, o motivo do abono e do afastamento só com <c>hr.view_health</c>, "Meu espelho" e IDOR.</summary>
    [Fact]
    public async Task Espelho_por_permissao_protege_o_motivo_e_meu_espelho_so_do_proprio()
    {
        var ana = await PersonAsync(name: "Ana Espelho");
        var other = await EmployeeAsync("Bruno Espelho");
        var viewer = await PersonAsync("hr:view", employee: false);
        var health = await PersonAsync("hr:view,view_health", employee: false);
        await AdmitAsync(ana.EmployeeId, new DateOnly(2026, 1, 5));
        await WorkdaysAsync(ana.EmployeeId, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 27), new()
        {
            [new DateOnly(2026, 2, 3)] = ["08:00", "12:00", "13:00"],
            [new DateOnly(2026, 2, 4)] = ["08:00", "12:00", "13:00", "13:48"],
            [new DateOnly(2026, 2, 5)] = [],
            [new DateOnly(2026, 2, 6)] = ["08:00", "12:00", "12:40", "17:48"],
        });
        await ApprovedAbsenceAsync(ana.EmployeeId, new DateOnly(2026, 2, 4), 240, AbsenceReason.MedicalCertificate);
        await DbAsync(async db =>
        {
            db.EmployeeHistory.Add(new EmployeeHistoryEntry
            {
                EmployeeId = ana.EmployeeId, Kind = EmployeeHistoryKind.Leave, EffectiveDate = new DateOnly(2026, 2, 16), LeaveEndDate = new DateOnly(2026, 2, 20),
                LeaveType = AbsenceReason.MedicalCertificate, NewValue = "Afastado", Reason = "Repouso", CreatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        var response = await viewer.Client.GetAsync(Sheet(ana.EmployeeId, "2026-02"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var sheet = await JsonAsync(response);
        sheet.GetProperty("banner").GetString().Should().Be(TimeClockRules.InternalControlBanner);
        sheet.GetProperty("status").GetString().Should().Be("Open");
        sheet.GetProperty("start").GetString().Should().Be("2026-02-01");
        sheet.GetProperty("end").GetString().Should().Be("2026-02-28");
        Day(sheet, "2026-02-02").GetProperty("status").GetString().Should().Be("Normal");
        Day(sheet, "2026-02-02").GetProperty("expectedMinutes").GetInt32().Should().Be(528);
        Day(sheet, "2026-02-02").GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("time").GetString()).Should().Equal("08:00", "12:00", "13:00", "17:48");
        var odd = Day(sheet, "2026-02-03");
        odd.GetProperty("status").GetString().Should().Be("Inconsistent");
        odd.GetProperty("issues").EnumerateArray().Select(i => i.GetString()).Should().Contain("Falta a saída");
        odd.GetProperty("workedMinutes").GetInt32().Should().Be(240, "CA 6: só os pares fechados");
        Day(sheet, "2026-02-06").GetProperty("issues").EnumerateArray().Select(i => i.GetString()).Should().Contain("Intervalo de 40 min, a jornada prevê 60");
        Day(sheet, "2026-02-05").GetProperty("status").GetString().Should().Be("Absent");
        var justified = Day(sheet, "2026-02-04");
        justified.GetProperty("justifiedMinutes").GetInt32().Should().Be(240);
        justified.GetProperty("absences")[0].GetProperty("label").GetString().Should().Be("Ausência justificada", "CA 7: sem hr.view_health, o motivo fica protegido");
        justified.GetProperty("absences")[0].GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
        var leave = Day(sheet, "2026-02-18");
        leave.GetProperty("status").GetString().Should().Be("OnLeave");
        leave.GetProperty("leaveLabel").GetString().Should().Be("Afastado");
        leave.GetProperty("absenceMinutes").GetInt32().Should().Be(0, "afastado não é falta");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Atestado").And.NotContain("Medical").And.NotContain("latitude");

        var withHealth = await JsonAsync(await health.Client.GetAsync(Sheet(ana.EmployeeId, "2026-02")));
        Day(withHealth, "2026-02-04").GetProperty("absences")[0].GetProperty("label").GetString().Should().Be("Atestado médico");
        Day(withHealth, "2026-02-18").GetProperty("leaveLabel").GetString().Should().Be("Afastado: Atestado médico");

        // CA 8: "Meu espelho" do próprio funcionário (com o próprio motivo); nunca o de outro.
        var mine = await ana.Client.GetAsync($"{MySheets}/2026-02");
        mine.StatusCode.Should().Be(HttpStatusCode.OK, await mine.Content.ReadAsStringAsync());
        var mineJson = await JsonAsync(mine);
        mineJson.GetProperty("employeeId").GetGuid().Should().Be(ana.EmployeeId);
        Day(mineJson, "2026-02-04").GetProperty("absences")[0].GetProperty("label").GetString().Should().Be("Atestado médico");
        (await ana.Client.GetAsync($"{MySheets}/2026-02?employeeId={other}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ana.Client.GetAsync(Sheet(other, "2026-02"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem hr.view, o espelho dos outros não abre");
        (await viewer.Client.GetAsync(Sheet(Guid.NewGuid(), "2026-02"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var periods = await JsonAsync(await ana.Client.GetAsync(MySheets));
        periods.EnumerateArray().Should().NotBeEmpty();

        // Visão geral: um funcionário por linha, com o chip de situação.
        var overview = await JsonAsync(await viewer.Client.GetAsync($"{Sheets}?period=2026-02"));
        var row = overview.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("employeeId").GetGuid() == ana.EmployeeId);
        row.GetProperty("inconsistentDays").GetInt32().Should().Be(2);
        row.GetProperty("situationTone").GetString().Should().Be("alert");
        row.GetProperty("ackLabel").GetString().Should().Be("Período em aberto");
        (await viewer.Client.PostAsync($"{Sheets}/2026-02/close", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "fechar exige hr.edit");
    }

    /// <summary>CA 1 e 2: os parâmetros do ponto "A confirmar com a contadora" e o acordo de banco com <c>hr.edit</c> + <c>hr.view_sensitive</c>.</summary>
    [Fact]
    public async Task Parametros_do_ponto_e_acordo_de_banco_de_horas()
    {
        var owner = await PersonAsync(Group.OwnerSeedKey, employee: false, name: "Dona Parametro");
        var view = await JsonAsync(await owner.Client.GetAsync("/api/v1/hr/parameters"));
        var items = view.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("key").GetString()!);
        items[HrParametersService.TolerancePerEntry].GetProperty("value").GetDecimal().Should().Be(5m);
        items[HrParametersService.TolerancePerDay].GetProperty("value").GetDecimal().Should().Be(10m);
        items[HrParametersService.HourBankEnabled].GetProperty("value").GetDecimal().Should().Be(1m);
        items[HrParametersService.TimesheetPeriodStartDay].GetProperty("value").GetDecimal().Should().Be(1m);
        foreach (var key in new[] { HrParametersService.TolerancePerEntry, HrParametersService.TolerancePerDay, HrParametersService.HourBankEnabled, HrParametersService.TimesheetPeriodStartDay })
        {
            items[key].GetProperty("legal").GetBoolean().Should().BeTrue();
            items[key].GetProperty("confirmed").GetBoolean().Should().BeFalse("A confirmar com a contadora");
        }

        (await owner.Client.PutAsJsonAsync("/api/v1/hr/parameters", new { changes = new[] { new { key = HrParametersService.TimesheetPeriodStartDay, value = 29m } } }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o corte vai de 1 a 28");
        var confirmed = await owner.Client.PostAsJsonAsync("/api/v1/hr/parameters/confirm", new { key = HrParametersService.TolerancePerEntry });
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await JsonAsync(confirmed);
        var item = after.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == HrParametersService.TolerancePerEntry);
        item.GetProperty("confirmed").GetBoolean().Should().BeTrue();
        item.GetProperty("confirmedByName").GetString().Should().Be("Dona Parametro");
        var parameterId = item.GetProperty("parameterId").GetGuid();
        (await AuditAsync(nameof(HrParameter), parameterId.ToString())).Should().NotBeEmpty("CA 21: quem e quando conferiu");

        // CA 2: acordo de banco.
        var bruno = await EmployeeAsync("Bruno Acordo");
        var editor = await PersonAsync("hr:view,edit", employee: false);
        var sensitive = await PersonAsync("hr:view,edit,view_sensitive", employee: false);
        (await editor.Client.PutAsJsonAsync($"/api/v1/hr/employees/{bruno}/hour-bank-agreement", new { agreementOn = "2026-11-01" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "o acordo exige hr.view_sensitive");
        (await editor.Client.GetAsync($"/api/v1/hr/employees/{bruno}/hour-bank-agreement")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var set = await sensitive.Client.PutAsJsonAsync($"/api/v1/hr/employees/{bruno}/hour-bank-agreement", new { agreementOn = "2026-11-01" });
        set.StatusCode.Should().Be(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
        (await JsonAsync(await sensitive.Client.GetAsync($"/api/v1/hr/employees/{bruno}/hour-bank-agreement"))).GetProperty("agreementOn").GetString().Should().Be("2026-11-01");
        var audit = await AuditAsync(nameof(Employee), bruno.ToString());
        audit.Should().Contain(a => a.Changes.Contains("hourBankAgreementOn"));
        audit.Should().NotContain(a => a.Changes.Contains("2026-11-01"), "a data do acordo é [PersonalData]: mascarada na auditoria");
    }

    /// <summary>
    /// CA 9 a 18 e 21 (cenário): pré-condições com o atalho para cada pendência, "Marcar como revisado", fechamento concorrente (só um grava),
    /// período fechado bloqueia ajuste (409), totais congelados com o banco, "Li e confirmo", PDF sem dado sensível, "Usar horas do espelho",
    /// <c>ponto.csv</c> no pacote v2, reabertura em cascata com o aviso na folha e o histórico.
    /// </summary>
    [Fact]
    public async Task Fechar_confirmar_exportar_e_reabrir_o_periodo()
    {
        var owner = await PersonAsync(Group.OwnerSeedKey, employee: false, name: "Paula Dona");
        var launcher = await PersonAsync("hr:view,create,edit", employee: false, name: "Rita RH");
        var ana = await PersonAsync(name: "Ana Fechamento");
        var bruno = await EmployeeAsync("Bruno Fechamento");
        await AdmitAsync(ana.EmployeeId, new DateOnly(2025, 1, 2));
        await AdmitAsync(bruno, new DateOnly(2025, 1, 2), agreement: new DateOnly(2025, 5, 1));
        await DbAsync(async db =>
        {
            var e = await db.Employees.SingleAsync(x => x.Id == ana.EmployeeId);
            e.Cpf = "52998224725";
            return await db.SaveChangesAsync();
        });
        await WorkdaysAsync(ana.EmployeeId, new DateOnly(2025, 5, 1), new DateOnly(2025, 6, 30), new()
        {
            [new DateOnly(2025, 5, 6)] = ["08:00", "12:00", "13:00"],
            [new DateOnly(2025, 5, 7)] = ["08:30", "12:00", "13:00", "17:48"],
            [new DateOnly(2025, 5, 8)] = ["08:00", "12:00", "13:00"],
            [new DateOnly(2025, 5, 9)] = [],
        });
        await WorkdaysAsync(bruno, new DateOnly(2025, 5, 1), new DateOnly(2025, 6, 30), new() { [new DateOnly(2025, 5, 5)] = ["08:00", "12:00", "13:00", "19:48"] });
        await ApprovedAbsenceAsync(ana.EmployeeId, new DateOnly(2025, 5, 9), null, AbsenceReason.MedicalCertificate);
        // Pedido pendente que corrige o dia 6 (falta a saída).
        var pending = await LaunchAsync(launcher.Client, ana.EmployeeId, new DateOnly(2025, 5, 6), "17:48", "Out");

        // CA 11: as pré-condições por funcionário (pedido pendente e dias inconsistentes) bloqueiam.
        var check = await JsonAsync(await launcher.Client.GetAsync($"{Sheets}/2025-05/close-check"));
        check.GetProperty("canClose").GetBoolean().Should().BeFalse();
        var blockers = check.GetProperty("employees").EnumerateArray().Single(e => e.GetProperty("employeeId").GetGuid() == ana.EmployeeId);
        blockers.GetProperty("pendingAdjustments").GetInt32().Should().Be(1);
        blockers.GetProperty("inconsistentDays").EnumerateArray().Select(d => d.GetString()).Should().Equal("2025-05-06", "2025-05-08");
        var refused = await launcher.Client.PostAsync($"{Sheets}/2025-05/close", null);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(refused)).Should().Be(TimesheetRules.PreconditionsCode);

        // Resolvendo no caminho: o pedido aprovado (por outra pessoa) fecha o dia 6; o dia 8 é marcado como revisado, com comentário.
        (await ApproveAsync(owner.Client, pending)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await launcher.Client.PostAsJsonAsync($"/api/v1/hr/employees/{ana.EmployeeId}/timesheets/reviews", new { day = "2025-05-08", comment = "ok" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o comentário tem de 5 a 200 caracteres");
        (await launcher.Client.PostAsJsonAsync($"/api/v1/hr/employees/{ana.EmployeeId}/timesheets/reviews", new { day = "2025-05-12", comment = "Dia sem problema nenhum" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "só dia inconsistente é revisado");
        var reviewed = await launcher.Client.PostAsJsonAsync($"/api/v1/hr/employees/{ana.EmployeeId}/timesheets/reviews", new { day = "2025-05-08", comment = "Saída conferida com o supervisor" });
        reviewed.StatusCode.Should().Be(HttpStatusCode.OK, await reviewed.Content.ReadAsStringAsync());
        (await JsonAsync(reviewed)).GetProperty("reviewed").GetBoolean().Should().BeTrue();
        (await JsonAsync(await launcher.Client.GetAsync($"{Sheets}/2025-05/close-check"))).GetProperty("canClose").GetBoolean().Should().BeTrue();

        // CA 14: dois fechamentos ao mesmo tempo, só um grava.
        var both = await Task.WhenAll(launcher.Client.PostAsync($"{Sheets}/2025-05/close", null), owner.Client.PostAsync($"{Sheets}/2025-05/close", null));
        both.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, string.Join(" | ", await Task.WhenAll(both.Select(r => r.Content.ReadAsStringAsync()))));
        var loser = both.Single(r => r.StatusCode != HttpStatusCode.OK);
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(loser)).Should().Be("concurrency_conflict");
        (await JsonAsync(both.Single(r => r.StatusCode == HttpStatusCode.OK))).GetProperty("message").GetString().Should().Be("Período maio de 2025 fechado");
        (await DbAsync(db => db.Timesheets.CountAsync(t => t.Period == new DateOnly(2025, 5, 1) && (t.EmployeeId == ana.EmployeeId || t.EmployeeId == bruno))))
            .Should().Be(2, "idempotente por (funcionário, período)");

        // CA 12: período fechado bloqueia lançar e pedir ajuste (409) e o espelho mostra quem fechou.
        var blocked = await launcher.Client.SendAsync(Post(Of(ana.EmployeeId), new { kind = "AddEntry", day = "2025-05-20", time = "18:00", entryType = "Out", justification = "Esqueceu de marcar no app" }));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(blocked)).Should().Be(TimesheetPeriods.ClosedCode);
        var closedSheet = await JsonAsync(await launcher.Client.GetAsync(Sheet(ana.EmployeeId, "2025-05")));
        closedSheet.GetProperty("status").GetString().Should().Be("Closed");
        closedSheet.GetProperty("closedBanner").GetString().Should().StartWith("Período fechado em ").And.Contain(" por ");

        // Totais congelados: Ana sem acordo (atraso 30 + 288 do dia 8 revisado; abono do dia 9); Bruno com banco (+2 h, sem extra).
        var anaTotals = closedSheet.GetProperty("totals");
        anaTotals.GetProperty("lateMinutes").GetInt32().Should().Be(318);
        anaTotals.GetProperty("absenceMinutes").GetInt32().Should().Be(0);
        anaTotals.GetProperty("justifiedMinutes").GetInt32().Should().Be(528);
        anaTotals.GetProperty("bankBalanceMinutes").ValueKind.Should().Be(JsonValueKind.Null, "sem acordo, sem saldo");
        var brunoSheet = await JsonAsync(await launcher.Client.GetAsync(Sheet(bruno, "2025-05")));
        brunoSheet.GetProperty("totals").GetProperty("overtimeMinutes").GetInt32().Should().Be(0);
        brunoSheet.GetProperty("totals").GetProperty("bankMonthMinutes").GetInt32().Should().Be(120);
        brunoSheet.GetProperty("totals").GetProperty("bankText").GetString().Should().Be("Banco de horas: +2 h no período, +2 h acumulado");

        // CA 15: "Li e confirmo" do próprio funcionário; o RH vê "Confirmado em".
        var ack = await ana.Client.PostAsync($"{MySheets}/2025-05/ack", null);
        ack.StatusCode.Should().Be(HttpStatusCode.OK, await ack.Content.ReadAsStringAsync());
        (await JsonAsync(ack)).GetProperty("summary").GetString().Should().Contain("atrasos 5 h 18 min");
        var overview = await JsonAsync(await launcher.Client.GetAsync($"{Sheets}?period=2025-05"));
        overview.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("employeeId").GetGuid() == ana.EmployeeId).GetProperty("ackLabel").GetString().Should().StartWith("Confirmado em ");
        overview.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("employeeId").GetGuid() == bruno).GetProperty("ackLabel").GetString().Should().Be("Aguardando o funcionário");

        // CA 18: PDF sem CPF, sem salário, sem motivo de saúde e sem localização; o download fica auditado.
        var pdf = await ana.Client.GetAsync($"{MySheets}/2025-05/pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync());
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        using (var document = PdfDocument.Open(await pdf.Content.ReadAsByteArrayAsync()))
        {
            var text = string.Join(" ", document.GetPages().Select(p => p.Text));
            text.Should().Contain("Ana Fechamento").And.Contain("Assinatura do funcionário").And.Contain("Controle interno de jornada; não substitui o registro legal de ponto.");
            text.Should().NotContain("529.982").And.NotContain("52998224725").And.NotContain("3000").And.NotContain("Atestado").And.NotContain("latitude", "CA 18");
        }

        var sheetId = await DbAsync(db => db.Timesheets.Where(t => t.EmployeeId == ana.EmployeeId && t.Period == new DateOnly(2025, 5, 1)).Select(t => t.Id).SingleAsync());
        (await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.AnyAsync(a => a.Entity == TimesheetPdfService.OwnerType && a.EntityId == sheetId.ToString() && a.Action == BemAqui.BuildingBlocks.Persistence.Auditing.AuditActions.SensitiveRead)))
            .Should().BeTrue("o download do espelho fica auditado");
        (await launcher.Client.GetAsync($"/api/v1/hr/employees/{bruno}/timesheets/2025-05/pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        var batch = await JsonAsync(await launcher.Client.PostAsync($"{Sheets}/2025-05/pdfs", null));
        batch.GetProperty("queued").GetBoolean().Should().BeFalse();
        var zip = await launcher.Client.GetAsync($"{Sheets}/2025-05/pdfs/{batch.GetProperty("attachmentId").GetGuid()}");
        zip.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var archive = new ZipArchive(new MemoryStream(await zip.Content.ReadAsByteArrayAsync())))
        {
            archive.Entries.Should().Contain(e => e.Name.Contains("ana-fechamento", StringComparison.Ordinal));
        }

        // CA 16: "Usar horas do espelho" na folha lançada do mês: a referência das rubricas "Origem no ponto", sem valor.
        var run = await owner.Client.SendAsync(Post("/api/v1/hr/payroll-runs", new { period = "2025-05", type = "Monthly" }));
        run.StatusCode.Should().Be(HttpStatusCode.Created, await run.Content.ReadAsStringAsync());
        var runId = (await JsonAsync(run)).GetProperty("id").GetGuid();
        var slips = await JsonAsync(await owner.Client.GetAsync($"/api/v1/hr/payroll-runs/{runId}/payslips/sensitive"));
        var slipId = slips.EnumerateArray().Single(s => s.GetProperty("employeeId").GetGuid() == ana.EmployeeId).GetProperty("id").GetGuid();
        var hours = await JsonAsync(await launcher.Client.GetAsync($"/api/v1/hr/payroll-runs/{runId}/payslips/{slipId}/timesheet"));
        hours.GetProperty("available").GetBoolean().Should().BeTrue();
        hours.GetProperty("lateMinutes").GetInt32().Should().Be(318);
        var references = hours.GetProperty("references").EnumerateArray().ToDictionary(r => r.GetProperty("code").GetString()!);
        references["ATRASOS"].GetProperty("reference").GetString().Should().Be("5:18 h");
        references["FALTAS"].GetProperty("reference").GetString().Should().Be("0:00 h");
        references["HE50"].GetProperty("source").GetString().Should().Be("Overtime");
        references.Values.SelectMany(r => r.EnumerateObject().Select(p => p.Name)).Should().NotContain("amount", "a referência nunca leva valor");

        // CA 17: o ponto.csv no pacote v2, igual ao espelho fechado; com o período em aberto, o aviso.
        var package = await owner.Client.GetAsync("/api/v1/hr/accountant-exports/package?period=2025-05");
        package.StatusCode.Should().Be(HttpStatusCode.OK, await package.Content.ReadAsStringAsync());
        var files = await UnzipAsync(package);
        files.Keys.Should().Contain("ponto.csv");
        var csv = files["ponto.csv"].Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        csv[0].Should().Be(string.Join(';', AccountantLayoutV2.TimesheetHeader));
        var anaRow = csv.Skip(1).Select(l => l.Split(';')).Single(r => r[1] == "Ana Fechamento");
        anaRow[0].Should().Be("529.982.247-25");
        anaRow[2..4].Should().Equal("01/05/2025", "31/05/2025");
        anaRow[6].Should().Be(TimesheetRules.Clock(anaTotals.GetProperty("workedMinutes").GetInt32()));
        anaRow[10].Should().Be("5:18");
        anaRow[12].Should().Be("Ausência justificada 8:48", "o motivo de saúde não sai pelo nome");
        csv.Skip(1).Select(l => l.Split(';')).Single(r => r[1] == "Bruno Fechamento")[13..15].Should().Equal("2:00", "2:00");
        files["leia-me.txt"].Should().Contain("Layout: padrão do sistema v2").And.Contain("Ponto: incluído (período fechado em ");
        var june = await JsonAsync(await owner.Client.GetAsync("/api/v1/hr/accountant-exports?period=2025-06"));
        june.GetProperty("notIncluded").EnumerateArray().Select(n => n.GetString()).Should().Contain(AccountantTimesheetSection.NotClosedNote);

        // Junho fechado: o acumulado do Bruno continua do fechamento de maio.
        (await launcher.Client.PostAsync($"{Sheets}/2025-06/close", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(await launcher.Client.GetAsync(Sheet(bruno, "2025-06")))).GetProperty("totals").GetProperty("bankBalanceMinutes").GetInt32().Should().Be(120);

        // CA 13: reabrir maio (hr.approve) reabre junho, tira a ciência, avisa o Proprietário e a folha fechada mostra o aviso.
        await DbAsync(async db =>
        {
            var r = await db.PayrollRuns.SingleAsync(x => x.Id == runId);
            r.Status = PayrollRunStatus.Closed;
            r.ClosedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });
        (await launcher.Client.PostAsJsonAsync($"{Sheets}/2025-05/reopen", new { reason = "Atestado entregue depois" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "reabrir exige hr.approve");
        (await owner.Client.PostAsJsonAsync($"{Sheets}/2025-05/reopen", new { reason = "ok" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var reopened = await owner.Client.PostAsJsonAsync($"{Sheets}/2025-05/reopen", new { reason = "Atestado entregue depois" });
        reopened.StatusCode.Should().Be(HttpStatusCode.OK, await reopened.Content.ReadAsStringAsync());
        (await JsonAsync(reopened)).GetProperty("periods").EnumerateArray().Select(p => p.GetString()).Should().Equal("2025-05-01", "2025-06-01");
        var rows = await DbAsync(db => db.Timesheets.AsNoTracking().Where(t => t.EmployeeId == ana.EmployeeId).ToListAsync());
        rows.Should().OnlyContain(t => t.Status == TimesheetStatus.Open && t.EmployeeAckAt == null && t.ReopenReason == "Atestado entregue depois");
        (await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().Notifications.AnyAsync(n => n.UserId == owner.UserId && n.Type == TimesheetService.NotificationType)))
            .Should().BeTrue("os membros do Proprietário são avisados");
        (await JsonAsync(await owner.Client.GetAsync($"/api/v1/hr/payroll-runs/{runId}"))).GetProperty("timesheetNotice").GetString()
            .Should().Be("O ponto deste mês foi reaberto depois do fechamento da folha.");
        (await JsonAsync(await launcher.Client.GetAsync($"/api/v1/hr/payroll-runs/{runId}/payslips/{slipId}/timesheet"))).GetProperty("available").GetBoolean().Should().BeFalse();

        // CA 21: o histórico do espelho tem o fechamento, a ciência e a reabertura (quem e quando), sem os dias em claro.
        var history = await AuditAsync(nameof(Timesheet), sheetId.ToString());
        history.Count.Should().BeGreaterThanOrEqualTo(3);
        history.Should().Contain(a => a.Changes.Contains("reopenReason"));
        history.Should().NotContain(a => a.Changes.Contains("MedicalCertificate"), "os dias congelados são [PersonalData]");
    }

    /// <summary>CA 19: o espelho geral com 50 funcionários e 4 marcações por dia útil responde em menos de 1 s.</summary>
    [Fact]
    [Trait(LoadTest.Trait, LoadTest.Value)]
    public async Task Espelho_geral_com_50_funcionarios_em_menos_de_1_s()
    {
        var viewer = await PersonAsync("hr:view", employee: false);
        for (var i = 0; i < 50; i++)
        {
            var id = await EmployeeAsync($"Carga {i:00}");
            await AdmitAsync(id, new DateOnly(2026, 2, 2));
            await WorkdaysAsync(id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        }

        (await viewer.Client.GetAsync($"{Sheets}?period=2026-03")).StatusCode.Should().Be(HttpStatusCode.OK);
        var watch = Stopwatch.StartNew();
        var response = await viewer.Client.GetAsync($"{Sheets}?period=2026-03");
        watch.Stop();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("rows").GetArrayLength().Should().BeGreaterThanOrEqualTo(50);
        watch.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    private static async Task<Dictionary<string, string>> UnzipAsync(HttpResponseMessage response)
    {
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        var files = new Dictionary<string, string>();
        foreach (var entry in zip.Entries)
        {
            await using var stream = entry.Open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var content = buffer.ToArray();
            files[entry.Name] = Encoding.UTF8.GetString(content, 3, content.Length - 3);
        }

        return files;
    }
}
