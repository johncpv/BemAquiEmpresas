using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.SharedKernel;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace BemAqui.Integration.Tests.Webhooks;

/// <summary>
/// Handler do provedor de teste: conta as chamadas por evento (efeito uma vez só, CA 12); o tipo <c>falhar</c> lança e o tipo
/// <c>ignorar</c> devolve <c>Ignored</c>. Só existe nos testes (o provedor <c>test</c> nunca vai para o build publicado).
/// </summary>
public sealed class TestWebhookHandler : IWebhookHandler
{
    public static ConcurrentDictionary<Guid, int> Calls { get; } = new();

    public static int DelayMs { get; set; }

    public async Task<WebhookHandling> HandleAsync(WebhookEventContext context, CancellationToken cancellationToken)
    {
        Calls.AddOrUpdate(context.EventId, 1, (_, n) => n + 1);
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs, cancellationToken);
        }

        return context.EventType switch
        {
            "falhar" => throw new InvalidOperationException("CPF 529.982.247-25 recusado pelo provedor"),
            "ignorar" => WebhookHandling.Ignored("Evento sem efeito"),
            _ => WebhookHandling.Processed(),
        };
    }
}

/// <summary>Jobs enfileirados (sem servidor do Hangfire nos testes): o teste executa o job à mão.</summary>
public sealed class CapturingJobClient : IBackgroundJobClient
{
    public ConcurrentQueue<(Guid TenantId, Guid EventId, string Queue)> Enqueued { get; } = new();

    /// <summary>Todo job criado (tipo e argumentos), para conferir que o job recebeu o tenantId (FIS-05).</summary>
    public ConcurrentQueue<(Type Type, IReadOnlyList<object?> Args)> All { get; } = new();

    public string Create(Job job, IState state)
    {
        ArgumentNullException.ThrowIfNull(job);
        All.Enqueue((job.Type, job.Args.ToList()));
        if (job.Type == typeof(ProcessWebhookEventJob) && job.Args.Count >= 2 && job.Args[0] is Guid tenant && job.Args[1] is WebhookEventJobArgs args)
        {
            Enqueued.Enqueue((tenant, args.EventId, (state as EnqueuedState)?.Queue ?? "-"));
        }

        return Guid.NewGuid().ToString("N");
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}

/// <summary>
/// Tenants A e B reais (Testcontainers) e um Host com os provedores de teste: <c>test</c> (HMAC com timestamp, id da entrega e tipo
/// no cabeçalho), <c>test-token</c> (token na query), <c>test-path</c> (token no caminho), <c>test-verify</c> (GET de verificação) e
/// <c>test-fiscal</c> (módulo dono fiscal). Logs, alertas e jobs ficam capturados.
/// </summary>
public sealed class WebhookFixture : IAsyncLifetime
{
    public const string PlatformHost = "plataforma.bemaquiapp.com.br";
    public const string TimestampHeader = "X-BemAqui-Timestamp";
    public const string SignatureHeader = "X-BemAqui-Signature";
    public const string DeliveryHeader = "X-BemAqui-Delivery-Id";
    public const string EventHeader = "X-BemAqui-Event";

    public static readonly WebhookProvider Hmac = new()
    {
        Code = "test", Name = "Provedor de teste", OwnerModule = "finance", HandlerType = typeof(TestWebhookHandler),
        Validator = new HmacSha256Validator(SignatureHeader, "sha256=", TimestampHeader), DeliveryIdHeader = DeliveryHeader, EventTypeHeader = EventHeader, RotationInSettings = true,
    };

    public static readonly WebhookProvider Token = new()
    {
        Code = "test-token", Name = "Provedor de teste (token)", OwnerModule = "finance", HandlerType = typeof(TestWebhookHandler),
        Validator = new UrlTokenValidator(), EventTypeHeader = EventHeader, RotationInSettings = true,
    };

    public static readonly WebhookProvider PathToken = new()
    {
        Code = "test-path", Name = "Provedor de teste (caminho)", OwnerModule = "finance", HandlerType = typeof(TestWebhookHandler),
        Validator = new UrlTokenValidator(UrlTokenMode.PathToken), RotationInSettings = true,
    };

    public static readonly WebhookProvider Verify = new()
    {
        Code = "test-verify", Name = "Provedor de teste (verificação)", OwnerModule = "finance", HandlerType = typeof(TestWebhookHandler),
        Validator = new UrlTokenValidator(), SupportsVerificationGet = true, RotationInSettings = true,
    };

    public static readonly WebhookProvider Fiscal = new()
    {
        Code = "test-fiscal", Name = "Provedor de teste (fiscal)", OwnerModule = "fiscal", HandlerType = typeof(TestWebhookHandler),
        Validator = new HmacSha256Validator(SignatureHeader, "sha256=", TimestampHeader), DeliveryIdHeader = DeliveryHeader, EventTypeHeader = EventHeader, RotationInSettings = true,
    };

