using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Host.Persistence;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Persistence;

/// <summary>FND-07 CA 9 (histórico exige view do módulo dono) e a migration do TenantDbContext.</summary>
[Collection(PostgresTestGroup.Name)]
public class AuditHistoryTests(PostgresFixture postgres)
{
    private static async Task<Guid> SeedAsync(IServiceProvider provider, int updates)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        var customer = new SampleCustomer { Name = "v0" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        for (var i = 1; i <= updates; i++)
        {
            customer.Name = $"v{i}";
            await db.SaveChangesAsync();
        }

        return customer.Id;
    }

    [Fact]
    public async Task Com_permissao_view_do_modulo_dono_retorna_historico_paginado()
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("hist"), permissions: new AllowAllPermissionChecker());
        var id = await SeedAsync(provider, updates: 4);

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AuditHistory>().GetAsync(nameof(SampleCustomer), id.ToString(), new PageRequest(1, 2));

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(5);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items[0].Action.Should().Be(AuditActions.Updated, "mais recente primeiro");
    }

    [Fact]
    public async Task Sem_permissao_e_forbidden_falha_fechada_por_padrao()
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("histno"));
        var id = await SeedAsync(provider, updates: 1);

        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AuditHistory>().GetAsync(nameof(SampleCustomer), id.ToString(), new PageRequest());

        result.Error!.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(Error.Codes.Forbidden);
    }

    [Fact]
    public async Task Entidade_sem_dono_registrado_nao_tem_historico()
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("histx"), permissions: new AllowAllPermissionChecker());
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<AuditHistory>().GetAsync("Qualquer", Guid.CreateVersion7().ToString(), new PageRequest());

        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Migration_do_TenantDbContext_cria_tabelas_e_protege_o_audit_log()
    {
        var connection = await postgres.CreateDatabaseAsync("tenantmig");
        await using var db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

        await db.Database.MigrateAsync();
        db.AuditEntries.Add(new AuditEntry { Entity = "X", EntityId = "1", Action = AuditActions.Created, At = DateTimeOffset.UtcNow, Changes = "{}" });
        await db.SaveChangesAsync();

        var apagar = () => db.Database.ExecuteSqlRawAsync("DELETE FROM audit.audit_log");
        await apagar.Should().ThrowAsync<PostgresException>();
        (await db.NumberSequences.CountAsync()).Should().Be(0);
    }
}
