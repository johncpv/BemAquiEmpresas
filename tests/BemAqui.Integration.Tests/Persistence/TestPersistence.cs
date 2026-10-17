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

public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : BemAquiDbContext(options)
{
    public DbSet<SampleCustomer> Customers => Set<SampleCustomer>();

    protected override void ConfigureModules(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<SampleCustomer>(b => b.ToTable("customers", "sample"));
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
