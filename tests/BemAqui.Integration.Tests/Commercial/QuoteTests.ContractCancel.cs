using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Cancelar_contrato_cancela_parcelas_sem_baixa_e_avisa_as_com_baixa()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false);
        var (contractId, projectId) = await ApproveAsync(owner, id, quote, Today);
        var parcels = await ParcelsAsync(contractId);
        var sinal = parcels.Single(p => p.Status == ReceivableStatus.Open);
        var entrega = parcels.Single(p => p.Status == ReceivableStatus.Planned);

        // Baixa parcial no sinal.
        var bank = await SettleBankAsync(owner);
        var current = await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{sinal.Id}"));
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{sinal.Id}/settlements",
            new { bankAccountId = bank, date = Iso(Today), principal = 10m, interest = 0m, fine = 0m, discount = 0m, backdateReason = (string?)null, version = Version(current) })).StatusCode.Should().Be(HttpStatusCode.OK);

        // CA 12 e 18: motivo obrigatório; só com projects.approve.
        var contract = await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"));
        var (_, engineering) = await ActorAsync("engineering");
        (await engineering.PostAsJsonAsync($"/api/v1/contracts/{contractId}/cancel", new { reason = "x", version = Version(contract) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync($"/api/v1/contracts/{contractId}/cancel", new { reason = " ", version = Version(contract) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cancelled = await owner.PostAsJsonAsync($"/api/v1/contracts/{contractId}/cancel", new { reason = "Cliente desistiu da obra", version = Version(contract) });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await JsonAsync(cancelled)).GetProperty("cancelReason").GetString().Should().Be("Cliente desistiu da obra");
        await DrainAsync();

        parcels = await ParcelsAsync(contractId);
        parcels.Single(p => p.Id == entrega.Id).Status.Should().Be(ReceivableStatus.Cancelled);
        parcels.Single(p => p.Id == sinal.Id).Status.Should().Be(ReceivableStatus.PartiallyPaid);
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == ownerId && n.Title == $"Parcela {sinal.Number} tem baixa: estorne ou mantenha."))).Should().Be(1);
        (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("status").GetString().Should().Be("Cancelled");
        var outbox = (await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.EndsWith("ContractCancelled")).ToListAsync())).Single(m => m.Payload.Contains(contractId.ToString()));
        outbox.Payload.Should().NotContain("desistiu", "sem texto livre no evento");
    }

    [Fact]
    public async Task Evento_em_dead_letter_avisa_o_financeiro_e_pode_ser_reprocessado()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var contractId = Guid.NewGuid();
        var messageId = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(new { ContractId = contractId, Milestones = "inválido" });
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO shared.outbox_messages (id, type, payload, occurred_at, attempts, next_attempt_at) VALUES ({0}, {1}, {2}::jsonb, now(), 9, now())",
            messageId, typeof(BemAqui.Commercial.Contracts.ContractSigned).FullName!, payload));
        try
        {
            await DeadLetterChecksAsync(owner, ownerId, messageId, contractId);
        }
        finally
        {
            // A mensagem inválida não fica rodando nos outros testes da classe (o dispatcher do host também a pega).
            await WithDbAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE shared.outbox_messages SET processed_at = now() WHERE id = {0}", messageId));
        }
    }

    private async Task DeadLetterChecksAsync(HttpClient owner, Guid ownerId, Guid messageId, Guid contractId)
    {
        await DrainAsync();
        (await WithDbAsync(db => db.OutboxMessages.Where(m => m.Id == messageId).Select(m => m.DeadLetteredAt).SingleAsync())).Should().NotBeNull();
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == ownerId && n.Title.StartsWith("Não foi possível gerar as parcelas do contrato")))).Should().BeGreaterThan(0);
        var list = await JsonAsync(await owner.GetAsync("/api/v1/finance/dead-letters"));
        list.EnumerateArray().Should().Contain(m => m.GetProperty("id").GetGuid() == messageId && m.GetProperty("contractId").GetGuid() == contractId);

        var (_, viewer) = await ActorAsync("commercial");
        (await viewer.PostAsync($"/api/v1/finance/dead-letters/{messageId}/reprocess", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsync($"/api/v1/finance/dead-letters/{messageId}/reprocess", null)).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        var requeued = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == messageId));
        requeued.DeadLetteredAt.Should().BeNull();
        requeued.Attempts.Should().BeLessThan(10, "voltou para a fila (o dispatcher pode já ter tentado de novo)");
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == "OutboxMessage" && a.EntityId == messageId.ToString()))).Should().Be(1);
    }
}
