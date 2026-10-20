using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class ReceivablesTests
{
    [Fact]
    public async Task Titulo_manual_e_parcelas_pela_condicao_com_numeros_seguidos()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var manual = await ReceivableAsync(owner, party);
        manual.GetProperty("number").GetString().Should().MatchRegex(@"^CR-\d{4}-\d{5}$", "numeração CR (CA 1)");
        manual.GetProperty("status").GetString().Should().Be("Open");
        manual.GetProperty("origin").GetString().Should().Be("Manual");

        var term = await QueryAsync(db => db.PaymentTerms.Where(t => t.SeedKey == "30-60-90-dias").Select(t => t.Id).SingleAsync());
        var preview = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/finance/receivables/split-preview", new { paymentTermId = term, amount = 1000m, baseDate = "2026-11-10" }));
        preview.EnumerateArray().Select(p => p.GetProperty("amount").GetDecimal()).Should().Equal(333.33m, 333.33m, 333.34m);
        var body = new { partyId = party, description = "Elevador", accountId = await RevenueAsync(), costCenterId = (Guid?)null, projectId = (Guid?)null, amount = 1000m, issueDate = "2026-11-10", paymentTermId = term, baseDate = "2026-11-10" };
        var created = await owner.SendAsync(Post("/api/v1/finance/receivables/installments", body));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var items = (await JsonAsync(created)).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("dueDate").GetString()).Should().Equal("2026-12-10", "2027-01-09", "2027-02-08");
        items.Select(i => i.GetProperty("description").GetString()).Should().Equal("Elevador (1/3)", "Elevador (2/3)", "Elevador (3/3)");
        items.Select(i => i.GetProperty("installmentGroupId").GetGuid()).Distinct().Should().ContainSingle();
        var numbers = items.Select(i => int.Parse(i.GetProperty("number").GetString()![^5..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        numbers.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        (numbers[2] - numbers[0]).Should().Be(2, "números seguidos (CA 3)");
    }

    [Fact]
    public async Task Validacoes_no_campo_e_so_cliente_ativo_com_conta_de_receita()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var supplier = await CustomerAsync(owner, "Supplier");
        var revenue = await RevenueAsync();
        var group = await QueryAsync(db => db.Accounts.Where(a => a.Code == "1").Select(a => a.Id).SingleAsync());
        var expense = await QueryAsync(db => db.Accounts.Where(a => a.Type == BemAqui.Finance.Contracts.AccountType.Expense && a.AcceptsEntries).Select(a => a.Id).FirstAsync());
        object Body(Guid p, Guid account, decimal amount, string due) => new { partyId = p, description = "X", accountId = account, costCenterId = (Guid?)null, projectId = (Guid?)null, amount, issueDate = "2026-10-01", dueDate = due };
        async Task<string> FailAsync(object body)
        {
            var response = await owner.SendAsync(Post("/api/v1/finance/receivables", body));
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            return await response.Content.ReadAsStringAsync();
        }

        (await FailAsync(Body(party, revenue, 0m, "2026-10-10"))).Should().Contain("Informe um valor maior que zero.");
        (await FailAsync(Body(party, revenue, 10m, "2026-09-10"))).Should().Contain("O vencimento não pode ser antes da emissão.");
        (await FailAsync(Body(party, group, 10m, "2026-10-10"))).Should().Contain("Escolha uma conta analítica.");
        (await FailAsync(Body(party, expense, 10m, "2026-10-10"))).Should().Contain("Escolha uma conta de receita ativa.");
        (await FailAsync(Body(supplier, revenue, 10m, "2026-10-10"))).Should().Contain("Escolha um terceiro com o papel Cliente.");
    }

    [Fact]
    public async Task Clique_duplo_com_a_mesma_chave_cria_um_titulo_so()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var key = Guid.NewGuid();
        var body = new { partyId = party, description = "Duplo", accountId = await RevenueAsync(), costCenterId = (Guid?)null, projectId = (Guid?)null, amount = 77m, issueDate = "2026-10-01", dueDate = "2026-10-20" };
        var first = await owner.SendAsync(Post("/api/v1/finance/receivables", body, key));
        var second = await owner.SendAsync(Post("/api/v1/finance/receivables", body, key));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        (await second.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        (await QueryAsync(db => db.Receivables.CountAsync(r => r.PartyId == party))).Should().Be(1, "CA 5");
        var noKey = await owner.PostAsJsonAsync("/api/v1/finance/receivables", body);
        noKey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Cinquenta_criacoes_paralelas_sem_numero_repetido()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var revenue = await RevenueAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => owner.SendAsync(Post("/api/v1/finance/receivables",
            new { partyId = party, description = "Paralelo " + i, accountId = revenue, costCenterId = (Guid?)null, projectId = (Guid?)null, amount = 10m, issueDate = "2026-10-01", dueDate = "2026-10-20" }))));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var numbers = await QueryAsync(db => db.Receivables.Where(r => r.PartyId == party).Select(r => r.Number).ToListAsync());
        numbers.Should().HaveCount(50).And.OnlyHaveUniqueItems();
    }
}
