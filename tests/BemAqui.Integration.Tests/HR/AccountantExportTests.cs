using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
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
/// RH-03: pacote mensal para a contadora (layout padrão v1). Cada teste usa a própria competência antiga (o mês atual ou um anterior), para os
/// eventos e as folhas não se misturarem; os dados de carga ficam no tenant da classe.
/// </summary>
public class AccountantExportTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Exports = "/api/v1/hr/accountant-exports";
    private const string Runs = "/api/v1/hr/payroll-runs";
    private const string Canary = "CANARIO77";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).GetProperty("code").GetString();

    private async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Func<TenantDbContext, Task<T>> action) => InTenantAsync(sp => action(sp.GetRequiredService<TenantDbContext>()));

    /// <summary>Usuário num grupo do seed, num grupo com as ações de <c>hr</c> dadas ("hr:view,view_sensitive") ou sem grupo.</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "x" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await InTenantAsync(async sp =>
            {
                var db = sp.GetRequiredService<IIdentityDb>();
                Guid groupId;
                if (seedKey.StartsWith("hr:", StringComparison.Ordinal))
                {
                    var group = new Group { Name = "Contadora " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
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
                return 0;
            });
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    private static async Task<(Guid Id, string Name, string Schedule)> PositionAsync(HttpClient owner, string? cbo = null)
    {
        var schedule = (await JsonAsync(await owner.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First();
        var name = "Montador " + Guid.NewGuid().ToString("N")[..6];
        var created = await owner.PostAsJsonAsync("/api/v1/hr/positions", new { name, cbo, defaultWorkCenterId = (Guid?)null, workScheduleId = schedule.GetProperty("id").GetGuid(), monthlyHours = (decimal?)null });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return ((await JsonAsync(created)).GetProperty("id").GetGuid(), name, schedule.GetProperty("name").GetString()!);
    }

    private static async Task<(Guid Id, string Cpf, string Registration, string Name)> EmployeeAsync(HttpClient owner, Guid position, string admission, decimal salary = 3300m, string? name = null)
    {
        var cpf = NewCpf();
        var registration = "C" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        name ??= "Pessoa " + Guid.NewGuid().ToString("N")[..6];
        var created = await owner.PostAsJsonAsync("/api/v1/hr/employees", new { name, cpf, birthDate = "1990-05-10", admissionDate = admission, positionId = position, workScheduleId = (Guid?)null, baseSalary = salary, registration });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return ((await JsonAsync(created)).GetProperty("employee").GetProperty("id").GetGuid(), cpf, registration, name);
    }

    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    private static string Masked(string cpf) => $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}";

    private static async Task<Guid> RunAsync(HttpClient owner, string period, string type, Guid[] employees)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Runs) { Content = JsonContent.Create(new { period, type, employeeIds = employees }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var created = await owner.SendAsync(request);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    /// <summary>Cada holerite com o salário e o INSS, mais a guia do FGTS.</summary>
    private static async Task FillAsync(HttpClient owner, Guid run)
    {
        var items = (await JsonAsync(await owner.GetAsync("/api/v1/hr/payroll-items"))).EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("id").GetGuid());
        foreach (var slip in (await JsonAsync(await owner.GetAsync($"{Runs}/{run}/payslips/sensitive"))).EnumerateArray().ToList())
        {
            var saved = await owner.PutAsJsonAsync($"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}",
                new { lines = new object[] { new { payrollItemId = items["SAL"], reference = "30 dias", amount = 3500m }, new { payrollItemId = items["INSS"], reference = (string?)null, amount = 312.45m } }, inssBase = 3500m, fgtsBase = 3500m, irrfBase = 0m, fgtsAmount = 280m, version = slip.GetProperty("version").GetUInt32() });
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        var charge = await owner.PostAsJsonAsync($"{Runs}/{run}/charges", new { kind = "Fgts", description = (string?)null, amount = 560m, dueDate = "2024-04-19", payeePartyId = (Guid?)null });
        charge.StatusCode.Should().Be(HttpStatusCode.OK, await charge.Content.ReadAsStringAsync());
    }

    /// <summary>Fecha direto no banco (o fechamento com 2FA e títulos tem os próprios testes, RH-02c): só a situação importa aqui.</summary>
    private Task<int> CloseRunAsync(Guid run, PayrollRunStatus status = PayrollRunStatus.Closed) =>
        DbAsync(async db =>
        {
            var r = await db.PayrollRuns.SingleAsync(x => x.Id == run);
            r.Status = status;
            r.ClosedAt = new DateTimeOffset(2024, 4, 5, 15, 0, 0, TimeSpan.Zero);
            await db.SaveChangesAsync();
            return 0;
        });

    private static async Task<Dictionary<string, string>> UnzipAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var files = new Dictionary<string, string>();
        foreach (var entry in zip.Entries)
        {
            await using var stream = entry.Open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var content = buffer.ToArray();
            content.Take(3).Should().Equal([0xEF, 0xBB, 0xBF], $"{entry.Name} em UTF-8 com BOM");
            files[entry.Name] = Encoding.UTF8.GetString(content, 3, content.Length - 3);
        }

        return files;
    }

    private static List<string[]> Rows(string csv) => csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split(';')).ToList();

    private Task<List<AuditEntry>> ExportAuditAsync(string period) =>
        InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking().Where(a => a.Entity == AccountantExportService.AuditEntity && a.EntityId == period).ToListAsync());

    private Task<int> OutboxCountAsync() => DbAsync(db => db.OutboxMessages.CountAsync());

    private async Task SetHrReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "hr").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>CA 1 a 5: o pacote com a folha fechada, a movimentação, o leia-me, a minimização, o SensitiveRead e o no-store.</summary>
    [Fact]
    public async Task Pacote_com_folha_fechada_movimentacao_minimizada_e_auditada()
    {
        const string period = "2024-03";
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (positionId, positionName, scheduleName) = await PositionAsync(owner, "715505");
        var (_, newPosition, _) = await PositionAsync(owner, "724315");
        var admitted = await EmployeeAsync(owner, positionId, "2024-03-05", 3500m, "=SOMA(A1) " + Guid.NewGuid().ToString("N")[..4]);
        var leaving = await EmployeeAsync(owner, positionId, "2020-01-02");
        var changed = await EmployeeAsync(owner, positionId, "2020-01-02");
        var other = await EmployeeAsync(owner, positionId, "2020-01-02");
        var deleted = await EmployeeAsync(owner, positionId, "2024-03-07");
        await DbAsync(async db =>
        {
            var a = await db.Employees.SingleAsync(e => e.Id == admitted.Id);
            (a.Street, a.Phone, a.Email, a.BankAccount, a.PixKey, a.City) = ($"Rua {Canary}", "1199" + Canary, $"{Canary}@teste.com", $"{Canary}-1", $"{Canary}-pix", Canary);
            db.EmployeeDependents.Add(new EmployeeDependent { EmployeeId = a.Id, Name = $"Dependente {Canary}", BirthDate = new DateOnly(2015, 1, 1), CreatedAt = DateTimeOffset.UtcNow });
            var l = await db.Employees.SingleAsync(e => e.Id == leaving.Id);
            l.TerminationDate = new DateOnly(2024, 3, 20);
            l.Status = EmployeeStatus.Terminated;
            var now = DateTimeOffset.UtcNow;
            db.EmployeeHistory.AddRange(
                new EmployeeHistoryEntry { EmployeeId = changed.Id, Kind = EmployeeHistoryKind.Salary, EffectiveDate = new DateOnly(2024, 3, 1), OldValue = "3300.00", NewValue = "4000.00", Reason = $"Motivo {Canary}", CreatedAt = now },
                new EmployeeHistoryEntry { EmployeeId = changed.Id, Kind = EmployeeHistoryKind.Position, EffectiveDate = new DateOnly(2024, 3, 10), OldValue = positionName, NewValue = newPosition, Reason = $"Motivo {Canary}", CreatedAt = now },
                new EmployeeHistoryEntry { EmployeeId = changed.Id, Kind = EmployeeHistoryKind.Leave, EffectiveDate = new DateOnly(2024, 2, 25), LeaveEndDate = new DateOnly(2024, 3, 5), LeaveType = AbsenceReason.MedicalCertificate, NewValue = "Illness", Reason = $"Afastamento {Canary}", CreatedAt = now },
                new EmployeeHistoryEntry { EmployeeId = other.Id, Kind = EmployeeHistoryKind.Leave, EffectiveDate = new DateOnly(2024, 3, 25), LeaveEndDate = new DateOnly(2024, 3, 28), LeaveType = AbsenceReason.JustifiedAbsence, NewValue = "Other", Reason = "Cancelado", CreatedAt = now, CancelledAt = now },
                new EmployeeHistoryEntry { EmployeeId = other.Id, Kind = EmployeeHistoryKind.Salary, EffectiveDate = new DateOnly(2024, 4, 1), OldValue = "3300.00", NewValue = "3600.00", Reason = "Fora da competência", CreatedAt = now });
            (await db.Employees.SingleAsync(e => e.Id == deleted.Id)).MarkDeleted(now);
            await db.SaveChangesAsync();
            return 0;
        });

        var closedRun = await RunAsync(owner, period, "Vacation", [admitted.Id, changed.Id]);
        await FillAsync(owner, closedRun);
        await CloseRunAsync(closedRun);
        var draftRun = await RunAsync(owner, period, "Termination", [leaving.Id]);
        await FillAsync(owner, draftRun);

        var outboxBefore = await OutboxCountAsync();
        var response = await owner.GetAsync($"{Exports}/package?period={period}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Headers.CacheControl!.NoStore.Should().BeTrue("CA 5: no-store");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        (response.Content.Headers.ContentDisposition.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName!.Trim('"')).Should().Be("contadora-2024-03-v2.zip");
        var files = await UnzipAsync(response);
        files.Keys.Should().BeEquivalentTo(new[] { "movimentacao.csv", "folha.csv", "leia-me.txt" });

        // CA 2 e 7: o cabeçalho golden e uma linha por evento.
        files["movimentacao.csv"].Split("\r\n")[0].Should().Be(string.Join(';', AccountantLayoutV1.MovementsHeader));
        var events = Rows(files["movimentacao.csv"]);
        var admission = events.Single(r => r[0] == "admissao" && r[1] == admitted.Registration);
        admission[2].Should().StartWith("'=SOMA(A1)", "CA 4: célula com = sai como texto");
        admission[3].Should().Be(Masked(admitted.Cpf));
        admission[4..10].Should().Equal("10/05/1990", positionName, "7155-05", "3500,00", scheduleName, "05/03/2024");
        events.Single(r => r[1] == leaving.Registration).Should().Equal("desligamento", leaving.Registration, leaving.Name, Masked(leaving.Cpf), "", "", "", "", "", "20/03/2024", "", "");
        events.Single(r => r[0] == "reajuste" && r[1] == changed.Registration)[7..10].Should().Equal("4000,00", "", "01/03/2024");
        events.Single(r => r[0] == "troca_cargo" && r[1] == changed.Registration)[5..10].Should().Equal(newPosition, "7243-15", "", "", "10/03/2024");
        events.Single(r => r[0] == "afastamento" && r[1] == changed.Registration)[9..].Should().Equal("25/02/2024", "Atestado médico", "05/03/2024");
        events.Should().NotContain(r => r[1] == other.Registration, "o afastamento cancelado e o reajuste de abril ficam fora");
        events.Should().NotContain(r => r[1] == deleted.Registration, "funcionário excluído não entra");

        // CA 3: as linhas da folha fechada iguais às da folha; a de rascunho fora.
        files["folha.csv"].Split("\r\n")[0].Should().Be(string.Join(';', AccountantLayoutV1.PayrollHeader));
        var payroll = Rows(files["folha.csv"]);
        var expected = await DbAsync(async db => await (from l in db.PayslipLines join p in db.Payslips on l.PayslipId equals p.Id where p.PayrollRunId == closedRun select new { p.EmployeeId, l.Code, l.Reference, l.Amount }).ToListAsync());
        expected.Should().HaveCount(4);
        payroll.Where(r => r[0] == "holerite").Should().HaveCount(4);
        foreach (var line in expected)
        {
            var key = line.EmployeeId == admitted.Id ? admitted.Registration : changed.Registration;
            payroll.Should().Contain(r => r[0] == "holerite" && r[1] == "Férias" && r[2] == key && r[3] == line.Code && r[5] == (line.Reference ?? string.Empty) && r[6] == AccountantLayoutV1.Money(line.Amount));
        }

        payroll.Where(r => r[0] == "guia").Should().ContainSingle().Which.Should().Equal("guia", "Férias", "", "", "", "", "560,00", "FGTS", "19/04/2024");
        payroll.Should().NotContain(r => r[1] == "Rescisão", "folha em rascunho não entra");
        payroll.Should().NotContain(r => r[2] == leaving.Registration);

        // CA 1 e 6: o leia-me.
        var readme = files["leia-me.txt"];
        readme.Should().Contain("Layout: padrão do sistema v2 (A confirmar com a contadora)").And.Contain("Competência: março de 2024 (03/2024)").And.Contain("Gerado em: ");
        readme.Should().Contain("- movimentacao.csv: Movimentação: 5 eventos.").And.Contain("- folha.csv: Folha: Férias fechada em 05/04/2024.");
        readme.Should().Contain("Ponto: não incluído; feche o período de ponto para exportar.").And.Contain("Folha Rescisão: em rascunho, não entrou.");
        readme.Should().NotContain("Substitui");

        // CA 4: nada de endereço, contato, conta, dependente nem motivo.
        string.Concat(files.Values).Should().NotContain(Canary).And.NotContain("Afastamento ").And.NotContain("Cancelado");

        // CA 5: SensitiveRead com a competência e o layout, sem valores; nada no outbox; nada guardado.
        var audit = (await ExportAuditAsync(period)).Should().ContainSingle().Subject;
        audit.Action.Should().Be(AuditActions.SensitiveRead);
        audit.UserId.Should().Be(ownerId);
        audit.Changes.Should().Contain("v2").And.NotContain("3500").And.NotContain(admitted.Cpf);
        (await OutboxCountAsync()).Should().Be(outboxBefore, "a exportação não publica evento");

        var overview = await JsonAsync(await owner.GetAsync($"{Exports}?period={period}"));
        overview.GetProperty("layout").GetString().Should().Be("v2");
        overview.GetProperty("layoutConfirmed").GetBoolean().Should().BeFalse("A confirmar com a contadora");
        overview.GetProperty("previous").GetProperty("period").GetString().Should().Be("2024-03-01", "CA 6: a exportação anterior da competência");
        overview.GetProperty("history").EnumerateArray().Should().Contain(h => h.GetProperty("period").GetString() == "2024-03-01" && h.GetProperty("layout").GetString() == "v2");
        overview.GetProperty("draftRuns").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).Should().Equal(draftRun);
        var sections = overview.GetProperty("sections").EnumerateArray().ToList();
        sections.Select(s => s.GetProperty("key").GetString()).Should().Equal("movements", "payroll", "calculated-payroll", "timeclock");
        sections[0].GetProperty("summary").GetString().Should().Be("Movimentação: 5 eventos");
        sections[3].GetProperty("included").GetBoolean().Should().BeFalse();
        overview.GetRawText().Should().NotContain(admitted.Cpf, "a tela não leva dado pessoal");
    }

    /// <summary>CA 1, 3 e 6: sem folha fechada, o pacote sai só com a movimentação; a nova exportação da competência diz o que substitui.</summary>
    [Fact]
    public async Task Sem_folha_fechada_e_nova_exportacao_substitui_a_anterior()
    {
        const string period = "2024-05";
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (positionId, _, _) = await PositionAsync(owner);
        var employee = await EmployeeAsync(owner, positionId, "2024-05-02");
        await RunAsync(owner, period, "Vacation", [employee.Id]);

        var first = await owner.GetAsync($"{Exports}/package?period={period}");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var files = await UnzipAsync(first);
        files.Keys.Should().BeEquivalentTo(new[] { "movimentacao.csv", "leia-me.txt" }, "sem folha fechada, folha.csv não entra");
        files["leia-me.txt"].Should().Contain("Folha: nenhuma folha fechada de maio de 2024; o arquivo folha.csv não entrou.").And.Contain("Folha Férias: em rascunho, não entrou.");
        Rows(files["movimentacao.csv"]).Should().ContainSingle(r => r[0] == "admissao" && r[1] == employee.Registration);

        var overview = await JsonAsync(await owner.GetAsync($"{Exports}?period={period}"));
        var previousAt = overview.GetProperty("previous").GetProperty("at").GetDateTimeOffset();
        var second = await UnzipAsync(await owner.GetAsync($"{Exports}/package?period={period}"));
        var local = TimeZoneInfo.ConvertTime(previousAt, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        second["leia-me.txt"].Should().Contain($"Substitui a exportação de {local:dd'/'MM'/'yyyy HH':'mm}.");
        (await ExportAuditAsync(period)).Should().HaveCount(2, "cada geração é registrada");
    }

    /// <summary>CA 5 e 8: as três camadas, hr.view_sensitive obrigatório, sem o tipo do afastamento para quem não vê saúde e a competência futura recusada.</summary>
    [Fact]
    public async Task Tres_camadas_e_competencia()
    {
        const string period = "2024-07";
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (positionId, _, _) = await PositionAsync(owner);
        var employee = await EmployeeAsync(owner, positionId, "2020-01-02");
        await DbAsync(async db =>
        {
            db.EmployeeHistory.Add(new EmployeeHistoryEntry { EmployeeId = employee.Id, Kind = EmployeeHistoryKind.Leave, EffectiveDate = new DateOnly(2024, 7, 1), LeaveEndDate = new DateOnly(2024, 7, 15), LeaveType = AbsenceReason.WorkAccident, NewValue = "WorkAccident", Reason = "x", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return 0;
        });

        var (_, nobody) = await ActorAsync();
        var noGroup = await nobody.GetAsync($"{Exports}/package?period={period}");
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(noGroup)).Should().Be("forbidden");
        var (_, viewer) = await ActorAsync("hr:view,edit,create,approve");
        var noSensitive = await viewer.GetAsync($"{Exports}/package?period={period}");
        noSensitive.StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem hr.view_sensitive");
        (await CodeAsync(noSensitive)).Should().Be("forbidden");
        (await viewer.GetAsync(Exports)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ExportAuditAsync(period)).Should().BeEmpty("a recusa não gera pacote nem registro");

        var (_, sensitive) = await ActorAsync("hr:view,view_sensitive");
        var package = await sensitive.GetAsync($"{Exports}/package?period={period}");
        package.StatusCode.Should().Be(HttpStatusCode.OK);
        var files = await UnzipAsync(package);
        Rows(files["movimentacao.csv"]).Single(r => r[1] == employee.Registration)[9..].Should().Equal(new[] { "01/07/2024", "", "15/07/2024" }, "sem hr.view_health o tipo não sai");
        files["leia-me.txt"].Should().Contain("Tipo do afastamento: não incluído");
        (await owner.GetAsync(Exports)).StatusCode.Should().Be(HttpStatusCode.OK, "sem competência, a sugerida (o mês anterior)");

        var future = await owner.GetAsync($"{Exports}/package?period=2099-01");
        future.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync($"{Exports}/package")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o pacote exige a competência");

        await SetHrReleasedAsync(false);
        try
        {
            var notReleased = await owner.GetAsync($"{Exports}/package?period={period}");
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetHrReleasedAsync(true);
        }
    }

    /// <summary>ADR 0042: o layout é parâmetro do RH, padrão v2 (RH-01c), "A confirmar com a contadora"; só um dos layouts disponíveis.</summary>
    [Fact]
    public async Task Layout_e_parametro_a_confirmar_com_a_contadora()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var view = await JsonAsync(await owner.GetAsync("/api/v1/hr/parameters"));
        var item = view.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == HrParametersService.AccountantExportLayout);
        item.GetProperty("kind").GetString().Should().Be("ExportLayout");
        item.GetProperty("code").GetString().Should().Be("v2");
        item.GetProperty("legal").GetBoolean().Should().BeTrue();
        view.GetProperty("exportLayouts").EnumerateArray().Select(l => l.GetProperty("code").GetString()).Should().Equal("v2", "v1");
        var bad = await owner.PutAsJsonAsync("/api/v1/hr/parameters", new { changes = new[] { new { key = HrParametersService.AccountantExportLayout, value = (decimal?)null, partyId = (Guid?)null, code = "v9" } } });
        bad.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
