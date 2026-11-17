using System.Diagnostics;
using BemAqui.Catalog.Application;
using BemAqui.Catalog.Contracts;
using BemAqui.Catalog.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Catalog;

public partial class CatalogTests
{
    private const int LoadProducts = 20_000;

    /// <summary>
    /// CA 13: com 20 mil produtos, a consulta por GTIN e por trecho do nome responde em menos de 50 ms no p95 (contrato <see cref="ICatalogLookup"/>,
    /// no banco real). Os dados de carga saem no fim, porque o tenant é da classe.
    /// </summary>
    [Fact]
    [Trait(LoadTest.Trait, LoadTest.Value)]
    public async Task Consulta_com_20_mil_produtos_fica_abaixo_de_50_ms_no_p95()
    {
        await RetailAsync();
        var marker = "carga" + Guid.NewGuid().ToString("N")[..6];
        var gtins = new List<string>();
        try
        {
            await using (var scope = await ScopeAsync())
            {
                var db = scope.ServiceProvider.GetRequiredService<ICatalogDb>();
                var defaults = scope.ServiceProvider.GetRequiredService<CatalogDefaults>();
                var category = await defaults.GeneralCategoryAsync();
                var list = await defaults.DefaultPriceListAsync();
                var now = DateTimeOffset.UtcNow;
                for (var batch = 0; batch < LoadProducts / 2_000; batch++)
                {
                    for (var i = 0; i < 2_000; i++)
                    {
                        var n = (batch * 2_000) + i;
                        var product = new Product { ItemId = Guid.CreateVersion7(), CategoryId = category.Id, SaleName = $"{marker} produto {n:00000} sabor {n % 37}", CreatedAt = now };
                        var gtin = NewGtin();
                        gtins.Add(gtin);
                        db.Products.Add(product);
                        db.ProductBarcodes.Add(new ProductBarcode { ProductId = product.Id, Gtin = gtin, CreatedAt = now });
                        db.PriceListItems.Add(new PriceListItem { PriceListId = list.Id, ProductId = product.Id, Price = 1m + (n % 100), UpdatedAt = now });
                    }

                    await db.SaveChangesAsync();
                    ((DbContext)db).ChangeTracker.Clear();
                }
            }

            await using (var scope = await ScopeAsync())
            {
                var lookup = scope.ServiceProvider.GetRequiredService<ICatalogLookup>();
                await lookup.ByGtinAsync(gtins[0]);
                await lookup.SearchAsync(marker + " produto 00001");
                var byGtin = new List<double>();
                var byName = new List<double>();
                for (var i = 0; i < 60; i++)
                {
                    var watch = Stopwatch.StartNew();
                    (await lookup.ByGtinAsync(gtins[Random.Shared.Next(gtins.Count)])).Should().NotBeNull();
                    byGtin.Add(watch.Elapsed.TotalMilliseconds);
                    watch.Restart();
                    (await lookup.SearchAsync($"produto {Random.Shared.Next(LoadProducts):00000}", 20)).Should().NotBeEmpty();
                    byName.Add(watch.Elapsed.TotalMilliseconds);
                }

                P95(byGtin).Should().BeLessThan(50, "consulta por GTIN (CA 13)");
                P95(byName).Should().BeLessThan(50, "consulta por trecho do nome (CA 13)");
            }
        }
        finally
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<ICatalogDb>();
            var ids = db.Products.IgnoreQueryFilters().Where(p => p.SaleName.StartsWith(marker)).Select(p => p.Id);
            await db.PriceListItems.Where(i => ids.Contains(i.ProductId)).ExecuteDeleteAsync();
            await db.ProductBarcodes.Where(b => ids.Contains(b.ProductId)).ExecuteDeleteAsync();
            await db.Products.IgnoreQueryFilters().Where(p => p.SaleName.StartsWith(marker)).ExecuteDeleteAsync();
        }
    }

    private static double P95(List<double> samples)
    {
        var sorted = samples.Order().ToList();
        return sorted[(int)Math.Ceiling(sorted.Count * 0.95) - 1];
    }
}
