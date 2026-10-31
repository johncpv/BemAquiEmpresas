using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.HR;

public partial class PayslipDocumentTests
{
    /// <summary>
    /// CA 5 e 6: "Meus holerites" pelo vínculo, sem grupo; só folhas fechadas e pagas, do mais recente ao mais antigo; holerite de outro
    /// funcionário ou de rascunho 404 (IDOR); no-store; "Visto em"; SensitiveRead; employeeId na query recusado; sem vínculo ou desligado,
    /// 403 employee_not_active; módulo não liberado, 403 module_not_released.
    /// </summary>
    [Fact]
    public async Task Meus_holerites_so_do_funcionario_da_sessao_e_das_folhas_fechadas()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner.Client);
        var ana = await EmployeeAsync(owner.Client, position);
        var bia = await EmployeeAsync(owner.Client, position);
        var older = await RunAsync(owner.Client, "2032-01", [ana.Id, bia.Id]);
        var newer = await RunAsync(owner.Client, "2032-02", [ana.Id, bia.Id]);
        var draft = await RunAsync(owner.Client, "2032-03", [ana.Id]);
        foreach (var run in new[] { older, newer, draft })
        {
            await FillAsync(owner.Client, run, 2_000m);
        }

        await SetStatusAsync(older, PayrollRunStatus.Paid);
        await SetStatusAsync(newer, PayrollRunStatus.Closed);
        var me = await LinkedAsync(ana.Id);

        var list = await me.Client.GetAsync(Mine);
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        list.Headers.CacheControl!.NoStore.Should().BeTrue();
        var items = (await JsonAsync(list)).EnumerateArray().ToList();
        items.Select(i => i.GetProperty("period").GetString()).Should().Equal("2032-02-01", "2032-01-01");
        items.Should().OnlyContain(i => i.GetProperty("net").GetDecimal() == 2_000m && i.GetProperty("firstViewedAt").ValueKind == System.Text.Json.JsonValueKind.Null);
        (await AuditCountAsync("Payslip.Mine", ana.Id.ToString(), AuditActions.SensitiveRead)).Should().Be(1);

        var mine = items[0].GetProperty("id").GetGuid();
        var pdf = await me.Client.GetAsync($"{Mine}/{mine}/pdf");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK);
        pdf.Headers.CacheControl!.NoStore.Should().BeTrue();
        Text(await pdf.Content.ReadAsByteArrayAsync()).Should().Contain(ana.Name).And.NotContain(ana.Cpf);
        var viewed = await DbAsync(db => db.Payslips.Where(p => p.Id == mine).Select(p => p.FirstViewedAt).SingleAsync());
        viewed.Should().NotBeNull("primeira leitura grava \"Visto em\"");
        await me.Client.GetAsync($"{Mine}/{mine}/pdf");
        (await DbAsync(db => db.Payslips.Where(p => p.Id == mine).Select(p => p.FirstViewedAt).SingleAsync())).Should().Be(viewed, "só a primeira");
        (await AuditCountAsync("Payslip", mine.ToString(), AuditActions.SensitiveRead)).Should().Be(2);

        // IDOR: o holerite da Bia e o rascunho da Ana respondem 404; employeeId na query, 400.
        var biaSlip = (await SlipsAsync(owner.Client, newer)).Single(s => s.GetProperty("employeeId").GetGuid() == bia.Id).GetProperty("id").GetGuid();
        var draftSlip = (await SlipsAsync(owner.Client, draft))[0].GetProperty("id").GetGuid();
        (await me.Client.GetAsync($"{Mine}/{biaSlip}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await me.Client.GetAsync($"{Mine}/{draftSlip}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound, "rascunho não aparece");
        (await me.Client.GetAsync($"{Mine}?employeeId={bia.Id}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Sem vínculo: 403 employee_not_active, com a mensagem. Desligado: o mesmo.
        var stranger = await ActorAsync(null);
        var refused = await stranger.Client.GetAsync(Mine);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await JsonAsync(refused);
        problem.GetProperty("code").GetString().Should().Be(PayslipDocumentRules.EmployeeNotActiveCode);
        problem.GetProperty("detail").GetString().Should().Be("Seu usuário não está ligado a um funcionário ativo. Fale com o RH.");
        (await stranger.Client.GetAsync("/api/v1/me/hr/link")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await me.Client.GetAsync("/api/v1/me/hr/link")).StatusCode.Should().Be(HttpStatusCode.OK);

        await DbAsync(async db =>
        {
            var e = await db.Employees.SingleAsync(x => x.Id == ana.Id);
            e.Status = EmployeeStatus.Terminated;
            e.TerminationDate = new DateOnly(2032, 4, 30);
            return await db.SaveChangesAsync();
        });
        (await CodeAsync(await me.Client.GetAsync(Mine))).Should().Be(PayslipDocumentRules.EmployeeNotActiveCode, "desligado: o RH entrega o PDF");

        await SetHrReleasedAsync(false);
        try
        {
            (await CodeAsync(await me.Client.GetAsync(Mine))).Should().Be("module_not_released");
        }
        finally
        {
            await SetHrReleasedAsync(true);
        }
    }

    /// <summary>CA 5 e 6: o afastado continua vendo os holerites (continua recebendo).</summary>
    [Fact]
    public async Task Afastado_tambem_entra_em_Meus_holerites()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var ana = await EmployeeAsync(owner.Client, await PositionAsync(owner.Client));
        static string Iso(int days) => BemAqui.BuildingBlocks.Time.BusinessTime.Today(TimeProvider.System).AddDays(days).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var version = (await JsonAsync(await owner.Client.GetAsync($"/api/v1/hr/employees/{ana.Id}"))).GetProperty("version").GetUInt32();
        var leave = await owner.Client.PostAsJsonAsync($"/api/v1/hr/employees/{ana.Id}/leaves",
            new { type = "MedicalCertificate", startDate = Iso(-1), expectedEndDate = Iso(10), reason = "Repouso", version });
        leave.IsSuccessStatusCode.Should().BeTrue(await leave.Content.ReadAsStringAsync());
        var me = await LinkedAsync(ana.Id);

        var link = await JsonAsync(await me.Client.GetAsync("/api/v1/me/hr/link"));
        link.GetProperty("situation").GetString().Should().Be("OnLeave");
        (await me.Client.GetAsync(Mine)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
