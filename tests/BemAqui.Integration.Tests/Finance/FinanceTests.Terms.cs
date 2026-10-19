using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

public partial class FinanceTests
{
    private static object Term(string name, params (int Days, decimal Percent)[] installments) =>
        new { name, installments = installments.Select(i => new { days = i.Days, percent = i.Percent }) };

    [Fact]
    public async Task Seed_cria_as_quatro_condicoes_e_nao_recria_a_excluida()
    {
        (await QueryAsync(db => db.PaymentTerms.Where(t => t.SeedKey != null).Select(t => t.Name).ToListAsync()))
            .Should().Contain(["À vista", "30 dias", "30/60 dias", "30/60/90 dias"]);
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var thirty = await QueryAsync(db => db.PaymentTerms.Where(t => t.SeedKey == "30-dias").Select(t => t.Id).SingleAsync());
        (await owner.DeleteAsync($"/api/v1/finance/payment-terms/{thirty}")).IsSuccessStatusCode.Should().BeTrue();

        await using (var scope = await ScopeAsync())
        {
            await new PaymentTermSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        (await QueryAsync(db => db.PaymentTerms.IgnoreQueryFilters().CountAsync(t => t.SeedKey == "30-dias"))).Should().Be(1, "idempotente e sem recriar a excluída (CA 15)");
        (await QueryAsync(db => db.PaymentTerms.CountAsync(t => t.SeedKey == "30-dias"))).Should().Be(0);
    }

    [Fact]
    public async Task Parcelas_somam_100_dias_crescentes_e_a_previa_divide_com_a_ultima_absorvendo()
    {
        var (_, financeiro) = await ActorAsync("finance");
        var bad = await financeiro.PostAsJsonAsync("/api/v1/finance/payment-terms", Term("Errada", (30, 50m), (60, 49.9999m)));
        bad.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("A soma das parcelas precisa ser 100%. Hoje está em 99,9999%.");
        (await financeiro.PostAsJsonAsync("/api/v1/finance/payment-terms", Term("Fora de ordem", (60, 50m), (30, 50m)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await financeiro.PostAsJsonAsync("/api/v1/finance/payment-terms", Term("Vazia"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var created = await CreatedAsync(await financeiro.PostAsJsonAsync("/api/v1/finance/payment-terms", Term("28/56 dias", (28, 50m), (56, 50m))));
        (await JsonAsync(await financeiro.GetAsync($"/api/v1/finance/payment-terms/{created}"))).GetProperty("installments").GetArrayLength().Should().Be(2);

        var preview = await financeiro.PostAsJsonAsync("/api/v1/finance/payment-terms/preview",
            new { installments = new[] { new { days = 30, percent = 33.3333m }, new { days = 60, percent = 33.3333m }, new { days = 90, percent = 33.3334m } }, amount = 1000.00m, baseDate = "2026-11-10" });
        var parts = (await JsonAsync(preview)).EnumerateArray().Select(p => (p.GetProperty("dueDate").GetString(), p.GetProperty("amount").GetDecimal())).ToList();
        parts.Should().Equal(("2026-12-10", 333.33m), ("2027-01-09", 333.33m), ("2027-02-08", 333.34m));

        await using var scope = await ScopeAsync();
        var contract = scope.ServiceProvider.GetRequiredService<BemAqui.Finance.Contracts.IPaymentTerms>();
        var seed = await QueryAsync(db => db.PaymentTerms.Where(t => t.SeedKey == "30-60-90-dias").Select(t => t.Id).SingleAsync());
        (await contract.SplitAsync(seed, 1000m, new DateOnly(2026, 11, 10)))!.Select(p => p.Amount).Should().Equal(333.33m, 333.33m, 333.34m);
    }
}
