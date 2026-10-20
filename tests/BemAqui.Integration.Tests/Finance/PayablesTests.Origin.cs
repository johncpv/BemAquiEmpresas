using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

public partial class PayablesTests
{
    [Fact]
    public async Task IPayables_cria_idempotente_confirma_e_a_tela_nao_muda_o_valor_da_origem()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (party, _) = await SupplierAsync(owner, "Installer");
        var order = Guid.CreateVersion7();
        PayableRef first;
        await using (var scope = await ScopeAsync())
        {
            var payables = scope.ServiceProvider.GetRequiredService<IPayables>();
            first = (await payables.CreateFromOriginAsync(DocumentOrigin.Installer, order, party, null, 800m, Today.AddDays(20), true, "Instalação OS-1", planned: true)).Value;
            var again = (await payables.CreateFromOriginAsync(DocumentOrigin.Installer, order, party, null, 800m, Today.AddDays(20), true, "Instalação OS-1", planned: true)).Value;
            again.Id.Should().Be(first.Id, "mesma origem devolve o mesmo título (CA 14)");
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync();
        }

        first.Status.Should().Be(PayableStatus.Planned);
        first.IsEstimate.Should().BeTrue();
        (await QueryAsync(db => db.Payables.Where(p => p.Id == first.Id).Select(p => db.Accounts.Where(a => a.Id == p.AccountId).Select(a => a.Code).Single()).SingleAsync())).Should().Be("2.3");

        var current = await JsonAsync(await owner.GetAsync($"/api/v1/finance/payables/{first.Id}"));
        var changed = await owner.PutAsJsonAsync($"/api/v1/finance/payables/{first.Id}", new { description = "X", accountId = current.GetProperty("accountId").GetGuid(), costCenterId = (Guid?)null, amount = 1m, dueDate = Iso(Today.AddDays(20)), retentionIss = 0m, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, version = Version(current) });
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await changed.Content.ReadAsStringAsync()).Should().Contain("ajuste na origem", "CA 13");
        (await owner.PostAsJsonAsync($"/api/v1/finance/payables/{first.Id}/cancel", new { reason = "x", version = Version(current) })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using (var scope = await ScopeAsync())
        {
            var payables = scope.ServiceProvider.GetRequiredService<IPayables>();
            (await payables.UpdatePlannedAsync(DocumentOrigin.Installer, order, 850m, null)).IsSuccess.Should().BeTrue();
            var confirmed = (await payables.ConfirmAsync(DocumentOrigin.Installer, order, 850m, Today.AddDays(30))).Value;
            confirmed.Status.Should().Be(PayableStatus.Open);
            confirmed.IsEstimate.Should().BeFalse("CA 15");
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync();
            (await payables.CancelFromOriginAsync(DocumentOrigin.Installer, order, "OS cancelada")).IsSuccess.Should().BeTrue();
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync();
        }

        (await QueryAsync(db => db.Payables.Where(p => p.Id == first.Id).Select(p => p.CancelReason).SingleAsync())).Should().StartWith("Origem cancelada");
    }

    [Fact]
    public async Task Recorrente_gera_uma_vez_por_competencia_em_paralelo_e_so_no_proprio_tenant()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (party, _) = await SupplierAsync(owner);
        var start = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-2);
        var body = new { description = "Aluguel", partyId = party, accountId = await AccountAsync("3.2"), costCenterId = (Guid?)null, amount = 2500m, dueDay = 31, start = Iso(start), end = (string?)null, leadDays = 10, paymentDetails = (object?)null };
        var created = await owner.SendAsync(Post("/api/v1/finance/recurring-expenses", body));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var expense = await JsonAsync(created);
        expense.GetProperty("nextDueDates").GetArrayLength().Should().Be(3, "mostra as próximas 3 ocorrências (CA 16)");
        var id = expense.GetProperty("id").GetGuid();

        var expected = 0;
        for (var c = start; ; c = c.AddMonths(1))
        {
            var due = new DateOnly(c.Year, c.Month, DateTime.DaysInMonth(c.Year, c.Month));
            if (due.AddDays(-10) > Today)
            {
                break;
            }

            expected++;
        }

        await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var scope = await ScopeAsync();
            await scope.ServiceProvider.GetRequiredService<RecurringExpenseService>().GenerateAsync();
        }));
        var generated = await QueryAsync(db => db.Payables.Where(p => p.RecurringExpenseId == id).OrderBy(p => p.Competence).ToListAsync());
        generated.Should().HaveCount(expected, "uma vez por competência, mesmo em paralelo, inclusive as atrasadas (CA 17 a 19)");
        generated.Should().OnlyContain(p => p.DueDate!.Value.Day == DateTime.DaysInMonth(p.DueDate.Value.Year, p.DueDate.Value.Month), "dia 31 vira o último dia do mês (CA 18)");
        generated[0].Description.Should().Be($"Aluguel — {start:MM/yyyy}");
        (await QueryAsync(db => db.Payables.CountAsync(p => p.RecurringExpenseId == id), tenancy.TenantB)).Should().Be(0, "um tenant não gera título no outro");

        var delete = await owner.DeleteAsync($"/api/v1/finance/recurring-expenses/{id}");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await delete.Content.ReadAsStringAsync()).Should().Contain("Encerre-a informando o fim.");
    }
}
