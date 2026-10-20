using BemAqui.Finance.Application;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

public partial class AgingTests
{
    /// <summary>CA 4, no tenant B (o aging do A fica intacto): um aviso por usuário com finance.edit, sem repetir, nem em paralelo.</summary>
    [Fact]
    public async Task Aviso_diario_dos_vencidos_de_ontem_uma_vez_por_usuario_e_por_tenant()
    {
        var b = tenancy.TenantB;
        var (ownerB, _) = await ActorAsync(Group.OwnerSeedKey, b);
        var (financeB, _) = await ActorAsync("finance", b);
        var (noGroupB, _) = await ActorAsync(null, b);
        await ActorAsync(Group.OwnerSeedKey);
        var party = await WithDbAsync(async db =>
        {
            var account = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var id = Guid.CreateVersion7();
            db.Receivables.AddRange(Rec(id, account, 150m, Today.AddDays(-1)), Rec(id, account, 50m, Today.AddDays(-1)));
            await db.SaveChangesAsync();
            return id;
        }, b);
        party.Should().NotBeEmpty();

        async Task<int> RunAsync(BemAqui.Platform.Domain.Tenant tenant)
        {
            await using var scope = await ScopeAsync(tenant);
            return await scope.ServiceProvider.GetRequiredService<AgingService>().NotifyOverdueAsync();
        }

        (await RunAsync(tenancy.TenantA)).Should().Be(0, "sem vencidos ontem, nada é enviado");
        await Task.WhenAll(RunAsync(b), RunAsync(b));
        await RunAsync(b);

        var notices = await WithDbAsync(db => db.Notifications.Where(n => n.Type == AgingService.NoticeType).Select(n => new { n.UserId, n.Title, n.Link }).ToListAsync(), b);
        notices.Count(n => n.UserId == ownerB).Should().Be(1);
        notices.Count(n => n.UserId == financeB).Should().Be(1);
        notices.Should().NotContain(n => n.UserId == noGroupB);
        notices.Should().AllSatisfy(n => n.Title.Should().StartWith("2 títulos a receber venceram ontem (R$").And.EndWith("200,00)."));
        notices[0].Link.Should().Be("/app/financeiro/receber?situacao=vencido&vencimento=ontem");
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.Type == AgingService.NoticeType))).Should().Be(0, "o tenant A não recebe o aviso do B");
    }
}
