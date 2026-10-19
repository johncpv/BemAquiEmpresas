using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Domain;
using BemAqui.Platform.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>MIG-01c: unidades, categorias, locais, itens e códigos do fornecedor, com uma fixture anonimizada.</summary>
public partial class CatalogLoadsTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    public const string SupplierCnpj = "60701190000104";

    public const string Mapping = """
        { "maps": { "units": { "none": "UN" }, "item-types": { "MP-1": "RawMaterial", "ELV-1": "produto-acabado" }, "supplier-factors": { "1": "1" } } }
        """;

    private static readonly string[] Entities = ["parties", "units", "item-categories", "locations", "items", "supplier-codes"];

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<ImportRunResult> RunAsync(FakeSnapshot source, string mapping = Mapping)
    {
        await using var scope = await ScopeAsync();
        var sp = scope.ServiceProvider;
        var runner = new ImportRunner(sp.GetRequiredService<IMigrationDb>(), sp.GetServices<IDolibarrLoader>(), sp.GetRequiredService<ITenantProfile>(),
            sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<AuditOrigin>(), TimeProvider.System);
        return await runner.RunAsync(source, new ImportRequest(ImportMode.Real, Entities, "operador@konebras.com.br", ImportMapping.Parse(mapping).Value));
    }

    private async Task<T> QueryAsync<T>(Func<TenantDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync();
        return await query(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private Task<Guid?> TargetAsync(string table, string id) =>
        QueryAsync(db => db.ExternalRefs.AsNoTracking().Where(r => r.SourceTable == table && r.SourceId == id).Select(r => (Guid?)r.TargetId).FirstOrDefaultAsync());

    private static Dictionary<string, object?> Product(int rowid, string reference, string label, int type = 0, int? unit = null, string? ncm = null, int batch = 0, int sell = 0, int buy = 1, double? min = null) => new()
    {
        ["rowid"] = rowid, ["ref"] = reference, ["label"] = label, ["fk_product_type"] = type, ["fk_unit"] = unit, ["customcode"] = ncm, ["tobatch"] = batch,
        ["tosell"] = sell, ["tobuy"] = buy, ["seuil_stock_alerte"] = min, ["entity"] = 1, ["tms"] = new DateTime(2026, 1, 1),
    };

    /// <summary>
    /// Fixture da story: chapa com lote em duas categorias, elevador com série sem NCM, serviço com lote ligado, componente sem
    /// de-para de tipo e com garantia inválida, ref repetido, três unidades (uma não usada), dois depósitos com sub-depósito e
    /// um inativo, e códigos do fornecedor com fornecedor ausente, repetido e sem código.
    /// </summary>
    internal static FakeSnapshot Fixture()
    {
        var s = new FakeSnapshot();
        s.Societe.Add(new() { ["rowid"] = 1, ["nom"] = "Aços Fornecedor Ltda", ["siren"] = SupplierCnpj, ["client"] = 0, ["fournisseur"] = 1, ["status"] = 1, ["entity"] = 1, ["tms"] = new DateTime(2026, 1, 1) });
        s.Table("llx_c_units").AddRange([
            new() { ["rowid"] = 1, ["code"] = "U", ["label"] = "Unit", ["active"] = 1 },
            new() { ["rowid"] = 2, ["code"] = "KG", ["label"] = "Kilogram", ["active"] = 1 },
            new() { ["rowid"] = 3, ["code"] = "CX", ["label"] = "Caixa", ["active"] = 1 },
        ]);
        s.Table("llx_categorie").AddRange([
            new() { ["rowid"] = 10, ["label"] = "Chapas", ["type"] = 0, ["fk_parent"] = 0, ["entity"] = 1 },
            new() { ["rowid"] = 11, ["label"] = "Aço carbono", ["type"] = 0, ["fk_parent"] = 10, ["entity"] = 1 },
            new() { ["rowid"] = 12, ["label"] = "Fornecedores de aço", ["type"] = 1, ["fk_parent"] = 0, ["entity"] = 1 },
        ]);
        s.Table("llx_categorie_product").AddRange([new() { ["fk_categorie"] = 11, ["fk_product"] = 1 }, new() { ["fk_categorie"] = 10, ["fk_product"] = 1 }]);
        s.Table("llx_product").AddRange([
            Product(1, "MP-1", "Chapa de aço 2 mm", unit: 2, ncm: "7208.51.00", batch: 1, min: 10.5),
            Product(2, "ELV-1", "Elevador social 8 pass.", batch: 2, sell: 1),
            Product(3, "SRV-1", "Mão de obra montador", type: 1, unit: 1, batch: 1),
            Product(4, "CMP-1", "Botoeira de pavimento", unit: 1, ncm: "8536.50.90"),
            Product(5, "MP-1", "Chapa repetida", unit: 2, ncm: "72085100"),
        ]);
        s.Table("llx_product_extrafields").AddRange([new() { ["fk_object"] = 2, ["elev_garantia"] = "24" }, new() { ["fk_object"] = 4, ["elev_garantia"] = "um ano" }]);
        s.Table("llx_product_fournisseur_price").AddRange([
            new() { ["rowid"] = 1, ["fk_product"] = 1, ["fk_soc"] = 1, ["ref_fourn"] = "CH-77", ["fk_unit"] = 2, ["entity"] = 1 },
            new() { ["rowid"] = 2, ["fk_product"] = 1, ["fk_soc"] = 99, ["ref_fourn"] = "X-1", ["entity"] = 1 },
            new() { ["rowid"] = 3, ["fk_product"] = 4, ["fk_soc"] = 1, ["ref_fourn"] = "CH-77", ["entity"] = 1 },
            new() { ["rowid"] = 4, ["fk_product"] = 4, ["fk_soc"] = 1, ["ref_fourn"] = "", ["entity"] = 1 },
        ]);
        s.Table("llx_entrepot").AddRange([
            new() { ["rowid"] = 1, ["ref"] = "ALMOX", ["label"] = "ALMOX", ["statut"] = 1, ["fk_parent"] = 0, ["entity"] = 1 },
            new() { ["rowid"] = 2, ["ref"] = "Chapa Inox 1,2mm / 2400 x 1250", ["label"] = "Chapa Inox 1,2mm / 2400 x 1250", ["statut"] = 1, ["fk_parent"] = 1, ["entity"] = 1 },
            new() { ["rowid"] = 3, ["ref"] = "PA", ["label"] = "Pronta entrega", ["statut"] = 0, ["fk_parent"] = 0, ["entity"] = 1 },
        ]);
        return s;
    }
}
