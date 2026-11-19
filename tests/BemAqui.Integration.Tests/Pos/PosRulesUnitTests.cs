using System.Security.Cryptography;
using System.Text;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Application.Shopfloor;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>PDV-01: regras puras (sem banco) do número da venda, do cadastro, da época do caixa e da assinatura do aparelho.</summary>
public class PosRulesUnitTests
{
    [Theory]
    [InlineData("LOJA", true)]
    [InlineData("CENTRO", true)]
    [InlineData("L1", true)]
    [InlineData("L", false)]
    [InlineData("LOJA-1", false)]
    [InlineData("LOJÃO", false)]
    [InlineData("LOJA123", false)]
    public void Codigo_da_loja_tem_2_a_6_letras_ou_numeros(string code, bool valid) =>
        PosRules.IsValidStoreCode(PosRules.NormalizeCode(code)).Should().Be(valid);

    [Fact]
    public void Numero_da_venda_leva_loja_caixa_epoca_e_sequencia()
    {
        PosSaleNumber.Format("LOJA", 2, 3, 127).Should().Be("LOJA-02.3-000127");
        PosSaleNumber.Prefix("LOJA", 2, 3).Should().Be("LOJA-02.3-");
        PosSaleNumber.Format("LOJA", 2, 3, 1).Should().NotBe(PosSaleNumber.Format("LOJA", 2, 4, 1), "a época nova não colide com a antiga");
        FluentActions.Invoking(() => PosSaleNumber.Format("LOJA", 2, 0, 1)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Troca_de_aparelho_sobe_a_epoca_e_a_revogacao_so_solta_o_aparelho_ativo()
    {
        var register = new Register { Number = 1 };
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        register.AssignDevice(first).Should().BeNull();
        register.DeviceEpoch.Should().Be(1);
        register.AssignDevice(second).Should().Be(first);
        register.DeviceEpoch.Should().Be(2);

        register.ReleaseDevice(first);
        register.ActiveDeviceId.Should().Be(second, "o aparelho antigo, revogado depois da troca, não solta o novo");
        register.ReleaseDevice(second);
        register.ActiveDeviceId.Should().BeNull();
        register.DeviceEpoch.Should().Be(2, "a época continua; o próximo aparelho abre a 3");
    }

    [Fact]
    public void Assinatura_do_aparelho_e_conferida_com_a_chave_publica_P256()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var payload = DeviceSignatureHeaders.Payload("post", "/api/v1/pos/sync/handshake", "1763460000000", Convert.ToBase64String(SHA256.HashData([])));
        var signature = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        payload.Should().StartWith("POST\n/api/v1/pos/sync/handshake\n1763460000000\n");
        RegisterDeviceService.IsValidPublicKey(publicKey).Should().BeTrue();
        RegisterDeviceService.IsValidPublicKey(Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo())).Should().BeFalse();
        RegisterDeviceService.IsValidPublicKey("não é base64").Should().BeFalse();
        RegisterDeviceService.VerifySignature(publicKey, payload, signature).Should().BeTrue();
        RegisterDeviceService.VerifySignature(publicKey, payload + " ", signature).Should().BeFalse();
        RegisterDeviceService.VerifySignature(Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()), payload, signature).Should().BeFalse();
        RegisterDeviceService.VerifySignature(publicKey, payload, "lixo").Should().BeFalse();
    }

    [Fact]
    public void Principal_do_aparelho_nao_tem_usuario_e_volta_igual()
    {
        var device = new DeviceIdentity(Guid.NewGuid(), "Register", Guid.NewGuid(), Guid.NewGuid(), 3, Revoked: true);
        var principal = DevicePrincipal.Create(Guid.NewGuid(), device);

        principal.FindFirst("sub").Should().BeNull();
        DevicePrincipal.From(principal).Should().Be(device);
    }

    [Theory]
    [InlineData(BemAqui.Parties.Contracts.PartyKind.PF, "52998224725", "***.***.247-**")]
    [InlineData(BemAqui.Parties.Contracts.PartyKind.PJ, "11222333000181", "11.222.333/0001-81")]
    [InlineData(BemAqui.Parties.Contracts.PartyKind.PF, null, null)]
    [InlineData(BemAqui.Parties.Contracts.PartyKind.PF, "123", null)]
    public void Cliente_na_tela_de_venda_desce_com_o_CPF_mascarado(BemAqui.Parties.Contracts.PartyKind kind, string? document, string? expected) =>
        BemAqui.Pos.Application.PosCustomerSearchService.Mask(kind, document).Should().Be(expected);
}
