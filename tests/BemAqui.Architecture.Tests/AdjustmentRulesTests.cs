using System.Reflection;
using System.Text.Json.Serialization;
using BemAqui.Inventory.Application;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// EST-01b: as rotas do ajuste ficam no grupo com o módulo e cada uma com <c>perm:</c> (lançar e cancelar com <c>inventory.adjust</c>;
/// aprovar, reprovar e estornar com <c>inventory.approve</c>, CA 16); lançar, aprovar e estornar exigem <c>Idempotency-Key</c>; a decisão
/// só aceita o comentário (CA 6); o efeito de estoque é só pelo motor (ADR 0023 §1) e o bloqueio do local é consultado em lançar, aprovar
/// e estornar e no estorno manual (CA 18).
/// </summary>
public class AdjustmentRulesTests
{
    private static string Inventory => Path.Combine(Solution.RootDirectory, "src", "Modules", "Inventory");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Inventory, .. parts]));

    private static string Statement(string endpoints, string name) => endpoints.Split(';').Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal));

    [Fact]
    public void Rotas_do_ajuste_tem_modulo_permissao_e_Idempotency_Key()
    {
        var endpoints = Read("BemAqui.Inventory.Endpoints", "AdjustmentEndpoints.cs");
        endpoints.Should().Contain(".MapGroup(\"/inventory/adjustments\").WithTags(\"Estoque\").RequireModule(\"inventory\")", "RequireModule no grupo de rotas");
        foreach (var statement in endpoints.Split(';').Where(s => s.Contains(".WithName(", StringComparison.Ordinal)))
        {
            statement.Should().MatchRegex(@"\.RequirePermission\(""inventory\.(view|adjust|approve)""\)", "CA 16: perm: do próprio módulo em cada rota");
        }

        foreach (var (name, permission) in new[] { ("CreateStockAdjustment", "adjust"), ("CancelStockAdjustment", "adjust"), ("UpdateStockAdjustment", "adjust"), ("ApproveStockAdjustment", "approve"), ("RejectStockAdjustment", "approve"), ("ReverseStockAdjustment", "approve") })
        {
            Statement(endpoints, name).Should().Contain($"RequirePermission(\"inventory.{permission}\")", name);
        }

        foreach (var name in new[] { "CreateStockAdjustment", "ApproveStockAdjustment", "ReverseStockAdjustment" })
        {
            Statement(endpoints, name).Should().Contain(".RequireIdempotencyKey()", $"{name} cria ajuste ou movimento");
        }
    }

    [Fact]
    public void A_decisao_so_aceita_o_comentario_e_a_linha_nao_tem_modo()
    {
        typeof(StockAdjustmentDecisionInput).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(["Comment", "Version", "Extra"], "CA 6: decide sem editar");
        typeof(StockAdjustmentDecisionInput).GetProperty("Extra")!.GetCustomAttribute<JsonExtensionDataAttribute>().Should().NotBeNull("o campo a mais vira 422, não é ignorado");
        typeof(StockAdjustmentLineInput).GetProperties().Select(p => p.Name).Should().NotContain("Mode", "CA 2: o modo é do ajuste inteiro");
        typeof(StockAdjustmentLineInput).GetProperty("Extra")!.GetCustomAttribute<JsonExtensionDataAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void O_efeito_e_so_pelo_motor_e_o_bloqueio_do_local_e_consultado()
    {
        var service = Read("BemAqui.Inventory.Application", "StockAdjustmentService.cs") + Read("BemAqui.Inventory.Application", "StockAdjustmentService.Decide.cs");
        service.Should().Contain("ledger.PostManyAsync(").And.Contain("ledger.ReverseAsync(", "ADR 0023 §1: aprovação e estorno síncronos pelo IStockMovements");
        service.Should().NotContainAny(["StockPositions.Update", "StockBalances.Update", "ApplyOnHand", "ExecuteSql"], "o ajuste não escreve saldo");
        service.Split("locks.CheckAsync(").Length.Should().Be(4, "CA 18: lançar, aprovar e estornar consultam o bloqueio do local");
        Read("BemAqui.Inventory.Application", "ManualPostingService.Reverse.cs").Should().Contain("locks.CheckAsync(", "CA 18: o estorno de movimento manual também");
        Read("BemAqui.Inventory.Infrastructure", "InventoryInfrastructure.cs").Should().Contain("TryAddScoped<ILocationCountLock, NoLocationCountLock>()", "até a EST-05 nunca bloqueia");
    }
}
