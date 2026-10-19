using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Importing;

public partial class CatalogLoadsTests
{
    [Fact]
    public async Task Carga_do_catalogo_fecha_a_soma_e_a_reexecucao_nao_muda_nada()
    {
        var first = await RunAsync(Fixture());

        first.ExitCode.Should().Be(0, first.Message);
        var totals = first.Report!.Entities.ToDictionary(e => e.Entity);
        totals["units"].Should().BeEquivalentTo(new { Read = 3, Linked = 2, Skipped = 1 }, "U liga à UN, KG à KG e CX não é usada (CA 1)");
        totals["item-categories"].Should().BeEquivalentTo(new { Read = 3, Imported = 2, Skipped = 1 }, "a categoria de fornecedor fica de fora (CA 2)");
        totals["locations"].Should().BeEquivalentTo(new { Read = 3, Imported = 3 }, "vários depósitos viram locais (CA 12)");
        totals["items"].Should().BeEquivalentTo(new { Read = 5, Imported = 4, Errors = 1 });
        totals["supplier-codes"].Should().BeEquivalentTo(new { Read = 4, Imported = 1, Skipped = 2, Errors = 1 });
        first.Report.Entities.Should().OnlyContain(e => e.Total == e.Read, "a soma fecha com o total lido (CA 15)");
        first.Items!.Single(i => i.SourceTable == "llx_product" && i.SourceId == "5").Message.Should().Be("Código repetido no Dolibarr: MP-1.");
        first.Items!.Single(i => i.SourceTable == "llx_product_fournisseur_price" && i.SourceId == "2").Message.Should().Be("Ignorado: fornecedor não importado.");
        first.Items!.Single(i => i.SourceTable == "llx_product_fournisseur_price" && i.SourceId == "3").Message.Should().Contain("já está ligado ao item MP-1");
        first.Items!.Single(i => i.SourceTable == "llx_entrepot" && i.SourceId == "1").Warning.Should().Contain("sugerido como local padrão");
        first.Items!.Single(i => i.SourceTable == "llx_product" && i.SourceId == "1").Warning.Should().Contain("ficou na primeira");
        first.Items!.Single(i => i.SourceTable == "llx_product" && i.SourceId == "4").Warning.Should().Contain("tipo não informado no de-para").And.Contain("usou o padrão do tipo");

        await AssertItemsAsync();
        await AssertLocationsAsync();

        var second = await RunAsync(Fixture());
        second.Report!.Entities.Should().OnlyContain(e => e.Imported == 0 && e.Updated == 0 && e.Conflicts == 0, "sem mudança no Dolibarr, nada muda (CA 14)");
        (await QueryAsync(db => db.Items.CountAsync())).Should().Be(4);

        // Editado no BemAqui e mudado no Dolibarr: conflito, sem sobrescrever (CA 14).
        var id = (await TargetAsync("llx_product", "4"))!.Value;
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
            (await db.Items.SingleAsync(i => i.Id == id)).Name = "Botoeira editada no BemAqui";
            await db.SaveChangesAsync();
        }

        var changed = Fixture();
        changed.Table("llx_product").Single(p => (int)p["rowid"]! == 4)["label"] = "Botoeira mudada no Dolibarr";
        var third = await RunAsync(changed);
        third.Report!.Entities.Single(e => e.Entity == "items").Conflicts.Should().Be(1);
        (await QueryAsync(db => db.Items.AsNoTracking().Where(i => i.Id == id).Select(i => i.Name).SingleAsync())).Should().Be("Botoeira editada no BemAqui");
    }

    private async Task AssertItemsAsync()
    {
        await using var scope = await ScopeAsync();
        var service = scope.ServiceProvider.GetRequiredService<ItemService>();
        var chapa = (await service.GetAsync((await TargetAsync("llx_product", "1"))!.Value)).Value;
        chapa.Should().BeEquivalentTo(new { Type = ItemType.RawMaterial, Code = "MP-1", UnitCode = "KG", CategoryName = "Chapas", Ncm = "72085100", MinStock = 10.5m, TracksLot = true, TracksSerial = false, IsActive = true, AverageCost = 0m, ImportSource = "dolibarr:llx_product:1" });
        chapa.WarrantyMonths.Should().BeNull("matéria-prima não tem garantia padrão");

        var elevator = (await service.GetAsync((await TargetAsync("llx_product", "2"))!.Value)).Value;
        elevator.Should().BeEquivalentTo(new { Type = ItemType.Finished, UnitCode = "UN", TracksSerial = true, WarrantyMonths = 24, IsActive = false }, "sem NCM entra inativo (CA 7); sem unidade usa a chave none");
        var labor = (await service.GetAsync((await TargetAsync("llx_product", "3"))!.Value)).Value;
        labor.Should().BeEquivalentTo(new { Type = ItemType.Service, TracksLot = false, IsActive = true }, "serviço sem lote e ativo sem NCM (CA 5 e 7)");
        var component = (await service.GetAsync((await TargetAsync("llx_product", "4"))!.Value)).Value;
        component.Should().BeEquivalentTo(new { Type = ItemType.Component, WarrantyMonths = 3 }, "sem de-para de tipo vira Componente com a garantia padrão (CA 4 e 6)");

        var codes = scope.ServiceProvider.GetRequiredService<IItemSupplierCodes>();
        var supplier = (await TargetAsync("llx_societe", "1"))!.Value;
        (await codes.FindAsync(supplier, "CH-77")).Should().BeEquivalentTo(new { ItemCode = "MP-1", SupplierUnit = "KG", ConversionFactor = 1m });
        var categories = await scope.ServiceProvider.GetRequiredService<CategoryService>().ListAsync();
        var steel = categories.Single(c => c.Name == "Aço carbono");
        steel.ParentId.Should().Be(categories.Single(c => c.Name == "Chapas").Id);
        steel.ImportSource.Should().Be("dolibarr:llx_categorie:11");
    }

    private async Task AssertLocationsAsync()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
        var locations = await db.Locations.AsNoTracking().Where(l => l.ImportSource != null).ToListAsync();
        locations.Select(l => (l.Code, l.Name, l.IsActive)).Should().BeEquivalentTo(new[]
        {
            ("ALMOX", "ALMOX", true), ("CHAPA-INOX-1-2MM-240", "ALMOX / Chapa Inox 1,2mm / 2400 x 1250", true), ("PA", "Pronta entrega", false),
        });
        (await db.Locations.SingleAsync(l => l.IsDefault)).Code.Should().Be("ALM", "a carga não troca o padrão (CA 12)");
    }
}
