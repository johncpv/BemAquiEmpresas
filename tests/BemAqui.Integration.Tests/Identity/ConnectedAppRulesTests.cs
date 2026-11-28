using System.Security.Cryptography;
using System.Text;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Identity.Application.ConnectedApps;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>INT-01: regras puras do servidor de autorização (CA 3, 5, 9 e 11), sem banco.</summary>
public class ConnectedAppRulesTests
{
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    /// <summary>INT-04 CA 9: o ERP só confere o formato do código de pareamento (XXXX-XXXX, sem 0, O, 1, I e L) e normaliza.</summary>
    [Theory]
    [InlineData("ABCD-2345", "ABCD-2345")]
    [InlineData(" abcd2345 ", "ABCD-2345")]
    [InlineData("wxyz-9876", "WXYZ-9876")]
    [InlineData("ABCD-0000", null)]
    [InlineData("ABCO-2345", null)]
    [InlineData("ABCI-2345", null)]
    [InlineData("ABCL-2345", null)]
    [InlineData("ABC-2345", null)]
    [InlineData("ABCD-23456", null)]
    [InlineData("ABÇD-2345", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Codigo_de_pareamento_no_formato(string? code, string? normalized) => OAuthRules.NormalizePairing(code).Should().Be(normalized);

    [Fact]
    public void PKCE_S256_com_o_vetor_da_RFC_7636()
    {
        OAuthRules.Challenge(Verifier).Should().Be(Challenge);
        OAuthRules.VerifyPkce(Verifier, Challenge).Should().BeTrue();
        OAuthRules.VerifyPkce(Verifier + "x", Challenge).Should().BeFalse();
        OAuthRules.VerifyPkce(Challenge, Challenge).Should().BeFalse("plain nunca vale");
        OAuthRules.VerifyPkce("curto", OAuthRules.Challenge("curto")).Should().BeFalse("verificador com menos de 43 caracteres");
        OAuthRules.IsValidChallenge(Challenge).Should().BeTrue();
        OAuthRules.IsValidChallenge(Challenge + "=").Should().BeFalse();
        OAuthRules.IsValidChallenge(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://conecta.example/oauth/callback", true)]
    [InlineData("https://conecta.example/oauth/callback/", false)]
    [InlineData("https://CONECTA.example/oauth/callback", false)]
    [InlineData("https://conecta.example/oauth/callback?x=1", false)]
    [InlineData("http://conecta.example/oauth/callback", false)]
    [InlineData(null, false)]
    public void Redirect_uri_comparada_caractere_a_caractere(string? uri, bool ok) =>
        OAuthRules.RedirectMatches(uri, ["https://conecta.example/oauth/callback"]).Should().Be(ok);

    [Fact]
    public void Amarracao_da_intencao_muda_com_qualquer_parte_do_pedido()
    {
        var hash = OAuthRules.BindingHash("s1", Challenge, "https://a/cb");
        OAuthRules.BindingHash("s1", Challenge, "https://a/cb").Should().Be(hash);
        OAuthRules.BindingHash("s2", Challenge, "https://a/cb").Should().NotBe(hash);
        OAuthRules.BindingHash("s1", Challenge[..42] + "A", "https://a/cb").Should().NotBe(hash);
        OAuthRules.BindingHash("s1", Challenge, "https://a/cb2").Should().NotBe(hash);
        OAuthRules.BindingHash("s1|x", "y", "z").Should().NotBe(OAuthRules.BindingHash("s1", "x|y", "z"), "as partes levam o tamanho");
    }

    [Fact]
    public void Segredo_do_cliente_contra_o_hash_e_basic_decodificado()
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("segredo-do-ambiente")));
        OAuthRules.SecretMatches("segredo-do-ambiente", hash).Should().BeTrue();
        OAuthRules.SecretMatches("outro", hash).Should().BeFalse();
        OAuthRules.SecretMatches("segredo-do-ambiente", string.Empty).Should().BeFalse("sem hash configurado, falha fechada");
        OAuthRules.ParseBasic("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("conecta:a%3Ab"))).Should().Be(("conecta", "a:b"));
        OAuthRules.ParseBasic("Bearer x").Should().BeNull();
    }

    [Fact]
    public void Concessao_e_pedidos_intersecao_permitidos_intersecao_efetivas_sem_reservadas()
    {
        var held = new HashSet<string> { "sales.view", "sales.create", "parties.view", "sales.approve", "settings.edit" };
        var grant = ConnectedAppScopes.Compute(["sales.view", "sales.create", "finance.view", "sales.view"], ConnectedAppScopes.Conecta, held.Contains);
        grant.Granted.Should().Equal("sales.view", "sales.create");
        grant.NotHeld.Should().Equal("finance.view");
        grant.Unknown.Should().BeEmpty();

        var reserved = ConnectedAppScopes.Compute(["sales.approve", "settings.edit", "quotes.create", "nada.view"], ["sales.approve", "settings.edit", "quotes.create"], held.Contains);
        reserved.Granted.Should().BeEmpty("reservadas e fora do catálogo nunca entram, mesmo permitidas e com a pessoa tendo");
        reserved.Unknown.Should().Equal("sales.approve", "settings.edit", "nada.view");
        reserved.NotHeld.Should().Equal("quotes.create");
        ConnectedAppScopes.Conecta.Should().NotContain(s => ConnectedAppScopes.IsNeverGranted(s)).And.OnlyContain(s => ModuleCatalog.IsKnownPermission(s));
        ConnectedAppScopes.Text("parties.view").Should().Be("Ver clientes (nome, telefone, e-mail, documento)");
    }
}
