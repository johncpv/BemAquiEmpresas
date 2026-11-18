using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// PDV-01 parte 2: PIN no aparelho de caixa (CA 11) e usuário de operação (CA 12, ADR 0049). A entrada por PIN chega pelo esquema Device (cookie e
/// assinatura do aparelho); a sessão por PIN do caixa só alcança a lista [PosPinSessionAllowed]; o usuário de operação nunca entra na retaguarda.
/// </summary>
public partial class PosTests
{
    private const string InitialPin = "482915";
    private const string OwnPin = "730284";

    /// <summary>GET assinado pelo aparelho (lista de operadores).</summary>
    private static HttpRequestMessage SignedGet(Device device, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var payload = DeviceSignatureHeaders.Payload("GET", path, timestamp, Convert.ToBase64String(SHA256.HashData([])));
        var signature = device.Key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        request.Headers.Add("Cookie", PosDeviceCookie.Name + "=" + device.Secret);
        request.Headers.Add(DeviceSignatureHeaders.Timestamp, timestamp);
        request.Headers.Add(DeviceSignatureHeaders.Signature, Convert.ToBase64String(signature));
        return request;
    }

    /// <summary>Entrada por PIN no caixa, assinada pelo aparelho.</summary>
    private async Task<HttpResponseMessage> PinLoginAsync(Device device, Guid userId, string pin, string? newPin = null)
    {
        var body = newPin is null
            ? JsonSerializer.Serialize(new { userId, pin })
            : JsonSerializer.Serialize(new { userId, pin, newPin, newPinConfirmation = newPin });
        return await Anonymous().SendAsync(Signed(device, "/api/v1/pos/sync/pin", body));
    }

    private async Task<string> PinTokenAsync(Device device, Guid userId, string pin) =>
        (await OkAsync(await PinLoginAsync(device, userId, pin))).GetProperty("accessToken").GetString()!;

    /// <summary>Cliente da sessão por PIN do caixa: o token do operador e o cookie do aparelho.</summary>
    private HttpClient Operator(string token, Device? device)
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (device is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", PosDeviceCookie.Name + "=" + device.Secret);
        }

