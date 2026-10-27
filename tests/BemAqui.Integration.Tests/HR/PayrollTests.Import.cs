using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.HR;

/// <summary>RH-02a CA 8 a 16: importação do CSV v1, conferência, corte de valores, canário, folha fechada e as três camadas.</summary>
public partial class PayrollTests
{
    private const string Canary = "98765.43";
    private const string CanaryBr = "98.765,43";

    [Fact]
    public async Task Importacao_mostra_a_previa_com_erros_por_linha_e_substitui_so_os_presentes()
    {
        var sink = new MemorySink();
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)));
        var (_, ownerClient) = await ActorAsync(Group.OwnerSeedKey);
        var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Host = Host;
        owner.DefaultRequestHeaders.Authorization = ownerClient.DefaultRequestHeaders.Authorization;
        var position = await PositionAsync(owner);
        var (ana, anaCpf, _) = await EmployeeAsync(owner, position, "2035-01-02", name: "Ana Importada");
        var (bia, _, biaRegistration) = await EmployeeAsync(owner, position, "2035-01-02", name: "Bia Importada");
        var (caio, _, _) = await EmployeeAsync(owner, position, "2035-01-02", name: "Caio Ausente");
        var items = await ItemIdsAsync(owner);
        var run = await RunAsync(owner, "2035-01", "Vacation", [ana, bia, caio]);
        var caioSlip = await PayslipRowAsync(owner, run, caio);
        (await owner.PutAsJsonAsync($"{Runs}/{run}/payslips/{caioSlip.GetProperty("id").GetGuid()}", Slip(caioSlip.GetProperty("version").GetUInt32(), [Line(items["FERIAS"], 1500m)]))).EnsureSuccessStatusCode();
        var stranger = NewCpf();

        var bad = "matricula_ou_cpf;codigo_rubrica;referencia;valor\n"
            + $"{anaCpf};HE75;;100,00\n"
            + $"{stranger};FERIAS;;100,00\n"
            + $"{biaRegistration};FERIAS;;=1+1\n"
            + $"{biaRegistration};FERIAS;30 dias;2.000,00\n";
        var preview = await owner.PostAsync($"{Runs}/{run}/import/preview", File(bad));
        preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var previewText = await preview.Content.ReadAsStringAsync();
        var body = await JsonAsync(preview);
        body.GetProperty("canImport").GetBoolean().Should().BeFalse("só com zero erro (CA 8)");
        body.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).Should().Equal(
            "Linha 2: rubrica 'HE75' não existe.",
            "Linha 3: CPF não pertence a funcionário ativo.",
            "Linha 4: valor inválido. Use número maior que zero com vírgula, como 1.234,56.");
        body.GetProperty("errorCount").GetInt32().Should().Be(3);
        previewText.Should().NotContain(stranger, "o CPF do arquivo não volta na mensagem (CA 10)").And.NotContain(anaCpf);
        (await owner.PostAsync($"{Runs}/{run}/import", File(bad))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "com erro não importa");

        var good = "matricula_ou_cpf;codigo_rubrica;referencia;valor\r\n"
            + $"{anaCpf[..3]}.{anaCpf[3..6]}.{anaCpf[6..9]}-{anaCpf[9..]};FERIAS;30 dias;3.000,00\r\n"
            + $"{anaCpf};FER13;;1.000,00\r\n"
            + $"{anaCpf};INSS;;{CanaryBr}\r\n"
            + $"{anaCpf};FERIAS;;{CanaryBr}\r\n"
            + $"{biaRegistration.ToLowerInvariant()};FERIAS;;2.000,00\r\n";
        var ok = await JsonAsync(await owner.PostAsync($"{Runs}/{run}/import/preview", File(good)));
        ok.GetProperty("canImport").GetBoolean().Should().BeTrue();
        ok.GetProperty("employees").GetInt32().Should().Be(2);
        ok.GetProperty("lines").GetInt32().Should().Be(5);
        ok.GetProperty("gross").GetDecimal().Should().Be(3000m + 1000m + 98765.43m + 2000m);
        ok.GetProperty("byItem").EnumerateArray().Single(i => i.GetProperty("code").GetString() == "FERIAS").GetProperty("lines").GetInt32().Should().Be(3);
        (await PayslipRowAsync(owner, run, ana)).GetProperty("lineCount").GetInt32().Should().Be(0, "a prévia não grava");

        var imported = await owner.PostAsync($"{Runs}/{run}/import", File(good));
        imported.StatusCode.Should().Be(HttpStatusCode.OK, await imported.Content.ReadAsStringAsync());
        (await JsonAsync(imported)).GetProperty("employees").GetInt32().Should().Be(2);
        var anaRow = await PayslipRowAsync(owner, run, ana);
        anaRow.GetProperty("net").GetDecimal().Should().Be(4000m, "proventos 102.765,43 − desconto 98.765,43");
        (await PayslipRowAsync(owner, run, bia)).GetProperty("net").GetDecimal().Should().Be(2000m);
        (await PayslipRowAsync(owner, run, caio)).GetProperty("net").GetDecimal().Should().Be(1500m, "ausente do arquivo fica como está (CA 9)");

        // Substitui de novo: as linhas da Ana são trocadas, não somadas.
        (await owner.PostAsync($"{Runs}/{run}/import", File($"{anaCpf};FERIAS;;10,00\n"))).EnsureSuccessStatusCode();
        (await PayslipRowAsync(owner, run, ana)).GetProperty("lineCount").GetInt32().Should().Be(1);

        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>();
        var summary = await db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(PayrollRun) && a.EntityId == run.ToString() && a.Action == AuditActions.Imported).ToListAsync();
        summary.Should().HaveCount(2, "o resumo de cada importação fica na auditoria (CA 9)");
        summary.Should().OnlyContain(a => a.Changes.Contains("csv-v1", StringComparison.Ordinal) && !a.Changes.Contains(anaCpf, StringComparison.Ordinal));
        var audit = string.Join("\n", await db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(PayslipLine) || a.Entity == nameof(Payslip)).Select(a => a.Changes).ToListAsync());
        audit.Should().NotContain(Canary).And.NotContain("98765,43", "valores mascarados na auditoria (CA 13)");
        var outbox = string.Join("\n", (await db.OutboxMessages.AsNoTracking().OrderByDescending(m => m.OccurredAt).Take(50).ToListAsync()).Select(m => m.Payload));
        outbox.Should().NotContain(Canary).And.NotContain(anaCpf);
        string events;
        lock (sink.Events)
        {
            events = string.Join("\n", sink.Events.Select(e => e.RenderMessage(CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString()))));
        }

        events.Should().NotContain(Canary).And.NotContain(CanaryBr).And.NotContain(anaCpf).And.NotContain(stranger, "nem valor nem CPF no log (CA 10 e 13)");
    }

    [Fact]
    public async Task Importacao_recusa_arquivo_grande_e_com_linhas_demais()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var (employee, _, _) = await EmployeeAsync(owner, position, "2035-03-01");
        var run = await RunAsync(owner, "2035-03", "Vacation", [employee]);
        var large = await owner.PostAsync($"{Runs}/{run}/import/preview", File(new string('1', PayrollCsv.MaxBytes + 10)));
        large.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "limite de 2 MB (CA 10)");
        var many = string.Join('\n', Enumerable.Range(1, PayrollCsv.MaxLines + 5).Select(i => $"{i};SAL;;1,00"));
        var lines = await JsonAsync(await owner.PostAsync($"{Runs}/{run}/import/preview", File(many)));
        lines.GetProperty("errors").EnumerateArray().Single().GetString().Should().Contain("5 mil linhas");
        lines.GetProperty("canImport").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Conferencia_com_drill_down_variacao_novos_e_conta_alterada()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var (veteran, _, _) = await EmployeeAsync(owner, position, "2036-01-02", name: "Vera Veterana");
        var (rookie, _, _) = await EmployeeAsync(owner, position, "2036-02-02", name: "Rui Novato");
        var (empty, _, _) = await EmployeeAsync(owner, position, "2036-02-02", name: "Eva Vazia");
        var items = await ItemIdsAsync(owner);
        var january = await RunAsync(owner, "2036-01", "Vacation", [veteran]);
        var jan = await PayslipRowAsync(owner, january, veteran);
        (await owner.PutAsJsonAsync($"{Runs}/{january}/payslips/{jan.GetProperty("id").GetGuid()}", Slip(jan.GetProperty("version").GetUInt32(), [Line(items["FERIAS"], 1000m)]))).EnsureSuccessStatusCode();

        var run = await RunAsync(owner, "2036-02", "Vacation", [veteran, rookie, empty]);
        foreach (var (employee, amount) in new[] { (veteran, 1200m), (rookie, 800m) })
        {
            var slip = await PayslipRowAsync(owner, run, employee);
            (await owner.PutAsJsonAsync($"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}", Slip(slip.GetProperty("version").GetUInt32(), [Line(items["FERIAS"], amount), Line(items["INSS"], 50m)]))).EnsureSuccessStatusCode();
        }

        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IHrDb>();
            var e = await db.Employees.SingleAsync(x => x.Id == veteran);
            e.BankAccountVersion = 2;
            e.BankChangedAt = DateTimeOffset.UtcNow;
            e.BankQuarantineUntil = DateTimeOffset.UtcNow.AddDays(20);
            await db.SaveChangesAsync();
        }

        var response = await owner.GetAsync($"{Runs}/{run}/review/sensitive");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var review = await JsonAsync(response);
        review.GetProperty("payslipCount").GetInt32().Should().Be(3);
        review.GetProperty("gross").GetDecimal().Should().Be(2000m);
        review.GetProperty("net").GetDecimal().Should().Be(1900m);
        foreach (var item in review.GetProperty("byItem").EnumerateArray())
        {
            item.GetProperty("payslips").EnumerateArray().Sum(p => p.GetProperty("amount").GetDecimal()).Should().Be(item.GetProperty("total").GetDecimal(), "o drill-down tem o mesmo total (padrão 3)");
        }

        review.GetProperty("byItem").EnumerateArray().Single(i => i.GetProperty("code").GetString() == "FERIAS").GetProperty("total").GetDecimal().Should().Be(2000m);
        var vera = review.GetProperty("variations").EnumerateArray().Single(v => v.GetProperty("employeeId").GetGuid() == veteran);
        vera.GetProperty("previousNet").GetDecimal().Should().Be(1000m);
        vera.GetProperty("variationPct").GetDecimal().Should().Be(15m);
        vera.GetProperty("alert").GetBoolean().Should().BeTrue("acima de 10% (CA 3)");
        review.GetProperty("empty").EnumerateArray().Select(e => e.GetProperty("employeeId").GetGuid()).Should().Equal(empty);
        review.GetProperty("newcomers").EnumerateArray().Select(e => e.GetProperty("employeeId").GetGuid()).Should().BeEquivalentTo([rookie, empty], "primeira folha do funcionário");
        review.GetProperty("accountChanged").EnumerateArray().Select(e => e.GetProperty("employeeId").GetGuid()).Should().Equal(veteran);
        (await AuditCountAsync(nameof(PayrollRun) + ".Review", run.ToString(), AuditActions.SensitiveRead)).Should().Be(1);
    }

    [Fact]
    public async Task So_hr_view_ve_o_resumo_sem_nenhum_valor_no_JSON()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("hr:view");
        var position = await PositionAsync(owner);
        var (employee, _, _) = await EmployeeAsync(owner, position, "2036-05-01", 7777.77m);
        var run = await RunAsync(owner, "2036-05");
        var slip = await PayslipRowAsync(owner, run, employee);

        // Sem ids nem instantes: um GUID pode conter "7777" por acaso.
        static string NoIds(string json) => System.Text.RegularExpressions.Regex.Replace(json, @"[0-9a-f]{8}-[0-9a-f-]{27}|\d{4}-\d{2}-\d{2}T[0-9:.+Z-]+", "#");
        var list = NoIds(await viewer.GetStringAsync(Runs));
        var one = NoIds(await viewer.GetStringAsync($"{Runs}/{run}"));
        foreach (var json in new[] { list, one })
        {
            json.Should().NotContain("7777").And.NotContain("\"net\"").And.NotContain("\"gross\"").And.NotContain("\"amount\"").And.NotContain("Base\"", "só competência, tipo, situação e holerites (CA 12)");
        }

        one.Should().Contain("\"payslipCount\":");
        foreach (var url in new[] { $"{Runs}/sensitive", $"{Runs}/{run}/payslips/sensitive", $"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}/sensitive", $"{Runs}/{run}/review/sensitive", $"{Runs}/{run}/charges/sensitive" })
        {
            (await viewer.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
        }

        var detail = await owner.GetAsync($"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}/sensitive");
        detail.Headers.CacheControl!.NoStore.Should().BeTrue("no-store (CA 12)");
        (await AuditCountAsync(nameof(Payslip), slip.GetProperty("id").GetGuid().ToString(), AuditActions.SensitiveRead)).Should().Be(1, "a leitura do holerite registra SensitiveRead");
        var totals = (await JsonAsync(await owner.GetAsync($"{Runs}/sensitive"))).EnumerateArray().Single(t => t.GetProperty("runId").GetGuid() == run);
        totals.GetProperty("net").GetDecimal().Should().BeGreaterThanOrEqualTo(7777.77m, "com hr.view_sensitive, o líquido total na lista");
    }

    [Fact]
    public async Task Folha_fechada_nao_muda_linha_guia_nem_importacao()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var (employee, cpf, _) = await EmployeeAsync(owner, position, "2036-07-01");
        var items = await ItemIdsAsync(owner);
        var run = await RunAsync(owner, "2036-07", "Vacation", [employee]);
        var slip = await PayslipRowAsync(owner, run, employee);
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IHrDb>();
            var entity = await db.PayrollRuns.SingleAsync(r => r.Id == run);
            entity.Status = PayrollRunStatus.Closed;
            entity.ClosedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var responses = new[]
        {
            await owner.PutAsJsonAsync($"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}", Slip(slip.GetProperty("version").GetUInt32(), [Line(items["FERIAS"], 10m)])),
            await owner.PostAsJsonAsync($"{Runs}/{run}/charges", new { kind = "Fgts", description = (string?)null, amount = 10m, dueDate = "2036-08-20", payeePartyId = (Guid?)null }),
            await owner.PostAsync($"{Runs}/{run}/import/preview", File($"{cpf};FERIAS;;10,00")),
            await owner.PostAsync($"{Runs}/{run}/import", File($"{cpf};FERIAS;;10,00")),
        };
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var problem = await JsonAsync(response);
            problem.GetProperty("code").GetString().Should().Be(PayrollRules.ClosedCode);
            problem.GetProperty("detail").GetString().Should().Be("A folha de julho de 2036 está fechada.");
        }

        (await JsonAsync(await owner.GetAsync($"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}/sensitive"))).GetProperty("editable").GetBoolean().Should().BeFalse("modo leitura");
    }

    [Fact]
    public async Task Tres_camadas_e_view_sensitive_nos_comandos_da_folha()
    {
        var (_, nobody) = await ActorAsync();
        var noGroup = await nobody.GetAsync(Runs);
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(noGroup)).Should().Be("forbidden");

        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var (employee, _, _) = await EmployeeAsync(owner, position, "2037-01-01");
        var items = await ItemIdsAsync(owner);
        var (_, viewer) = await ActorAsync("hr:view");
        (await CreateRunAsync(viewer, "2037-01", "Vacation", [employee])).StatusCode.Should().Be(HttpStatusCode.Forbidden, "criar exige hr.create");
        var (_, creator) = await ActorAsync("hr:view,create");
        (await CreateRunAsync(creator, "2037-01", "Vacation", [employee])).StatusCode.Should().Be(HttpStatusCode.Forbidden, "criar exige também hr.view_sensitive");
        var (_, sensitiveCreator) = await ActorAsync("hr:view,create,view_sensitive");
        var run = await RunAsync(sensitiveCreator, "2037-01", "Vacation", [employee]);
        var slip = await PayslipRowAsync(owner, run, employee);
        var url = $"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}";
        var (_, editor) = await ActorAsync("hr:view,edit");
        (await editor.PutAsJsonAsync(url, Slip(slip.GetProperty("version").GetUInt32(), [Line(items["FERIAS"], 10m)]))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "editar exige hr.view_sensitive");
        (await editor.PostAsync($"{Runs}/{run}/import/preview", File("1;FERIAS;;1,00"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "importar exige hr.view_sensitive");
        var (_, payroll) = await ActorAsync("hr:view,edit,view_sensitive");
        (await payroll.PutAsJsonAsync(url, Slip(slip.GetProperty("version").GetUInt32(), [Line(items["FERIAS"], 10m)]))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await payroll.PutAsJsonAsync($"{Runs}/{Guid.NewGuid()}/payslips/{slip.GetProperty("id").GetGuid()}", Slip(0, []))).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR");

        await SetHrReleasedAsync(false);
        try
        {
            var notReleased = await owner.GetAsync(Runs);
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetHrReleasedAsync(true);
        }
    }
}
