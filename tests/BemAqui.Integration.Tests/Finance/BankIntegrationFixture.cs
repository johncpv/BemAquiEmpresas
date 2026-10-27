using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Infrastructure.Inter;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// Tenants A e B reais (Testcontainers) e um Host com o fake do Inter no lugar do <see cref="SocketsInterHandlerFactory"/>: a fábrica
/// recebe o certificado do mTLS de cada conta e o fake responde o OAuth2 e o saldo. Logs e alertas capturados; handler descartado na
/// hora (sem a espera das chamadas em curso).
/// </summary>
public sealed class BankIntegrationFixture : IAsyncLifetime
{
    public const string HostA = "a.localhost";
    public const string HostB = "b.localhost";
    public const string Base = "/api/v1/settings/integrations/inter";

    public TenancyFixture Tenancy { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public FakeInter Inter { get; } = new();

    /// <summary>Antivírus do extrato OFX (BNK-03a CA 4): limpo por padrão; o teste troca o veredito.</summary>
    public BemAqui.Integration.Tests.Attachments.FakeScanner Scanner { get; } = new();

    public string FilesRoot { get; } = Directory.CreateTempSubdirectory("bemaqui-inter-files-").FullName;

    public MemorySink Logs { get; } = new();

    public CapturingAlertSink Alerts { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Tenancy.InitializeAsync();
        Factory = Tenancy.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Inter:HandlerDisposeDelay", "00:00:00");
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IInterHandlerFactory>();
                s.AddSingleton<IInterHandlerFactory>(Inter);
                s.AddSingleton<ILogEventSink>(Logs);
                s.RemoveAll<IAlertSink>();
                s.AddSingleton<IAlertSink>(Alerts);
                s.AddSingleton<BemAqui.BuildingBlocks.Attachments.IVirusScanner>(Scanner);
                s.Configure<BemAqui.BuildingBlocks.Attachments.FileStorageOptions>(o => o.Root = FilesRoot);
            });
        });
        _ = Factory.Server;
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await Tenancy.DisposeAsync();
    }

    public async Task<AsyncServiceScope> ScopeAsync(Tenant tenant)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    public async Task<T> QueryAsync<T>(Tenant tenant, Func<TenantDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Usuário com o Proprietário ou um grupo com as ações dadas; <paramref name="recentTwoFactor"/> põe a prova do 2FA no token.</summary>
    public async Task<(Guid Id, HttpClient Client, string Stamp)> ActorAsync(Tenant tenant, string host, bool owner, bool recentTwoFactor, params string[] grants)
    {
        var (id, stamp) = await Tenancy.SeedUserAsync(tenant, "i" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (owner || grants.Length > 0)
        {
            await using var scope = await ScopeAsync(tenant);
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (owner)
            {
                groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            }
            else
            {
                var group = new Group { Name = "Inter " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(group);
                foreach (var module in grants.Select(g => g.Split('.')[0]).Distinct(StringComparer.Ordinal))
                {
                    db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                }

                db.GroupPermissions.AddRange(grants.Select(g => new GroupGrant { GroupId = group.Id, ModuleCode = g.Split('.')[0], Action = g.Split('.')[1] }));
                groupId = group.Id;
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        return (id, Client(tenant, host, id, stamp, recentTwoFactor ? DateTimeOffset.UtcNow : null), stamp);
    }

    public HttpClient Client(Tenant tenant, string host, Guid userId, string stamp, DateTimeOffset? otpAt)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: userId, stamp: stamp, otpAt: otpAt));
        return client;
    }

    /// <summary>Liga o 2FA do usuário com um segredo conhecido; devolve o gerador do código do passo atual.</summary>
    public async Task<byte[]> EnableTotpAsync(Tenant tenant, Guid userId)
    {
        var secret = Totp.NewSecret();
        await using var scope = await ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.BeginTotpSetup(Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenant.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
        user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
        await db.SaveChangesAsync();
        return secret;
    }

    public async Task<string> StampAsync(Tenant tenant, Guid userId)
    {
        await using var scope = await ScopeAsync(tenant);
        return await scope.ServiceProvider.GetRequiredService<IIdentityDb>().Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync();
    }

    /// <summary>Conta bancária nova pelo próprio cadastro (o Proprietário cria).</summary>
    public static async Task<Guid> BankAccountAsync(HttpClient owner, string bankCode = "077")
    {
        var created = await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new
        {
            name = "Inter " + Guid.NewGuid().ToString("N")[..6], kind = "Bank", bankCode, agency = "0001", account = "1234567-8",
            openingBalance = (decimal?)null, openingDate = (string?)null, isDefault = false,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Formulário de "Salvar integração bancária" com o .crt e o .key (ou o .pfx).</summary>
    public static MultipartFormDataContent Form(string clientId, string clientSecret, TestInterCertificate? certificate, string environment = "Sandbox", bool pfx = false,
        bool confirmProduction = false, string? accountNumber = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(environment), "environment" },
            { new StringContent(clientId), "clientId" },
            { new StringContent(clientSecret), "clientSecret" },
        };
        if (accountNumber is not null)
        {
            form.Add(new StringContent(accountNumber), "accountNumber");
        }

        if (confirmProduction)
        {
            form.Add(new StringContent("true"), "confirmProduction");
        }

        if (certificate is not null)
        {
            if (pfx)
            {
                form.Add(new ByteArrayContent(certificate.Pfx), "pfx", "inter.pfx");
                form.Add(new StringContent(certificate.PfxPassword), "pfxPassword");
            }
            else
            {
                form.Add(new ByteArrayContent(certificate.CertificateBytes), "certificate", "Inter_API_Certificado.crt");
                form.Add(new ByteArrayContent(certificate.KeyBytes), "privateKey", "Inter_API_Chave.key");
            }
        }

        return form;
    }

    /// <summary>Liga ou desliga a cobrança real do tenant no catálogo (GOL-01b) e limpa o cache do resolvedor.</summary>
    public async Task SetProductionAllowedAsync(Tenant tenant, bool allowed)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants.Where(t => t.Id == tenant.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BankProductionAllowed, allowed));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    public async Task SetModuleAsync(Tenant tenant, string module, bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenant.Id && m.ModuleCode == module)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    public string RenderedLogs()
    {
        lock (Logs.Events)
        {
            return string.Join('\n', Logs.Events.Select(e => e.RenderMessage(CultureInfo.InvariantCulture) + " " + string.Join(';', e.Properties.Select(p => p.Key + "=" + p.Value)) + " " + e.Exception));
        }
    }

}
