using BemAqui.BuildingBlocks.Persistence.Numbering;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Persistence;

/// <summary>FND-07 CA 3, 4 e 5: formato, 50 chamadas paralelas sem lacuna e virada do ano em America/Sao_Paulo.</summary>
[Collection(PostgresTestGroup.Name)]
public class NumberingTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    private static async Task<string> NextAsync(IServiceProvider provider, string docType)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INumberSequence>().NextAsync(docType);
    }

    [Fact]
    public async Task Primeiro_numero_do_ano()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 17, 9, 0, 0, Brasilia));
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("num1"), clock);

        (await NextAsync(provider, "ORC")).Should().Be("ORC-2026-0001");
        (await NextAsync(provider, "ORC")).Should().Be("ORC-2026-0002");
        (await NextAsync(provider, "OS")).Should().Be("OS-2026-00001");
    }

    [Fact]
    public async Task Cinquenta_chamadas_paralelas_sao_distintas_e_consecutivas()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 17, 9, 0, 0, Brasilia));
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("num50"), clock);

        var numbers = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => NextAsync(provider, "CP"))));

        numbers.Should().OnlyHaveUniqueItems();
        numbers.Order().Should().Equal(Enumerable.Range(1, 50).Select(i => $"CP-2026-{i:00000}"));
    }

    [Fact]
    public async Task Virada_do_ano_segue_o_horario_de_Brasilia()
    {
        // 31/12 às 23:30 em Brasília já é 01/01 em UTC: ainda conta como 2026.
        var clock = new MutableClock(new DateTimeOffset(2026, 12, 31, 23, 30, 0, Brasilia));
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("numano"), clock);

        (await NextAsync(provider, "ORC")).Should().Be("ORC-2026-0001");
        (await NextAsync(provider, "ORC")).Should().Be("ORC-2026-0002");

        clock.Now = new DateTimeOffset(2027, 1, 1, 0, 10, 0, Brasilia);
        (await NextAsync(provider, "ORC")).Should().Be("ORC-2027-0001");
    }

    [Fact]
    public async Task Documento_nao_salvo_devolve_o_numero()
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("numrb"));

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<INumberSequence>().NextAsync("PAT");
            await transaction.RollbackAsync();
        }

        (await NextAsync(provider, "PAT")).Should().Be("PAT-00001");
    }

    [Fact]
    public async Task Tipo_desconhecido_e_recusado()
    {
        await using var provider = await TestPersistence.CreateAsync(await postgres.CreateDatabaseAsync("numx"));

        var gerar = () => NextAsync(provider, "XYZ");

        await gerar.Should().ThrowAsync<ArgumentException>();
    }
}
