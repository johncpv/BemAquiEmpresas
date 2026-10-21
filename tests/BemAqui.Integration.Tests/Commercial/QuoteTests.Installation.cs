using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    private const string Installations = "/api/v1/production/installations";

    private static object[] AcceptanceMilestones => [Milestone("Sinal", 30m), Milestone("Aceite", 70m, triggerEvent: "TechnicalAcceptance", days: 10)];

    [Fact]
    public async Task OS_de_instalacao_cria_titulo_previsto_e_o_aceite_entrega_o_projeto()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false, milestones: AcceptanceMilestones);
        var (contractId, projectId) = await ApproveAsync(owner, id, quote, Today.AddDays(-5));
        var installer = await CustomerAsync(owner, "Installer");

        // CA 1: a Engenharia cria OS (seed patch) e vê as sugestões.
        var (_, engineering) = await ActorAsync("engineering");
        var defaults = await engineering.GetAsync($"{Installations}/defaults?projectId={projectId}");
        defaults.StatusCode.Should().Be(HttpStatusCode.OK, await defaults.Content.ReadAsStringAsync());
        (await JsonAsync(defaults)).GetProperty("items").GetArrayLength().Should().Be(1);

        // CA 2 a 4: OS com etapas, checklist e o título previsto do instalador.
        object Create(Guid party) => new { projectId, projectItemIds = (Guid[]?)null, installerId = party, contractValue = 5_000m, plannedStart = Iso(Today), plannedEnd = Iso(Today.AddDays(5)) };
        (await owner.SendAsync(Post(Installations, Create(await CustomerAsync(owner))))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "terceiro sem papel Instalador");
        var created = await owner.SendAsync(Post(Installations, Create(installer)));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var os = await JsonAsync(created);
        var osId = os.GetProperty("id").GetGuid();
        os.GetProperty("number").GetString().Should().StartWith("OS-");
        os.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("name").GetString()).Should().Equal("Preparação da obra", "Montagem na obra", "Testes e ajustes", "Entrega técnica");
        os.GetProperty("checklist").GetArrayLength().Should().Be(5);
        var payable = await WithDbAsync(db => db.Payables.AsNoTracking().SingleAsync(p => p.OriginId == osId));
        payable.Status.Should().Be(PayableStatus.Planned);
        payable.IsEstimate.Should().BeTrue();
        payable.DueDate.Should().Be(Today.AddDays(5));
        payable.CostType.Should().Be(CostType.Installation);
        payable.ProjectId.Should().Be(projectId);

        // CA 5 e COM-04b CA 10: item já coberto; entrega manual recusada com OS aberta.
        var again = await owner.SendAsync(Post(Installations, Create(installer)));
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await again.Content.ReadAsStringAsync()).Should().Contain("já está na " + os.GetProperty("number").GetString());
        var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
        var manual = await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today), version = Version(project) });
        (await manual.Content.ReadAsStringAsync()).Should().Contain("Há OS de instalação aberta");

        // CA 6 e 7: iniciar, etapas em sequência; a última só pelo aceite.
        os = await JsonAsync(await owner.PostAsJsonAsync($"{Installations}/{osId}/start", new { date = Iso(Today), version = Version(os) }));
        await DrainAsync();
        (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("status").GetString().Should().Be("Installing");
        var steps = os.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        (await owner.PostAsJsonAsync($"{Installations}/{osId}/steps/{steps[1]}/complete", new { version = Version(os) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        foreach (var step in steps.Take(3))
        {
            os = await JsonAsync(await owner.PostAsJsonAsync($"{Installations}/{osId}/steps/{step}/complete", new { version = Version(os) }));
        }

        (await owner.PostAsJsonAsync($"{Installations}/{osId}/steps/{steps[3]}/complete", new { version = Version(os) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await AcceptFlowAsync(owner, os, osId, projectId, contractId);
    }
}
