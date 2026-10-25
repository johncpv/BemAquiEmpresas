using System.Net;
using BemAqui.BuildingBlocks.Email;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-03b CA 5: o limite por usuário (20, somando orçamentos e pedidos) e o do tenant (50, somado com os orçamentos da COM-03), com
/// dois envios simultâneos no último lugar: só um passa e nada mais é enfileirado. Um teste só, num banco só desta classe, porque a
/// contagem é do dia inteiro do tenant.
/// </summary>
public class PurchaseOrderDispatchLimitTests(TenancyFixture tenancy) : PurchaseOrderDispatchTestBase(tenancy), IClassFixture<TenancyFixture>
{
    private Task<int> SeedLogsAsync(string template, int count, Guid? requestedBy) => WithDbAsync(async db =>
    {
        db.EmailLogs.AddRange(Enumerable.Range(0, count).Select(_ => new EmailLog
        {
            To = "x@y.com", Template = template, Model = "{}", Status = EmailStatus.Sent, RequestedBy = requestedBy, CreatedAt = DateTimeOffset.UtcNow,
        }));
        return await db.SaveChangesAsync();
    });

    [Fact]
    public async Task Limite_por_usuario_e_do_tenant_somados_com_o_orcamento_e_atomicos()
    {
        var (_, owner) = await ActorAsync("owner");
        var (buyerId, buyer) = await ActorAsync(Buyer);
        var (_, second) = await ActorAsync(Buyer);
        var (_, third) = await ActorAsync(Buyer);
        var supplier = await SupplierWithContactAsync(owner);

        // Por usuário: 10 orçamentos e 9 pedidos dele hoje; o 20º passa e o 21º é recusado sem enfileirar.
        await SeedLogsAsync(EmailDispatchLimiter.QuoteTemplate, 10, buyerId);
        await SeedLogsAsync(EmailDispatchLimiter.PurchaseOrderTemplate, 9, buyerId);
        var first = await ApprovedAsync(owner, buyer, supplier);
        (await SendAsync(buyer, first, Dispatch(first, "compras@gama.com.br"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var mine = await ApprovedAsync(owner, buyer, supplier);
        var refused = await SendAsync(buyer, mine, Dispatch(mine, "compras@gama.com.br"));
        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("email_dispatch_user_limit");
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Você atingiu o seu limite diário de envios. Tente amanhã ou baixe o PDF.");
        (await WithDbAsync(db => db.EmailLogs.CountAsync(l => l.Template == EmailDispatchLimiter.PurchaseOrderTemplate && l.RelatedId == Id(mine)))).Should().Be(0);
        (await GetAsync(buyer, Id(mine))).GetProperty("status").GetString().Should().Be("Approved");

        // Do tenant: completa 49 com orçamentos de outros usuários; dois envios simultâneos no 50º: só um passa.
        var today = await WithDbAsync(db => db.EmailLogs.CountAsync(l => EmailDispatchLimiter.DispatchTemplates.Contains(l.Template)));
        await SeedLogsAsync(EmailDispatchLimiter.QuoteTemplate, EmailDispatchLimiter.TenantDailyLimit - 1 - today, null);
        var a = await ApprovedAsync(owner, second, supplier);
        var b = await ApprovedAsync(owner, third, supplier);
        var results = await Task.WhenAll(SendAsync(second, a, Dispatch(a, "compras@gama.com.br")), SendAsync(third, b, Dispatch(b, "compras@gama.com.br")));
        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.TooManyRequests]);
        var blocked = results.Single(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("Limite diário de envios atingido. Tente amanhã ou baixe o PDF.");
        (await WithDbAsync(db => db.EmailLogs.CountAsync(l => EmailDispatchLimiter.DispatchTemplates.Contains(l.Template)))).Should().Be(EmailDispatchLimiter.TenantDailyLimit);

        // O PDF continua disponível (a alternativa da mensagem).
        (await buyer.GetAsync($"{Base}/{Id(mine)}/pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
