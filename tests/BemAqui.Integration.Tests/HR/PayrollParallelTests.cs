using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Attachments;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-06: conferência paralela da folha calculada com a da contadora e a virada. As folhas são gravadas direto no banco (o cálculo e a
/// importação têm os próprios testes): só os valores por funcionário e rubrica importam aqui. No TenantA cada teste usa a própria competência
/// de 2035; a virada e a volta mudam o modo do tenant e rodam num tenant próprio.
/// </summary>
public class PayrollParallelTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>, IDisposable
{
    private const string Parallel = "/api/v1/hr/payroll-parallel";
    private const string Rh = "hr:view,create,edit,view_sensitive";
    private static readonly byte[] Pdf = "%PDF-1.4\n%%EOF"u8.ToArray();

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IVirusScanner>(new FakeScanner())));

    public void Dispose()
    {
        _factory?.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record SeedLine(Guid Employee, string Code, PayrollItemKind Kind, decimal? Entered, decimal? Calculated, string? EnteredCode = null);

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

    /// <summary>Usuário num grupo próprio ("hr:view,view_sensitive") ou no Proprietário (<see cref="Group.OwnerSeedKey"/>).</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(Tenant tenant, string grants)
    {
        var host = tenant.Domains.First().Host;
        var (id, stamp) = await tenancy.SeedUserAsync(tenant, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await InTenantAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (grants == Group.OwnerSeedKey)
            {
                groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            }
            else
            {
                var group = new Group { Name = "Conferência " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "hr", Enabled = true });
                foreach (var action in grants[3..].Split(','))
                {
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "hr", Action = action });
                }

                groupId = group.Id;
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
            await sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
            return 0;
        });
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
        return (id, client);
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

    private static async Task<(Guid Id, string Name, string Cpf, string Registration)> EmployeeAsync(HttpClient hr, Guid? position = null)
    {
        position ??= await PositionAsync(hr);
        var name = "Conferido " + Guid.NewGuid().ToString("N")[..6];
        var cpf = NewCpf();
        var registration = "Q" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var created = await OkAsync(await hr.PostAsJsonAsync("/api/v1/hr/employees", new
        {
            name, cpf, birthDate = "1988-03-15", admissionDate = "2026-01-05", positionId = position, workScheduleId = (Guid?)null, baseSalary = 3000m, registration,
        }), HttpStatusCode.Created);
        return (created.GetProperty("employee").GetProperty("id").GetGuid(), name, cpf, registration);
    }

    private static async Task<Guid> PositionAsync(HttpClient hr)
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        return (await OkAsync(await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = "Ajustador " + Guid.NewGuid().ToString("N")[..6], cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
    }

    /// <summary>A folha lançada e a calculada (simulação) da competência, com as linhas dadas; o retrato da calculada quando o teste pede.</summary>
    private Task<(Guid Entered, Guid Calculated)> SeedAsync(Tenant tenant, DateOnly period, IReadOnlyList<SeedLine> lines, PayrollRunType type = PayrollRunType.Monthly, string? snapshot = null) =>
        DbAsync(tenant, async db =>
        {
            var items = await db.PayrollItems.ToDictionaryAsync(i => i.Code, i => i.Id);
            var now = DateTimeOffset.UtcNow;
            var entered = new PayrollRun { Period = period, Type = type, Mode = PayrollRunMode.Entered, CreatedAt = now };
            var calculated = new PayrollRun { Period = period, Type = type, Mode = PayrollRunMode.Calculated, CreatedAt = now, CalculatedAt = now, CalculatorVersion = PayrollCalculator.Version, IsSimulation = true };
            db.PayrollRuns.AddRange(entered, calculated);
            foreach (var group in lines.GroupBy(l => l.Employee))
            {
                foreach (var (run, side) in new[] { (entered, false), (calculated, true) })
                {
                    var slip = new Payslip { PayrollRunId = run.Id, EmployeeId = group.Key, CreatedAt = now, UpdatedAt = now, InputsSnapshot = side ? snapshot : null, CalculatedAt = side ? now : null };
                    var rows = group.Where(l => (side ? l.Calculated : l.Entered) is not null).Select((l, i) => new PayslipLine
                    {
                        PayslipId = slip.Id, PayrollItemId = items.GetValueOrDefault(l.Code, l.Kind == PayrollItemKind.Deduction ? items["INSS"] : items["SAL"]),
                        Code = side ? l.Code : l.EnteredCode ?? l.Code, Description = l.Code, Type = l.Kind, Amount = (side ? l.Calculated : l.Entered)!.Value, Sort = i, Calculated = side,
                        Memory = side ? PayrollCalculator.WriteMemory([$"Memória {l.Code}: base da competência"]) : null,
                    }).ToList();
                    slip.ApplyTotals(PayrollRules.Totals(rows.Select(r => new PayslipLineValue(r.Type, r.Amount, null))));
                    db.Payslips.Add(slip);
                    db.PayslipLines.AddRange(rows);
                }
            }

            await db.SaveChangesAsync();
            return (entered.Id, calculated.Id);
        });

    private static async Task<JsonElement> CompareAsync(HttpClient client, DateOnly period, string type = "Monthly") =>
        await OkAsync(await client.PostAsJsonAsync($"{Parallel}/compare", new { period = PayrollParallelRules.Month(period), type }));

    private static JsonElement Cell(JsonElement detail, Guid employee, string code) =>
        detail.GetProperty("employees").EnumerateArray().Single(e => e.GetProperty("employeeId").GetGuid() == employee).GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("code").GetString() == code);

    private static async Task<Guid> AttachTermAsync(HttpClient owner, Guid run)
    {
        using var content = new MultipartFormDataContent { { new ByteArrayContent(Pdf) { Headers = { ContentType = new("application/pdf") } }, "file", "termo-assinado.pdf" } };
        return (await OkAsync(await owner.PostAsync($"{Parallel}/{run}/term", content), HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> AcceptAsync(HttpClient owner, Guid run, uint version, Guid term, HttpStatusCode expected = HttpStatusCode.OK, string crc = "SP-123456/O")
    {
        var response = await owner.SendAsync(Post($"{Parallel}/{run}/accept", new { accountantName = "Contadora Fictícia", accountantCrc = crc, signedTermAttachmentId = term, version }));
        return await OkAsync(response, expected);
    }

    /// <summary>
    /// CA 1, 2, 4 e 9: a comparação por funcionário e rubrica (R$, % e tolerância zero) reprova a competência; a classificação fica registrada, o
    /// "Erro do sistema" gera o caso anonimizado (canário: sem nome, CPF, matrícula nem nascimento) e a justificativa não aceita nada: só a
    /// comparação refeita com diferença zero.
    /// </summary>
    [Fact]
    public async Task Compara_classifica_gera_caso_anonimizado_e_so_aceita_com_diferenca_zero()
    {
        var a = tenancy.TenantA;
        var (_, hr) = await ActorAsync(a, Rh);
        var (_, viewer) = await ActorAsync(a, "hr:view");
        var (_, owner) = await ActorAsync(a, Group.OwnerSeedKey);
        var period = new DateOnly(2035, 3, 1);
        var position = await PositionAsync(hr);
        var ana = await EmployeeAsync(hr, position);
        var bia = await EmployeeAsync(hr, position);
        var snapshot = PayrollCalculator.WriteSnapshot(new PayrollCalcSnapshot(3000m, new DateOnly(2026, 1, 5), null, true, 220m, null, false, false,
            [new PayrollCalcDependent(new DateOnly(2013, 7, 19), true, true, true)], [], true, 0, 0, [new PayrollCalcManualLine(Guid.CreateVersion7(), null, 50m, "Prêmio de " + ana.Name)], PayrollCalcParameters.Default));
        var (entered, calculated) = await SeedAsync(a, period,
            [new(ana.Id, "SAL", PayrollItemKind.Earning, 3000m, 3000m), new(ana.Id, "INSS", PayrollItemKind.Deduction, 254.97m, 254.98m), new(bia.Id, "SAL", PayrollItemKind.Earning, 2000m, 2000m)],
            snapshot: snapshot);

        var pending = (await OkAsync(await viewer.GetAsync(Parallel))).GetProperty("pending").EnumerateArray().Single(p => p.GetProperty("period").GetString() == "2035-03-01");
        pending.GetProperty("canCompare").GetBoolean().Should().BeTrue("as duas folhas da competência estão prontas (Propositivo)");
        (await viewer.PostAsJsonAsync($"{Parallel}/compare", new { period = "2035-03" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "comparar mostra valores: hr.view_sensitive");

        var detail = await CompareAsync(hr, period);
        detail.GetProperty("run").GetProperty("status").GetString().Should().Be("Rejected", "com qualquer diferença, a competência fica reprovada");
        detail.GetProperty("run").GetProperty("differences").GetInt32().Should().Be(1);
        detail.GetProperty("canAccept").GetBoolean().Should().BeFalse();
        detail.GetProperty("netDifference").GetDecimal().Should().Be(-0.01m);
        detail.GetProperty("employees")[0].GetProperty("employeeId").GetGuid().Should().Be(ana.Id, "quem tem diferença vem primeiro");
        var inss = Cell(detail, ana.Id, "INSS");
        inss.GetProperty("entered").GetDecimal().Should().Be(254.97m);
        inss.GetProperty("calculated").GetDecimal().Should().Be(254.98m);
        inss.GetProperty("difference").GetDecimal().Should().Be(0.01m);
        inss.GetProperty("differencePct").GetDecimal().Should().Be(0.0039m);
        inss.GetProperty("memory").EnumerateArray().Select(m => m.GetString()).Should().Contain("Memória INSS: base da competência", "o drill-down mostra a memória do sistema");
        detail.GetProperty("items")[0].GetProperty("code").GetString().Should().Be("INSS");
        (await viewer.GetAsync($"{Parallel}/{detail.GetProperty("run").GetProperty("id").GetGuid()}/sensitive")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var id = detail.GetProperty("run").GetProperty("id").GetGuid();
        var cell = inss.GetProperty("id").GetGuid();
        var equal = Cell(detail, ana.Id, "SAL").GetProperty("id").GetGuid();
        (await hr.PutAsJsonAsync($"{Parallel}/{id}/differences/{equal}", new { classification = "Rounding", justification = "Sem diferença" })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "só a linha com diferença é classificada");
        var rule = await OkAsync(await hr.PutAsJsonAsync($"{Parallel}/{id}/differences/{cell}", new { classification = "RuleDifference", justification = "A convenção arredonda o INSS" }));
        rule.GetProperty("nextStep").GetProperty("link").GetString().Should().Be(PayrollParallelService.ParametersLink, "\"Regra diferente\" leva ao parâmetro");
        var system = await OkAsync(await hr.PutAsJsonAsync($"{Parallel}/{id}/differences/{cell}", new { classification = "SystemError", justification = "Motor somou um centavo para " + ana.Name }));
        system.GetProperty("cell").GetProperty("hasCorrectionCase").GetBoolean().Should().BeTrue();
        system.GetProperty("cell").GetProperty("classificationLabel").GetString().Should().Be("Erro do sistema");

        var cases = await hr.GetAsync($"{Parallel}/{id}/correction-cases");
        cases.StatusCode.Should().Be(HttpStatusCode.OK, await cases.Content.ReadAsStringAsync());
        cases.Content.Headers.ContentDisposition!.FileName.Should().Contain("casos-correcao-2035-03.json");
        var json = await cases.Content.ReadAsStringAsync();
        json.Should().Contain("INSS").And.Contain("254.98");
        json.Should().NotContain(ana.Name).And.NotContain(ana.Cpf).And.NotContain(ana.Registration).And.NotContain("2013-07-19").And.NotContain("1988-03-15", "o caso de correção não leva o que identifica");

        // CA 3 e 4: a justificativa não basta; aceitar fica bloqueado e o termo não sai.
        var version = detail.GetProperty("run").GetProperty("version").GetUInt32();
        (await owner.SendAsync(Post($"{Parallel}/{id}/accept", new { accountantName = "Contadora", accountantCrc = "SP-123456/O", signedTermAttachmentId = Guid.CreateVersion7(), version })))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await hr.GetAsync($"{Parallel}/{id}/term")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // CA 10: o histórico mostra quem e o quê; a justificativa só para hr.view_sensitive.
        var forViewer = (await OkAsync(await viewer.GetAsync($"{Parallel}/history"))).EnumerateArray().Where(e => e.GetProperty("description").GetString() == "Mensal de março de 2035").ToList();
        forViewer.Should().Contain(e => e.GetProperty("action").GetString() == "Comparou");
        forViewer.Should().Contain(e => e.GetProperty("action").GetString() == "Classificou uma diferença");
        string.Join(' ', forViewer.Select(e => e.GetRawText())).Should().NotContain("Motor somou");
        (await OkAsync(await hr.GetAsync($"{Parallel}/history"))).GetRawText().Should().Contain("Motor somou");

        // CA 9 (LGPD-01a): a exportação do titular leva a conferência; a anonimização troca a justificativa pelo marcador e tira o caso.
        await InTenantAsync(a, async sp =>
        {
            var provider = sp.GetServices<IPersonalDataProvider>().OfType<HrPersonalDataProvider>().Single();
            var subject = (await provider.ResolveAsync(PersonalDataSubjectKind.Employee, ana.Id))!;
            var section = (await provider.ExportAsync(new PersonalDataExportContext(subject, "LGPD-2035-1", new HashSet<string> { "hr.view_sensitive" })))!;
            section.Blocks.Should().Contain(b => b.Source == "hr.parallel_differences" && b.Records.Count == 2);
            var retention = new PersonalDataRetention([new PersonalDataRetentionRule(PersonalDataSubjectKind.Employee, RetentionGroups.Labor, RetentionRuleKind.Anytime, 0, RetentionBase.Termination)]);
            var context = new PersonalDataAnonymizationContext(subject, "LGPD-2035-1", new DateOnly(2035, 6, 1), retention, new Dictionary<string, DateOnly>());
            (await provider.PlanAnonymizationAsync(context))!.Items.Should().Contain(i => i.Source == "hr.parallel_differences");
            await provider.AnonymizeAsync(context);
            return 0;
        });
        var cleaned = await DbAsync(a, db => db.PayrollParallelDifferences.AsNoTracking().Where(d => d.ParallelRunId == id && d.EmployeeId == ana.Id && d.PayrollItemCode == "INSS").SingleAsync());
        cleaned.CorrectionCase.Should().BeNull();
        cleaned.Justification.Should().StartWith("Titular anonimizado");
        cleaned.Difference.Should().Be(0.01m, "os valores ficam, como os do holerite");

        // A calculada recalculada (aqui direto no banco) bate: a comparação refeita deixa a competência em conferência, sem a classificação.
        await DbAsync(a, async db =>
        {
            var line = await db.PayslipLines.SingleAsync(l => l.Code == "INSS" && db.Payslips.Any(p => p.Id == l.PayslipId && p.PayrollRunId == calculated));
            db.PayslipLines.Remove(line);
            db.PayslipLines.Add(new PayslipLine { PayslipId = line.PayslipId, PayrollItemId = line.PayrollItemId, Code = "INSS", Description = "INSS", Type = PayrollItemKind.Deduction, Amount = 254.97m, Calculated = true });
            return await db.SaveChangesAsync();
        });
        var again = await CompareAsync(hr, period);
        again.GetProperty("run").GetProperty("status").GetString().Should().Be("Open");
        again.GetProperty("canAccept").GetBoolean().Should().BeTrue();
        Cell(again, ana.Id, "INSS").GetProperty("classification").ValueKind.Should().Be(JsonValueKind.Null, "a linha que ficou igual perde a classificação");
        again.GetProperty("nextStep").GetProperty("label").GetString().Should().Be("Aceitar competência março de 2035");
    }

    /// <summary>CA 1: o de-para liga o código da folha da contadora ao do sistema.</summary>
    [Fact]
    public async Task De_para_liga_os_codigos_diferentes()
    {
        var a = tenancy.TenantA;
        var (_, hr) = await ActorAsync(a, Rh);
        var (_, viewer) = await ActorAsync(a, "hr:view,view_sensitive");
        var period = new DateOnly(2035, 5, 1);
        var ana = await EmployeeAsync(hr);
        var code = "Q" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        await SeedAsync(a, period, [new(ana.Id, "SAL", PayrollItemKind.Earning, 3000m, 3000m, EnteredCode: code)]);
        (await CompareAsync(hr, period)).GetProperty("run").GetProperty("differences").GetInt32().Should().Be(2, "sem o de-para, os códigos não se encontram");

        (await viewer.PutAsJsonAsync($"{Parallel}/mappings", new { enteredCode = code, calculatedCode = "SAL" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "editar o de-para é hr.edit");
        (await hr.PutAsJsonAsync($"{Parallel}/mappings", new { enteredCode = code, calculatedCode = "NAOEXISTE" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var list = await OkAsync(await hr.PutAsJsonAsync($"{Parallel}/mappings", new { enteredCode = code, calculatedCode = "SAL" }));
        list.EnumerateArray().Should().Contain(m => m.GetProperty("enteredCode").GetString() == code && m.GetProperty("calculatedCode").GetString() == "SAL");
        var detail = await CompareAsync(hr, period);
        detail.GetProperty("run").GetProperty("differences").GetInt32().Should().Be(0);
        detail.GetProperty("run").GetProperty("status").GetString().Should().Be("Open");
        var mapping = list.EnumerateArray().Single(m => m.GetProperty("enteredCode").GetString() == code).GetProperty("id").GetGuid();
        await OkAsync(await hr.DeleteAsync($"{Parallel}/mappings/{mapping}"));
    }

    /// <summary>
    /// CA 3 e 9: o aceite é do Proprietário, com a contadora (nome e CRC) e o termo assinado anexado; a folha mudada depois da comparação pede
    /// nova comparação; a competência aceita não é comparada de novo.
    /// </summary>
    [Fact]
    public async Task Aceite_so_pelo_Proprietario_com_contadora_e_termo_assinado()
    {
        var a = tenancy.TenantA;
        var (_, hr) = await ActorAsync(a, Rh);
        var (ownerId, owner) = await ActorAsync(a, Group.OwnerSeedKey);
        var period = new DateOnly(2035, 4, 1);
        var ana = await EmployeeAsync(hr);
        var (_, calculated) = await SeedAsync(a, period, [new(ana.Id, "SAL", PayrollItemKind.Earning, 3100m, 3100m), new(ana.Id, "INSS", PayrollItemKind.Deduction, 260m, 260m)]);
        var detail = await CompareAsync(hr, period);
        var id = detail.GetProperty("run").GetProperty("id").GetGuid();
        var version = detail.GetProperty("run").GetProperty("version").GetUInt32();

        var term = await hr.GetAsync($"{Parallel}/{id}/term");
        term.StatusCode.Should().Be(HttpStatusCode.OK, await term.Content.ReadAsStringAsync());
        term.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await term.Content.ReadAsByteArrayAsync()).Take(4).Should().Equal("%PDF"u8.ToArray());

        using (var file = new MultipartFormDataContent { { new ByteArrayContent(Pdf) { Headers = { ContentType = new("application/pdf") } }, "file", "termo.pdf" } })
        {
            (await hr.PostAsync($"{Parallel}/{id}/term", file)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "só o Proprietário anexa o termo");
        }

        using (var generic = new MultipartFormDataContent { { new ByteArrayContent(Pdf) { Headers = { ContentType = new("application/pdf") } }, "file", "termo.pdf" } })
        {
            (await owner.PostAsync($"/api/v1/attachments/{PayrollParallelService.TermOwner}/{id}", generic)).IsSuccessStatusCode.Should().BeFalse("o termo só entra pela rota da conferência");
        }

        var attached = await AttachTermAsync(owner, id);
        var denied = await hr.SendAsync(Post($"{Parallel}/{id}/accept", new { accountantName = "Contadora Fictícia", accountantCrc = "SP-123456/O", signedTermAttachmentId = attached, version }));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(denied)).Should().Be(Error.Codes.Forbidden);
        await AcceptAsync(owner, id, version, Guid.CreateVersion7(), HttpStatusCode.UnprocessableEntity);
        await AcceptAsync(owner, id, version, attached, HttpStatusCode.UnprocessableEntity, crc: "CRC");

        // A calculada mudou depois da comparação: aceitar pede comparar de novo.
        await DbAsync(a, async db =>
        {
            var line = await db.PayslipLines.SingleAsync(l => l.Code == "SAL" && db.Payslips.Any(p => p.Id == l.PayslipId && p.PayrollRunId == calculated));
            db.PayslipLines.Remove(line);
            db.PayslipLines.Add(new PayslipLine { PayslipId = line.PayslipId, PayrollItemId = line.PayrollItemId, Code = "SAL", Description = "SAL", Type = PayrollItemKind.Earning, Amount = 3101m, Calculated = true });
            return await db.SaveChangesAsync();
        });
        var changed = await owner.SendAsync(Post($"{Parallel}/{id}/accept", new { accountantName = "Contadora Fictícia", accountantCrc = "SP-123456/O", signedTermAttachmentId = attached, version }));
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(changed)).Should().Be(PayrollParallelRules.ChangedCode);
        await DbAsync(a, async db =>
        {
            var line = await db.PayslipLines.SingleAsync(l => l.Code == "SAL" && db.Payslips.Any(p => p.Id == l.PayslipId && p.PayrollRunId == calculated));
            db.PayslipLines.Remove(line);
            db.PayslipLines.Add(new PayslipLine { PayslipId = line.PayslipId, PayrollItemId = line.PayrollItemId, Code = "SAL", Description = "SAL", Type = PayrollItemKind.Earning, Amount = 3100m, Calculated = true });
            return await db.SaveChangesAsync();
        });

        var accepted = await AcceptAsync(owner, id, version, attached, crc: "sp-123456/o");
        var run = accepted.GetProperty("run");
        run.GetProperty("status").GetString().Should().Be("Accepted");
        run.GetProperty("statusLabel").GetString().Should().Be("Aceita");
        run.GetProperty("accountantName").GetString().Should().Be("Contadora Fictícia");
        run.GetProperty("accountantCrc").GetString().Should().Be("SP-123456/O");
        run.GetProperty("hasSignedTerm").GetBoolean().Should().BeTrue();
        run.GetProperty("acceptedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        var stored = await DbAsync(a, db => db.PayrollParallelRuns.AsNoTracking().SingleAsync(r => r.Id == id));
        stored.AcceptedBy.Should().Be(ownerId);
        stored.SignedTermAttachmentId.Should().Be(attached);

        var again = await hr.PostAsJsonAsync($"{Parallel}/compare", new { period = "2035-04" });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(again)).Should().Be(PayrollParallelRules.AcceptedCode);
        (await OkAsync(await hr.GetAsync($"{Parallel}/history"))).EnumerateArray()
            .Should().Contain(e => e.GetProperty("action").GetString() == "Aceitou a competência" && e.GetProperty("detail").GetString() == "Contadora Contadora Fictícia, CRC SP-123456/O");
    }

    /// <summary>
    /// CA 5 a 8: 3 competências Mensais seguidas aceitas liberam a virada (antes, 409); o caso de férias aceito vira junto; dali a calculada é a
    /// oficial (FLH-02a CA 15), a decisão fica registrada e avisada, e o pacote da contadora deixa de levar o folha-calculada.csv. A volta para a
    /// lançada pede o motivo e a contagem recomeça.
    /// </summary>
    [Fact]
    public async Task Virada_depois_de_tres_competencias_seguidas_e_volta_com_motivo()
    {
        var tenant = await tenancy.ProvisionAsync("flh06-virada", "Virada Paralela Ltda", "vp.localhost", Factory.Services);
        var (_, hr) = await ActorAsync(tenant, Rh);
        var (ownerId, owner) = await ActorAsync(tenant, Group.OwnerSeedKey);
        var ana = await EmployeeAsync(hr);
        var months = new[] { new DateOnly(2026, 6, 1), new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1) };
        foreach (var month in months)
        {
            await SeedAsync(tenant, month, [new(ana.Id, "SAL", PayrollItemKind.Earning, 3000m, 3000m), new(ana.Id, "INSS", PayrollItemKind.Deduction, 254.97m, 254.97m)]);
        }

        // Férias: um caso conferido (aceito antes da virada, vira junto com a Mensal).
        await SeedAsync(tenant, months[1], [new(ana.Id, "SAL", PayrollItemKind.Earning, 4000m, 4000m)], PayrollRunType.Vacation);
        var (_, september) = await SeedAsync(tenant, new DateOnly(2026, 9, 1), [new(ana.Id, "SAL", PayrollItemKind.Earning, 3000m, 3000m)]);

        // CA 8: em conferência, o pacote leva o folha-calculada.csv no layout da lançada.
        var exportView = await OkAsync(await hr.GetAsync("/api/v1/hr/accountant-exports?period=2026-08"));
        exportView.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("key").GetString() == AccountantCalculatedPayrollSection.SectionKey).GetProperty("included").GetBoolean().Should().BeTrue();
        var package = await hr.GetAsync("/api/v1/hr/accountant-exports/package?period=2026-08");
        package.StatusCode.Should().Be(HttpStatusCode.OK, await package.Content.ReadAsStringAsync());
        using (var zip = new ZipArchive(new MemoryStream(await package.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read))
        {
            var entry = zip.GetEntry(AccountantCalculatedPayrollSection.FileName);
            entry.Should().NotBeNull();
            await using var stream = entry!.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            (await reader.ReadToEndAsync()).Should().Contain("254,97");
        }

        foreach (var (month, type) in months.Select(m => (m, "Monthly")).Append((months[1], "Vacation")).ToList())
        {
            var detail = await CompareAsync(hr, month, type);
            var id = detail.GetProperty("run").GetProperty("id").GetGuid();
            var term = await AttachTermAsync(owner, id);
            if (month == months[2] && type == "Monthly")
            {
                var early = await owner.SendAsync(Post($"{Parallel}/switch", new { confirmed = true }));
                early.StatusCode.Should().Be(HttpStatusCode.Conflict, "com 2 de 3 a virada fica bloqueada");
                (await CodeAsync(early)).Should().Be(PayrollParallelRules.NotEnoughMonthsCode);
            }

            await AcceptAsync(owner, id, detail.GetProperty("run").GetProperty("version").GetUInt32(), term);
        }

        var overview = await OkAsync(await hr.GetAsync(Parallel));
        overview.GetProperty("consecutive").GetArrayLength().Should().Be(3);
        overview.GetProperty("canSwitch").GetBoolean().Should().BeTrue();
        overview.GetProperty("switchFrom").GetString().Should().Be("2026-09-01");
        overview.GetProperty("modes").EnumerateArray().Single(m => m.GetProperty("type").GetString() == "Vacation").GetProperty("situation").GetString()
            .Should().Be("Caso conferido em julho de 2026: vira junto com a Mensal");
        overview.GetProperty("modes").EnumerateArray().Single(m => m.GetProperty("type").GetString() == "Termination").GetProperty("situation").GetString().Should().Be("Aguardando um caso conferido");

        (await hr.SendAsync(Post($"{Parallel}/switch", new { confirmed = true }))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "virar é do Proprietário");
        (await owner.SendAsync(Post($"{Parallel}/switch", new { confirmed = false }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a virada pede a confirmação explícita");
        var switched = await OkAsync(await owner.SendAsync(Post($"{Parallel}/switch", new { confirmed = true })));
        switched.GetProperty("message").GetString().Should().Be("Folha calculada é a oficial a partir de setembro de 2026 (Mensal, Férias).");
        var modes = switched.GetProperty("overview").GetProperty("modes").EnumerateArray().ToDictionary(m => m.GetProperty("type").GetString()!);
        modes["Monthly"].GetProperty("mode").GetString().Should().Be("Calculated");
        modes["Monthly"].GetProperty("fromPeriod").GetString().Should().Be("2026-09-01");
        modes["Vacation"].GetProperty("mode").GetString().Should().Be("Calculated");
        modes["Thirteenth"].GetProperty("mode").GetString().Should().Be("Entered");
        modes["Monthly"].GetProperty("situation").GetString().Should().StartWith("Oficial desde ");
        (await owner.SendAsync(Post($"{Parallel}/switch", new { confirmed = true }))).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // FLH-02a CA 15 liberado só a partir da competência seguinte; a calculada em rascunho deixa de ser simulação.
        await InTenantAsync(tenant, async sp =>
        {
            var parameters = sp.GetRequiredService<HrParametersService>();
            (await parameters.CalculatedOfficialAsync(PayrollRunType.Monthly, new DateOnly(2026, 9, 1))).Should().BeTrue();
            (await parameters.CalculatedOfficialAsync(PayrollRunType.Monthly, new DateOnly(2026, 8, 1))).Should().BeFalse("as competências conferidas continuam pela lançada");
            (await parameters.CalculatedOfficialAsync(PayrollRunType.Thirteenth2, new DateOnly(2026, 12, 1))).Should().BeFalse("o 13º espera um caso conferido");
            var db = sp.GetRequiredService<TenantDbContext>();
            (await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == september)).IsSimulation.Should().BeFalse();
            var calculation = sp.GetRequiredService<PayrollCalculationService>();
            var august = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Period == months[2] && r.Type == PayrollRunType.Monthly && r.Mode == PayrollRunMode.Calculated);
            (await calculation.CloseErrorAsync(august))!.Code.Should().Be(PayrollCalculationRules.SimulationCode);
            (await calculation.CloseErrorAsync(await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == september)))?.Code.Should().NotBe(PayrollCalculationRules.SimulationCode);
            var changes = await db.PayrollModeChanges.AsNoTracking().Where(c => c.Mode == PayrollRunMode.Calculated).ToListAsync();
            changes.Should().HaveCount(2);
            changes.Single(c => c.RunType == PayrollParallelType.Monthly).ParallelRunIds.Should().HaveCount(3);
            changes.Should().OnlyContain(c => c.ChangedBy == ownerId && c.AccountantName == "Contadora Fictícia");
            (await db.Notifications.AsNoTracking().AnyAsync(n => n.UserId == ownerId && n.Title.StartsWith("Folha calculada é a oficial"))).Should().BeTrue("os membros do Proprietário são avisados");
            var outbox = await db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains("PayrollModeChanged")).Select(m => m.Payload).ToListAsync();
            outbox.Should().HaveCount(2).And.OnlyContain(p => !p.Contains("3000") && !p.Contains("254"), "o evento não leva valor");
            return 0;
        });

        // Depois da virada, a setembro (oficial) não vai mais como folha-calculada.csv.
        (await OkAsync(await hr.GetAsync("/api/v1/hr/accountant-exports?period=2026-09"))).GetProperty("sections").EnumerateArray()
            .Single(s => s.GetProperty("key").GetString() == AccountantCalculatedPayrollSection.SectionKey).GetProperty("included").GetBoolean().Should().BeFalse();

        // CA 7: a volta pede o motivo; por tipo e depois para todos; a contagem recomeça.
        (await owner.SendAsync(Post($"{Parallel}/revert", new { type = "Vacation", reason = "curto" }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await hr.SendAsync(Post($"{Parallel}/revert", new { type = (string?)null, reason = "Divergência grave apontada pela contadora" }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var vacation = await OkAsync(await owner.SendAsync(Post($"{Parallel}/revert", new { type = "Vacation", reason = "As férias com abono pecuniário divergiram" })));
        vacation.GetProperty("message").GetString().Should().Be("Folha lançada é a oficial para Férias.");
        var all = await OkAsync(await owner.SendAsync(Post($"{Parallel}/revert", new { type = (string?)null, reason = "Divergência grave apontada pela contadora em outubro" })));
        all.GetProperty("message").GetString().Should().Be("Folha lançada é a oficial.");
        var after = all.GetProperty("overview");
        after.GetProperty("modes").EnumerateArray().Should().OnlyContain(m => m.GetProperty("mode").GetString() == "Entered");
        after.GetProperty("consecutive").GetArrayLength().Should().Be(0, "depois da volta, a contagem das seguidas recomeça");
        after.GetProperty("canSwitch").GetBoolean().Should().BeFalse();
        await InTenantAsync(tenant, async sp =>
        {
            (await sp.GetRequiredService<HrParametersService>().CalculatedOfficialAsync(PayrollRunType.Monthly, new DateOnly(2026, 10, 1))).Should().BeFalse();
            (await sp.GetRequiredService<TenantDbContext>().PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == september)).IsSimulation.Should().BeTrue();
            return 0;
        });
        var none = await owner.SendAsync(Post($"{Parallel}/revert", new { type = (string?)null, reason = "Mais uma vez, sem nada calculado" }));
        none.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(none)).Should().Be(PayrollParallelRules.NotCalculatedCode);
    }
}
