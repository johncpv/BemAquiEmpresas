using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Contrato_gera_parcelas_libera_pela_entrega_e_conclui_com_as_baixas()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false);
        var signed = Today.AddDays(-2);
        var (contractId, projectId) = await ApproveAsync(owner, id, quote, signed);

        // CA 1, 2 e 4: sinal aberto na assinatura; entrega prevista com o gatilho e o prazo.
        var parcels = await ParcelsAsync(contractId);
        parcels.Should().HaveCount(2);
        var sinal = parcels.Single(p => p.Description.Contains("Sinal"));
        sinal.Status.Should().Be(BemAqui.Finance.Contracts.ReceivableStatus.Open);
        sinal.DueDate.Should().Be(signed);
        sinal.Description.Should().MatchRegex(@"^CTR-\d{4}-\d{4} — Sinal \(30%\)$");
        var entrega = parcels.Single(p => p.Description.Contains("Entrega"));
        entrega.Status.Should().Be(BemAqui.Finance.Contracts.ReceivableStatus.Planned);
        entrega.DueDate.Should().BeNull();
        entrega.TriggerEvent.Should().Be(BemAqui.SharedKernel.TriggerEvent.Delivered);
        entrega.DaysAfterEvent.Should().Be(10);
        var number = (await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"))).GetProperty("number").GetString();
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == ownerId && n.Title.StartsWith($"Contrato {number} gerou 2 parcelas")))).Should().Be(1);
        (await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables?contractId={contractId}"))).GetProperty("page").GetProperty("items").GetArrayLength().Should().Be(2);

        // CA 3: a mesma mensagem de novo não duplica.
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE shared.outbox_messages SET processed_at = NULL, next_attempt_at = now() WHERE type LIKE '%ContractSigned' AND payload::text LIKE {0}", "%" + contractId + "%"));
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync("DELETE FROM shared.inbox_messages WHERE handler LIKE '%ContractSignedConsumer'"));
        await DrainAsync();
        (await ParcelsAsync(contractId)).Should().HaveCount(2);

        // CA 16: o contrato mostra a parcela de cada marco.
        var contract = await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"));
        contract.GetProperty("milestones").EnumerateArray().Should().OnlyContain(m => m.GetProperty("receivable").GetProperty("number").GetString()!.StartsWith("CR-"));

        // CA 10 e 18: entrega com projects.approve; o comercial (só view) recebe 403.
        var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
        var (_, commercial) = await ActorAsync("commercial");
        (await commercial.PostAsJsonAsync($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today), version = Version(project) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today.AddDays(1)), version = Version(project) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var delivered = await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/delivery", new { date = Iso(Today), version = Version(project) });
        delivered.StatusCode.Should().Be(HttpStatusCode.OK, await delivered.Content.ReadAsStringAsync());
        (await JsonAsync(delivered)).GetProperty("status").GetString().Should().Be("Delivered");
        await DrainAsync();

        // CA 6 e 8: a entrega libera a parcela com o prazo; CA 13: entregue não cancela.
        entrega = (await ParcelsAsync(contractId)).Single(p => p.Id == entrega.Id);
        entrega.Status.Should().Be(BemAqui.Finance.Contracts.ReceivableStatus.Open);
        entrega.DueDate.Should().Be(Today.AddDays(10));
        contract = await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"));
        var cancel = await owner.PostAsJsonAsync($"/api/v1/contracts/{contractId}/cancel", new { reason = "Desistiu", version = Version(contract) });
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await cancel.Content.ReadAsStringAsync()).Should().Contain("Projeto entregue: o contrato não é cancelado. Use um aditivo.");

        // CA 14: quitadas as duas, o contrato conclui e o projeto encerra.
        var bank = await SettleBankAsync(owner);
        await SettleAsync(owner, bank, sinal.Id);
        await DrainAsync();
        (await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"))).GetProperty("status").GetString().Should().Be("Active");
        await SettleAsync(owner, bank, entrega.Id);
        await DrainAsync();
        contract = await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"));
        contract.GetProperty("status").GetString().Should().Be("Completed");
        (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("status").GetString().Should().Be("Closed");

        // CA 15 e 19: o estorno reabre, com o motivo no histórico.
        var settlement = (await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{entrega.Id}/settlements")))[0].GetProperty("id").GetGuid();
        var current = await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{entrega.Id}"));
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{entrega.Id}/settlements/{settlement}/reverse", new { reason = "Cheque devolvido", version = Version(current) })).StatusCode.Should().Be(HttpStatusCode.OK);
        await DrainAsync();
        (await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"))).GetProperty("status").GetString().Should().Be("Active");
        (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("status").GetString().Should().Be("Delivered");
        var audit = await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == "Contract" && a.EntityId == contractId.ToString()).ToListAsync());
        audit.Should().Contain(a => a.Origin != null && a.Origin.StartsWith("Baixa estornada"));
        (await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == "Receivable" && a.EntityId == entrega.Id.ToString()).ToListAsync()))
            .Should().Contain(a => a.Origin != null && a.Origin.StartsWith("Evento ProjectDelivered"));
    }
}
