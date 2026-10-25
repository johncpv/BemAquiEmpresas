using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// EST-04: a busca da rastreabilidade só usa parâmetros (nada de <c>LIKE</c> montado com texto do usuário, CA 10); a exportação passa pelo
/// CSV seguro e vai para a auditoria (CA 4); o cliente sai sempre pelo <c>CustomerNameMasker</c> (CA 9); as rotas ficam no grupo com o
/// módulo e cada uma com <c>perm:</c> (CA 8); o Inventory não lê o Purchasing (ADR 0006).
/// </summary>
public class TraceabilityRulesTests
{
    private static string Inventory => Path.Combine(Solution.RootDirectory, "src", "Modules", "Inventory");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Inventory, .. parts]));

    [Fact]
    public void Busca_so_usa_parametros()
    {
        var service = Read("BemAqui.Inventory.Application", "TraceabilityService.cs");
        service.Should().NotContainAny(["EF.Functions.Like", "EF.Functions.ILike", "FromSqlRaw", "ExecuteSqlRaw", "SqlQueryRaw", "LIKE '"], "CA 10: o termo do usuário vai só como parâmetro");
        service.Should().Contain(".StartsWith(term)", "busca exata ou por prefixo pelo EF, que escapa o curinga no parâmetro");
        service.Should().Contain("TraceabilityRules.MaxResults", "até 50 resultados");
    }

    [Fact]
    public void Exportacao_passa_pelo_CSV_seguro_e_pela_auditoria()
    {
        var service = Read("BemAqui.Inventory.Application", "TraceabilityService.cs");
        service.Should().Contain("SafeCsv.Text(").And.Contain("SafeCsv.Bytes(").And.Contain("audit.RecordExportAsync(", "CA 4");
        service.Split("CustomerNameMasker.Customer(").Length.Should().BeGreaterThan(2, "CA 9: série e lote montam o cliente pelo mascarador");
        service.Should().NotContain(".Document", "CA 9: nenhum CPF ou CNPJ sai da rastreabilidade");
    }

    [Fact]
    public void Rotas_da_rastreabilidade_tem_modulo_e_permissao()
    {
        var endpoints = Read("BemAqui.Inventory.Endpoints", "TraceabilityEndpoints.cs");
        endpoints.Should().Contain(".RequireModule(\"inventory\")", "CA 8: RequireModule no grupo de rotas");
        foreach (var statement in endpoints.Split(';').Where(s => s.Contains(".WithName(", StringComparison.Ordinal)))
        {
            statement.Should().MatchRegex(@"\.RequirePermission\(""inventory\.(view|edit)""\)", "CA 8: perm: em cada rota");
        }

        endpoints.Should().Contain("WithName(\"CorrectItemLot\")");
        endpoints.Split(';').Single(s => s.Contains("WithName(\"CorrectItemLot\")", StringComparison.Ordinal)).Should().Contain("inventory.edit", "CA 6");
    }

    [Fact]
    public void Inventory_nao_le_o_Purchasing()
    {
        var application = Path.Combine(Inventory, "BemAqui.Inventory.Application");
        // CMP-01 (aviso de falta) lê Compras só pelo Purchasing.Contracts; a rastreabilidade não lê Compras.
        File.ReadAllText(Path.Combine(application, "BemAqui.Inventory.Application.csproj")).Should()
            .NotContainAny(["Purchasing.Application", "Purchasing.Domain", "Purchasing.Infrastructure"], "ADR 0006: só o Purchasing.Contracts");
        File.ReadAllText(Path.Combine(application, "TraceabilityService.cs")).Should().NotContain("Purchasing", "o recebimento entra pelo movimento (número REC), sem ler a tabela de Compras");
    }
}
