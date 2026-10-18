using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Migrations.Infrastructure;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>MIG-01a CA 2 a 4: GRANT por coluna, extração consistente e restauração no MariaDB temporário com o DDL fixo.</summary>
public class DolibarrSnapshotTests(MariaDbFixture maria) : IClassFixture<MariaDbFixture>
{
    [Theory]
    [InlineData("SELECT `birthday` FROM `llx_socpeople`")]
    [InlineData("SELECT * FROM `llx_socpeople`")]
    [InlineData("SELECT `pass_crypted` FROM `llx_societe`")]
    [InlineData("INSERT INTO `llx_societe` (`rowid`, `nom`) VALUES (99, 'x')")]
    [InlineData("UPDATE `llx_societe` SET `nom` = 'x'")]
    [InlineData("DELETE FROM `llx_societe`")]
    [InlineData("CREATE TABLE `bqe_x` (`id` INT)")]
    [InlineData("DROP TABLE `llx_societe`")]
    public async Task Usuario_somente_leitura_nao_le_coluna_fora_da_lista_nem_grava(string sql)
    {
        var (exit, _, error) = await maria.ReadOnlyAsync(sql + ";");

        exit.Should().NotBe(0, "o MariaDB recusa (CA 2)");
        error.Should().MatchRegex("denied|ERROR 11[0-9]{2}");
    }

    [Fact]
    public async Task Usuario_somente_leitura_le_as_colunas_mapeadas()
    {
        var (exit, output, error) = await maria.ReadOnlyAsync(SnapshotScripts.Select(SnapshotTables.Societe) + ";", "--batch", "--skip-column-names");

        exit.Should().Be(0, error);
        output.Should().Contain("Elevadores");
    }

    [Fact]
    public async Task Snapshot_restaurado_no_DDL_fixo_le_os_cadastros_com_acento_e_so_o_MAIN_INFO_SIREN()
    {
        var (exit, dump, error) = await maria.ReadOnlyAsync(SnapshotScripts.Snapshot(), "--batch", "--raw", "--skip-column-names");
        exit.Should().Be(0, error);
        dump.Should().NotContain("segredo", "só a linha MAIN_INFO_SIREN do llx_const sai (CA 3)").And.NotContain("1980-01-01");

        await maria.RootFileAsync("snapshot", SnapshotScripts.Ddl() + dump);
        await using var source = await MariaDbSnapshotSource.OpenAsync(maria.Connection("snapshot"), TestContext.Current.CancellationToken);

        (await source.CompanyDocumentAsync(TestContext.Current.CancellationToken)).Should().Be("47.225.005/0001-86");
        (await source.TakenAtAsync(TestContext.Current.CancellationToken)).Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        var rows = new List<SourceRow>();
        await foreach (var row in source.ReadAsync(SnapshotTables.Societe, TestContext.Current.CancellationToken))
        {
            rows.Add(row);
        }

        rows.Select(r => r.Id).Should().Equal("1", "2");
        rows[0].Text("nom").Should().Be("Elevadores Ação D'Ávila");
        rows[0].Text("note_private").Should().Be("linha 1\nlinha 2");
        rows[0].WholeNumber("entity").Should().Be(1);
        rows[1].Text("siren").Should().BeNull();
        rows[1]["tms"].Should().BeNull("data zerada do Dolibarr vira null");
    }
}
