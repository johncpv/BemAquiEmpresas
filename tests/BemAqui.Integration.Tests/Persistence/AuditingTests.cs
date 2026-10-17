using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Persistence;

/// <summary>FND-07 CA 6, 7, 8 e 10: auditoria na mesma transação, imutável, com máscara, e exclusão lógica.</summary>
[Collection(PostgresTestGroup.Name)]
public class AuditingTests(PostgresFixture postgres)
{
    private static async Task<List<AuditEntry>> AuditOf(IServiceProvider provider, Guid id)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SampleDbContext>().AuditEntries
            .Where(a => a.EntityId == id.ToString()).OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync();
    }

    [Fact]
    public async Task Criacao_alteracao_e_exclusao_logica_ficam_auditadas()
    {
        var userId = Guid.CreateVersion7();
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("audit"), user: new FixedUser(userId, "198.51.100.7"));
        var customer = new SampleCustomer { Name = "Konebras", Email = "contato@konebras.com.br", ApiKeyCipher = "CfDJ8-cifrado" };

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            var loaded = await db.Customers.SingleAsync(c => c.Id == customer.Id);
            loaded.Name = "Konebras Elevadores";
            loaded.Email = "financeiro@konebras.com.br";
            loaded.ApiKeyCipher = "CfDJ8-outro";
            await db.SaveChangesAsync();
            db.Customers.Remove(loaded);
            await db.SaveChangesAsync();
        }

        var audit = await AuditOf(provider, customer.Id);
        audit.Select(a => a.Action).Should().Equal(AuditActions.Created, AuditActions.Updated, AuditActions.Deleted);
        audit.Should().AllSatisfy(a =>
        {
            a.Entity.Should().Be(nameof(SampleCustomer));
            a.UserId.Should().Be(userId);
            a.Ip.Should().Be("198.51.100.7");
            a.At.Offset.Should().Be(TimeSpan.Zero);
        });

        var updated = JsonDocument.Parse(audit[1].Changes).RootElement;
        updated.GetProperty("name").GetProperty("old").GetString().Should().Be("Konebras");
        updated.GetProperty("name").GetProperty("new").GetString().Should().Be("Konebras Elevadores");
        updated.GetProperty("email").GetProperty("new").GetString().Should().Be("***");
        updated.GetProperty("apiKeyCipher").GetProperty("changed").GetBoolean().Should().BeTrue();
        audit.Should().AllSatisfy(a =>
        {
            a.Changes.Should().NotContain("konebras.com.br", "dado pessoal aparece mascarado");
            a.Changes.Should().NotContain("CfDJ8", "segredo nunca aparece no diff");
        });

        var deleted = JsonDocument.Parse(audit[2].Changes).RootElement;
        deleted.GetProperty("isDeleted").GetProperty("new").GetBoolean().Should().BeTrue();
        deleted.TryGetProperty("name", out _).Should().BeFalse("o diff traz só os campos alterados");
    }

    [Fact]
    public async Task Excluido_some_das_consultas_padrao()
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("softdel"));
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        var customer = new SampleCustomer { Name = "A excluir" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        db.Customers.Remove(customer);
        await db.SaveChangesAsync();

        (await db.Customers.AnyAsync(c => c.Id == customer.Id)).Should().BeFalse();
        (await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == customer.Id)).IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Rollback_nao_deixa_auditoria()
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("auditrb"));
        var customer = new SampleCustomer { Name = "Nunca confirmado" };

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        (await AuditOf(provider, customer.Id)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("UPDATE audit.audit_log SET action = 'Hacked'")]
    [InlineData("DELETE FROM audit.audit_log")]
    [InlineData("TRUNCATE audit.audit_log")]
    public async Task Audit_log_recusa_alteracao_e_exclusao(string sql)
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("auditro"));
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        db.Customers.Add(new SampleCustomer { Name = "Auditado" });
        await db.SaveChangesAsync();

        var alterar = () => db.Database.ExecuteSqlRawAsync(sql);

        (await alterar.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("somente inserção");
        (await db.AuditEntries.CountAsync()).Should().Be(1);
    }
}
