using System.Globalization;
using System.Text;
using BemAqui.BuildingBlocks.Webhooks;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>
/// FIS-01 CA 11 (lacuna L-g da ADR 0027): assinatura do EmissorFiscal no formato <c>t=…,v1=…</c> sobre <c>{timestamp}.{corpo}</c>,
/// janela de 5 min, dois <c>v1</c> na rotação e o segredo anterior só dentro das 24 h.
/// </summary>
public class WebhookTimestampedSignatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("segredo-do-emissor-fiscal-32-byt");
    private static readonly byte[] OldSecret = Encoding.UTF8.GetBytes("segredo-antigo-do-emissor-fiscal");
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"tipo\":\"nfe.autorizada\",\"documentoId\":\"019fca35\"}");
    private static readonly TimestampedSignatureValidator Validator = new();

    private static string Ts(DateTimeOffset at) => at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static WebhookRequest Request(string signature, string? timestamp = null, byte[]? body = null)
    {
        var headers = new Dictionary<string, string> { ["X-BemAqui-Signature"] = signature };
        if (timestamp is not null)
        {
            headers["X-BemAqui-Timestamp"] = timestamp;
        }

        return new WebhookRequest(body ?? Body, headers, null, null);
    }

    private static WebhookKeys Keys(byte[]? previous = null, DateTimeOffset? until = null) => new(Secret.ToArray(), previous?.ToArray(), until);

    [Fact]
    public void Assinatura_sobre_timestamp_e_corpo_e_aceita()
    {
        var ts = Ts(Now);

        Validator.Validate(Request(TimestampedSignatureValidator.Sign(ts, Body, Secret), ts), Keys(), Now).Should().Be(WebhookOriginMatch.Current);
        Validator.Validate(Request(TimestampedSignatureValidator.Sign(ts, Body, Secret)), Keys(), Now).Should().Be(WebhookOriginMatch.Current, "o cabeçalho separado é opcional");
    }

    [Fact]
    public void Vetor_calculado_fora_confere_com_o_contrato_do_Emissor()
    {
        // HMAC-SHA256 em hex minúsculo de "{t}.{corpo cru}" com o segredo do cadastro do webhook (UTF-8), calculado com o openssl:
        // printf '1785758400.{"tipo":"nfe.autorizada"}' | openssl dgst -sha256 -hmac 'segredo-do-cadastro-do-webhook'
        const string expected = "45006553d644f5868c16c68a42097e5d4a4ee7aced22712f3f441d74b405fb5d";
        var body = Encoding.UTF8.GetBytes("{\"tipo\":\"nfe.autorizada\"}");
        var keys = new WebhookKeys(Encoding.UTF8.GetBytes("segredo-do-cadastro-do-webhook"), null, null);
        var at = DateTimeOffset.FromUnixTimeSeconds(1785758400).AddMinutes(4);

        TimestampedSignatureValidator.Sign("1785758400", body, keys.Current!).Should().Be("t=1785758400,v1=" + expected);
        Validator.Validate(Request("t=1785758400,v1=" + expected, "1785758400", body), keys, at).Should().Be(WebhookOriginMatch.Current);
        Validator.Validate(Request("t=1785758400, v1=" + new string('0', 64) + ", v1=" + expected, "1785758400", body), keys, at)
            .Should().Be(WebhookOriginMatch.Current, "confere contra cada v1, com espaço depois da vírgula");
        Validator.Validate(Request("t=1785758400,v1=" + expected, "1785758400", body), keys, at.AddMinutes(2))
            .Should().Be(WebhookOriginMatch.Invalid, "6 min depois do t");
    }

    [Fact]
    public void Timestamp_fora_da_janela_de_5_minutos_e_recusado_mesmo_com_assinatura_certa()
    {
        var old = Ts(Now.AddMinutes(-6));
        var future = Ts(Now.AddMinutes(6));

        Validator.Validate(Request(TimestampedSignatureValidator.Sign(old, Body, Secret), old), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        Validator.Validate(Request(TimestampedSignatureValidator.Sign(future, Body, Secret), future), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        var edge = Ts(Now.AddMinutes(-5));
        Validator.Validate(Request(TimestampedSignatureValidator.Sign(edge, Body, Secret), edge), Keys(), Now).Should().Be(WebhookOriginMatch.Current);
    }

    [Fact]
    public void Trocar_o_timestamp_sem_reassinar_e_recusado()
    {
        var ts = Ts(Now.AddMinutes(-10));
        var signature = TimestampedSignatureValidator.Sign(ts, Body, Secret);
        var replayed = signature.Replace("t=" + ts, "t=" + Ts(Now), StringComparison.Ordinal);

        Validator.Validate(Request(replayed, Ts(Now)), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid, "a assinatura cobre o timestamp");
    }

    [Fact]
    public void Cabecalho_de_timestamp_diferente_do_t_e_recusado()
    {
        var ts = Ts(Now);

        Validator.Validate(Request(TimestampedSignatureValidator.Sign(ts, Body, Secret), Ts(Now.AddSeconds(-1))), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Corpo_alterado_formato_errado_ou_t_repetido_sao_recusados()
    {
        var ts = Ts(Now);
        var good = TimestampedSignatureValidator.Sign(ts, Body, Secret);
        var altered = Body.ToArray();
        altered[^3] ^= 0x01;

        Validator.Validate(Request(good, ts, altered), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        Validator.Validate(Request("sha256=" + good[(good.IndexOf("v1=", StringComparison.Ordinal) + 3)..], ts), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        Validator.Validate(Request("t=" + ts, ts), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid, "sem v1");
        Validator.Validate(Request(good + ",t=" + ts, ts), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid, "dois t");
        Validator.Validate(Request(good.Replace("v1=", "v1=zz", StringComparison.Ordinal), ts), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
        Validator.Validate(Request(string.Empty, ts), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Na_rotacao_dois_v1_e_basta_um_conferir()
    {
        var ts = Ts(Now);
        var both = TimestampedSignatureValidator.Sign(ts, Body, OldSecret, Secret);

        Validator.Validate(Request(both, ts), Keys(), Now).Should().Be(WebhookOriginMatch.Current);
    }

    [Fact]
    public void Segredo_anterior_vale_so_dentro_das_24_horas()
    {
        var ts = Ts(Now);
        var signedWithOld = TimestampedSignatureValidator.Sign(ts, Body, OldSecret);

        Validator.Validate(Request(signedWithOld, ts), Keys(OldSecret, Now.AddHours(1)), Now).Should().Be(WebhookOriginMatch.Previous);
        Validator.Validate(Request(signedWithOld, ts), Keys(OldSecret, Now.AddSeconds(-1)), Now).Should().Be(WebhookOriginMatch.Invalid);
        Validator.Validate(Request(signedWithOld, ts), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }

    [Fact]
    public void Mais_de_quatro_assinaturas_e_recusado()
    {
        var ts = Ts(Now);

        Validator.Validate(Request(TimestampedSignatureValidator.Sign(ts, Body, OldSecret, OldSecret, OldSecret, OldSecret, Secret), ts), Keys(), Now).Should().Be(WebhookOriginMatch.Invalid);
    }
}
