using System.Net;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

public partial class PayslipDocumentTests
{
    /// <summary>
    /// EXP-04 CA 18: o bloco "Holerites" da ficha lê os últimos holerites do funcionário em folhas de qualquer situação (o rascunho também),
    /// do mais recente ao mais antigo, com <c>?take</c>, <c>no-store</c> e a leitura na auditoria; sem <c>hr.view_sensitive</c>, 403; funcionário
    /// inexistente, 404.
    /// </summary>
    [Fact]
    public async Task Holerites_da_ficha_com_a_situacao_da_folha_so_com_view_sensitive()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner.Client);
        var ana = await EmployeeAsync(owner.Client, position);
        var older = await RunAsync(owner.Client, "2033-01", [ana.Id]);
        var newer = await RunAsync(owner.Client, "2033-02", [ana.Id]);
        await FillAsync(owner.Client, older, 2_500m);
        await FillAsync(owner.Client, newer, 2_500m);
        await SetStatusAsync(older, PayrollRunStatus.Paid);

        var url = $"/api/v1/hr/employees/{ana.Id}/payslips";
        var list = await owner.Client.GetAsync(url + "?take=6");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        list.Headers.CacheControl!.NoStore.Should().BeTrue();
        var items = (await JsonAsync(list)).EnumerateArray().ToList();
        items.Select(i => i.GetProperty("period").GetString()).Should().Equal("2033-02-01", "2033-01-01");
        items.Select(i => i.GetProperty("runStatus").GetString()).Should().Equal("Draft", "Paid");
        items[0].GetProperty("payrollRunId").GetGuid().Should().Be(newer);
        items.Should().OnlyContain(i => i.GetProperty("net").GetDecimal() == 2_500m);
        (await JsonAsync(await owner.Client.GetAsync(url + "?take=1"))).GetArrayLength().Should().Be(1);
        (await AuditCountAsync("Payslip.Employee", ana.Id.ToString(), AuditActions.SensitiveRead)).Should().Be(2);

        var viewer = await ActorAsync("hr:view");
        (await viewer.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.Client.GetAsync($"/api/v1/hr/employees/{Guid.CreateVersion7()}/payslips")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
