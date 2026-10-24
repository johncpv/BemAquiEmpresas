using BemAqui.BuildingBlocks.Email;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>
/// CMP-03b CA 5 a 7 (e COM-03 CA 9 e 10): endereço puro, sem CR/LF no nome da empresa, máscara do endereço, os dois templates no
/// limite diário e a mensagem do usuário saindo literal no e-mail do pedido (valor escapado, nunca template, sem link gerado).
/// </summary>
public class DocumentDispatchEmailTests
{
    private static readonly EmailTemplateRenderer Renderer = new();

    [Theory]
    [InlineData("compras@gama.com.br", true)]
    [InlineData(" compras@gama.com.br ", true)]
    [InlineData("Compras <compras@gama.com.br>", false)]
    [InlineData("\"Compras\" compras@gama.com.br", false)]
    [InlineData("a@gama.com.br,b@gama.com.br", false)]
    [InlineData("a@gama.com.br;b@gama.com.br", false)]
    [InlineData("a@gama.com.br\r\nBcc: x@y.com", false)]
    [InlineData("a@gama.com.br\nBcc: x@y.com", false)]
    [InlineData("sem-arroba", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Endereco_puro(string? address, bool ok) => EmailAddresses.IsPlain(address).Should().Be(ok);

    [Fact]
    public void Endereco_acima_de_254_caracteres_e_recusado() => EmailAddresses.IsPlain(new string('a', 245) + "@gama.com.br").Should().BeFalse();

    [Fact]
    public void Nome_da_empresa_sem_CR_LF_no_assunto_e_no_remetente()
    {
        EmailAddresses.OneLine("Konebras\r\nBcc: x@y.com").Should().Be("Konebras Bcc: x@y.com").And.NotContainAny("\r", "\n");
        EmailAddresses.OneLine("  Konebras   Elevadores \n").Should().Be("Konebras   Elevadores");
        EmailAddresses.OneLine(null).Should().BeEmpty();
    }

    [Fact]
    public void Endereco_mascarado_para_quem_nao_ve_dado_pessoal() => EmailAddresses.Mask("marta@aurora.com.br").Should().Be("m***@aurora.com.br");

    [Fact]
    public void Limite_diario_soma_orcamento_e_pedido_de_compra()
    {
        EmailDispatchLimiter.DispatchTemplates.Should().BeEquivalentTo("quote-sent", "purchase-order-sent");
        EmailDispatchLimiter.TenantDailyLimit.Should().Be(50);
        EmailDispatchLimiter.DefaultPerUserDailyLimit.Should().Be(20);
        EmailTemplateRenderer.SystemTemplates.Should().Contain(EmailDispatchLimiter.DispatchTemplates);
    }

    [Fact]
    public void Email_do_pedido_mostra_a_mensagem_literal_sem_link()
    {
        var rendered = Renderer.Render("purchase-order-sent", new Dictionary<string, object?>
        {
            ["tenant_name"] = "Konebras", ["order_number"] = "PC-2026-0001", ["expected_date"] = "13/11/2026", ["sender_name"] = "Bruno Compras",
            ["message"] = "Valor {{ 1 | plus: 1 }} {% if true %}sim{% endif %} <b>negrito</b> <a href=\"https://mal.example\">clique</a> https://mal.example/x",
        });
        rendered.Subject.Should().Be("Pedido de compra PC-2026-0001 — Konebras");
        rendered.Html.Should().Contain("{{ 1 | plus: 1 }}").And.Contain("{% if true %}sim{% endif %}").And.Contain("&lt;b&gt;negrito&lt;/b&gt;")
            .And.NotContain("Valor 2").And.NotContain("<a").And.Contain("&lt;a href=");
        rendered.Text.Should().Contain("{{ 1 | plus: 1 }}").And.Contain("https://mal.example/x");
        rendered.Html.Should().Contain("PC-2026-0001").And.Contain("13/11/2026").And.Contain("Bruno Compras");
    }

    [Fact]
    public void Email_do_pedido_sem_mensagem_nao_deixa_paragrafo_vazio()
    {
        var rendered = Renderer.Render("purchase-order-sent", new Dictionary<string, object?>
        {
            ["tenant_name"] = "Konebras", ["order_number"] = "PC-2026-0002", ["expected_date"] = "13/11/2026", ["sender_name"] = "Bruno", ["message"] = string.Empty,
        });
        rendered.Html.Should().NotContain("white-space:pre-line");
    }
}