        return client;
    }

    /// <summary>Grupo próprio do teste com o caixa (pos.view e pos.create) e, se pedido, ações a mais (ex.: uma reservada).</summary>
    private async Task<Guid> CashierGroupAsync(params string[] extra)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        var group = new Group { Name = Unique("Caixa ", 6), Description = "teste", CreatedAt = DateTimeOffset.UtcNow };
        db.Groups.Add(group);
        var grants = new[] { "pos.view", "pos.create" }.Concat(extra).Select(p => p.Split('.')).ToList();
        foreach (var module in grants.Select(g => g[0]).Distinct())
        {
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
        }

        db.GroupPermissions.AddRange(grants.Select(g => new GroupGrant { GroupId = group.Id, ModuleCode = g[0], Action = g[1] }));
        await db.SaveChangesAsync();
        return group.Id;
    }

    /// <summary>Loja com caixa e aparelho registrado; o Proprietário com TOTP recente.</summary>
    private async Task<(HttpClient Owner, Guid Store, Device Device)> RegisterAsync(HttpClient? owner = null)
    {
        owner ??= (await OwnerAsync(otp: true)).Client;
        var (store, register, _) = await StoreAsync(owner);
        var device = new Device();
        await OkAsync(await RegisterDeviceAsync(owner, device, store, register), HttpStatusCode.Created);
        return (owner, store, device);
    }

    private static async Task<Guid> CreateOperationUserAsync(HttpClient owner, Guid group, Guid store, string name = "Caio Operador")
    {
        var created = await OkAsync(await owner.PostAsJsonAsync("/api/v1/identity/operation-users",
            new { name, groupIds = new[] { group }, storeIds = new[] { store }, pin = InitialPin, pinConfirmation = InitialPin }), HttpStatusCode.Created);
        return created.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Usuario_de_operacao_entra_por_PIN_troca_o_inicial_e_a_sessao_so_alcanca_o_PDV()
    {
        await CommerceAsync(true);
        var (owner, store, device) = await RegisterAsync();
        using var registered = device;
        var group = await CashierGroupAsync();
        var id = await CreateOperationUserAsync(owner, group, store);

        // CA 12: na tela de usuários, com a marca de operação, sem e-mail e sem 2FA.
        var row = (await OkAsync(await owner.GetAsync("/api/v1/identity/users"))).EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == id);
        row.GetProperty("isOperationUser").GetBoolean().Should().BeTrue();
        row.GetProperty("email").GetString().Should().BeEmpty();
        row.GetProperty("twoFactor").GetString().Should().Be("none");

        // CA 12: login normal recusado (nem com o e-mail técnico), sem token.
        var login = await Anonymous().PostAsJsonAsync("/api/v1/auth/login", new { email = User.OperationEmail(id), password = "qualquer-senha-123" });
        login.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await login.Content.ReadAsStringAsync()).Should().NotContain("accessToken");

        // CA 11: o nome aparece no caixa (primeiro nome e inicial), só pela assinatura do aparelho.
        var operators = await OkAsync(await Anonymous().SendAsync(SignedGet(device, "/api/v1/pos/sync/operators")));
        operators.GetProperty("operators").EnumerateArray().Should().Contain(o => o.GetProperty("userId").GetGuid() == id && o.GetProperty("displayName").GetString() == "Caio O.");
        (await Anonymous().GetAsync("/api/v1/pos/sync/operators")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "sem o aparelho, a lista não existe");

        // O PIN inicial vale só para a primeira entrada, que pede a troca; o PIN novo não pode ser o mesmo.
        var change = await PinLoginAsync(device, id, InitialPin);
        change.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(change)).Should().Be("pin_change_required");
        (await PinLoginAsync(device, id, InitialPin, InitialPin)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var first = await PinLoginAsync(device, id, InitialPin, OwnPin);
        await OkAsync(first);
        var refresh = CookieOf(first, "__Host-bq_rt");
        refresh.Should().NotBeNull("a sessão por PIN do caixa renova pelo cookie de refresh");
        (await PinLoginAsync(device, id, InitialPin)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o PIN inicial deixou de valer");

        // Quem está no caixa; e a sessão por PIN do caixa só alcança a lista [PosPinSessionAllowed].
        var token = await PinTokenAsync(device, id, OwnPin);
        var op = Operator(token, device);
        var session = await OkAsync(await op.GetAsync("/api/v1/pos/session"));
        session.GetProperty("name").GetString().Should().Be("Caio Operador");
        session.GetProperty("isOperationUser").GetBoolean().Should().BeTrue();
        session.GetProperty("registerNumber").GetInt32().Should().Be(1);
        session.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).Should().Contain("create");
        foreach (var path in new[] { "/api/v1/me", "/api/v1/me/permissions", "/api/v1/pos/stores", "/api/v1/identity/users" })
        {
            var refused = await op.GetAsync(path);
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
            Code(await JsonAsync(refused)).Should().Be("pin_session_scope", path);
        }

        // Sem o cookie do aparelho de caixa (ou com o da fábrica), a sessão não vale.
        var noDevice = await Operator(token, null).GetAsync("/api/v1/pos/session");
        noDevice.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Code(await JsonAsync(noDevice)).Should().Be("device_not_registered");

        // Sair: a sessão cai no servidor; o PIN continua.
        (await op.PostAsync("/api/v1/pos/session/end", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var ended = await op.GetAsync("/api/v1/pos/session");
        ended.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Code(await JsonAsync(ended)).Should().Be("pin_session_ended");

        // O PIN nunca vai para a auditoria; a criação, o PIN inicial, a troca e as entradas ficam.
        var audits = await WithDbAsync(db => db.AuditEntries.Where(a => a.EntityId == id.ToString()).Select(a => new { a.Action, a.Changes }).ToListAsync());
        audits.Select(a => a.Action).Should().Contain(["OperationUserCreated", "PosPinCreated", "PosPinChangeRequired", "PosPinChanged", "PosPinLoginSucceeded", "PosPinSessionEnded"]);
        audits.Should().NotContain(a => a.Changes.Contains(InitialPin) || a.Changes.Contains(OwnPin));
        (await WithDbAsync(db => db.PosPins.SingleAsync(p => p.UserId == id))).PinHash.Should().NotContain(OwnPin);
    }

    [Fact]
    public async Task Sessao_do_caixa_renova_so_no_aparelho_dela_e_o_PIN_da_fabrica_nao_abre_o_caixa()
    {
        await CommerceAsync(true);
        var (owner, store, device) = await RegisterAsync();
        using var registered = device;
        var id = await CreateOperationUserAsync(owner, await CashierGroupAsync(), store);
        var login = await PinLoginAsync(device, id, InitialPin, OwnPin);
        await OkAsync(login);
        var refresh = ValueOf(CookieOf(login, "__Host-bq_rt")!);

        HttpRequestMessage Refresh(string? posSecret)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            request.Headers.Add("X-CSRF", "1");
            request.Headers.Add("Origin", "http://" + Host);
            request.Headers.Add("Cookie", "__Host-bq_rt=" + refresh + (posSecret is null ? string.Empty : "; " + PosDeviceCookie.Name + "=" + posSecret));
            return request;
        }

        var renewedResponse = await Anonymous().SendAsync(Refresh(device.Secret));
        var renewed = await OkAsync(renewedResponse);
        refresh = ValueOf(CookieOf(renewedResponse, "__Host-bq_rt")!);
        var token = renewed.GetProperty("accessToken").GetString()!;
        var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64Url(token.Split('.')[1]))).RootElement;
        payload.GetProperty("amr").EnumerateArray().Select(a => a.GetString()).Should().BeEquivalentTo(["pin", "hwk"], "continua sessão por PIN do caixa");

        // Sem o cookie do caixa, a renovação é recusada e a família cai.
        (await Anonymous().SendAsync(Refresh(null))).IsSuccessStatusCode.Should().BeFalse();
        (await Anonymous().SendAsync(Refresh(device.Secret))).IsSuccessStatusCode.Should().BeFalse("a família foi revogada");
    }

    [Fact]
    public async Task PIN_errado_bloqueia_e_apaga_e_operador_de_outra_loja_nao_entra()
    {
        await CommerceAsync(true);
        var (owner, store, device) = await RegisterAsync();
        using var registered = device;
        var (_, otherStore, otherDevice) = await RegisterAsync(owner);
        using var other = otherDevice;
        var id = await CreateOperationUserAsync(owner, await CashierGroupAsync(), store, "Bia Lopes");
        await OkAsync(await PinLoginAsync(device, id, InitialPin, OwnPin));

        // Outra loja: não aparece no caixa de lá e o PIN certo responde igual a PIN errado.
        var others = await OkAsync(await Anonymous().SendAsync(SignedGet(otherDevice, "/api/v1/pos/sync/operators")));
        others.GetProperty("operators").EnumerateArray().Should().NotContain(o => o.GetProperty("userId").GetGuid() == id);
        var elsewhere = await PinLoginAsync(otherDevice, id, OwnPin);
        elsewhere.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(elsewhere)).Should().Be("auth.pin_invalid");
        _ = otherStore;

        // 5 erros seguidos bloqueiam 15 min (o PIN certo também é recusado no bloqueio).
        for (var i = 1; i <= 4; i++)
        {
            Code(await JsonAsync(await PinLoginAsync(device, id, "111111"))).Should().Be("auth.pin_invalid");
        }

        Code(await JsonAsync(await PinLoginAsync(device, id, "111111"))).Should().Be("auth.pin_locked");
        Code(await JsonAsync(await PinLoginAsync(device, id, OwnPin))).Should().Be("auth.pin_locked");

        // O 10º erro em 24 h apaga o PIN; o gerente dá um PIN novo, que pede a troca de novo.
        await WithDbAsync(db => db.PosPins.Where(p => p.UserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LockedUntil, (DateTimeOffset?)null).SetProperty(p => p.FailuresInWindow, 9)));
        Code(await JsonAsync(await PinLoginAsync(device, id, "111111"))).Should().Be("auth.pin_erased");
        (await WithDbAsync(db => db.PosPins.AnyAsync(p => p.UserId == id))).Should().BeFalse();
        (await owner.PostAsJsonAsync($"/api/v1/identity/operation-users/{id}/pin", new { pin = InitialPin, pinConfirmation = InitialPin })).IsSuccessStatusCode.Should().BeTrue();
        Code(await JsonAsync(await PinLoginAsync(device, id, InitialPin))).Should().Be("pin_change_required");
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityId == id.ToString() && (a.Action == "PosPinLocked" || a.Action == "PosPinErased")))).Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Usuario_de_operacao_nunca_tem_acao_reservada_e_desativar_apaga_o_PIN_e_derruba_a_sessao()
    {
        await CommerceAsync(true);
        var (owner, store, device) = await RegisterAsync();
        using var registered = device;
        var cashier = await CashierGroupAsync();
        var approver = await CashierGroupAsync("sales.approve");

        // Grupo com ação reservada não aceita usuário de operação: na criação e na troca de grupos.
        var refused = await owner.PostAsJsonAsync("/api/v1/identity/operation-users",
            new { name = "Davi", groupIds = new[] { approver }, storeIds = new[] { store }, pin = InitialPin, pinConfirmation = InitialPin });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(refused)).Should().Be(GroupService.OperationUserGroupCode);
        var id = await CreateOperationUserAsync(owner, cashier, store, "Davi Souza");
        var setGroups = await owner.PutAsJsonAsync($"/api/v1/identity/users/{id}/groups", new { groupIds = new[] { cashier, approver } });
        setGroups.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(setGroups)).Should().Be(GroupService.OperationUserGroupCode);

        // Desativar (o desligamento do funcionário passa pelo mesmo caminho, USR-01) apaga o PIN e derruba a sessão por PIN.
        await OkAsync(await PinLoginAsync(device, id, InitialPin, OwnPin));
        var op = Operator(await PinTokenAsync(device, id, OwnPin), device);
        (await op.GetAsync("/api/v1/pos/session")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PostAsync($"/api/v1/identity/users/{id}/deactivate", null)).IsSuccessStatusCode.Should().BeTrue();
        (await WithDbAsync(db => db.PosPins.AnyAsync(p => p.UserId == id))).Should().BeFalse();
        (await op.GetAsync("/api/v1/pos/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Reativado, volta sem PIN: some do caixa até o gerente dar um PIN novo.
        (await owner.PostAsync($"/api/v1/identity/users/{id}/reactivate", null)).IsSuccessStatusCode.Should().BeTrue();
        var operators = await OkAsync(await Anonymous().SendAsync(SignedGet(device, "/api/v1/pos/sync/operators")));
        operators.GetProperty("operators").EnumerateArray().Should().NotContain(o => o.GetProperty("userId").GetGuid() == id);
    }

    [Fact]
    public async Task Usuario_completo_cria_o_proprio_PIN_do_caixa_e_perfil_reservado_nao_cria()
    {
        await CommerceAsync(true);
        var (owner, _, device) = await RegisterAsync();
        using var registered = device;
        var (userId, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "caixa" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            (await db.Users.SingleAsync(u => u.Id == userId)).Name = "Ana Completa";
            db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = await CashierGroupAsync() });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([userId]);
        }

        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: userId, stamp: stamp));
        (await client.PutAsJsonAsync("/api/v1/pos/my-pin", new { pin = "123456", confirmation = "123456" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "PIN óbvio");
        (await OkAsync(await client.PutAsJsonAsync("/api/v1/pos/my-pin", new { pin = OwnPin, confirmation = OwnPin }))).GetProperty("hasPin").GetBoolean().Should().BeTrue();
        await OkAsync(await PinLoginAsync(device, userId, OwnPin));

        // O Proprietário tem ação reservada: não cria PIN do caixa (o PIN de supervisor de balcão é da PDV-04).
        (await owner.PutAsJsonAsync("/api/v1/pos/my-pin", new { pin = OwnPin, confirmation = OwnPin })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Fabrica_sem_Comercio_nao_ve_nem_cria_usuario_de_operacao_nem_PIN_do_caixa()
    {
        await CommerceAsync(false);
        try
        {
            var (_, owner) = await OwnerAsync();
            var create = await owner.PostAsJsonAsync("/api/v1/identity/operation-users",
                new { name = "X", groupIds = Array.Empty<Guid>(), storeIds = Array.Empty<Guid>(), pin = InitialPin, pinConfirmation = InitialPin });
            create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(create)).Should().Be("module_not_released");
            var mine = await owner.GetAsync("/api/v1/pos/my-pin");
            mine.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(mine)).Should().Be("module_not_released");
            var operators = await Anonymous().GetAsync("/api/v1/pos/sync/operators");
            operators.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
        }
        finally
        {
            await CommerceAsync(true);
        }
    }

    [Fact]
    public async Task Gerente_sem_as_acoes_do_grupo_do_operador_nao_da_PIN_novo_nem_troca_a_loja()
    {
        // Revisão de segurança da PDV-01 (M1): "Novo PIN" é poder entrar no caixa como o operador; a delegação da ADR 0008 vale sobre os grupos dele.
        await CommerceAsync(true);
        var (owner, store, device) = await RegisterAsync();
        using var registered = device;
        var id = await CreateOperationUserAsync(owner, await CashierGroupAsync(), store, "Eva Prado");
        var weak = await ManagerAsync("identity.view", "identity.create");
        var reset = await weak.PostAsJsonAsync($"/api/v1/identity/operation-users/{id}/pin", new { pin = InitialPin, pinConfirmation = InitialPin });
        reset.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(reset)).Should().Be(GroupService.DelegationDeniedCode);
        var update = await weak.PutAsJsonAsync($"/api/v1/identity/operation-users/{id}", new { name = "Eva Prado", storeIds = new[] { store } });
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(update)).Should().Be(GroupService.DelegationDeniedCode);

        // Gerente que tem o que o grupo do operador dá (sem ser membro dele) pode.
        var full = await ManagerAsync("identity.view", "identity.create", "pos.view", "pos.create");
        (await full.PostAsJsonAsync($"/api/v1/identity/operation-users/{id}/pin", new { pin = InitialPin, pinConfirmation = InitialPin })).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task PINs_errados_em_paralelo_contam_um_a_um()
    {
        // Revisão de segurança da PDV-01 (M2): a trava por aparelho e por usuário põe as tentativas em fila; 5 em paralelo são 5 erros e bloqueiam.
        await CommerceAsync(true);
        var (owner, store, device) = await RegisterAsync();
        using var registered = device;
        var id = await CreateOperationUserAsync(owner, await CashierGroupAsync(), store, "Rui Paz");
        await OkAsync(await PinLoginAsync(device, id, InitialPin, OwnPin));

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PinLoginAsync(device, id, "111111")));
        var codes = new List<string>();
        foreach (var response in responses)
        {
            codes.Add(Code(await JsonAsync(response)));
        }

        codes.Count(c => c == "auth.pin_invalid").Should().Be(4, string.Join(",", codes));
        codes.Count(c => c == "auth.pin_locked").Should().Be(1, string.Join(",", codes));
        var row = await WithDbAsync(db => db.PosPins.AsNoTracking().SingleAsync(p => p.UserId == id));
        row.FailuresInWindow.Should().Be(5);
        row.LockedUntil.Should().NotBeNull();
    }

    /// <summary>Usuário completo (não Proprietário) num grupo próprio com as ações pedidas.</summary>
    private async Task<HttpClient> ManagerAsync(params string[] grants)
    {
        var (userId, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "gerente" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = Unique("Gerente ", 6), Description = "teste", CreatedAt = DateTimeOffset.UtcNow };
            db.Groups.Add(group);
            var split = grants.Select(p => p.Split('.')).ToList();
            foreach (var module in split.Select(g => g[0]).Distinct())
            {
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
            }

            db.GroupPermissions.AddRange(split.Select(g => new GroupGrant { GroupId = group.Id, ModuleCode = g[0], Action = g[1] }));
            db.UserGroups.Add(new UserGroup { UserId = userId, GroupId = group.Id });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([userId]);
        }

        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: userId, stamp: stamp));
        return client;
    }

    private static byte[] Base64Url(string value) =>
        Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '='));
}
