using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// PDV-01: lojas, caixas e o aparelho de caixa registrado, com PostgreSQL real. O tenant A da classe ganha o perfil Comércio (nasce fábrica, como a
/// Konebras); cada teste cria a própria loja, com local, tabela e contas próprias.
/// </summary>
public partial class PosTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static string Unique(string prefix, int size = 4) => prefix + Guid.NewGuid().ToString("N")[..size].ToUpperInvariant();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static string? CookieOf(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.LastOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal)) : null;

    private static string ValueOf(string setCookie) => setCookie.Split(';')[0][(setCookie.IndexOf('=', StringComparison.Ordinal) + 1)..];

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Liga ou desliga o perfil Comércio do tenant A.</summary>
    private async Task CommerceAsync(bool on)
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Application.Modules.TenantModuleService>();
        var result = on
            ? await service.ApplyProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Retail, null, null)
            : await service.RemoveProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Retail, null, null);
        result.IsSuccess.Should().BeTrue();
        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Proprietário do tenant A; com <paramref name="otp"/>, o token traz a prova recente do TOTP (registrar aparelho, CA 4).</summary>
    private async Task<(Guid Id, HttpClient Client)> OwnerAsync(bool otp = false, string name = "Paula Lima")
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "pdv" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            (await db.Users.SingleAsync(u => u.Id == id)).Name = name;
            var groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp, otpAt: otp ? DateTimeOffset.UtcNow : null));
        return (id, client);
    }

    /// <summary>Local de estoque e duas contas Caixa próprias do teste.</summary>
    private async Task<(Guid Location, Guid Cash1, Guid Cash2, Guid Bank)> SetupAsync()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var location = new Location { Code = Unique("L"), Name = "Loja " + Unique(string.Empty), CreatedAt = DateTimeOffset.UtcNow };
        db.Locations.Add(location);
        var cash1 = new BemAqui.Finance.Domain.BankAccount { Name = Unique("Caixa "), Kind = BankAccountKind.Cash, CreatedAt = DateTimeOffset.UtcNow };
        var cash2 = new BemAqui.Finance.Domain.BankAccount { Name = Unique("Caixa "), Kind = BankAccountKind.Cash, CreatedAt = DateTimeOffset.UtcNow };
        var bank = new BemAqui.Finance.Domain.BankAccount { Name = Unique("Banco "), Kind = BankAccountKind.Bank, BankCode = "077", Agency = "0001", Account = "123-4", CreatedAt = DateTimeOffset.UtcNow };
        db.BankAccounts.AddRange(cash1, cash2, bank);
        await db.SaveChangesAsync();
        return (location.Id, cash1.Id, cash2.Id, bank.Id);
    }

    private static async Task<Guid> DefaultListAsync(HttpClient owner) =>
        (await OkAsync(await owner.GetAsync("/api/v1/pos/stores/options"))).GetProperty("defaultPriceListId").GetGuid();

    /// <summary>Loja com o caixa 01; devolve a loja e o caixa.</summary>
    private async Task<(Guid Store, Guid Register, string Code)> StoreAsync(HttpClient owner)
    {
        var (location, cash, _, _) = await SetupAsync();
        var code = Unique("S", 5);
        var store = await OkAsync(await owner.PostAsJsonAsync("/api/v1/pos/stores", new { code, name = "Loja " + code, stockLocationId = location, defaultPriceListId = await DefaultListAsync(owner) }), HttpStatusCode.Created);
        var withRegister = await OkAsync(await owner.PostAsJsonAsync($"/api/v1/pos/stores/{store.GetProperty("id").GetGuid()}/registers", new { number = 1, cashAccountId = cash, uiMode = "Keyboard" }), HttpStatusCode.Created);
        return (store.GetProperty("id").GetGuid(), withRegister.GetProperty("registers")[0].GetProperty("id").GetGuid(), code);
    }

    /// <summary>Aparelho de caixa com chave ECDSA P-256 (o WebCrypto do navegador faz o mesmo, sem exportar a privada).</summary>
    private sealed class Device : IDisposable
    {
        public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public string PublicKey => Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());

        public string? Secret { get; set; }

        public void Dispose() => Key.Dispose();
    }

    private static async Task<HttpResponseMessage> RegisterDeviceAsync(HttpClient owner, Device device, Guid store, Guid register, bool replace = false, string name = "Caixa da frente")
    {
        var response = await owner.PostAsJsonAsync("/api/v1/identity/devices", new { name, kind = "Register", storeId = store, registerId = register, publicKey = device.PublicKey, replaceActive = replace });
        if (CookieOf(response, PosDeviceCookie.Name) is { } cookie)
        {
            device.Secret = ValueOf(cookie);
        }

        return response;
    }

    /// <summary>Requisição assinada pelo aparelho (ADR 0048 §2), só com o cookie do aparelho: sem usuário.</summary>
    private static HttpRequestMessage Signed(Device device, string pathAndQuery, string? body = null, ECDsa? signer = null, string? secret = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, pathAndQuery);
        var bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var payload = DeviceSignatureHeaders.Payload("POST", pathAndQuery, timestamp, Convert.ToBase64String(SHA256.HashData(bytes)));
        var signature = (signer ?? device.Key).SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        request.Headers.Add("Cookie", PosDeviceCookie.Name + "=" + (secret ?? device.Secret));
        request.Headers.Add(DeviceSignatureHeaders.Timestamp, timestamp);
        request.Headers.Add(DeviceSignatureHeaders.Signature, Convert.ToBase64String(signature));
        return request;
    }

    private HttpClient Anonymous()
    {
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        return client;
    }

    /// <summary>Aperto de mão do aparelho; guarda o segredo novo (o segredo do caixa gira a cada sincronização).</summary>
    private async Task<HttpResponseMessage> HandshakeAsync(Device device, string path = "/api/v1/pos/sync/handshake", ECDsa? signer = null, string? secret = null)
    {
        var response = await Anonymous().SendAsync(Signed(device, path, null, signer, secret));
        if (response.IsSuccessStatusCode && CookieOf(response, PosDeviceCookie.Name) is { } cookie)
        {
            device.Secret = ValueOf(cookie);
        }

        return response;
    }

    private async Task<List<string>> NotificationsAsync(string startsWith) =>
        await WithDbAsync(db => db.Notifications.Where(n => n.Title.StartsWith(startsWith)).Select(n => n.Title).ToListAsync());
}
