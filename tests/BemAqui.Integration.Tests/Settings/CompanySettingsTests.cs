using System.Buffers.Binary;
using System.Net.Http.Headers;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Attachments;
using BemAqui.Integration.Tests.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Settings;

/// <summary>CAD-06: dados e logo da empresa, numeração editável e parâmetros de cálculo.</summary>
public partial class CompanySettingsTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>, IDisposable
{
    private const string Host = "a.localhost";

    private readonly FakeScanner _scanner = new();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "bqe-cad06-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b
        .UseSetting("Storage:Root", _storage)
        .ConfigureTestServices(s => s.AddSingleton<IVirusScanner>(_scanner)));

    public void Dispose()
    {
        _factory?.Dispose();
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task<HttpClient> ClientAsync(string? seedKey = Group.OwnerSeedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "s" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid group;
            if (seedKey == "view")
            {
                var g = new Group { Name = "Consulta de configurações " + Guid.NewGuid().ToString("N")[..6], Description = "Só vê" };
                db.Groups.Add(g);
                db.GroupModules.Add(new GroupModule { GroupId = g.Id, ModuleCode = "settings", Enabled = true });
                db.GroupPermissions.Add(new GroupGrant { GroupId = g.Id, ModuleCode = "settings", Action = "view" });
                group = g.Id;
            }
            else
            {
                group = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync();
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group });
            await db.SaveChangesAsync();
        }

        return Anonymous(new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp)));
    }

    private HttpClient Anonymous(AuthenticationHeaderValue? auth = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = auth;
        return client;
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>PNG com o cabeçalho IHDR na largura e altura pedidas (o suficiente para a allowlist e a leitura de tamanho).</summary>
    internal static byte[] Png(int width, int height, int extraBytes = 0)
    {
        var data = new byte[33 + extraBytes];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), height);
        data[24] = 8;
        data[25] = 2;
        return data;
    }

    private static MultipartFormDataContent File(byte[] content, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name);
        return form;
    }
}
