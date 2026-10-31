using System.Net;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// RH-01b CA 13 (D6, <c>SoleOwnerRule</c>): com um só usuário com <c>hr.approve</c> (o Proprietário único), ele aprova as próprias marcações
/// e o próprio lançamento, com a marca "autoaprovado" no pedido e na marcação. Banco desta classe, sem outro aprovador.
/// </summary>
public class TimeAdjustmentSoleOwnerTests(TenancyFixture tenancy) : TimeAdjustmentTestBase(tenancy), IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Proprietario_unico_aprova_as_proprias_marcacoes_com_a_marca_autoaprovado()
    {
        var owner = await PersonAsync(Group.OwnerSeedKey, name: "Paula Dona");
        var day = LastWorkday();

        var own = await LaunchAsync(owner.Client, owner.EmployeeId, day, "08:00", "In", "Esqueci de marcar a entrada");
        own.GetProperty("canDecide").GetBoolean().Should().BeTrue("Proprietário único");
        var approved = await ApproveAsync(owner.Client, own);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        (await JsonAsync(approved)).GetProperty("selfApproved").GetBoolean().Should().BeTrue();

        var rows = await JsonAsync(await owner.Client.GetAsync($"/api/v1/hr/employees/{owner.EmployeeId}/time-entries?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}"));
        rows.GetProperty("items").EnumerateArray().Single().GetProperty("selfApproved").GetBoolean().Should().BeTrue("a marca aparece também na marcação");
        (await DbAsync(db => db.TimeEntries.SingleAsync(e => e.EmployeeId == owner.EmployeeId))).Source.Should().Be(TimeEntrySource.Manual);
    }
}
