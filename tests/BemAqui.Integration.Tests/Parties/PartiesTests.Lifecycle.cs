using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using BemAqui.Parties.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Parties;

public partial class PartiesTests
{
    [Fact]
    public async Task Contato_com_email_invalido_papel_unico_e_dados_fiscais()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(NewCnpj(), "Fiscal Ltda")));

        var bad = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/parties/{id}/contacts", new { name = "Sem e-mail bom", email = "nao-e-email", receivesBilling = true }));
        bad.GetProperty("errors").GetProperty("email")[0].GetString().Should().Be("E-mail inválido.");

        var update = new { kind = "PJ", document = (string?)null, legalName = "Fiscal Ltda", tradeName = "Fiscal", roles = Array.Empty<string>(), stateRegistration = "123456789", municipalRegistration = "9988", taxRegime = "SimplesNacional", email = (string?)null, phone = (string?)null, notes = (string?)null };
        (await JsonAsync(await owner.PutAsJsonAsync("/api/v1/parties/" + id, update))).GetProperty("errors").GetProperty("roles")[0].GetString().Should().Be("O terceiro precisa de ao menos um papel.");

        var saved = await JsonAsync(await owner.PutAsJsonAsync("/api/v1/parties/" + id, update with { roles = new[] { "Supplier", "Carrier" } }));
        saved.GetProperty("taxRegime").GetString().Should().Be("SimplesNacional");
        saved.GetProperty("stateRegistration").GetString().Should().Be("123456789");
        saved.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Supplier", "Carrier");
    }

    [Fact]
    public async Task Inativo_some_da_lista_e_da_busca_do_contrato_e_reativar_volta()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var name = "Inativável " + Guid.NewGuid().ToString("N")[..6];
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(NewCnpj(), name, "Supplier")));

        (await JsonAsync(await owner.PostAsync($"/api/v1/parties/{id}/inactivate", null))).GetProperty("isActive").GetBoolean().Should().BeFalse();
        (await JsonAsync(await owner.GetAsync("/api/v1/parties?q=" + Uri.EscapeDataString(name)))).GetProperty("total").GetInt32().Should().Be(0);
        (await JsonAsync(await owner.GetAsync($"/api/v1/parties?includeInactive=true&q={Uri.EscapeDataString(name)}"))).GetProperty("total").GetInt32().Should().Be(1);

        await using (var scope = await ScopeAsync())
        {
            var contract = scope.ServiceProvider.GetRequiredService<IParties>();
            (await contract.SearchAsync(name, PartyRoleKind.Supplier)).Should().BeEmpty();
            (await contract.FindAsync(id))!.IsActive.Should().BeFalse("o resumo por id devolve até inativo e excluído (CA 21)");
        }

        await owner.PostAsync($"/api/v1/parties/{id}/reactivate", null);
        (await JsonAsync(await owner.GetAsync("/api/v1/parties?q=" + Uri.EscapeDataString(name)))).GetProperty("total").GetInt32().Should().Be(1);
    }

    private sealed class FakeUsage(Guid used) : IPartyUsageProvider
    {
        public Task<string?> UsageAsync(Guid partyId, CancellationToken cancellationToken = default) => Task.FromResult(partyId == used ? "código de fornecedor" : null);
    }

    [Fact]
    public async Task Terceiro_em_uso_nao_e_excluido_e_a_resposta_sugere_inativar()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var used = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(NewCnpj(), "Em uso Ltda")));
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IPartyUsageProvider>(_ => new FakeUsage(used))));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;

        var refused = await client.DeleteAsync("/api/v1/parties/" + used);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("party_in_use");
        problem.GetProperty("detail").GetString().Should().Be("O terceiro está em uso (código de fornecedor). Inative-o.");
    }
}
