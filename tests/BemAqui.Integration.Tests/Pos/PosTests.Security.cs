using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>Revisão de segurança do PR #227 (PDV-01): clones revezando o segredo (M1), outro tenant (host) e a revogação do caixa só com pos.manage_devices (B6).</summary>
public partial class PosTests
{
    [Fact]
    public async Task Clone_que_reapresenta_o_segredo_anterior_depois_da_troca_e_revogado()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync(otp: true, name: "Dono do revezamento");
        var (store, register, _) = await StoreAsync(owner);
        using var original = new Device();
        var created = await OkAsync(await RegisterDeviceAsync(owner, original, store, register), HttpStatusCode.Created);
        var deviceId = created.GetProperty("id").GetGuid();
        var s0 = original.Secret!;

        // A sincroniza (S0 → S1). O clone B, com a mesma chave e o S0, sincroniza com o anterior: passa uma vez (parece resposta perdida) e recebe S2.
        (await HandshakeAsync(original)).StatusCode.Should().Be(HttpStatusCode.OK);
        var s1 = original.Secret!;
        var clone = await HandshakeAsync(original, secret: s0);
        clone.StatusCode.Should().Be(HttpStatusCode.OK);
        original.Secret = s1;

        // A volta com S1, que virou o anterior já aposentado (a rotação foi pedida com o anterior): é reuso. O aparelho é revogado e o Proprietário avisado.
        var reuse = await HandshakeAsync(original);
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var device = await WithDbAsync(db => db.ShopfloorDevices.SingleAsync(d => d.Id == deviceId));
        device.RevokedAt.Should().NotBeNull();
        device.RevokedReason.Should().Be("secret_reused");
        (await WithDbAsync(db => db.Registers.SingleAsync(r => r.Id == register))).ActiveDeviceId.Should().BeNull("o caixa fica sem aparelho");
        (await NotificationsAsync("Aparelho Caixa da frente revogado: o segredo antigo")).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Usar_o_segredo_atual_aposenta_o_anterior()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync(otp: true);
        var (store, register, _) = await StoreAsync(owner);
        using var device = new Device();
        var created = await OkAsync(await RegisterDeviceAsync(owner, device, store, register), HttpStatusCode.Created);
        var s0 = device.Secret!;
        (await HandshakeAsync(device)).StatusCode.Should().Be(HttpStatusCode.OK);
        var s1 = device.Secret!;

        // O aparelho usa o S1 (atual): o S0 se aposenta; a rotação deste aperto de mão põe o S1 como anterior válido.
        (await HandshakeAsync(device)).StatusCode.Should().Be(HttpStatusCode.OK);
        var row = await WithDbAsync(db => db.ShopfloorDevices.SingleAsync(d => d.Id == created.GetProperty("id").GetGuid()));
        row.PreviousSecretRetiredAt.Should().BeNull("a rotação pedida com o atual deixa o anterior novo valer para a resposta perdida");
        (await HandshakeAsync(device, secret: s1)).StatusCode.Should().Be(HttpStatusCode.OK, "S1 é o anterior, ainda não aposentado");
        (await HandshakeAsync(device, secret: s0)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "S0 saiu de vez");
    }

    [Fact]
    public async Task Aparelho_de_um_tenant_nao_entra_no_host_de_outro_e_o_caixa_nao_e_revogado_pela_rota_da_fabrica()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync(otp: true);
        var (store, register, _) = await StoreAsync(owner);
        using var device = new Device();
        var created = await OkAsync(await RegisterDeviceAsync(owner, device, store, register), HttpStatusCode.Created);

        // O mesmo cookie e a mesma assinatura no host do tenant B: o segredo não existe lá.
        var other = tenancy.Factory.CreateClient();
        other.DefaultRequestHeaders.Host = "b.localhost";
        (await other.SendAsync(Signed(device, "/api/v1/pos/sync/handshake"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // B6: a rota de Configurações (settings.edit ou production.approve) não revoga caixa do PDV.
        var settings = await owner.PostAsync($"/api/v1/settings/security/shopfloor-devices/{created.GetProperty("id").GetGuid()}/revoke", null);
        settings.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await HandshakeAsync(device)).StatusCode.Should().Be(HttpStatusCode.OK, "o caixa continua ativo");
    }
}
