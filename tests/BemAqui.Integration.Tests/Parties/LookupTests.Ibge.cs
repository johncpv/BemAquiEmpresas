using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Parties;

public partial class LookupTests
{
    [Fact]
    public async Task Endereco_sem_codigo_IBGE_recebe_o_codigo_pelo_municipio_ao_gravar_e_na_recomposicao()
    {
        _api.Responses["/api/ibge/municipios/v1/SP"] = (HttpStatusCode.OK, """[{"nome":"CAÇAPAVA","codigo_ibge":"3508504"},{"nome":"SÃO JOSÉ DOS CAMPOS","codigo_ibge":"3549904"}]""");
        var client = await ClientAsync();

        // Pela API, sem o código: a gravação resolve pelo município.
        var created = await client.PostAsJsonAsync("/api/v1/parties", new
        {
            kind = "PJ", legalName = "Cliente IBGE " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Customer" },
            billingAddress = new { kind = "Billing", zipCode = "12280000", street = "Rua das Flores", number = "1", district = "Centro", city = "Cacapava", ibgeCode = (string?)null, state = "sp" },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var detail = await created.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("addresses")[0].GetProperty("ibgeCode").GetString().Should().Be("3508504");
        var partyId = detail.GetProperty("id").GetGuid();

        // Já gravado sem o código (importação antiga): a recomposição preenche; município desconhecido fica vazio sem travar.
        await using var scope = await StubbedScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IPartiesDb>();
        var old = new PartyAddress { PartyId = partyId, Kind = AddressKind.Delivery, ZipCode = "12200000", Street = "Av. Central", Number = "2", District = "Centro", City = "são josé  dos campos", State = "SP" };
        var unknown = new PartyAddress { PartyId = partyId, Kind = AddressKind.Site, ZipCode = "12200000", Street = "Rua X", Number = "3", District = "Centro", City = "Cidade Que Não Existe", State = "SP" };
        db.PartyAddresses.AddRange(old, unknown);
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<PartyAddressIbge>().BackfillAsync(CancellationToken.None)).Should().BeGreaterThanOrEqualTo(1);
        var codes = await db.PartyAddresses.AsNoTracking().Where(a => a.Id == old.Id || a.Id == unknown.Id).ToDictionaryAsync(a => a.Id, a => a.IbgeCode);
        codes[old.Id].Should().Be("3549904");
        codes[unknown.Id].Should().BeNullOrEmpty();
    }

    /// <summary>Escopo do tenant no host com a BrasilAPI falsa (o <see cref="ScopeAsync"/> usa o host sem o stub).</summary>
    private async Task<AsyncServiceScope> StubbedScopeAsync()
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }
}
