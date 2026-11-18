using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Domain;
using BemAqui.Pos.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Pos;

public partial class PosTests
{
    [Fact]
    public async Task Loja_e_caixa_conferem_codigo_numero_e_conta_Caixa()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync();
        var (location, cash1, cash2, bank) = await SetupAsync();
        var list = await DefaultListAsync(owner);
        var code = Unique("C", 5);

        // CA 2: código curto e único (normalizado em maiúsculas).
        var store = await OkAsync(await owner.PostAsJsonAsync("/api/v1/pos/stores", new { code = code.ToLowerInvariant(), name = "Loja Centro", stockLocationId = location, defaultPriceListId = list }), HttpStatusCode.Created);
        store.GetProperty("code").GetString().Should().Be(code);
        var storeId = store.GetProperty("id").GetGuid();
        var dup = await owner.PostAsJsonAsync("/api/v1/pos/stores", new { code, name = "Outra", stockLocationId = location, defaultPriceListId = list });
        dup.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.PostAsJsonAsync("/api/v1/pos/stores", new { code = "X", name = "Curta", stockLocationId = location, defaultPriceListId = list })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // CA 3: número único na loja; conta do tipo Caixa e que nenhum outro caixa usa.
        await OkAsync(await owner.PostAsJsonAsync($"/api/v1/pos/stores/{storeId}/registers", new { number = 2, cashAccountId = cash1, uiMode = "Touch" }), HttpStatusCode.Created);
        var sameNumber = await owner.PostAsJsonAsync($"/api/v1/pos/stores/{storeId}/registers", new { number = 2, cashAccountId = cash2, uiMode = "Touch" });
        sameNumber.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(sameNumber)).GetProperty("errors").TryGetProperty("number", out _).Should().BeTrue();
        var sameAccount = await owner.PostAsJsonAsync($"/api/v1/pos/stores/{storeId}/registers", new { number = 3, cashAccountId = cash1, uiMode = "Keyboard" });
        sameAccount.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(sameAccount)).GetProperty("errors").TryGetProperty("cashAccountId", out _).Should().BeTrue();
        (await owner.PostAsJsonAsync($"/api/v1/pos/stores/{storeId}/registers", new { number = 3, cashAccountId = bank, uiMode = "Keyboard" })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "conta do tipo Banco não é caixa");
        var ok = await OkAsync(await owner.PostAsJsonAsync($"/api/v1/pos/stores/{storeId}/registers", new { number = 3, cashAccountId = cash2, uiMode = "Keyboard" }), HttpStatusCode.Created);
        ok.GetProperty("registers").EnumerateArray().Select(r => r.GetProperty("number").GetInt32()).Should().Equal(2, 3);

        // A conta já usada some das opções do formulário; a loja e o caixa ficam na auditoria (módulo sales).
        var options = await OkAsync(await owner.GetAsync("/api/v1/pos/stores/options"));
        options.GetProperty("cashAccounts").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).Should().NotContain([cash1, cash2]);
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == "Store" && a.EntityId == storeId.ToString()))).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Registrar_aparelho_exige_TOTP_recente_avisa_o_Proprietario_e_troca_abre_epoca_nova()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync(otp: false);
        var (ownerId, confirmed) = await OwnerAsync(otp: true, name: "Rita Gerente");
        var (store, register, code) = await StoreAsync(confirmed);
        using var first = new Device();

        // CA 4: sem TOTP recente, 403 two_factor_required (a tela pede o código antes).
        var noOtp = await RegisterDeviceAsync(owner, first, store, register);
        noOtp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(noOtp)).Should().Be("two_factor_required");

        // Chave que não é P-256: recusada.
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var badKey = await confirmed.PostAsJsonAsync("/api/v1/identity/devices", new { name = "X", kind = "Register", storeId = store, registerId = register, publicKey = Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo()) });
        badKey.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var created = await OkAsync(await RegisterDeviceAsync(confirmed, first, store, register), HttpStatusCode.Created);
        created.GetProperty("kind").GetString().Should().Be("Register");
        created.GetProperty("epoch").GetInt32().Should().Be(1);
        first.Secret.Should().NotBeNullOrEmpty("o segredo vai só para o cookie __Host-bq_pos");
        (await NotificationsAsync($"Novo caixa registrado na loja Loja {code} por Rita Gerente")).Should().NotBeEmpty("CA 4: o Proprietário é avisado");
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == "ShopfloorDevice" && a.Action == "DeviceRegistered" && a.EntityId == created.GetProperty("id").GetString()))).Should().Be(1);

        // CA 6: o caixa já tem aparelho; sem confirmar, 409 com a pergunta; confirmando, o anterior cai e a época sobe.
        using var second = new Device();
        var conflict = await RegisterDeviceAsync(confirmed, second, store, register, name: "Caixa novo");
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(conflict);
        Code(problem).Should().Be("register_has_device");
        problem.GetProperty("detail").GetString().Should().Be("O caixa 01 já tem um aparelho (Caixa da frente). Trocar?");
        var replaced = await OkAsync(await RegisterDeviceAsync(confirmed, second, store, register, replace: true, name: "Caixa novo"), HttpStatusCode.Created);
        replaced.GetProperty("epoch").GetInt32().Should().Be(2);

        var old = await WithDbAsync(db => db.ShopfloorDevices.SingleAsync(d => d.Id == created.GetProperty("id").GetGuid()));
        old.RevokedReason.Should().Be("replaced");
        old.RegisterEpoch.Should().Be(1);
        var row = await WithDbAsync(db => db.Registers.SingleAsync(r => r.Id == register));
        row.ActiveDeviceId.Should().Be(replaced.GetProperty("id").GetGuid());
        row.DeviceEpoch.Should().Be(2);

        // As vendas que o aparelho antigo ainda não enviou não colidem com as do novo: a época entra no número e na chave.
        PosSaleNumber.Format(code, 1, old.RegisterEpoch, 1).Should().NotBe(PosSaleNumber.Format(code, 1, row.DeviceEpoch, 1));
        PosSaleNumber.Format(code, 1, 2, 1).Should().Be($"{code}-01.2-000001");

        // O novo aparelho sincroniza; o antigo perde a descida (401, pede novo registro).
        var handshake = await OkAsync(await HandshakeAsync(second));
        handshake.GetProperty("salePrefix").GetString().Should().Be($"{code}-01.2-");
        var revoked = await HandshakeAsync(first);
        revoked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Code(await JsonAsync(revoked)).Should().Be("device_revoked");
        _ = ownerId;
    }

    [Fact]
    public async Task Aparelho_revogado_continua_autenticando_so_para_subir_e_o_segredo_gira_na_sincronizacao()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync(otp: true);
        var (store, register, _) = await StoreAsync(owner);
        using var device = new Device();
        var created = await OkAsync(await RegisterDeviceAsync(owner, device, store, register), HttpStatusCode.Created);
        var deviceId = created.GetProperty("id").GetGuid();

        // O segredo gira a cada sincronização bem-sucedida; o anterior ainda vale com a assinatura certa (resposta perdida na rede não bloqueia o caixa).
        var original = device.Secret!;
        (await HandshakeAsync(device)).StatusCode.Should().Be(HttpStatusCode.OK);
        device.Secret.Should().NotBe(original);
        (await WithDbAsync(db => db.ShopfloorDevices.SingleAsync(d => d.Id == deviceId))).LastSyncAt.Should().NotBeNull();
        (await HandshakeAsync(device, secret: original)).StatusCode.Should().Be(HttpStatusCode.OK, "o segredo anterior com a assinatura do aparelho");

        // Bloqueio (CA 8): o caixa fica sem aparelho e o Proprietário é avisado.
        (await owner.DeleteAsync($"/api/v1/identity/devices/{deviceId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await WithDbAsync(db => db.Registers.SingleAsync(r => r.Id == register))).ActiveDeviceId.Should().BeNull();
        var refused = await HandshakeAsync(device);
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Code(await JsonAsync(refused)).Should().Be("device_revoked");

        // A subida (OFF-02) aceita o revogado: o esquema reconhece o aparelho com a assinatura dele e marca revogado; nunca como ativo.
        await using var scope = await ScopeAsync();
        var auth = scope.ServiceProvider.GetRequiredService<IDeviceAuthenticator>();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var payload = DeviceSignatureHeaders.Payload("POST", "/api/v1/pos/sync/events", timestamp, Convert.ToBase64String(SHA256.HashData([])));
        var signature = Convert.ToBase64String(device.Key.SignData(System.Text.Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        var result = await auth.AuthenticateAsync(device.Secret, _ => Task.FromResult(payload), signature);
        result.Status.Should().Be(DeviceAuthStatus.Revoked);
        result.Device!.RegisterId.Should().Be(register);
        (await auth.AuthenticateAsync(device.Secret, _ => Task.FromResult(payload + "x"), signature)).Status.Should().Be(DeviceAuthStatus.Invalid, "texto adulterado");
    }

    [Fact]
    public async Task Loja_e_caixa_vem_do_principal_do_aparelho_e_outro_aparelho_nao_se_passa_por_ele()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync(otp: true);
        var (storeA, registerA, codeA) = await StoreAsync(owner);
        var (storeB, registerB, _) = await StoreAsync(owner);
        using var deviceA = new Device();
        using var deviceB = new Device();
        (await RegisterDeviceAsync(owner, deviceA, storeA, registerA)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await RegisterDeviceAsync(owner, deviceB, storeB, registerB)).StatusCode.Should().Be(HttpStatusCode.Created);

        // CA 10: a loja e o caixa vêm do principal; o que vier na URL é ignorado.
        var a = await OkAsync(await HandshakeAsync(deviceA, $"/api/v1/pos/sync/handshake?storeId={storeB}&registerId={registerB}"));
        a.GetProperty("storeId").GetGuid().Should().Be(storeA);
        a.GetProperty("registerId").GetGuid().Should().Be(registerA);
        a.GetProperty("storeCode").GetString().Should().Be(codeA);

        // O segredo de um aparelho com a chave do outro (ou a assinatura de outra requisição) não passa.
        (await HandshakeAsync(deviceA, signer: deviceB.Key)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await HandshakeAsync(deviceB, secret: deviceA.Secret)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var stale = Signed(deviceA, "/api/v1/pos/sync/handshake");
        stale.Headers.Remove(DeviceSignatureHeaders.Timestamp);
        stale.Headers.Add(DeviceSignatureHeaders.Timestamp, DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        var skew = await Anonymous().SendAsync(stale);
        skew.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "instante fora da tolerância");
        var skewProblem = await JsonAsync(skew);
        Code(skewProblem).Should().Be("device_clock_skew", "B5: a tela pede para acertar o relógio, não para registrar de novo");
        skewProblem.TryGetProperty("serverTime", out _).Should().BeTrue();

        // O aparelho não é usuário: com o cookie e a assinatura, nenhuma rota de usuário responde.
        var asUser = Signed(deviceA, "/api/v1/pos/stores");
        asUser.Method = HttpMethod.Get;
        (await Anonymous().SendAsync(asUser)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // O aparelho de caixa não abre o /fab (cookie e regras próprios).
        var fab = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/pin/users");
        fab.Headers.Add("Cookie", ShopfloorDeviceCookie.Name + "=" + deviceA.Secret);
        fab.Headers.Add("X-CSRF", "1");
        (await Anonymous().SendAsync(fab)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Aparelhos_da_fabrica_ficam_como_Fabrica_e_o_teto_e_contado_por_tipo()
    {
        await CommerceAsync(true);
        var (ownerId, owner) = await OwnerAsync(otp: true);

        // CA 5: aparelho gravado sem o tipo (como os de antes da migration) fica Fábrica pelo padrão da coluna.
        var factoryId = Guid.CreateVersion7();
        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity.shopfloor_devices (id, name, secret_hash, secret_rotated_at, created_by, created_at, last_seen_at, expires_at, theme, pin_failures)
            VALUES ({factoryId}, 'Tablet antigo', {Guid.NewGuid().ToString("N")}, now(), {ownerId}, now(), now(), now() + interval '30 days', 'System', 0)
            """));
        (await WithDbAsync(db => db.ShopfloorDevices.SingleAsync(d => d.Id == factoryId))).Kind.Should().Be(DeviceKind.Factory);

        // CA 7: com o teto da fábrica cheio, o caixa ainda registra; e a lista conta como ativos só os da fábrica.
        var activeFactory = await WithDbAsync(db => db.ShopfloorDevices.CountAsync(d => d.Kind == DeviceKind.Factory && d.RevokedAt == null && d.ExpiresAt > DateTimeOffset.UtcNow));
        var original = await SetFactoryCapAsync(activeFactory.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            var (store, register, _) = await StoreAsync(owner);
            using var device = new Device();
            (await RegisterDeviceAsync(owner, device, store, register)).StatusCode.Should().Be(HttpStatusCode.Created);
            var list = await OkAsync(await owner.GetAsync("/api/v1/settings/security/shopfloor-devices"));
            list.GetProperty("activeCount").GetInt32().Should().Be(activeFactory);
            list.GetProperty("devices").EnumerateArray().Should().Contain(d => d.GetProperty("kind").GetString() == "Register" && d.GetProperty("registerLabel").GetString()!.EndsWith("Caixa 01", StringComparison.Ordinal));
        }
        finally
        {
            await SetFactoryCapAsync(original);
        }
    }

    /// <summary>Troca o teto dos aparelhos da fábrica (o seed já grava o parâmetro) e devolve o valor anterior.</summary>
    private Task<string?> SetFactoryCapAsync(string? value) => WithDbAsync(async db =>
    {
        var row = await db.TenantSettings.SingleOrDefaultAsync(s => s.Key == TenantSetting.MaxShopfloorDevices);
        var before = row?.Value;
        if (row is null && value is not null)
        {
            db.TenantSettings.Add(new TenantSetting { Key = TenantSetting.MaxShopfloorDevices, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else if (row is not null && value is null)
        {
            db.TenantSettings.Remove(row);
        }
        else if (row is not null)
        {
            row.Value = value!;
        }

        await db.SaveChangesAsync();
        return before;
    });

    [Fact]
    public async Task Fabrica_nao_ve_lojas_caixas_nem_aparelhos_do_PDV()
    {
        await CommerceAsync(true);
        var (_, owner) = await OwnerAsync(otp: true);
        var (store, register, _) = await StoreAsync(owner);
        using var device = new Device();
        (await RegisterDeviceAsync(owner, device, store, register)).StatusCode.Should().Be(HttpStatusCode.Created);

        await CommerceAsync(false);
        try
        {
            // Como a Konebras (só factory): pos e sales ocultos, e as rotas respondem 403 module_not_released, inclusive a do aparelho.
            foreach (var path in new[] { "/api/v1/pos/stores", "/api/v1/identity/devices" })
            {
                var denied = await owner.GetAsync(path);
                denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
                Code(await JsonAsync(denied)).Should().Be("module_not_released");
            }

            (await HandshakeAsync(device)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "o aparelho do caixa também para");
            var me = await OkAsync(await owner.GetAsync("/api/v1/me/permissions"));
            me.GetProperty("modules").EnumerateArray().Where(m => m.GetProperty("code").GetString() is "pos" or "sales" && !(m.TryGetProperty("hidden", out var h) && h.GetBoolean()))
                .Should().BeEmpty("oculto para a fábrica (ADR 0046)");
        }
        finally
        {
            await CommerceAsync(true);
        }
    }
}
