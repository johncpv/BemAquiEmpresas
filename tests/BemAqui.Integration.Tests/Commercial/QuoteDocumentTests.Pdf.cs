using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// EXP-03 CA 7 e 8: o "Baixar PDF" do cabeçalho (GET) baixa a revisão corrente sem gravar anexo; vencido sai com a faixa da validade,
    /// recusado com "Cancelado" e "Orçamento recusado", sem custo.
    /// </summary>
    [Fact]
    public async Task Baixar_PDF_nao_grava_anexo_e_marca_vencido_e_recusado()
    {
        var owner = await OwnerAsync();
        var (id, detail) = await QuoteAsync(owner);
        var response = await owner.GetAsync($"{Base}/{id}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be($"{detail.GetProperty("number").GetString()}-rev0.pdf");
        string Text(byte[] bytes)
        {
            using var document = PdfDocument.Open(bytes);
            return string.Join(" ", document.GetPages().Select(p => p.Text));
        }

        var draft = Text(await response.Content.ReadAsByteArrayAsync());
        draft.Should().Contain("Elevador 6 paradas").And.NotContain("600,00").And.NotContainAny("Validade vencida", "CANCELADO", "Margem");
        (await JsonAsync(await owner.GetAsync($"{Base}/{id}/documents"))).GetProperty("generated").GetArrayLength().Should().Be(0, "o GET não grava no histórico");

        var validUntil = new DateOnly(2026, 10, 1);
        await WithDbAsync(db => db.Set<BemAqui.Commercial.Domain.Quote>().Where(q => q.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, BemAqui.Commercial.Domain.QuoteStatus.Expired).SetProperty(q => q.ValidUntil, validUntil)));
        Text(await owner.GetByteArrayAsync($"{Base}/{id}/pdf")).Should().Contain("Validade vencida em 01/10/2026: reabra com uma nova revisão.").And.NotContain("CANCELADO");

        await WithDbAsync(db => db.Set<BemAqui.Commercial.Domain.Quote>().Where(q => q.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, BemAqui.Commercial.Domain.QuoteStatus.Rejected)));
        Text(await owner.GetByteArrayAsync($"{Base}/{id}/pdf")).Should().Contain("Cancelado · Orçamento recusado").And.Contain("CANCELADO");
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
