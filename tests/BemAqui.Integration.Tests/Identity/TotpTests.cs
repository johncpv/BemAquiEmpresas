using BemAqui.BuildingBlocks.Security;
using System.Text;
using BemAqui.Identity.Application;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>Unidade: TOTP pelos vetores da RFC 6238 (SHA-1) e Base32 da RFC 4648.</summary>
public class TotpTests
{
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59, "287082")]
    [InlineData(1111111109, "081804")]
    [InlineData(1234567890, "005924")]
    [InlineData(2000000000, "279037")]
    public void Codigo_bate_com_os_vetores_da_RFC_6238(long unixSeconds, string expected) =>
        Totp.Code(RfcSecret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))).Should().Be(expected);

    [Fact]
    public void Aceita_um_intervalo_antes_e_um_depois_e_recusa_o_resto()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepAt(now);

        Totp.Match(RfcSecret, Totp.Code(RfcSecret, step), now).Should().Be(step);
        Totp.Match(RfcSecret, Totp.Code(RfcSecret, step - 1), now).Should().Be(step - 1);
        Totp.Match(RfcSecret, Totp.Code(RfcSecret, step + 1), now).Should().Be(step + 1);
        Totp.Match(RfcSecret, Totp.Code(RfcSecret, step - 2), now).Should().BeNull();
        Totp.Match(RfcSecret, "12345", now).Should().BeNull();
        Totp.Match(RfcSecret, null, now).Should().BeNull();
    }

    [Fact]
    public void Base32_ida_e_volta_e_chave_manual_em_grupos()
    {
        Base32.Encode("foobar"u8).Should().Be("MZXW6YTBOI");
        Base32.Decode("mzxw 6ytb oi").Should().Equal("foobar"u8.ToArray());
        var secret = Totp.NewSecret();
        Base32.Decode(Totp.ManualKey(secret)).Should().Equal(secret);
        Totp.OtpAuthUri("BemAqui Empresas", "ana@konebras.com.br", secret).Should()
            .StartWith("otpauth://totp/BemAqui%20Empresas:ana%40konebras.com.br?secret=" + Base32.Encode(secret));
    }
}
