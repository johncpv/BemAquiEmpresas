using System.Reflection;
using BemAqui.Inventory.Domain;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// EST-01a (ADR 0023 §2 a §4): só o motor (<c>StockLedger</c>) escreve na posição, no lote e no custo médio; o movimento de estoque
/// nunca é alterado nem apagado (sem setter público, sem <c>Update</c>/<c>Remove</c>); todo comando que cria movimento ou
/// lançamento exige <c>Idempotency-Key</c> (CA 22, 30 e 32).
/// </summary>
public class InventoryRulesTests
{
    private const string Ledger = "Inventory/BemAqui.Inventory.Application/StockLedger.cs";

    private static string Modules => Path.Combine(Solution.RootDirectory, "src", "Modules");

    private static List<string> FilesContaining(params string[] texts)
    {
        var sep = Path.DirectorySeparatorChar;
        return Directory.GetFiles(Modules, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Where(f => { var text = File.ReadAllText(f); return texts.Any(t => text.Contains(t, StringComparison.Ordinal)); })
            .Select(f => Path.GetRelativePath(Modules, f).Replace(sep, '/'))
            .ToList();
    }

    [Fact]
    public void So_o_motor_escreve_posicao_lote_e_custo_medio()
    {
        var writers = FilesContaining(
            ".ApplyOnHand(", ".ApplyAverageCost(", "StockPositions.Add", "StockBalances.Add", "INSERT INTO inventory.stock_positions", "INSERT INTO inventory.stock_balances",
            "UPDATE inventory.stock_positions", "UPDATE inventory.stock_balances", "SET average_cost");
        writers.Should().Contain(Ledger, "a regra precisa enxergar o motor");
        writers.Should().BeSubsetOf([Ledger], "posição, lote e custo médio mudam só pelo IStockMovements (CA 32)");
    }

    [Fact]
    public void Movimento_de_estoque_nao_e_alterado_nem_apagado()
    {
        FilesContaining(
                "StockMovements.Remove", "StockMovements.Update", "StockMovements.Attach", "StockMovements.ExecuteUpdate", "StockMovements.ExecuteDelete",
                "UPDATE inventory.stock_movements", "DELETE FROM inventory.stock_movements")
            .Should().BeEmpty("movimento é imutável: estorno é movimento inverso (CA 22)");
        typeof(StockMovement).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true }).Select(p => p.Name)
            .Should().BeEmpty("nenhum setter público no movimento");
        typeof(StockMovement).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Should().BeEmpty("só a fábrica cria o movimento");
    }

    [Fact]
    public void Comandos_que_criam_movimento_ou_lancamento_exigem_Idempotency_Key()
    {
        var text = File.ReadAllText(Path.Combine(Modules, "Inventory", "BemAqui.Inventory.Endpoints", "StockEndpoints.cs"));
        var statements = text.Split(';');
        string[] commands = ["CreateOpeningBalance", "CreateOpeningBalanceBatch", "CreateManualPosting", "ApproveManualPosting", "ReverseStockMovement"];
        foreach (var name in commands)
        {
            var statement = statements.Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal));
            statement.Should().Contain(".RequireIdempotencyKey()", $"{name} cria movimento ou lançamento (CA 30)");
            statement.Should().MatchRegex(@"\.Require(Any)?Permission\(", $"{name} tem perm:");
        }
    }
}
