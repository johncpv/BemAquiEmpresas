using System.Net;
using System.Net.Http.Json;
using BemAqui.HR.Application;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

public partial class PayrollClosingTests
{
    /// <summary>
    /// FLH-01 CA 12: tabela legal criada depois do último fechamento aparece no diálogo de fechamento (o quê, quem, quando, da auditoria) e
    /// fechar sem a ciência dá 422; depois do fechamento, a mudança sai da lista da folha seguinte. Tabela sintética.
    /// </summary>
    [Fact]
    public async Task Mudanca_de_tabela_desde_o_ultimo_fechamento_pede_ciencia()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey, totp: true);
        var (run, _, _, _) = await ReadyRunAsync(owner, "2036-07");
        var created = await owner.Client.PostAsJsonAsync("/api/v1/hr/payroll-tax-tables", new
        {
            kind = "MinimumWage", validFrom = "2047-01-01", rounding = "AwayFromZero", source = "Tabela sintética de teste", content = new { minimumWage = new { monthly = 999m } }, version = (uint?)null,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var preview = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{run}/close/sensitive"));
        var change = preview.GetProperty("changes").EnumerateArray().Should().Contain(c => c.GetProperty("what").GetString() == "Tabela Salário mínimo de janeiro/2047").Which;
        change.GetProperty("action").GetString().Should().Be("Criada");
        change.GetProperty("byName").GetString().Should().NotBeNullOrEmpty();
        change.GetProperty("fields").EnumerateArray().Should().Contain(f => f.GetProperty("field").GetString() == "content");

        var refused = await CloseAsync(owner, run, "2036-07-31", ack: false);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(refused)).Should().Be(PayrollChangeNotices.NotAcknowledgedCode);
        (await PayrollPayablesAsync(run)).Should().BeEmpty();
        (await CloseAsync(owner, run, "2036-07-31")).StatusCode.Should().Be(HttpStatusCode.OK);

        var (next, _, _, _) = await ReadyRunAsync(owner, "2036-08");
        var after = await JsonAsync(await owner.Client.GetAsync($"{Runs}/{next}/close/sensitive"));
        after.GetProperty("changes").EnumerateArray().Should().NotContain(c => c.GetProperty("what").GetString() == "Tabela Salário mínimo de janeiro/2047");
    }
}
