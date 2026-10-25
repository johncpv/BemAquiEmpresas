using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// EST-05: as rotas do inventário ficam no grupo com o módulo e cada uma com <c>perm:</c> (abrir, divergências, recontagem, fechar,
/// cancelar e reabrir com <c>inventory.adjust</c>; contar com <c>inventory.create</c>, CA 14); abrir e fechar exigem
/// <c>Idempotency-Key</c>; o inventário não escreve saldo nem chama o motor (o efeito é só pelo ajuste, ADR 0023 §1) e o bloqueio do
/// local é o real (CA 6).
/// </summary>
public class InventoryCountRulesTests
{
    private static string Inventory => Path.Combine(Solution.RootDirectory, "src", "Modules", "Inventory");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Inventory, .. parts]));

    private static string Statement(string endpoints, string name) => endpoints.Split(';').Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal));

    [Fact]
    public void Rotas_do_inventario_tem_modulo_permissao_e_Idempotency_Key()
    {
        var endpoints = Read("BemAqui.Inventory.Endpoints", "InventoryCountEndpoints.cs");
        endpoints.Should().Contain(".MapGroup(\"/inventory/inventory-counts\").WithTags(\"Estoque\").RequireModule(\"inventory\")", "RequireModule no grupo de rotas");
        foreach (var statement in endpoints.Split(';').Where(s => s.Contains(".WithName(", StringComparison.Ordinal)))
        {
            statement.Should().MatchRegex(@"\.RequirePermission\(""inventory\.(view|create|adjust)""\)", "CA 14: perm: do próprio módulo em cada rota");
        }

        foreach (var (name, permission) in new[]
                 {
                     ("OpenInventoryCount", "adjust"), ("GetInventoryCountDivergences", "adjust"), ("RecountInventoryLines", "adjust"), ("CloseInventoryCount", "adjust"),
                     ("CancelInventoryCount", "adjust"), ("ReopenInventoryCount", "adjust"), ("CountInventoryLine", "create"), ("AddInventoryCountLine", "create"),
                 })
        {
            Statement(endpoints, name).Should().Contain($"RequirePermission(\"inventory.{permission}\")", name);
        }

        foreach (var name in new[] { "OpenInventoryCount", "CloseInventoryCount" })
        {
            Statement(endpoints, name).Should().Contain(".RequireIdempotencyKey()", $"{name} cria inventário ou ajuste");
        }
    }

    [Fact]
    public void O_inventario_so_gera_o_ajuste_e_o_bloqueio_e_o_real()
    {
        var service = Read("BemAqui.Inventory.Application", "InventoryCountService.cs") + Read("BemAqui.Inventory.Application", "InventoryCountService.Views.cs");
        service.Should().Contain("adjustments.CreateFromCountAsync(", "CA 10: o fechamento gera o ajuste em modo Delta");
        service.Should().NotContainAny(["IStockMovements", "ledger.", "StockPositions.", "ApplyOnHand", "ApplyReserved", "ExecuteSql"], "o inventário nunca mexe no saldo");
        Read("BemAqui.Inventory.Infrastructure", "InventoryInfrastructure.cs").Should().Contain("TryAddScoped<ILocationCountLock, InventoryCountLock>()", "CA 6");
        Read("BemAqui.Inventory.Application", "StockAdjustmentService.Decide.cs").Should().Contain("allocator.ReduceForCountAsync(", "decisão do PO: reduz as reservas na aprovação");
    }
}