    public TenancyFixture Tenancy { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public MemorySink Logs { get; } = new();

    public CapturingAlertSink Alerts { get; } = new();

    public CapturingJobClient Jobs { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await Tenancy.InitializeAsync();
        Factory = Build(Tenancy.Factory, perMinute: 100_000, Logs, Alerts, Jobs);
        _ = Factory.Server;
    }

    public static WebApplicationFactory<Program> Build(WebApplicationFactory<Program> baseFactory, int perMinute, MemorySink logs, CapturingAlertSink alerts, CapturingJobClient jobs, Action<IServiceCollection>? extra = null) =>
        baseFactory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Webhooks:PerIpPerMinute", perMinute.ToString(CultureInfo.InvariantCulture));
            b.UseSetting("Platform:AllowedNetworks:0", "10.99.0.0/16"); // allowlist do painel ligada: /webhooks fica fora dela (CA 1)
            b.ConfigureTestServices(s =>
            {
                s.AddWebhookProvider(Hmac).AddWebhookProvider(Token).AddWebhookProvider(PathToken).AddWebhookProvider(Verify).AddWebhookProvider(Fiscal);
                s.AddSingleton<ILogEventSink>(logs);
                s.RemoveAll<IAlertSink>();
                s.AddSingleton<IAlertSink>(alerts);
                s.RemoveAll<IBackgroundJobClient>();
                s.AddSingleton<IBackgroundJobClient>(jobs);
                extra?.Invoke(s);
            });
        });

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await Tenancy.DisposeAsync();
    }

    public HttpClient Platform()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = PlatformHost;
        return client;
    }

    public async Task<AsyncServiceScope> ScopeAsync(Tenant tenant)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    public async Task<T> QueryAsync<T>(Tenant tenant, Func<BemAquiDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<BemAquiDbContext>());
    }

    public Task<string> KeyAsync(Tenant tenant) => Factory.Services.GetRequiredService<IWebhookTenantKeys>().EnsureAsync(tenant.Id);

    /// <summary>Credencial do provedor no tenant, gravada direto (sem a tela): devolve o segredo ou o token em claro.</summary>
    public async Task<string> ConfigureAsync(Tenant tenant, WebhookProvider provider, Guid? scopeId = null)
    {
        await using var scope = await ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>();
        var value = OpaqueToken.New();
        var credential = await db.WebhookCredentials.FirstOrDefaultAsync(c => c.Provider == provider.Code && c.ScopeId == (scopeId ?? Guid.Empty));
        if (credential is null)
        {
            credential = new WebhookCredential { Provider = provider.Code, ScopeId = scopeId ?? Guid.Empty, Kind = provider.Validator.Kind, CreatedAt = DateTimeOffset.UtcNow };
            db.WebhookCredentials.Add(credential);
        }

        if (provider.Validator.Kind == WebhookCredentialKind.UrlToken)
        {
            credential.Rotate(Convert.ToHexStringLower(UrlTokenValidator.Hash(value)), null, null, DateTimeOffset.UtcNow);
        }
        else
        {
            var cipher = Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenant.Id), SecretKind.WebhookSecret, new Secret(value));
            credential.Rotate(null, cipher, null, DateTimeOffset.UtcNow);
        }

        await db.SaveChangesAsync();
        return value;
    }

    public static string Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>POST assinado do provedor <c>test</c> (ou outro HMAC), com id da entrega e tipo.</summary>
    public static HttpRequestMessage SignedPost(string path, string secret, string body, string? delivery = null, string eventType = "nota.autorizada", string? timestamp = null)
    {
        var ts = timestamp ?? Now();
        var bytes = Encoding.UTF8.GetBytes(body);
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(SignatureHeader, HmacSha256Validator.Sign(Encoding.UTF8.GetBytes(secret), bytes, ts));
        request.Headers.Add(TimestampHeader, ts);
        request.Headers.Add(DeliveryHeader, delivery ?? "d-" + Guid.NewGuid().ToString("N"));
        request.Headers.Add(EventHeader, eventType);
        return request;
    }

    public static HttpRequestMessage Json(string path, string body, string? eventType = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (eventType is not null)
        {
            request.Headers.Add(EventHeader, eventType);
        }

        return request;
    }

    public static string Body(string marker = "") => "{\"id\":\"" + Guid.NewGuid() + "\",\"valor\":10.5,\"pagador\":{\"nome\":\"Maria " + marker + "\"}}";

    /// <summary>Roda o job como o Hangfire rodaria: com o tenantId e o id do evento; devolve a exceção, se houve.</summary>
    public async Task<Exception?> RunJobAsync(Tenant tenant, Guid eventId)
    {
        var job = ActivatorUtilities.CreateInstance<ProcessWebhookEventJob>(Factory.Services);
        try
        {
            await job.ExecuteAsync(tenant.Id, new WebhookEventJobArgs(eventId), CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    public async Task<WebhookEvent> EventAsync(Tenant tenant, Guid id) =>
        await QueryAsync(tenant, db => db.WebhookEvents.AsNoTracking().SingleAsync(e => e.Id == id));

    public async Task<WebhookEvent?> EventByDeliveryAsync(Tenant tenant, string provider, string delivery) =>
        await QueryAsync(tenant, db => db.WebhookEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Provider == provider && e.ExternalId == delivery));

    /// <summary>Usuário do tenant com um grupo das ações dadas ("finance.view", "settings.view"...) ou do Proprietário.</summary>
    public async Task<(Guid Id, HttpClient Client)> ActorAsync(Tenant tenant, string host, bool owner = false, params string[] grants)
    {
        var (id, stamp) = await Tenancy.SeedUserAsync(tenant, "w" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (owner)
            {
                groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            }
            else
            {
                var group = new Group { Name = "Integrações " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
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
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
        return (id, client);
    }

    /// <summary>Liga o 2FA do usuário com um segredo conhecido; devolve o código do passo atual (aceito uma vez só).</summary>
    public async Task<string> EnableTotpAsync(Tenant tenant, Guid userId)
    {
        var secret = Totp.NewSecret();
        await using (var scope = await ScopeAsync(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.BeginTotpSetup(Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenant.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
            user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            await db.SaveChangesAsync();
        }

        return Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow));
    }

    /// <summary>Libera ou bloqueia um módulo do tenant no catálogo e limpa o cache do resolvedor.</summary>
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
}
