using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Parties;

public partial class PartiesTests
{
    private static object Address(string kind, string street, bool isDefault = false) =>
        new { kind, zipCode = "01310100", street, number = "1000", district = "Bela Vista", city = "São Paulo", state = "SP", isDefault };

    private static List<JsonElement> Addresses(JsonElement detail, string kind) =>
        detail.GetProperty("addresses").EnumerateArray().Where(a => a.GetProperty("kind").GetString() == kind).ToList();

    [Fact]
    public async Task Primeiro_endereco_de_cada_tipo_e_o_padrao_trocar_desmarca_o_anterior_e_remover_promove_outro()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(NewCnpj(), "Endereços Ltda")));

        await owner.PostAsJsonAsync($"/api/v1/parties/{id}/addresses", Address("Delivery", "Rua A"));
        var detail = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/parties/{id}/addresses", Address("Delivery", "Rua B", isDefault: true)));
        var delivery = Addresses(detail, "Delivery");
        delivery.Single(a => a.GetProperty("isDefault").GetBoolean()).GetProperty("street").GetString().Should().Be("Rua B");

        var defaultId = delivery.Single(a => a.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        detail = await JsonAsync(await owner.DeleteAsync($"/api/v1/parties/{id}/addresses/{defaultId}"));
        Addresses(detail, "Delivery").Should().ContainSingle().Which.GetProperty("isDefault").GetBoolean().Should().BeTrue();

        var invalid = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/parties/{id}/addresses", new { kind = "Site", zipCode = "123", street = "", number = "", district = "", city = "", state = "XX" }));
        invalid.GetProperty("errors").GetProperty("zipCode")[0].GetString().Should().Be("CEP com 8 dígitos.");
        invalid.GetProperty("errors").GetProperty("state")[0].GetString().Should().Be("Escolha a UF.");
    }

    [Fact]
    public async Task Endereco_e_contato_de_outro_terceiro_respondem_404_sem_alterar_nada()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var a = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(NewCnpj(), "Dono A")));
        var b = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(NewCnpj(), "Outro B")));
        var detail = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/parties/{a}/addresses", Address("Billing", "Rua de A")));
        var addressOfA = detail.GetProperty("addresses")[0].GetProperty("id").GetGuid();
        detail = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/parties/{a}/contacts", new { name = "Contato de A", email = "a@a.com", receivesBilling = false }));
        var contactOfA = detail.GetProperty("contacts")[0].GetProperty("id").GetGuid();

        (await owner.PutAsJsonAsync($"/api/v1/parties/{b}/addresses/{addressOfA}", Address("Billing", "Invadida"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/parties/{b}/addresses/{addressOfA}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PutAsJsonAsync($"/api/v1/parties/{b}/contacts/{contactOfA}", new { name = "Invadido", receivesBilling = false })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/v1/parties/{b}/contacts/{contactOfA}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var untouched = await JsonAsync(await owner.GetAsync("/api/v1/parties/" + a));
        untouched.GetProperty("addresses")[0].GetProperty("street").GetString().Should().Be("Rua de A");
        untouched.GetProperty("contacts")[0].GetProperty("name").GetString().Should().Be("Contato de A");
    }
}
