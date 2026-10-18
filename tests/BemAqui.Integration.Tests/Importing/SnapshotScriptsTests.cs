using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Infrastructure;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>
/// Os scripts de <c>scripts/dolibarr/</c> saem da lista fixa (<see cref="SnapshotTables"/>): GRANT, snapshot e DDL nunca
/// divergem. Para regravar depois de mudar a lista: <c>BQ_UPDATE_SNAPSHOT_SQL=1 sh scripts/test-docker.sh</c>.
/// </summary>
public class SnapshotScriptsTests
{
    public static TheoryData<string> Files => new() { "grant-readonly.sql", "snapshot.sql", "snapshot-ddl.sql" };

    internal static string Expected(string file) => file switch
    {
        "grant-readonly.sql" => SnapshotScripts.Grant(),
        "snapshot.sql" => SnapshotScripts.Snapshot(),
        _ => SnapshotScripts.Ddl(),
    };

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Script_versionado_e_igual_ao_gerado_da_lista_fixa(string file)
    {
        var path = Path.Combine(OpenApiDocumentTests.RepositoryRoot(), "scripts", "dolibarr", file);
        if (Environment.GetEnvironmentVariable("BQ_UPDATE_SNAPSHOT_SQL") == "1")
        {
            await File.WriteAllTextAsync(path, Expected(file), TestContext.Current.CancellationToken);
        }

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Should().Be(Expected(file), "a lista fixa mudou: rode BQ_UPDATE_SNAPSHOT_SQL=1 sh scripts/test-docker.sh");
    }

    [Fact]
    public void Lista_fixa_nao_tem_coluna_proibida_pela_ADR_0017()
    {
        string[] forbidden = ["birthday", "iban_prefix", "bic", "proprio", "pmp", "price", "price_ttc", "solde", "amount", "pass", "pass_crypted", "api_key"];
        SnapshotTables.All.SelectMany(t => t.Columns).Select(c => c.Name).Should().NotIntersectWith(forbidden);
        SnapshotTables.All.Select(t => t.Name).Should().NotContain(["llx_facture", "llx_paiement", "llx_bank", "llx_product_stock", "llx_user", "llx_societe_rib"]);
        SnapshotScripts.Snapshot().Should().Contain("START TRANSACTION WITH CONSISTENT SNAPSHOT, READ ONLY").And.NotContain("SELECT *");
    }
}
