using BemAqui.Migrations.Application;
using BemAqui.Migrations.Application.Loaders;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.Parties.Contracts;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>MIG-01b: regras puras do mapeamento (papéis, documento, endereço, notas, conta).</summary>
public class DolibarrMappingTests
{
    private static readonly PartyLookups Lookups = new(new Dictionary<int, string> { [151] = "SP" }, new Dictionary<int, string> { [8] = "TE_PRIVATE" },
        new Dictionary<int, IReadOnlyList<int>> { [9] = [6] });

    private static SourceRow Row(SnapshotTable table, params (string Column, object? Value)[] values) =>
        new(table, Convert.ToString(values.First(v => v.Column is "rowid").Value, System.Globalization.CultureInfo.InvariantCulture)!,
            table.Columns.ToDictionary(c => c.Name, c => values.FirstOrDefault(v => v.Column == c.Name).Value));

    [Theory]
    [InlineData("Rua das Flores, 120 - Sala 3", "Rua das Flores", "120", "Sala 3")]
    [InlineData("Av. Brasil nº 45", "Av. Brasil", "45", null)]
    [InlineData("Rodovia SP-62 km 3", "Rodovia SP-62 km 3", "S/N", null)]
    public void Numero_separado_do_logradouro(string address, string street, string number, string? complement) =>
        DolibarrText.Street(address).Should().BeEquivalentTo(new { Street = street, Number = number, Complement = complement });

    [Fact]
    public void Papeis_especiais_pelo_de_para_por_id_ou_por_categoria()
    {
        var mapping = ImportMapping.Parse("{\"maps\":{\"special-roles\":{\"7\":\"Installer\"},\"special-role-categories\":{\"6\":\"Carrier\"}}}").Value;
        PartyRowMapper.Map(Row(SnapshotTables.Societe, ("rowid", 7), ("nom", "Instala Ltda"), ("fournisseur", 1)), Lookups, mapping, null).Data!.Roles
            .Should().BeEquivalentTo([PartyRoleKind.Supplier, PartyRoleKind.Installer]);
        PartyRowMapper.Map(Row(SnapshotTables.Societe, ("rowid", 9), ("nom", "Frete Ltda"), ("fournisseur", 1)), Lookups, mapping, null).Data!.Roles
            .Should().Contain(PartyRoleKind.Carrier);
        PartyRowMapper.Map(Row(SnapshotTables.Societe, ("rowid", 10), ("nom", "Fornecedor"), ("fournisseur", 1)), Lookups, ImportMapping.Empty, null).Warnings
            .Should().Contain("papel especial não informado no de-para");
    }

    [Fact]
    public void Endereco_incompleto_nao_entra_e_avisa()
    {
        var mapped = PartyRowMapper.Map(Row(SnapshotTables.Societe, ("rowid", 1), ("nom", "Sem CEP"), ("client", 1), ("address", "Rua A, 1"), ("town", "Taubaté")), Lookups, ImportMapping.Empty, null);
        mapped.Data!.BillingAddress.Should().BeNull();
        mapped.Warnings.Should().Contain(w => w.StartsWith("endereço incompleto", StringComparison.Ordinal));
    }

    [Fact]
    public void Conta_de_banco_usa_o_de_para_e_caixa_nao_pede_banco()
    {
        var mapping = ImportMapping.Parse("{\"maps\":{\"bank-codes\":{\"1\":\"341\"},\"bank-agencies\":{\"1\":\"1234\"}}}").Value;
        var bank = BankAccountsLoader.Map(Row(SnapshotTables.BankAccount, ("rowid", 1), ("ref", "IT"), ("label", "Itaú"), ("courant", 1), ("number", "55555-1")), mapping).Data!;
        bank.Should().BeEquivalentTo(new { BankCode = "341", Agency = "1234", Account = "55555-1" });
        BankAccountsLoader.Map(Row(SnapshotTables.BankAccount, ("rowid", 2), ("ref", "CX"), ("courant", 2), ("clos", 1)), ImportMapping.Empty).Data!
            .Should().BeEquivalentTo(new { BankCode = (string?)null, IsActive = false });
    }

    [Fact]
    public void Html_vira_texto_e_telefone_com_DDD()
    {
        DolibarrText.HtmlToText("<div>Linha&nbsp;1</div><ul><li>a</li><li>b</li></ul>").Should().Be("Linha 1\na\nb");
        DolibarrText.Phone("(12) 3653-1000").Should().Be("1236531000");
        DolibarrText.Phone("3653-1000").Should().BeNull();
    }
}
