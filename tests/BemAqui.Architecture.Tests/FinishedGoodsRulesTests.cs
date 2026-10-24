using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Application;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PRD-06a2 (ADR 0023 §1 e §5, ADR 0006): as séries e os lotes consumidos só mudam pelo <c>StockSerialService</c> (na transação de quem é
/// dono do documento); a Produção não grava tabela do estoque, só chama os contratos; e os DTOs da OS, das séries e do diálogo não têm
/// custo (CA 14: o custo apurado vem em DTO separado, com <c>projects.view_cost</c>).
/// </summary>
public class FinishedGoodsRulesTests
{
    private const string SerialService = "Inventory/BemAqui.Inventory.Application/StockSerialService.cs";
    private static readonly string[] CostWords = ["Cost", "Amount", "Price", "Value", "Average"];

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
    public void So_o_servico_de_series_grava_series_lotes_consumidos_e_a_sequencia()
    {
        var writers = FilesContaining(
            "Serials.Add", "SerialComponents.Add", "SerialSequences.Add", "INSERT INTO inventory.serial", "UPDATE inventory.serial", "Status = SerialState.", "component.ReversedAt");
        writers.Should().Contain(SerialService, "a regra precisa enxergar o serviço");
        writers.Should().BeSubsetOf([SerialService], "série e vínculo de lotes mudam só pelo IStockSerials/IStockTraceability (PRD-06a2)");
    }

    [Fact]
    public void Producao_nao_grava_tabela_do_estoque()
    {
        FilesContaining("INTO inventory.", "UPDATE inventory.", "FROM inventory.", "IInventoryDb").Where(f => f.StartsWith("Production/", StringComparison.Ordinal))
            .Should().BeEmpty("a Produção chama o IStockMovements/IStockSerials, nunca grava no schema inventory (ADR 0006 e 0023 §1)");
    }

    [Fact]
    public void Conclusao_chama_o_estoque_dentro_da_transacao_da_OS()
    {
        var completion = File.ReadAllText(Path.Combine(Modules, "Production", "BemAqui.Production.Application", "FabricationOrderService.Completion.cs"));
        completion.Should().Contain("BeginTransactionAsync", "a Produção é dona da transação da conclusão (ADR 0023 §1)");
        completion.Should().Contain("RollbackAsync", "qualquer falha do estoque desfaz a conclusão (CA 5)");
    }

    [Theory]
    [InlineData(typeof(OrderSerialsView))]
    [InlineData(typeof(OrderSerialRow))]
    [InlineData(typeof(OrderSerialDetail))]
    [InlineData(typeof(ItemSerialRow))]
    [InlineData(typeof(OrderConsumedLotRow))]
    [InlineData(typeof(SerialInfo))]
    [InlineData(typeof(ConsumedLot))]
    [InlineData(typeof(CompletionReservationView))]
    [InlineData(typeof(CompletionLocationOption))]
    [InlineData(typeof(OrderProductOption))]
    [InlineData(typeof(FabricationOrderDetail))]
    public void DTO_das_series_e_da_OS_nao_tem_custo(Type dto) =>
        dto.GetProperties().Select(p => p.Name).Where(n => CostWords.Any(w => n.Contains(w, StringComparison.Ordinal)))
            .Should().BeEmpty($"{dto.Name} é lido sem projects.view_cost (PRD-06a2 CA 14)");

    [Fact]
    public void Impacto_no_estoque_do_dialogo_so_tem_a_marca_do_custo_zerado()
    {
        typeof(CompletionStockView).GetProperties().Select(p => p.Name).Where(n => CostWords.Any(w => n.Contains(w, StringComparison.Ordinal)))
            .Should().Equal(["ZeroCost"], "o custo apurado vem do GET …/completion/cost, com projects.view_cost (CA 2 e 14)");
    }
}
