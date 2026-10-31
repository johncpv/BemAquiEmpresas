using System.Diagnostics;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>CAD-02a CA 19: com 50 mil itens, a primeira página da lista responde em menos de 1 s (tenant próprio da classe).</summary>
[Trait(LoadTest.Trait, LoadTest.Value)]
public class ItemPerformanceTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Primeira_pagina_com_50_mil_itens_abaixo_de_1_segundo()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO inventory.items (id, type, code, name, search_text, unit_id, origin, min_stock, tracks_lot, tracks_serial, average_cost, is_active, created_at, is_deleted)
            SELECT gen_random_uuid(), 'Component', 'PERF-' || g, 'Peça de teste ' || g, 'perf-' || g || ' peca de teste ' || g, {unit}, 0, 0, false, false, 0, true, now(), false
            FROM generate_series(1, 50000) g
            """);
        await db.Database.ExecuteSqlRawAsync("ANALYZE inventory.items");
        var service = scope.ServiceProvider.GetRequiredService<ItemService>();
        await service.ListAsync(new ItemListQuery(null, null, null, false), new PageRequest());

        var watch = Stopwatch.StartNew();
        var first = await service.ListAsync(new ItemListQuery(null, null, null, false), new PageRequest());
        var searched = await service.ListAsync(new ItemListQuery("teste 4999", null, null, false), new PageRequest());
        watch.Stop();

        first.Total.Should().BeGreaterThanOrEqualTo(50000);
        searched.Total.Should().BeGreaterThan(0);
        (watch.ElapsedMilliseconds / 2).Should().BeLessThan(1000, "cada consulta responde em menos de 1 s (CA 19)");
    }
}
