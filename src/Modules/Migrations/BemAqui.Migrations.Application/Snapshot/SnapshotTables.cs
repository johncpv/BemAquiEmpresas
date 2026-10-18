namespace BemAqui.Migrations.Application.Snapshot;

/// <summary>Coluna mapeada do Dolibarr: nome, tipo no DDL fixo do snapshot e se é dado pessoal (mascarado na staging e no relatório).</summary>
public sealed record SnapshotColumn(string Name, string Type, bool Personal = false);

/// <summary>Tabela da lista fixa (ADR 0017): só estas colunas entram no GRANT, no snapshot e no DDL.</summary>
public sealed record SnapshotTable(string Name, IReadOnlyList<string> Key, IReadOnlyList<SnapshotColumn> Columns, string? Filter = null)
{
    public SnapshotColumn? Column(string name) => Columns.FirstOrDefault(c => c.Name == name);
}

/// <summary>
/// Lista fixa de tabelas e colunas lidas do Dolibarr (MIG-01a, mapeamento de <c>docs/operacao/migracao-dolibarr.md</c>).
/// Fonte única: os scripts de GRANT, de snapshot e o DDL em <c>scripts/dolibarr/</c> são gerados daqui (teste de consistência).
/// Os nomes seguem o Dolibarr padrão: <c>siren</c>, <c>siret</c>, <c>ape</c> e <c>idprof4</c> são o <c>idprof1</c> a
/// <c>idprof4</c> da tela; o inventário da base real (CA 1) confirma antes da primeira carga.
/// </summary>
public static partial class SnapshotTables
{
    private const string Int = "INT";
    private const string TinyInt = "TINYINT";
    private const string SmallInt = "SMALLINT";
    private const string Timestamp = "DATETIME";
    private const string Text = "TEXT";

    private static string Str(int length) => "VARCHAR(" + length + ")";

    private static SnapshotColumn C(string name, string type, bool personal = false) => new(name, type, personal);

    public static readonly SnapshotTable Societe = new("llx_societe", ["rowid"],
    [
        C("rowid", Int), C("nom", Str(128)), C("name_alias", Str(128)), C("client", TinyInt), C("fournisseur", TinyInt),
        C("siren", Str(128), true), C("siret", Str(128), true), C("ape", Str(128)), C("idprof4", Str(128)), C("tva_intra", Str(20)),
        C("address", Str(255), true), C("zip", Str(25)), C("town", Str(50)), C("fk_departement", Int), C("fk_pays", Int),
        C("phone", Str(20), true), C("email", Str(128), true), C("note_private", Text, true), C("note_public", Text, true),
        C("status", TinyInt), C("cond_reglement", Int), C("fk_typent", Int), C("entity", Int), C("tms", Timestamp),
    ]);

    public static readonly SnapshotTable SocPeople = new("llx_socpeople", ["rowid"],
    [
        C("rowid", Int), C("fk_soc", Int), C("lastname", Str(50), true), C("firstname", Str(50), true), C("poste", Str(255)),
        C("email", Str(255), true), C("phone", Str(30), true), C("phone_mobile", Str(30), true), C("address", Str(255), true),
        C("zip", Str(25)), C("town", Str(255)), C("statut", TinyInt), C("entity", Int), C("tms", Timestamp),
    ]);

    public static readonly SnapshotTable PaymentTerm = new("llx_c_payment_term", ["rowid"],
    [
        C("rowid", Int), C("code", Str(16)), C("libelle", Str(255)), C("nbjour", SmallInt), C("type_cdr", TinyInt),
        C("decalage", SmallInt), C("active", TinyInt), C("entity", Int),
    ]);

    public static readonly SnapshotTable BankAccount = new("llx_bank_account", ["rowid"],
    [
        C("rowid", Int), C("ref", Str(12)), C("label", Str(30)), C("bank", Str(60)), C("code_banque", Str(128)),
        C("code_guichet", Str(6)), C("number", Str(255)), C("cle_rib", Str(5)), C("courant", SmallInt), C("clos", SmallInt), C("entity", Int),
    ]);
}
