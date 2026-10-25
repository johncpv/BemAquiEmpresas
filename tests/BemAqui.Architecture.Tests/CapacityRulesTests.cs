using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// EST-06: a capacidade de produzir só lê (nenhuma gravação, CA 6), lê saldos e pedidos em aberto em lote, uma chamada por contrato
/// (CA 7), e as rotas ficam no grupo com o módulo <c>inventory</c>, o <c>engineering</c> e os dois <c>perm:</c> (CA 6).
/// </summary>
public class CapacityRulesTests
{
    private static string Inventory => Path.Combine(Solution.RootDirectory, "src", "Modules", "Inventory");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Inventory, .. parts]));

    private static int Count(string text, string term) => text.Split(term).Length - 1;

    [Fact]
    public void Capacidade_so_le()
    {
        var service = Read("BemAqui.Inventory.Application", "ProductionCapacityService.cs") + Read("BemAqui.Inventory.Application", "ProductionCapacityCalculator.cs");
        service.Should().NotContainAny(["SaveChanges", "ExecuteUpdate", "ExecuteDelete", "ExecuteSql", "IEventPublisher", "IStockMovements", "ReservationAllocator", "Attach("],
            "CA 6: o cálculo só lê, nada é gravado");
        service.Should().NotMatchRegex(@"db\.\w+\.(Add|AddRange|Remove|RemoveRange|Update)\(", "nenhuma tabela recebe linha nova nem alterada");
        service.Should().Contain(".AsNoTracking()");
    }

    [Fact]
    public void Saldos_e_pedidos_em_aberto_numa_chamada_por_contrato()
    {
        var service = Read("BemAqui.Inventory.Application", "ProductionCapacityService.cs");
        Count(service, "balances.GetAsync(").Should().Be(1, "CA 7: o disponível de todos os itens numa consulta à posição agregada");
        Count(service, "purchaseOrders.OpenQuantityByItemAsync(").Should().Be(1, "CA 7: o saldo a receber de todos os itens numa chamada");
        service.Should().Contain("PurchaseOrderDestination.All", "pedidos em aberto de todos os destinos");
        Count(service, "boms.ReleasedForOrderAsync(").Should().Be(1);
    }

    [Fact]
    public void Rotas_da_capacidade_tem_o_modulo_do_grupo_e_as_duas_permissoes()
    {
        var endpoints = Read("BemAqui.Inventory.Endpoints", "CapacityEndpoints.cs");
        endpoints.Should().Contain("MapGroup(\"/inventory/production-capacity\").WithTags(\"Estoque\").RequireModule(\"inventory\")", "RequireModule no grupo de rotas");
        var routes = endpoints.Split(';').Where(s => s.Contains(".WithName(", StringComparison.Ordinal)).ToList();
        routes.Should().HaveCount(2);
        foreach (var route in routes)
        {
            route.Should().NotContain(".RequireModule(", "um RequireModule só, no grupo; o engineering é conferido no serviço").And.Contain(".RequirePermission(\"inventory.view\")").And.Contain(".RequirePermission(\"engineering.view\")");
            route.Should().NotContain("MapPost").And.NotContain("MapPut").And.NotContain("MapDelete");
        }
    }
}
