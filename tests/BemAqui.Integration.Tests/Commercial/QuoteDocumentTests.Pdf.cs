using System.Diagnostics;
using System.Net;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteDocumentTests
{
    [Fact]
    public async Task PDF_sem_custo_so_com_Archivo_e_guardado_como_anexo_do_sistema()
    {
        var owner = await OwnerAsync();
        var (id, detail) = await QuoteAsync(owner);
        var watch = Stopwatch.StartNew();
        var generated = await owner.PostAsync($"{Base}/{id}/pdf", null);
        watch.Stop();
        generated.StatusCode.Should().Be(HttpStatusCode.OK, await generated.Content.ReadAsStringAsync());
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        var pdf = await JsonAsync(generated);
        pdf.GetProperty("fileName").GetString().Should().Be($"{detail.GetProperty("number").GetString()}-rev0.pdf");
        pdf.GetProperty("origin").GetString().Should().Be("System");

        var bytes = await owner.GetByteArrayAsync($"/api/v1/attachments/{pdf.GetProperty("id").GetGuid()}/content");
        using var document = PdfDocument.Open(bytes);
        var text = string.Join(" ", document.GetPages().Select(p => p.Text));
        text.Should().Contain(detail.GetProperty("number").GetString()).And.Contain("Elevador 6 paradas").And.Contain("1.400,00").And.Contain("Sem efeito de contrato.");
        text.Should().NotContain("600,00", "custo nunca vai no PDF (CA 2)").And.NotContain("Frete especial").And.NotContainAny("Markup", "Margem", "Custo");
        document.GetPages().SelectMany(p => p.Letters).Select(l => l.FontName).Distinct().Should().OnlyContain(f => f.Contains("Archivo"), "ADR 0022: só a Archivo embutida");

        var documents = await JsonAsync(await owner.GetAsync($"{Base}/{id}/documents"));
        documents.GetProperty("generated").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Modelo_do_email_mostra_a_mensagem_literal()
    {
        var renderer = new BemAqui.BuildingBlocks.Email.EmailTemplateRenderer();
        var rendered = renderer.Render("quote-sent", new Dictionary<string, object?>
        {
            ["tenant_name"] = "Konebras", ["quote_number"] = "ORC-2026-0001", ["revision"] = "0", ["valid_until"] = "30/11/2026", ["sender_name"] = "Ana",
            ["message"] = "Valor {{ 1 | plus: 1 }} {% if true %}sim{% endif %} <b>negrito</b>",
        });
        rendered.Html.Should().Contain("{{ 1 | plus: 1 }}").And.Contain("{% if true %}sim{% endif %}").And.Contain("&lt;b&gt;negrito&lt;/b&gt;").And.NotContain("Valor 2");
        rendered.Subject.Should().Be("Orçamento ORC-2026-0001 rev. 0 — Konebras");
    }
}
