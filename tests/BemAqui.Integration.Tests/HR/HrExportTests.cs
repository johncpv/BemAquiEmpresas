using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// EXP-05 CA 11 a 15: exportação do RH com a máscara da tela (CPF 3+2 e <c>***</c> sem <c>hr.view_sensitive</c>, sem 2FA), o registro
/// <c>sensitive</c> na auditoria, os holerites da folha só com a ação e a prévia do 13º em PDF com "Simulação". Tenant próprio (a prévia do
/// 13º pega todos os funcionários ativos do ano).
/// </summary>
public class HrExportTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Hr = "/api/v1/hr";
    private const string Grants = "hr:view,create,edit,view_sensitive";

    private async Task<T> InTenantAsync<T>(Tenant tenant, Func<IServiceProvider, Task<T>> action)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    private async Task<(Guid Id, HttpClient Client)> ActorAsync(Tenant tenant, string grants)
    {
        var host = tenant.Domains.First().Host;
        var (id, _) = await tenancy.SeedUserAsync(tenant, "x" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        var stamp = await InTenantAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "RH exp " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
            db.Groups.Add(group);
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "hr", Enabled = true });
            foreach (var action in grants[3..].Split(','))
            {
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "hr", Action = action });
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            await db.SaveChangesAsync();
            await sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
            return (await db.Users.SingleAsync(u => u.Id == id)).SecurityStamp;
        });
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
        return (id, client);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<IXLWorksheet> SheetAsync(HttpResponseMessage response, string name)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        return workbook.Worksheet(name);
    }

    private static IXLRow Row(IXLWorksheet sheet, string registration) => sheet.RowsUsed().Single(r => r.Cell(1).GetString() == registration);

    private static List<string> Headers(IXLWorksheet sheet) => sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();

    private static string Cpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    [Fact]
    public async Task Funcionarios_cargos_e_holerites_saem_mascarados_sem_hr_view_sensitive_e_a_previa_do_13_sai_como_simulacao()
    {
        var tenant = await tenancy.ProvisionAsync("exp05-hr", "Exporta RH Ltda", "exp05hr.localhost", tenancy.Factory.Services);
        var (hrId, hr) = await ActorAsync(tenant, Grants);
        var (viewerId, viewer) = await ActorAsync(tenant, "hr:view");
        var schedule = (await OkAsync(await hr.GetAsync($"{Hr}/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var position = (await OkAsync(await hr.PostAsJsonAsync($"{Hr}/positions", new { name = "Montador exp", cbo = "723310", defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        (await hr.PutAsJsonAsync($"{Hr}/positions/{position}/salary", new { salaryMin = 2800m, salaryMax = 3600m, referenceSalary = 3200m })).EnsureSuccessStatusCode();
        var cpf = Cpf();
        const string registration = "EXP0501";
        var name = "Fictício Exporta " + Guid.NewGuid().ToString("N")[..4];
        await OkAsync(await hr.PostAsJsonAsync($"{Hr}/employees", new
        {
            name, cpf, birthDate = "1990-05-10", admissionDate = "2026-01-05", positionId = position, workScheduleId = (Guid?)null, baseSalary = 3000m, registration,
        }), HttpStatusCode.Created);

        // CA 12: sem hr.view_sensitive, CPF 3+2 (como o holerite) e salário "***"; o restante igual à tela.
        var masked = await SheetAsync(await viewer.GetAsync($"{Hr}/employees/export?format=xlsx"), "Funcionários");
        Headers(masked).Should().Equal("Matrícula", "Nome", "CPF", "Cargo", "Admissão", "Situação", "Jornada", "Sindicato", "Salário");
        var row = Row(masked, registration);
        row.Cell(2).GetString().Should().Be(name);
        row.Cell(3).GetString().Should().Be($"{cpf[..3]}.***.***-{cpf[9..]}");
        row.Cell(6).GetString().Should().Be("Ativo");
        row.Cell(9).GetString().Should().Be("***");

        var full = Row(await SheetAsync(await hr.GetAsync($"{Hr}/employees/export?format=xlsx"), "Funcionários"), registration);
        full.Cell(3).GetString().Should().Be(BrazilianDocuments.Format(cpf));
        full.Cell(9).GetValue<decimal>().Should().Be(3000m);

        // CA 15: salário base e custo-hora mascarados sem a ação.
        var positions = await SheetAsync(await viewer.GetAsync($"{Hr}/positions/export?format=xlsx"), "Cargos");
        var montador = positions.RowsUsed().Single(r => r.Cell(1).GetString() == "Montador exp");
        montador.Cell(2).GetString().Should().Be("7233-10");
        montador.Cell(6).GetString().Should().Be("***");
        montador.Cell(7).GetString().Should().Be("***");
        Row(await SheetAsync(await hr.GetAsync($"{Hr}/positions/export?format=xlsx"), "Cargos"), "Montador exp").Cell(6).GetValue<decimal>().Should().Be(3200m);

        // A auditoria marca "sensitive" só quando o arquivo levou CPF ou salário sem máscara.
        var exports = await InTenantAsync(tenant, sp => sp.GetRequiredService<TenantDbContext>().AuditEntries.AsNoTracking()
            .Where(a => a.Entity == "Employee" && a.EntityId == "list" && a.Action == AuditActions.Exported).ToListAsync());
        exports.Single(a => a.UserId == viewerId).Changes.Should().NotContain("\"sensitive\"");
        exports.Single(a => a.UserId == hrId).Changes.Should().Contain("\"sensitive\"");

        // CA 13: holerites da folha só com hr.view_sensitive; a lista das folhas com hr.view e os totais mascarados.
        var create = new HttpRequestMessage(HttpMethod.Post, $"{Hr}/payroll-runs") { Content = JsonContent.Create(new { period = "2026-10", type = "Monthly", employeeIds = (Guid[]?)null }) };
        create.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var run = (await OkAsync(await hr.SendAsync(create), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        (await viewer.GetAsync($"{Hr}/payroll-runs/{run}/payslips/sensitive/export?format=xlsx")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var payslipsResponse = await hr.GetAsync($"{Hr}/payroll-runs/{run}/payslips/sensitive/export?format=xlsx");
        payslipsResponse.Content.Headers.ContentDisposition!.FileName.Should().StartWith("folha-2026-10-mensal-").And.NotContain(name).And.NotContain(cpf);
        var payslips = await SheetAsync(payslipsResponse, "Holerites");
        Headers(payslips).Should().Equal("Matrícula", "Nome", "Cargo", "Proventos", "Descontos", "Líquido", "FGTS", "Situação");
        Row(payslips, registration).Cell(3).GetString().Should().StartWith("Montador exp");
        var lines = payslips.RowsUsed().Skip(1).ToList();
        lines[^1].Cell(2).GetString().Should().Be("Total");
        lines[^1].Cell(6).GetValue<decimal>().Should().Be(lines.SkipLast(1).Sum(r => r.Cell(6).GetValue<decimal>()));
        Row(await SheetAsync(await viewer.GetAsync($"{Hr}/payroll-runs/export?format=xlsx"), "Folhas"), "10/2026").Cell(8).GetString().Should().Be("***");

        // CA 11: prévia do 13º em PDF, sempre "Simulação", sem nome nem CPF no arquivo, no-store; sem a ação, 403.
        const string preview = $"{Hr}/payroll-runs/thirteenth-preview/sensitive/pdf?year=2026&type=Thirteenth1";
        (await viewer.GetAsync(preview)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var pdf = await hr.GetAsync(preview);
        pdf.StatusCode.Should().Be(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync());
        pdf.Content.Headers.ContentDisposition!.FileName.Should().Be("previa-13-2026-1a-parcela.pdf");
        pdf.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var document = PdfDocument.Open(await pdf.Content.ReadAsByteArrayAsync());
        var text = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), @"\s+", string.Empty);
        text.Should().Contain("SIMULAÇÃO").And.Contain("Prévia do 13º".Replace(" ", string.Empty, StringComparison.Ordinal)).And.Contain(registration)
            .And.Contain(name.Replace(" ", string.Empty, StringComparison.Ordinal)).And.Contain("Valordaparcela").And.Contain("Total");
        text.Should().NotContain("Semvalores:", "o pacote de tabelas e as rubricas do 13º vêm no provisionamento").And.NotContain(cpf);
    }
}
