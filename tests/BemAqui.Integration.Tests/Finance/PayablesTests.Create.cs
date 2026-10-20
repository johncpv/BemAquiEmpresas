using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class PayablesTests
{
    [Fact]
    public async Task Titulo_manual_com_retencoes_e_valor_liquido()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (party, _) = await SupplierAsync(owner);
        var created = await PayableAsync(owner, party, 1000m, iss: 50m);
        var item = created.GetProperty("items")[0];
        item.GetProperty("number").GetString().Should().MatchRegex(@"^CP-\d{4}-\d{5}$");
        item.GetProperty("netAmount").GetDecimal().Should().Be(950m, "líquido = valor − retenções (CA 4)");
        item.GetProperty("status").GetString().Should().Be("Open");

        async Task<string> FailAsync(object body)
        {
            var response = await owner.SendAsync(Post("/api/v1/finance/payables", body));
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            return await response.Content.ReadAsStringAsync();
        }

        (await FailAsync(await BodyAsync(party, 100m, iss: -1m))).Should().Contain("A retenção não pode ser negativa.");
        (await FailAsync(await BodyAsync(party, 100m, iss: 100m))).Should().Contain("As retenções precisam ser menores que o valor.");
        var customer = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Só cliente " + Guid.NewGuid().ToString("N")[..4], roles = new[] { "Customer" } });
        (await FailAsync(await BodyAsync((await JsonAsync(customer)).GetProperty("id").GetGuid()))).Should().Contain("Escolha um fornecedor ou instalador.");
        var revenue = await AccountAsync("1.1");
        (await FailAsync(new { partyId = party, description = "X", accountId = revenue, costCenterId = (Guid?)null, amount = 10m, issueDate = "2026-10-01", dueDate = "2026-10-02", retentionIss = 0m, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, paymentDetails = (object?)null }))
            .Should().Contain("Escolha uma conta de despesa");
        var asset = await AccountAsync("4.1");
        (await owner.SendAsync(Post("/api/v1/finance/payables", new { partyId = party, description = "Máquina", accountId = asset, costCenterId = (Guid?)null, amount = 10m, issueDate = "2026-10-01", dueDate = "2026-10-02", retentionIss = 0m, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, paymentDetails = (object?)null })))
            .StatusCode.Should().Be(HttpStatusCode.Created, "imobilizado também passa por OP (conta de Ativo)");
    }

    [Fact]
    public async Task Parcelas_com_retencao_na_primeira_e_idempotencia()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (party, _) = await SupplierAsync(owner, "Installer");
        var term = await QueryAsync(db => db.PaymentTerms.Where(t => t.SeedKey == "30-60-dias").Select(t => t.Id).SingleAsync());
        var key = Guid.NewGuid();
        var body = new { partyId = party, description = "Instalação", accountId = await AccountAsync("2.3"), costCenterId = (Guid?)null, amount = 1000m, issueDate = "2026-11-10", paymentTermId = term, baseDate = "2026-11-10", retentionIss = 50m, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, paymentDetails = (object?)null };
        var first = await owner.SendAsync(Post("/api/v1/finance/payables/installments", body, key));
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        (await owner.SendAsync(Post("/api/v1/finance/payables/installments", body, key))).StatusCode.Should().Be(HttpStatusCode.Created);
        var items = (await JsonAsync(first)).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("description").GetString()).Should().Equal("Instalação (1/2)", "Instalação (2/2)");
        items.Select(i => i.GetProperty("netAmount").GetDecimal()).Should().Equal(450m, 500m);
        (await QueryAsync(db => db.Payables.CountAsync(p => p.PartyId == party))).Should().Be(2, "o reenvio não duplica (CA 3)");
    }

    [Fact]
    public async Task Tres_camadas_e_sem_rota_de_baixa()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("view");
        var (_, nobody) = await ActorAsync();
        var (party, _) = await SupplierAsync(owner);
        var payable = (await PayableAsync(owner, party)).GetProperty("items")[0].GetProperty("id").GetGuid();
        (await nobody.GetAsync("/api/v1/finance/payables")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.GetAsync("/api/v1/finance/payables")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.SendAsync(Post("/api/v1/finance/payables", await BodyAsync(party)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PostAsJsonAsync($"/api/v1/finance/payables/{payable}/settlements", new { })).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await owner.DeleteAsync($"/api/v1/finance/payables/{payable}")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}
