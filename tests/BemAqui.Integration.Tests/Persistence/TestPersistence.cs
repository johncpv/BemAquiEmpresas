using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Persistence;

/// <summary>Cadastro de exemplo: auditável, com exclusão lógica, dado pessoal e segredo cifrado.</summary>
public sealed class SampleCustomer : Entity, IAuditable, ISoftDeletable
{
    public required string Name { get; set; }

    [PersonalData]
    public string? Email { get; set; }

    [ProtectedSecret]
    public string? ApiKeyCipher { get; set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>Documento de exemplo com concorrência otimista pelo xmin (FND-09e CA 13).</summary>
[DocumentName("O documento de teste")]
public sealed class SampleDocument : Entity, IConcurrencyVersioned
{
    public required string Name { get; set; }

    public uint Version { get; set; }
}

public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : BemAquiDbContext(options)
{
    public DbSet<SampleCustomer> Customers => Set<SampleCustomer>();

    public DbSet<SampleDocument> Documents => Set<SampleDocument>();

    protected override void ConfigureModules(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SampleCustomer>(b => b.ToTable("customers", "sample"));
        modelBuilder.Entity<SampleDocument>(b => b.ToTable("documents", "sample"));
    }
}

/// <summary>Relógio controlável para testes de virada de ano.</summary>
public sealed class MutableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}

public sealed class FixedUser(Guid? userId, string? ip) : ICurrentUser
{
    public Guid? UserId { get; } = userId;

    public string? IpAddress { get; } = ip;
}

public sealed class AllowAllPermissionChecker : IPermissionChecker
{
    public ValueTask<bool> HasPermissionAsync(string module, string action, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
}

/// <summary>Ninguém é Proprietário: o histórico esconde as leituras sensíveis.</summary>
public sealed class NoOwnerPermissionService : IPermissionService
{
    public Task<EffectivePermissions> GetAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(EffectivePermissions.None);

    public Task BumpAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public static class TestPersistence
{
    public static async Task<ServiceProvider> CreateAsync(string connectionString, TimeProvider? clock = null, ICurrentUser? user = null, IPermissionChecker? permissions = null, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddScoped(_ => user ?? new FixedUser(Guid.CreateVersion7(), "203.0.113.10"));
        if (permissions is not null)
        {
            services.AddScoped(_ => permissions);
        }

        services.AddScoped<IPermissionService, NoOwnerPermissionService>();
        services.AddBemAquiPersistenceCore();
        services.AddDbContext<SampleDbContext>((sp, o) => o.UseBemAquiDefaults(connectionString, sp));
        services.AddScoped<BemAquiDbContext>(sp => sp.GetRequiredService<SampleDbContext>());
        services.AddSingleton(new AuditableEntityRegistry().Register<SampleCustomer>("parties"));
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync(AuditLogSql.Protect);
        return provider;
    }
}
