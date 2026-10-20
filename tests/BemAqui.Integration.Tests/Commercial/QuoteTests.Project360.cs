using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Projeto_360_lista_edita_financeiro_custos_e_linha_do_tempo_por_permissao()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false);
        var (contractId, projectId) = await ApproveAsync(owner, id, quote, Today);

        // CA 1: lista com nomes; margem e recebido em rotas próprias.
        var list = await JsonAsync(await owner.GetAsync("/api/v1/projects?situation=andamento"));
        var row = list.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == projectId);
        row.GetProperty("contractValue").GetDecimal().Should().BeGreaterThan(0);
        row.GetProperty("managerName").GetString().Should().NotBeNullOrEmpty();
        (await JsonAsync(await owner.GetAsync($"/api/v1/projects/margins?ids={projectId}")))[0].GetProperty("marginPct").GetDecimal().Should().BeGreaterThan(0);
        (await JsonAsync(await owner.GetAsync($"/api/v1/projects/received?ids={projectId}")))[0].GetProperty("received").GetDecimal().Should().Be(0);

        // CA 4: edição com versão.
        var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
        var saved = await owner.PutAsJsonAsync($"/api/v1/projects/{projectId}", new { name = "Obra Aurora", managerUserId = ownerId, siteAddressId = (Guid?)null, version = Version(project) });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await JsonAsync(saved)).GetProperty("name").GetString().Should().Be("Obra Aurora");
        (await owner.PutAsJsonAsync($"/api/v1/projects/{projectId}", new { name = "Outra", managerUserId = ownerId, siteAddressId = (Guid?)null, version = Version(project) })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // CA 6, 8: título a pagar com projeto e natureza entra no financeiro e no realizado.
        var supplier = await CustomerAsync(owner, "Supplier");
        var account = await WithDbAsync(db => db.Accounts.Where(a => a.Code == "2.1").Select(a => a.Id).SingleAsync());
        var payable = await owner.SendAsync(Post("/api/v1/finance/payables", new
        {
            partyId = supplier, description = "Cabos", accountId = account, costCenterId = (Guid?)null, amount = 150m, issueDate = Iso(Today), dueDate = Iso(Today.AddDays(10)),
            retentionIss = 0m, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, paymentDetails = (object?)null, projectId, costType = "Material",
        }));
        payable.StatusCode.Should().Be(HttpStatusCode.Created, await payable.Content.ReadAsStringAsync());
        var financial = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}/financial"));
        financial.GetProperty("receivables").GetArrayLength().Should().Be(2);
        financial.GetProperty("payables").GetArrayLength().Should().Be(1);
        financial.GetProperty("toPay").GetDecimal().Should().Be(150m);
        var costs = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}/costs"));
        var material = costs.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("costType").GetString() == "Material");
        material.GetProperty("realized").GetDecimal().Should().Be(150m);

        // CA 7 e 10: engenharia vê o projeto, mas não financeiro nem custo.
        var (_, engineering) = await ActorAsync("engineering");
        (await engineering.GetAsync($"/api/v1/projects/{projectId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await engineering.GetAsync($"/api/v1/projects/{projectId}/financial")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await engineering.GetAsync($"/api/v1/projects/{projectId}/costs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await engineering.GetAsync($"/api/v1/projects/margins?ids={projectId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await engineering.GetAsync($"/api/v1/projects/received?ids={projectId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CA 12: linha do tempo; eventos financeiros só com finance.view.
        var timeline = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}/timeline"));
        var kinds = timeline.EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        kinds.Should().Contain(["quote_approved", "contract_signed", "receivable_created", "payable_created"]);
        var engineeringKinds = (await JsonAsync(await engineering.GetAsync($"/api/v1/projects/{projectId}/timeline"))).EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        engineeringKinds.Should().NotContain("receivable_created").And.Contain("contract_signed");

        // CA 13: IDOR.
        (await owner.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/timeline")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        _ = contractId;
    }
}
