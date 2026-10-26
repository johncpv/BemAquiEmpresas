using System.Globalization;
using System.Text;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Webhooks;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>FND-16 CA 5 e 6: HMAC do corpo cru e token na URL, em tempo constante, com a sobreposição de 24 h da rotação (CA 7).</summary>
public class WebhookValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("segredo-de-teste-com-32-bytes-ok!");
    private static readonly byte[] OldSecret = Encoding.UTF8.GetBytes("segredo-antigo-de-teste-32-bytes");
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"evento\":\"nota.autorizada\",\"id\":\"0199\"}");

    private static WebhookRequest Request(byte[] body, Dictionary<string, string> headers, string? query = null, string? path = null) => new(body, headers, query, path);

    private static string Ts(DateTimeOffset at) => at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static Dictionary<string, string> Signed(byte[] key, byte[] body, string? timestamp = null)
    {
        var headers = new Dictionary<string, string> { ["X-BemAqui-Signature"] = HmacSha256Validator.Sign(key, body, timestamp) };
        if (timestamp is not null)
        {
            headers["X-BemAqui-Timestamp"] = timestamp;
        }

        return headers;
    }

    private static readonly HmacSha256Validator Hmac = new(timestampHeader: "X-BemAqui-Timestamp");

    private static WebhookKeys Keys(byte[]? previous = null, DateTimeOffset? until = null) => new(Secret.ToArray(), previous?.ToArray(), until);

    [Fact]
    public void Hmac_valido_sobre_o_corpo_cru_e_aceito()
    {
        Hmac.Validate(Request(Body, Signed(Secret, Body, Ts(Now))), Keys(), Now).Should().Be(WebhookOriginMatch.Current);
    }

    [Fact]
    public void Corpo_alterado_em_um_byte_e_recusado()
    {
        var headers = Signed(Secret, Body, Ts(Now));
        var altered = Body.ToArray();
        altered[^2] ^= 0x01;

        Hmac.Validate(Request(altered, headers), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Cabecalho_ausente_prefixo_errado_ou_hex_invalido_e_recusado()
    {
        var ts = Ts(Now);
        var good = HmacSha256Validator.Sign(Secret, Body, ts);

        Hmac.Validate(Request(Body, new Dictionary<string, string> { ["X-BemAqui-Timestamp"] = ts }), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        Hmac.Validate(Request(Body, new Dictionary<string, string> { ["X-BemAqui-Signature"] = "sha1=" + good[7..], ["X-BemAqui-Timestamp"] = ts }), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        Hmac.Validate(Request(Body, new Dictionary<string, string> { ["X-BemAqui-Signature"] = "sha256=zz" + good[9..], ["X-BemAqui-Timestamp"] = ts }), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        Hmac.Validate(Request(Body, new Dictionary<string, string> { ["X-BemAqui-Signature"] = good[..^2], ["X-BemAqui-Timestamp"] = ts }), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Cabecalho_vale_sem_diferenca_de_maiusculas()
    {
        var ts = Ts(Now);
        var headers = new Dictionary<string, string> { ["x-bemaqui-signature"] = HmacSha256Validator.Sign(Secret, Body, ts), ["x-bemaqui-timestamp"] = ts };

        Hmac.Validate(Request(Body, headers), Keys(), Now).Should().Be(WebhookOriginMatch.Current);
    }

    [Theory]
    [InlineData(-6)]
    [InlineData(6)]
    public void Timestamp_fora_da_janela_de_5_minutos_e_recusado_contra_replay(int minutes)
    {
        var ts = Ts(Now.AddMinutes(minutes));

        Hmac.Validate(Request(Body, Signed(Secret, Body, ts)), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Timestamp_trocado_invalida_a_assinatura()
    {
        var headers = Signed(Secret, Body, Ts(Now));
        headers["X-BemAqui-Timestamp"] = Ts(Now.AddSeconds(-30));

        Hmac.Validate(Request(Body, headers), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Timestamp_nao_numerico_ou_absurdo_e_recusado()
    {
        foreach (var ts in new[] { "abc", "-1", "99999999999999", "" })
        {
            var headers = new Dictionary<string, string> { ["X-BemAqui-Signature"] = HmacSha256Validator.Sign(Secret, Body, ts), ["X-BemAqui-Timestamp"] = ts };
            Hmac.Validate(Request(Body, headers), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid, ts);
        }
    }

    [Fact]
    public void Segredo_anterior_vale_so_dentro_das_24_horas()
    {
        var ts = Ts(Now);
        var headers = Signed(OldSecret, Body, ts);

        Hmac.Validate(Request(Body, headers), Keys(OldSecret, Now.AddHours(1)), Now).Should().Be(WebhookOriginMatch.Previous);
        Hmac.Validate(Request(Body, headers), Keys(OldSecret, Now.AddSeconds(-1)), Now).Should().Be(WebhookOriginMatch.Invalid);
        Hmac.Validate(Request(Body, headers), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Segredo_de_outro_tenant_nao_valida()
    {
        var other = Encoding.UTF8.GetBytes("segredo-do-outro-tenant-32-bytes");

        Hmac.Validate(Request(Body, Signed(other, Body, Ts(Now))), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Sem_timestamp_configurado_a_assinatura_cobre_so_o_corpo()
    {
        var plain = new HmacSha256Validator();
        var headers = new Dictionary<string, string> { ["X-BemAqui-Signature"] = HmacSha256Validator.Sign(Secret, Body) };

        plain.Validate(Request(Body, headers), Keys(), Now).Should().Be(WebhookOriginMatch.Current);
    }

    [Fact]
    public void Sem_credencial_configurada_nada_valida()
    {
        Hmac.Validate(Request(Body, Signed(Secret, Body, Ts(Now))), new WebhookKeys(null, null, null), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    private static readonly string Token = OpaqueToken.New();
    private static readonly string OldToken = OpaqueToken.New();

    private static WebhookKeys TokenKeys(string? previous = null, DateTimeOffset? until = null) =>
        new(UrlTokenValidator.Hash(Token), previous is null ? null : UrlTokenValidator.Hash(previous), until);

    [Fact]
    public void Token_na_query_e_aceito_e_no_caminho_e_recusado_no_modo_query()
    {
        var validator = new UrlTokenValidator();

        validator.Validate(Request(Body, [], query: Token), TokenKeys(), Now).Should().Be(WebhookOriginMatch.Current);
        validator.Validate(Request(Body, [], path: Token), TokenKeys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        validator.Validate(Request(Body, [], query: Token, path: Token), TokenKeys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Token_no_caminho_e_aceito_e_na_query_e_recusado_no_modo_PathToken()
    {
        var validator = new UrlTokenValidator(UrlTokenMode.PathToken);

        validator.Validate(Request(Body, [], path: Token), TokenKeys(), Now).Should().Be(WebhookOriginMatch.Current);
        validator.Validate(Request(Body, [], query: Token), TokenKeys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Token_errado_curto_ou_ausente_e_recusado()
    {
        var validator = new UrlTokenValidator();

        validator.Validate(Request(Body, [], query: OpaqueToken.New()), TokenKeys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        validator.Validate(Request(Body, [], query: Token[..^1]), TokenKeys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        validator.Validate(Request(Body, []), TokenKeys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Token_anterior_vale_so_dentro_das_24_horas()
    {
        var validator = new UrlTokenValidator();

        validator.Validate(Request(Body, [], query: OldToken), TokenKeys(OldToken, Now.AddHours(23)), Now).Should().Be(WebhookOriginMatch.Previous);
        validator.Validate(Request(Body, [], query: OldToken), TokenKeys(OldToken, Now), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Token_tem_256_bits_e_so_o_hash_e_guardado()
    {
        Token.Should().HaveLength(UrlTokenValidator.TokenLength);
        UrlTokenValidator.Hash(Token).Should().HaveCount(32).And.NotEqual(Encoding.UTF8.GetBytes(Token));
    }

    [Fact]
    public void Rotacao_guarda_a_anterior_por_24_horas_e_a_primeira_nao_tem_anterior()
    {
        var credential = new WebhookCredential { Provider = "test", Kind = WebhookCredentialKind.UrlToken, CreatedAt = Now };

        credential.Rotate("hash-1", null, Guid.CreateVersion7(), Now);
        credential.PreviousTokenHash.Should().BeNull();
        credential.PreviousValidUntil.Should().BeNull();

        credential.Rotate("hash-2", null, Guid.CreateVersion7(), Now.AddDays(1));
        credential.TokenHash.Should().Be("hash-2");
        credential.PreviousTokenHash.Should().Be("hash-1");
        credential.PreviousValidUntil.Should().Be(Now.AddDays(1) + TimeSpan.FromHours(24));
    }

    [Fact]
    public void Chaves_sao_zeradas_depois_do_uso()
    {
        var keys = Keys(OldSecret, Now.AddHours(1));

        keys.Clear();

        keys.Current.Should().OnlyContain(b => b == 0);
        keys.Previous.Should().OnlyContain(b => b == 0);
    }
}
