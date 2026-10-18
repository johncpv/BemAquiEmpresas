namespace BemAqui.Migrations.Application.Snapshot;

public static partial class SnapshotTables
{
    public static readonly SnapshotTable Departements = new("llx_c_departements", ["rowid"], [C("rowid", Int), C("code_departement", Str(6))]);

    public static readonly SnapshotTable TypeEnt = new("llx_c_typent", ["id"], [C("id", Int), C("code", Str(12))]);

    public static readonly SnapshotTable Categorie = new("llx_categorie", ["rowid"],
        [C("rowid", Int), C("label", Str(180)), C("type", Int), C("fk_parent", Int), C("entity", Int)]);

    public static readonly SnapshotTable CategorieFournisseur = new("llx_categorie_fournisseur", ["fk_categorie", "fk_soc"], [C("fk_categorie", Int), C("fk_soc", Int)]);

    public static readonly SnapshotTable CategorieProduct = new("llx_categorie_product", ["fk_categorie", "fk_product"], [C("fk_categorie", Int), C("fk_product", Int)]);

    public static readonly SnapshotTable Units = new("llx_c_units", ["rowid"], [C("rowid", Int), C("code", Str(3)), C("label", Str(128)), C("active", TinyInt)]);

    public static readonly SnapshotTable Product = new("llx_product", ["rowid"],
    [
        C("rowid", Int), C("ref", Str(128)), C("label", Str(255)), C("fk_product_type", Int), C("fk_unit", Int), C("customcode", Str(32)),
        C("fk_country", Int), C("seuil_stock_alerte", "FLOAT"), C("tobatch", TinyInt), C("tosell", TinyInt), C("tobuy", TinyInt),
        C("entity", Int), C("tms", Timestamp),
    ]);

    public static readonly SnapshotTable ProductExtrafields = new("llx_product_extrafields", ["fk_object"], [C("fk_object", Int), C("elev_garantia", Str(255))]);

    public static readonly SnapshotTable SupplierPrice = new("llx_product_fournisseur_price", ["rowid"],
        [C("rowid", Int), C("fk_product", Int), C("fk_soc", Int), C("ref_fourn", Str(128)), C("quantity", "DOUBLE"), C("fk_unit", Int), C("entity", Int)]);

    public static readonly SnapshotTable Entrepot = new("llx_entrepot", ["rowid"],
        [C("rowid", Int), C("ref", Str(255)), C("label", Str(255)), C("lieu", Str(64)), C("statut", TinyInt), C("fk_parent", Int), C("entity", Int)]);

    /// <summary>Só a linha <c>MAIN_INFO_SIREN</c> (CNPJ da empresa no Dolibarr), para a proteção contra tenant errado (CA 6).</summary>
    public static readonly SnapshotTable Const = new("llx_const", ["name", "entity"],
        [C("name", Str(180)), C("value", Text), C("entity", Int)], "name = 'MAIN_INFO_SIREN'");

    /// <summary>Ordem do snapshot e do DDL. Propriedade (e não campo) porque os campos ficam em dois arquivos parciais.</summary>
    public static IReadOnlyList<SnapshotTable> All =>
    [
        Const, Societe, SocPeople, PaymentTerm, BankAccount, Departements, TypeEnt, Categorie, CategorieFournisseur,
        CategorieProduct, Units, Product, ProductExtrafields, SupplierPrice, Entrepot,
    ];

    public static SnapshotTable? Find(string name) => All.FirstOrDefault(t => t.Name == name);
}
