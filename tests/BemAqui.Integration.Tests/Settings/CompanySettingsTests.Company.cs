using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Settings;

public partial class CompanySettingsTests
{
    private static object Profile(string tradeName) => new
    {
        legalName = "Empresa A Ltda", tradeName, stateRegistration = "123456789", municipalRegistration = (string?)null, zipCode = "12280000", street = "Rua das Flores",
        number = "120", complement = (string?)null, district = "Centro", city = "Caçapava", cityIbge = "3508504", state = "SP", phone = "1236531000", email = "contato@empresa-a.com.br",
    };

    [Fact]
    public async Task Perfil_nasce_do_catalogo_e_salvo_vira_a_fonte_da_verdade_do_login()
    {
        var owner = await ClientAsync();
        var draft = await owner.GetFromJsonAsync<JsonElement>("/api/v1/settings/company");
        if (!draft.GetProperty("exists").GetBoolean())
        {
            draft.GetProperty("legalName").GetString().Should().Be("Empresa A Ltda", "o perfil nasce do catálogo (CA 1)");
        }

        draft.GetProperty("document").GetString().Should().Be("47225005000186", "CNPJ só leitura, do catálogo (CA 3)");

        var saved = await owner.PutAsJsonAsync("/api/v1/settings/company", Profile("Fantasia A"));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        (await Anonymous().GetFromJsonAsync<JsonElement>("/api/v1/public/tenant-context")).GetProperty("name").GetString().Should().Be("Fantasia A", "CA 4");

        var withDocument = await owner.PutAsync("/api/v1/settings/company",
            new StringContent("{\"legalName\":\"X Ltda\",\"document\":\"11222333000181\"}", Encoding.UTF8, "application/json"));
        withDocument.StatusCode.Should().Be(HttpStatusCode.BadRequest, "o CNPJ não entra pelo corpo (CA 3)");
        (await owner.PutAsJsonAsync("/api/v1/settings/company", new { legalName = "X", email = "sem-arroba" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Logo_valido_aparece_so_depois_de_limpo_e_a_rota_publica_tem_os_cabecalhos()
    {
        var owner = await ClientAsync();
        (await owner.PutAsJsonAsync("/api/v1/settings/company", Profile("Fantasia Logo"))).EnsureSuccessStatusCode();

        _scanner.Verdict = ScanVerdict.Unavailable;
        var pending = await owner.PostAsync("/api/v1/settings/company/logo", File(Png(400, 200), "logo.png"));
        pending.StatusCode.Should().Be(HttpStatusCode.OK, await pending.Content.ReadAsStringAsync());
        (await pending.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Pending", "aguardando verificação (CA 5)");
        (await Anonymous().GetAsync("/api/v1/public/tenant-logo")).StatusCode.Should().Be(HttpStatusCode.NotFound, "sem logo limpo, nada é publicado");

        _scanner.Verdict = ScanVerdict.Clean;
        var png = Png(512, 512);
        (await owner.PostAsync("/api/v1/settings/company/logo", File(png, "logo.png"))).EnsureSuccessStatusCode();
        var context = await Anonymous().GetFromJsonAsync<JsonElement>("/api/v1/public/tenant-context");
        var url = context.GetProperty("logoUrl").GetString();
        url.Should().StartWith("/api/v1/public/tenant-logo?v=").And.NotContain("data:");

        var logo = await Anonymous().GetAsync(url);
        logo.StatusCode.Should().Be(HttpStatusCode.OK);
        logo.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        logo.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        logo.Headers.GetValues("Content-Security-Policy").Should().Equal("default-src 'none'");
        (await logo.Content.ReadAsByteArrayAsync()).Should().Equal(png);

        var again = new HttpRequestMessage(HttpMethod.Get, "/api/v1/public/tenant-logo");
        again.Headers.IfNoneMatch.Add(logo.Headers.ETag!);
        (await Anonymous().SendAsync(again)).StatusCode.Should().Be(HttpStatusCode.NotModified, "ETag (CA 7)");
    }

    [Theory]
    [InlineData("svg", "logo.svg")]
    [InlineData("html", "logo.png")]
    [InlineData("big", "logo.png")]
    [InlineData("wide", "logo.png")]
    [InlineData("infected", "logo.png")]
    [InlineData("pdf", "logo.pdf")]
    public async Task Logo_fora_da_regra_e_recusado_e_o_anterior_continua(string kind, string name)
    {
        var owner = await ClientAsync();
        (await owner.PutAsJsonAsync("/api/v1/settings/company", Profile("Fantasia Recusa"))).EnsureSuccessStatusCode();
        _scanner.Verdict = ScanVerdict.Clean;
        var current = Png(100, 100);
        (await owner.PostAsync("/api/v1/settings/company/logo", File(current, "atual.png"))).EnsureSuccessStatusCode();

        _scanner.Verdict = kind == "infected" ? ScanVerdict.Infected : ScanVerdict.Clean;
        var content = kind switch
        {
            "svg" => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"),
            "html" => Encoding.UTF8.GetBytes("<!doctype html><html><script>alert(1)</script></html>"),
            "big" => Png(200, 200, extraBytes: 1024 * 1024 + 10),
            "wide" => Png(2000, 300),
            "pdf" => Encoding.UTF8.GetBytes("%PDF-1.7 teste"),
            _ => Png(64, 64),
        };
        var refused = await owner.PostAsync("/api/v1/settings/company/logo", File(content, name));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, kind + ": " + await refused.Content.ReadAsStringAsync());

        _scanner.Verdict = ScanVerdict.Clean;
        (await (await Anonymous().GetAsync("/api/v1/public/tenant-logo")).Content.ReadAsByteArrayAsync()).Should().Equal(current, "o logo anterior continua valendo (CA 6)");
    }
}
