using BemAqui.Migrations.Application.Snapshot;

namespace BemAqui.Migrations.Infrastructure;

/// <summary>Como uma tabela da lista fixa é lida da API REST do Dolibarr: endpoint e campo do JSON para cada coluna.</summary>
internal sealed record ApiTable(string Endpoint, IReadOnlyDictionary<string, string> Fields, IReadOnlyDictionary<string, object?>? Constants = null);

/// <summary>
/// De-para coluna da lista fixa → campo da API REST do Dolibarr 21 (inventário de 18/10/2026). Só as colunas da lista entram:
/// o resto do JSON (IBAN, saldo, preços, custo, estoque) é descartado na leitura e nunca chega à staging (ADR 0017, adendo 2).
/// </summary>
internal static class DolibarrApiMap
{
    private static Dictionary<string, string> F(params (string Column, string Field)[] pairs) => pairs.ToDictionary(p => p.Column, p => p.Field, StringComparer.Ordinal);

    /// <summary>Colunas cujo nome no JSON é igual ao da tabela.</summary>
    private static Dictionary<string, string> Same(SnapshotTable table, Dictionary<string, string> overrides)
    {
        foreach (var column in table.Columns.Where(c => !overrides.ContainsKey(c.Name)))
        {
            overrides[column.Name] = column.Name;
        }

        return overrides;
    }

    public static readonly IReadOnlyDictionary<string, ApiTable> Tables = new Dictionary<string, ApiTable>(StringComparer.Ordinal)
    {
        [SnapshotTables.Societe.Name] = new("/thirdparties", Same(SnapshotTables.Societe, F(
            ("rowid", "id"), ("nom", "name"), ("siren", "idprof1"), ("siret", "idprof2"), ("ape", "idprof3"),
            ("fk_departement", "state_id"), ("fk_pays", "country_id"), ("cond_reglement", "cond_reglement_id"), ("fk_typent", "typent_id"),
            ("tms", "date_modification")))),
        [SnapshotTables.SocPeople.Name] = new("/contacts", Same(SnapshotTables.SocPeople, F(("rowid", "id"), ("fk_soc", "socid"), ("phone", "phone_pro"), ("tms", "date_modification")))),
        [SnapshotTables.PaymentTerm.Name] = new("/setup/dictionary/payment_terms", Same(SnapshotTables.PaymentTerm, F(("rowid", "id"), ("libelle", "label"))),
            new Dictionary<string, object?> { ["active"] = 1, ["entity"] = 1 }),
        [SnapshotTables.BankAccount.Name] = new("/bankaccounts", Same(SnapshotTables.BankAccount, F(("rowid", "id")))),
        [SnapshotTables.Departements.Name] = new("/setup/dictionary/states?country=56", F(("rowid", "id"), ("code_departement", "code_departement"))),
        [SnapshotTables.Units.Name] = new("/setup/dictionary/units", Same(SnapshotTables.Units, F())),
        [SnapshotTables.Product.Name] = new("/products", Same(SnapshotTables.Product, F(
            ("rowid", "id"), ("fk_product_type", "type"), ("fk_country", "country_id"), ("tobatch", "status_batch"), ("tosell", "status"),
            ("tobuy", "status_buy"), ("tms", "date_modification")))),
        [SnapshotTables.Entrepot.Name] = new("/warehouses", Same(SnapshotTables.Entrepot, F(("rowid", "id")))),
    };

    /// <summary>Tipo da categoria na API (<c>type=</c>) e o código numérico do <c>llx_categorie.type</c>.</summary>
    public static readonly IReadOnlyList<(string Api, int Code)> CategoryTypes = [("product", 0), ("supplier", 1), ("customer", 2)];
}
