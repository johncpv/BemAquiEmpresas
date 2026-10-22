using System.Text.Json;
using BemAqui.BuildingBlocks.Email;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>D19: o link com token sai do modelo guardado quando o envio termina; o resto fica para o suporte.</summary>
public class EmailSecretsTests
{
    private static string Json(object model) => JsonSerializer.Serialize(model);

    [Theory]
    [InlineData("invite")]
    [InlineData("password-reset")]
    public void Campo_declarado_e_apagado_e_o_resto_fica(string template)
    {
        var redacted = EmailSecrets.Redact(template, Json(new Dictionary<string, object?> { ["link"] = "https://app.konebras.com.br/x?token=abc", ["tenant_name"] = "Konebras" }));

        var root = JsonDocument.Parse(redacted).RootElement;
        root.GetProperty("link").GetString().Should().Be(EmailSecrets.Redacted);
        root.GetProperty("tenant_name").GetString().Should().Be("Konebras");
        redacted.Should().NotContain("abc");
    }

    [Fact]
    public void Link_com_token_em_template_sem_campo_declarado_tambem_e_apagado()
    {
        var redacted = EmailSecrets.Redact("payment-order-pending", Json(new Dictionary<string, object?> { ["link"] = "https://app.konebras.com.br/op/1?acao=aprovar&token=abc", ["number"] = "OP-1" }));

        redacted.Should().NotContain("token=abc").And.Contain("OP-1");
    }

    [Fact]
    public void Modelo_sem_segredo_fica_igual()
    {
        var model = Json(new Dictionary<string, object?> { ["link"] = "https://app.konebras.com.br/esqueci-senha", ["message"] = "o token= do texto não é link" });

        EmailSecrets.Redact("account-locked", model).Should().Be(model);
    }

    [Theory]
    [InlineData("https://a.com/convite?token=x", true)]
    [InlineData("http://a.com/p?a=1&TOKEN=x", true)]
    [InlineData("https://a.com/p?notoken=x", false)]
    [InlineData("texto com token=x", false)]
    [InlineData(null, false)]
    public void Reconhece_link_com_token(string? value, bool expected) => EmailSecrets.IsTokenLink(value).Should().Be(expected);
}
