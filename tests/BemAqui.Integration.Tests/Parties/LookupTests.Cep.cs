using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Parties.Application;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Parties;

public partial class LookupTests
{
    [Fact]
    public async Task CEP_preenche_o_endereco_com_o_codigo_IBGE()
    {
        _api.Responses["/api/cep/v2/12280000"] = (HttpStatusCode.OK, """{"cep":"12280000","state":"SP","city":"Caçapava","neighborhood":"Centro","street":"Rua das Flores"}""");
        _api.Responses["/api/ibge/municipios/v1/SP"] = (HttpStatusCode.OK, """[{"nome":"CAÇAPAVA","codigo_ibge":"3508504"},{"nome":"TAUBATÉ","codigo_ibge":"3554102"}]""");
        var client = await ClientAsync();

        var address = await client.GetFromJsonAsync<JsonElement>("/api/v1/parties/cep/12280-000");
        address.GetProperty("street").GetString().Should().Be("Rua das Flores");
        address.GetProperty("state").GetString().Should().Be("SP");
        address.GetProperty("ibgeCode").GetString().Should().Be("3508504");

        var missing = await client.GetAsync("/api/v1/parties/cep/99999999");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("CEP não encontrado. Preencha o endereço.");
    }

    [Fact]
    public async Task Sem_criar_nem_editar_terceiros_e_403_e_passando_de_30_por_minuto_e_429()
    {
        var viewer = await ClientAsync("engineering");
        (await viewer.GetAsync("/api/v1/parties/cep/12280000")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "engenharia só vê terceiros (CA 11)");

        _api.Responses["/api/cep/v2/12280001"] = (HttpStatusCode.OK, """{"cep":"12280001","state":"SP","city":"Caçapava"}""");
        var client = await ClientAsync();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 31; i++)
        {
            codes.Add((await client.GetAsync("/api/v1/parties/cep/12280001")).StatusCode);
        }

        codes.Take(30).Should().OnlyContain(c => c == HttpStatusCode.OK);
        codes.Last().Should().Be(HttpStatusCode.TooManyRequests, "30 por minuto por usuário (CA 12)");
    }

    [Fact]
    public async Task Expurgo_apaga_o_cache_com_mais_de_30_dias()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IPartiesDb>();
        db.CnpjLookups.Add(new BemAqui.Parties.Domain.CnpjLookupCache { Document = NewCnpj(), Payload = "{}", LookedUpAt = DateTimeOffset.UtcNow.AddDays(-31) });
        await db.SaveChangesAsync();

        (await scope.ServiceProvider.GetRequiredService<CnpjLookupService>().PurgeAsync()).Should().BeGreaterThanOrEqualTo(1, "CA 14");
    }
}
