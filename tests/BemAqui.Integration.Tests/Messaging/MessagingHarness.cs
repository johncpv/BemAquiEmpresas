using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Integration.Tests.Persistence;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Messaging;

public sealed record CustomerRegistered(Guid CustomerId, string Name) : IIntegrationEvent;

public sealed record AlwaysFails(string Reason) : IIntegrationEvent;

/// <summary>Efeito observável: grava um cliente "efeito:{id}" na mesma transação da inbox.</summary>
public sealed class RecordEffectConsumer(SampleDbContext db) : IIntegrationEventConsumer<CustomerRegistered>
{
    public static int DelayMs { get; set; }

    public async Task HandleAsync(CustomerRegistered integrationEvent, CancellationToken cancellationToken)
    {
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs, cancellationToken);
        }

        db.Customers.Add(new SampleCustomer { Name = "efeito:" + integrationEvent.CustomerId });
    }
}

public sealed record DocumentRenamed(Guid DocumentId, string Name) : IIntegrationEvent;

/// <summary>
/// Renomeia o documento (versão pelo xmin). Enquanto <see cref="ConflictsLeft"/> &gt; 0, outra transação grava a mesma linha entre a
/// leitura e a gravação do handler: a gravação dele toma <see cref="DbUpdateConcurrencyException"/> de verdade (a disputa do
/// <c>ContractCompletionConsumer</c> com outro processador, de forma determinística).
/// </summary>
public sealed class ConflictingRenameConsumer(SampleDbContext db, IServiceScopeFactory scopes) : IIntegrationEventConsumer<DocumentRenamed>
{
    public static int ConflictsLeft { get; set; }

    public static int Calls { get; set; }

    public async Task HandleAsync(DocumentRenamed integrationEvent, CancellationToken cancellationToken)
    {
        Calls++;
        var doc = await db.Documents.SingleAsync(d => d.Id == integrationEvent.DocumentId, cancellationToken);
        if (ConflictsLeft > 0)
        {
            ConflictsLeft--;
            await using var other = scopes.CreateAsyncScope();
            var otherDb = other.ServiceProvider.GetRequiredService<SampleDbContext>();
            var same = await otherDb.Documents.SingleAsync(d => d.Id == integrationEvent.DocumentId, cancellationToken);
            same.Name = "concorrente " + Calls;
            await otherDb.SaveChangesAsync(cancellationToken);
        }

        doc.Name = integrationEvent.Name;
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class FailingConsumer :IIntegrationEventConsumer<AlwaysFails>
{
    public Task HandleAsync(AlwaysFails integrationEvent, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("falha proposital: " + integrationEvent.Reason);
}

public sealed class FakeTenantDirectory : ITenantDirectory
{
    public List<TenantInfo> Tenants { get; } = [];

    public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantInfo>>(Tenants.Where(t => t.Status == TenantStatus.Active).ToList());

    public Task<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tenants.FirstOrDefault(t => t.Id == tenantId));
}

/// <summary>Nos testes todos os tenants usam o mesmo banco; cada chamada abre um escopo novo.</summary>
public sealed class SameDatabaseScopeFactory(IServiceProvider root) : ITenantScopeFactory
{
    public Task<AsyncServiceScope> CreateScopeAsync(TenantInfo tenant, CancellationToken cancellationToken = default) =>
        Task.FromResult(root.CreateAsyncScope());
}

public static class MessagingHarness
{
    public static TenantInfo Tenant { get; } = new(Guid.CreateVersion7(), "konebras", TenantStatus.Active);

    public static async Task<ServiceProvider> CreateAsync(string connectionString, TimeProvider? clock = null, int maxAttempts = 10, BemAqui.BuildingBlocks.Observability.IAlertSink? alerts = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock ?? TimeProvider.System);
        var directory = new FakeTenantDirectory();
        directory.Tenants.Add(Tenant);
        services.AddSingleton(directory);
        services.AddSingleton<ITenantDirectory>(directory);
        services.AddSingleton<ITenantScopeFactory>(sp => new SameDatabaseScopeFactory(sp));
        if (alerts is not null)
        {
            services.AddSingleton(alerts);
        }

        services.AddBemAquiPersistenceCore();
        services.AddDbContext<SampleDbContext>((sp, o) => o.UseBemAquiDefaults(connectionString, sp));
        services.AddScoped<BemAquiDbContext>(sp => sp.GetRequiredService<SampleDbContext>());

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Outbox:MaxAttempts"] = maxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Outbox:BatchSize"] = "50",
        }).Build();
        services.AddBemAquiMessaging(configuration, r => r
            .AddHandler<CustomerRegistered, RecordEffectConsumer>()
            .AddHandler<AlwaysFails, FailingConsumer>()
            .AddHandler<DocumentRenamed, ConflictingRenameConsumer>(), runDispatcher: false);
        services.AddSingleton<OutboxDispatcher>();

        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SampleDbContext>().Database.EnsureCreatedAsync();
        return provider;
    }

    public static async Task PublishAsync(IServiceProvider provider, params IIntegrationEvent[] events)
    {
        await using var scope = provider.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        foreach (var e in events)
        {
            publisher.Publish(e);
        }

        await scope.ServiceProvider.GetRequiredService<SampleDbContext>().SaveChangesAsync();
    }

    public static async Task<int> ProcessAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(Tenant);
    }

    public static async Task<int> EffectsAsync(IServiceProvider provider, Guid? customerId = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        return customerId is null
            ? await db.Customers.CountAsync(c => c.Name.StartsWith("efeito:"))
            : await db.Customers.CountAsync(c => c.Name == "efeito:" + customerId);
    }

    public static async Task<List<OutboxMessage>> OutboxAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SampleDbContext>().OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ThenBy(m => m.Id).ToListAsync();
    }
}
