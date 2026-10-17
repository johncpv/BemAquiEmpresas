using FluentAssertions;

namespace BemAqui.Architecture.Tests;

public class MigrationRulesTests
{
    private const string Header = "public partial class X : Migration {\n protected override void Up(MigrationBuilder migrationBuilder) {\n";
    private const string Footer = "\n }\n protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable(name: \"t\"); }\n}";

    [Fact]
    public void Nenhuma_migration_da_solucao_e_destrutiva_sem_ADR() =>
        MigrationRules.DestructiveWithoutAdr(MigrationRules.SolutionMigrations()).Should().BeEmpty();

    [Fact]
    public void Existem_migrations_para_verificar() =>
        MigrationRules.SolutionMigrations().Should().HaveCountGreaterThanOrEqualTo(4);

    [Theory]
    [InlineData("migrationBuilder.DropColumn(name: \"email\", table: \"users\");")]
    [InlineData("migrationBuilder.RenameColumn(name: \"a\", table: \"t\", newName: \"b\");")]
    [InlineData("migrationBuilder.DropTable(name: \"legado\");")]
    public void Remover_ou_renomear_no_Up_sem_ADR_falha(string statement) =>
        MigrationRules.DestructiveWithoutAdr([("M.cs", Header + statement + Footer)]).Should().ContainSingle();

    [Fact]
    public void Com_ADR_referenciada_passa() =>
        MigrationRules.DestructiveWithoutAdr([("M.cs", "// contract da coluna email: ADR 0012\n" + Header + "migrationBuilder.DropColumn(name: \"email\", table: \"users\");" + Footer)])
            .Should().BeEmpty();

    [Fact]
    public void DropTable_no_Down_nao_conta() =>
        MigrationRules.DestructiveWithoutAdr([("M.cs", Header + "migrationBuilder.CreateTable(name: \"t\");" + Footer)]).Should().BeEmpty();
}
