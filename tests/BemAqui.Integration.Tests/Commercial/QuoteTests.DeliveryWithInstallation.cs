using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    /// <summary>
    /// COM-04b CA 10 (achado da operação DEMO, 15/11/2026): venda com instalação ainda sem OS não aceita a entrega manual, que pularia o
    /// aceite técnico e travaria a abertura da OS de instalação. Sem instalação, a entrega manual segue (ContractChain).
    /// </summary>
    [Fact]
    public async Task Entrega_manual_e_recusada_em_projeto_com_instalacao_sem_OS()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false, milestones: AcceptanceMilestones);
        var (_, projectId) = await ApproveAsync(owner, id, quote, Today.AddDays(-5));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"INSERT INTO commercial.project_budget (id, project_id, cost_type, budgeted_amount) VALUES ({Guid.CreateVersion7()}, {projectId}, 'Installation', 5000)"));

        var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
        project.GetProperty("hasInstallation").GetBoolean().Should().BeTrue();
        var manual = await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today), version = Version(project) });
        manual.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await manual.Content.ReadAsStringAsync();
        body.Should().Contain("installation_pending").And.Contain("Abra a OS de instalação.");
        (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("status").GetString().Should().Be("Planned");
    }
}
