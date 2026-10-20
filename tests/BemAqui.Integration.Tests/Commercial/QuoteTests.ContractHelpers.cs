using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    /// <summary>Processa o outbox do tenant até não sobrar mensagem pendente (o dispatcher do host pode pegar parte; SKIP LOCKED).</summary>
    private async Task DrainAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        for (var round = 0; round < 60; round++)
        {
            await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
            {
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(info);
            }

            var pending = await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1)));
            if (pending == 0)
            {
                var errors = await WithDbAsync(db => db.OutboxMessages.Where(m => m.ProcessedAt == null && m.DeadLetteredAt == null).Select(m => m.Type + ": " + m.Error).ToListAsync());
                errors.Should().BeEmpty("nenhum evento pode falhar na cadeia");
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("O outbox não esvaziou.");
    }

    /// <summary>Aprova o orçamento enviado e esvazia o outbox: as parcelas já existem na volta.</summary>
    private async Task<(Guid Contract, Guid Project)> ApproveAsync(HttpClient owner, Guid id, JsonElement quote, DateOnly signed)
    {
        var account = (await JsonAsync(await owner.GetAsync($"{Base}/{id}/approval"))).GetProperty("suggestedRevenueAccountId").GetGuid();
        var approved = await owner.SendAsync(Post($"{Base}/{id}/approve", new { signedAt = Iso(signed), revenueAccountId = account, version = Version(quote) }));
        approved.StatusCode.Should().Be(HttpStatusCode.Created, await approved.Content.ReadAsStringAsync());
        var body = await JsonAsync(approved);
        await DrainAsync();
        return (body.GetProperty("contractId").GetGuid(), body.GetProperty("projectId").GetGuid());
    }

    private Task<List<BemAqui.Finance.Domain.Receivable>> ParcelsAsync(Guid contract) =>
        WithDbAsync(db => db.Receivables.AsNoTracking().Where(r => r.ContractId == contract).OrderBy(r => r.Number).ToListAsync());

    private static async Task<Guid> SettleBankAsync(HttpClient owner)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Banco " + Guid.NewGuid().ToString("N")[..6], kind = "Bank", bankCode = "077", agency = "1234-5", account = "12345678-9", openingBalance = (decimal?)null, openingDate = (string?)null, isDefault = false });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> SettleAsync(HttpClient owner, Guid bank, Guid receivable)
    {
        var current = await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{receivable}"));
        var settled = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{receivable}/settlements",
            new { bankAccountId = bank, date = Iso(Today), principal = current.GetProperty("balance").GetDecimal(), interest = 0m, fine = 0m, discount = 0m, backdateReason = (string?)null, version = Version(current) });
        settled.StatusCode.Should().Be(HttpStatusCode.OK, await settled.Content.ReadAsStringAsync());
        return await JsonAsync(settled);
    }
}
