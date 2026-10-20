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
    [Fact]
    public async Task Contrato_de_implantacao_nao_gera_titulo_do_recebido_antes_e_conclui_com_ele()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false, milestones:
            [Milestone("Sinal", 20m), Milestone("Parcela", 30m, trigger: "Date", triggerEvent: null, dueDate: Iso(Today.AddDays(5)), days: null), Milestone("Fabricação", 50m, triggerEvent: "FabricationDone", days: 10)]);
        var preview = await JsonAsync(await owner.GetAsync($"{Base}/{id}/approval"));
        var account = preview.GetProperty("suggestedRevenueAccountId").GetGuid();
        var ms = preview.GetProperty("milestones").EnumerateArray().ToList();
        Guid Ms(string name) => ms.Single(m => m.GetProperty("description").GetString() == name).GetProperty("id").GetGuid();
        var signed = Today.AddDays(-60);
        object Body(bool parcelaRecebida) => new
        {
            signedAt = Iso(signed), revenueAccountId = account, version = Version(quote), isOnboarding = true,
            milestones = new object[] { new { milestoneId = Ms("Sinal"), dueDate = (string?)null, receivedBefore = true }, new { milestoneId = Ms("Parcela"), dueDate = Iso(Today.AddDays(-10)), receivedBefore = parcelaRecebida } },
        };

        // CA 1: sem data de corte, recusado.
        var noCutover = await owner.SendAsync(Post($"{Base}/{id}/approve", Body(false)));
        noCutover.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await noCutover.Content.ReadAsStringAsync()).Should().Contain("Defina a data de corte na Implantação financeira antes.");
        // A data de corte é preparação (a FIN-00 tem os próprios testes); outro teste da classe pode já ter travado a tela.
        var cutover = Today.AddDays(-30);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE finance.opening_setup SET cutover_date = {cutover}"));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"INSERT INTO finance.opening_setup (id, cutover_date) SELECT {Guid.CreateVersion7()}, {cutover} WHERE NOT EXISTS (SELECT 1 FROM finance.opening_setup)"));

        // CA 3: recebido depois do corte vai pelo caixa.
        var late = await owner.SendAsync(Post($"{Base}/{id}/approve", Body(true)));
        late.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await late.Content.ReadAsStringAsync()).Should().Contain("Recebido depois da data de corte: lance a baixa no título.");

        // CA 2 e 6: o sinal recebido antes não vira título; a parcela no passado nasce vencida; o Proprietário é avisado.
        var approved = await owner.SendAsync(Post($"{Base}/{id}/approve", Body(false)));
        approved.StatusCode.Should().Be(HttpStatusCode.Created, await approved.Content.ReadAsStringAsync());
        var body = await JsonAsync(approved);
        var (contractId, projectId) = (body.GetProperty("contractId").GetGuid(), body.GetProperty("projectId").GetGuid());
        await DrainAsync();
        var parcels = await ParcelsAsync(contractId);
        parcels.Should().HaveCount(2);
        var parcela = parcels.Single(p => p.Description.Contains("Parcela"));
        parcela.Status.Should().Be(ReceivableStatus.Open);
        parcela.DueDate.Should().Be(Today.AddDays(-10));
        parcela.IsOverdue(Today).Should().BeTrue();
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == ownerId && n.Title.StartsWith("Contrato de implantação") && n.Title.Contains("1 marco recebido antes")))).Should().Be(1);

        // CA 5: marca no contrato e no marco.
        var contract = await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"));
        contract.GetProperty("isOnboarding").GetBoolean().Should().BeTrue();
        contract.GetProperty("milestones").EnumerateArray().Single(m => m.GetProperty("description").GetString() == "Sinal").GetProperty("receivedBeforeOnboarding").GetBoolean().Should().BeTrue();

        // CA 4: fabricação com a data real no passado libera já vencida.
        var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
        project.GetProperty("isOnboarding").GetBoolean().Should().BeTrue();
        project = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/fabrication", new { date = Iso(Today.AddDays(-40)), version = Version(project) }));
        await DrainAsync();
        var fab = (await ParcelsAsync(contractId)).Single(p => p.TriggerEvent == TriggerEvent.FabricationDone);
        fab.Status.Should().Be(ReceivableStatus.Open);
        fab.DueDate.Should().Be(Today.AddDays(-30));

        // CA 5: com a entrega e as duas parcelas pagas, conclui (o sinal conta como quitado).
        (await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today.AddDays(-35)), version = Version(project) })).StatusCode.Should().Be(HttpStatusCode.OK);
        var bank = await SettleBankAsync(owner);
        await SettleAsync(owner, bank, parcela.Id);
        await SettleAsync(owner, bank, fab.Id);
        await DrainAsync();
        (await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"))).GetProperty("status").GetString().Should().Be("Completed");
    }
}
