using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Settings;

public partial class CompanySettingsTests
{
    private async Task<string> NextAsync(string docType)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<INumberSequence>().NextAsync(docType);
    }

    [Fact]
    public async Task Prefixo_e_digitos_mudam_o_proximo_e_o_proximo_numero_so_sobe()
    {
        var owner = await ClientAsync();
        var year = DateTime.UtcNow.AddHours(-3).Year;
        var rows = await owner.GetFromJsonAsync<JsonElement>("/api/v1/settings/numbering");
        rows.EnumerateArray().Single(r => r.GetProperty("docType").GetString() == "COT").GetProperty("next").GetString().Should().Be($"COT-{year}-0001", "prévia (CA 9)");

        (await NextAsync("COT")).Should().Be($"COT-{year}-0001");
        var saved = await owner.PutAsJsonAsync("/api/v1/settings/numbering/COT", new { prefix = "COTA", yearly = true, digits = 6 });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("next").GetString().Should().Be($"COTA-{year}-000002", "a sequência continua do último (CA 10)");
        (await NextAsync("COT")).Should().Be($"COTA-{year}-000002");

        var lower = await owner.PutAsJsonAsync("/api/v1/settings/numbering/COT", new { prefix = "COTA", yearly = true, digits = 6, nextNumber = 2 });
        lower.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await lower.Content.ReadAsStringAsync()).Should().Contain("O próximo número precisa ser maior que o último emitido (2).");
        (await owner.PutAsJsonAsync("/api/v1/settings/numbering/COT", new { prefix = "COTA", yearly = true, digits = 6, nextNumber = 150 })).EnsureSuccessStatusCode();
        (await NextAsync("COT")).Should().Be($"COTA-{year}-000150", "continua a numeração do sistema antigo (CA 11)");

        var yearly = await owner.PutAsJsonAsync("/api/v1/settings/numbering/COT", new { prefix = "COTA", yearly = false, digits = 6 });
        yearly.StatusCode.Should().Be(HttpStatusCode.Conflict, "reinício anual trava depois do primeiro número (CA 12)");
    }

    [Fact]
    public async Task Proximo_numero_num_tipo_sem_nenhum_numero_ainda_e_regras_do_prefixo()
    {
        var owner = await ClientAsync();
        var year = DateTime.UtcNow.AddHours(-3).Year;
        (await owner.PutAsJsonAsync("/api/v1/settings/numbering/MAN", new { prefix = "MAN", yearly = true, digits = 4, nextNumber = 37 })).EnsureSuccessStatusCode();
        (await NextAsync("MAN")).Should().Be($"MAN-{year}-0037", "ano sem linha (CA 11)");

        foreach (var invalid in new object[]
        {
            new { prefix = "", yearly = true, digits = 4 }, new { prefix = "MUITOLONGO", yearly = true, digits = 4 }, new { prefix = "MÃN", yearly = true, digits = 4 },
            new { prefix = "ORC", yearly = true, digits = 4 }, new { prefix = "MAN", yearly = true, digits = 2 }, new { prefix = "MAN", yearly = true, digits = 9 },
        })
        {
            (await owner.PutAsJsonAsync("/api/v1/settings/numbering/MAN", invalid)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, JsonSerializer.Serialize(invalid));
        }
    }

    [Fact]
    public async Task Troca_de_formato_durante_50_numeracoes_paralelas_nao_repete_numero()
    {
        var owner = await ClientAsync();
        var numbering = Enumerable.Range(0, 50).Select(_ => Task.Run(() => NextAsync("REC"))).ToList();
        var change = owner.PutAsJsonAsync("/api/v1/settings/numbering/REC", new { prefix = "RCB", yearly = true, digits = 5 });
        var numbers = await Task.WhenAll(numbering);
        (await change).EnsureSuccessStatusCode();

        numbers.Select(n => n.Split('-')[^1].TrimStart('0')).Should().OnlyHaveUniqueItems("nenhum número repetido (CA 14)").And.HaveCount(50);
    }
}
