using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Parties;

public partial class PartiesTests
{
    [Fact]
    public async Task Cria_PJ_com_endereco_de_cobranca_padrao_e_contato_principal()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var cnpj = NewCnpj();
        var response = await owner.PostAsJsonAsync("/api/v1/parties", new
        {
            kind = "PJ", document = cnpj[..2] + "." + cnpj[2..5] + "." + cnpj[5..8] + "/" + cnpj[8..12] + "-" + cnpj[12..], legalName = "Elevação Indústria Ltda", tradeName = "Elevação", roles = new[] { "Customer", "Supplier" },
            stateRegistration = "isento", taxRegime = "LucroPresumido",
            billingAddress = new { kind = "Billing", zipCode = "13069000", street = "Rua das Indústrias", number = "120", district = "Distrito Industrial", city = "Campinas", state = "sp" },
            mainContact = new { name = "Ana Souza", email = "Ana@Cliente.com.br", phone = "(11) 98765-4321", receivesBilling = true },
        });

        var id = await CreatedAsync(response);
        var detail = await JsonAsync(await owner.GetAsync("/api/v1/parties/" + id));
        detail.GetProperty("document").GetString().Should().Be($"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}");
        detail.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Customer", "Supplier");
        detail.GetProperty("stateRegistration").GetString().Should().Be("ISENTO");
        var address = detail.GetProperty("addresses")[0];
        address.GetProperty("isDefault").GetBoolean().Should().BeTrue();
        address.GetProperty("state").GetString().Should().Be("SP");
        detail.GetProperty("contacts")[0].GetProperty("email").GetString().Should().Be("ana@cliente.com.br");
        detail.GetProperty("contacts")[0].GetProperty("phone").GetString().Should().Be("11987654321");
    }

    [Fact]
    public async Task Documento_invalido_ou_do_tipo_errado_responde_por_campo_e_PF_sem_documento_e_aceita()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);

        var badCnpj = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company("47.225.005/0001-87", "Com dígito errado")));
        badCnpj.GetProperty("errors").GetProperty("document")[0].GetString().Should().Be("CNPJ inválido.");
        var badCpf = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PF", document = "123.456.789-00", legalName = "Pessoa", roles = new[] { "Customer" } }));
        badCpf.GetProperty("errors").GetProperty("document")[0].GetString().Should().Be("CPF inválido.");
        var cpfAsPj = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(NewCpf(), "Tipo errado")));
        cpfAsPj.GetProperty("errors").GetProperty("document")[0].GetString().Should().Contain("CNPJ com 14");
        var noRole = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", legalName = "Sem papel", roles = Array.Empty<string>() }));
        noRole.GetProperty("errors").GetProperty("roles")[0].GetString().Should().Be("O terceiro precisa de ao menos um papel.");

        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PF", legalName = "João Instalador", tradeName = "Ignorado", roles = new[] { "Installer" } }));
        var person = await JsonAsync(await owner.GetAsync("/api/v1/parties/" + id));
        person.GetProperty("document").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        person.GetProperty("tradeName").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "PF não tem nome fantasia (CA 9)");
        await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(CnpjAlpha, "CNPJ alfanumérico Ltda")));
    }

    [Fact]
    public async Task Mesmo_documento_nao_duplica_e_a_conferencia_aponta_o_cadastro_existente()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var cnpj = NewCnpj();
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(cnpj, "Original Ltda")));

        var check = await JsonAsync(await owner.GetAsync("/api/v1/parties/document-check?document=" + cnpj));
        check.GetProperty("exists").GetBoolean().Should().BeTrue();
        check.GetProperty("id").GetGuid().Should().Be(id);
        check.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Customer");

        var again = await owner.PostAsJsonAsync("/api/v1/parties", Company(cnpj, "Cópia Ltda", "Supplier"));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(again);
        Code(problem).Should().Be("party_exists");
        problem.GetProperty("existingId").GetGuid().Should().Be(id);
        problem.GetProperty("detail").GetString().Should().Be("Este CNPJ já está cadastrado como Original Ltda.");
    }

    [Fact]
    public async Task Duas_gravacoes_simultaneas_do_mesmo_documento_criam_um_so()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var cnpj = NewCnpj();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => owner.PostAsJsonAsync("/api/v1/parties", Company(cnpj, "Corrida " + i))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Documento_de_cadastro_excluido_oferece_restaurar_e_restaurar_exige_excluir()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, commercial) = await ActorAsync("commercial");
        var cnpj = NewCnpj();
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(cnpj, "Excluída Ltda")));
        (await owner.DeleteAsync("/api/v1/parties/" + id)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetAsync("/api/v1/parties/" + id)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var recreate = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/parties", Company(cnpj, "Nova Ltda")));
        Code(recreate).Should().Be("party_deleted");
        recreate.GetProperty("existingId").GetGuid().Should().Be(id);
        (await JsonAsync(await owner.GetAsync("/api/v1/parties/document-check?document=" + cnpj))).GetProperty("deleted").GetBoolean().Should().BeTrue();

        (await commercial.PostAsync($"/api/v1/parties/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var restored = await owner.PostAsync($"/api/v1/parties/{id}/restore", null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(restored)).GetProperty("isActive").GetBoolean().Should().BeTrue();
    }
}
