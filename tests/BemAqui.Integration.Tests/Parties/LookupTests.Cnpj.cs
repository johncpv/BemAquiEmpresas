using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Parties;

public partial class LookupTests
{
    [Fact]
    public async Task CNPJ_consulta_mapeia_sem_QSA_e_a_segunda_vem_do_cache()
    {
        var cnpj = NewCnpj();
        _api.Responses["/api/cnpj/v1/" + cnpj] = (HttpStatusCode.OK, Cnpj(cnpj, "BAIXADA"));
        var client = await ClientAsync();

        var first = await client.GetFromJsonAsync<JsonElement>($"/api/v1/parties/cnpj/{cnpj}");
        var data = first.GetProperty("data");
        data.GetProperty("legalName").GetString().Should().Be("ELEVADORES TESTE LTDA");
        data.GetProperty("situation").GetString().Should().Be("BAIXADA", "situação não ativa só avisa (CA 3)");
        data.GetProperty("activeAtReceita").GetBoolean().Should().BeFalse();
        data.GetProperty("address").GetProperty("street").GetString().Should().Be("Rua das Flores");
        data.GetProperty("address").GetProperty("ibgeCode").GetString().Should().Be("3508504");
        data.GetProperty("phone").GetString().Should().Be("1236531000");
        first.GetProperty("fromCache").GetBoolean().Should().BeFalse();

        var second = await client.GetFromJsonAsync<JsonElement>($"/api/v1/parties/cnpj/{cnpj}");
        second.GetProperty("fromCache").GetBoolean().Should().BeTrue("30 dias de cache por tenant (CA 4)");
        _api.Calls.Count(c => c.EndsWith(cnpj, StringComparison.Ordinal)).Should().Be(1);

        await using var scope = await ScopeAsync();
        var cached = await scope.ServiceProvider.GetRequiredService<IPartiesDb>().CnpjLookups.AsNoTracking().SingleAsync(c => c.Document == cnpj);
        cached.Payload.Should().NotContain("FULANO").And.NotContain("qsa", "o quadro societário não é guardado (CA 14)");
    }

    [Fact]
    public async Task CNPJ_invalido_ou_ja_cadastrado_nao_chama_o_provedor()
    {
        var client = await ClientAsync();
        (await client.GetAsync("/api/v1/parties/cnpj/11111111111111")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CNPJ inválido (CA 7)");

        var cnpj = NewCnpj();
        Guid partyId;
        await using (var scope = await ScopeAsync())
        {
            partyId = (await scope.ServiceProvider.GetRequiredService<PartyService>().CreateAsync(new CreatePartyRequest(PartyKind.PJ, cnpj, "Já cadastrado", null, [PartyRoleKind.Customer]))).Value.Id;
        }

        (await client.GetAsync($"/api/v1/parties/cnpj/{cnpj}")).StatusCode.Should().Be(HttpStatusCode.Conflict, "duplicidade antes da consulta (CA 1)");
        _api.Responses["/api/cnpj/v1/" + cnpj] = (HttpStatusCode.OK, Cnpj(cnpj));
        (await client.GetAsync($"/api/v1/parties/cnpj/{cnpj}?partyId={partyId}")).StatusCode.Should().Be(HttpStatusCode.OK, "Atualizar pela Receita do próprio terceiro (CA 8)");
        _api.Calls.Should().NotContain("/api/cnpj/v1/11111111111111");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}", 404, "CNPJ não encontrado na base pública")]
    [InlineData(HttpStatusCode.InternalServerError, "{}", 503, "Não foi possível consultar o CNPJ agora")]
    [InlineData(HttpStatusCode.OK, "slow", 503, "Não foi possível consultar o CNPJ agora")]
    [InlineData(HttpStatusCode.Found, "{}", 503, "Não foi possível consultar o CNPJ agora")]
    [InlineData(HttpStatusCode.OK, "big", 503, "Não foi possível consultar o CNPJ agora")]
    public async Task Falha_do_provedor_vira_mensagem_e_o_cadastro_segue_manual(HttpStatusCode status, string body, int expected, string message)
    {
        var cnpj = NewCnpj();
        _api.Responses["/api/cnpj/v1/" + cnpj] = (status, body == "big" ? "{\"razao_social\":\"" + new string('x', 70_000) + "\"}" : body);
        var response = await (await ClientAsync()).GetAsync($"/api/v1/parties/cnpj/{cnpj}");
        ((int)response.StatusCode).Should().Be(expected);
        (await response.Content.ReadAsStringAsync()).Should().Contain(message);
    }

    [Fact]
    public async Task Circuit_breaker_aberto_nao_chama_mais_o_provedor()
    {
        var api = new FakeBrasilApi();
        await using var factory = Build(api, circuitFailures: 2);
        var client = await ClientAsync(factory: factory);
        for (var i = 0; i < 3; i++)
        {
            var cnpj = NewCnpj();
            api.Responses["/api/cnpj/v1/" + cnpj] = (HttpStatusCode.InternalServerError, "{}");
            (await client.GetAsync($"/api/v1/parties/cnpj/{cnpj}")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }

        api.Calls.Should().HaveCount(2, "depois de 2 falhas o circuito abre e a terceira nem sai (CA 5)");
    }
}
