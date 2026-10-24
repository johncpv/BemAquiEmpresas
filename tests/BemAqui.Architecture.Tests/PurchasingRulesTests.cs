using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Host;
using BemAqui.Inventory.Contracts;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// CMP-01 (ADR 0006, ADR 0023 §6, ADR 0024 §5): o Purchasing só conversa com o Estoque, o Comercial e a Produção pelos Contracts; a ação
/// <c>purchasing.receive</c> está no catálogo e nenhum endpoint desta fase a usa; o aviso <see cref="StockShortageChanged"/> tem o
/// consumidor do Purchasing; a SC manual exige <c>Idempotency-Key</c>.
/// </summary>
public class PurchasingRulesTests
{
    private static string Endpoints => Path.Combine(Solution.RootDirectory, "src", "Modules", "Purchasing", "BemAqui.Purchasing.Endpoints", "PurchasingEndpoints.cs");

    [Fact]
    public void Purchasing_so_referencia_os_Contracts_do_Estoque_do_Comercial_e_da_Producao()
    {
        var others = Solution.Projects.Where(p => p.Module == "Purchasing")
            .SelectMany(p => p.ProjectReferences.Select(Solution.Classify))
            .Where(t => t.Module is not null && t.Module != "Purchasing")
            .Select(t => $"BemAqui.{t.Module}.{t.Layer}")
            .Distinct()
            .ToList();
        others.Should().BeEquivalentTo(["BemAqui.Inventory.Contracts", "BemAqui.Commercial.Contracts", "BemAqui.Production.Contracts"]);
    }

    [Fact]
    public void Contracts_do_Purchasing_nao_depende_de_outro_modulo() =>
        Solution.Projects.Single(p => p.Name == "BemAqui.Purchasing.Contracts").ProjectReferences
            .Select(Solution.Classify).Where(t => t.Module is not null).Should().BeEmpty();

    /// <summary>CA 1: a ação específica com o texto da ADR 0003 (adendo de 21/10/2026).</summary>
    [Fact]
    public void Catalogo_declara_purchasing_receive()
    {
        var purchasing = ModuleCatalog.Find("purchasing")!;
        purchasing.HasAction("receive").Should().BeTrue();
        purchasing.Specific.Single(a => a.Code == "receive").Description
            .Should().Be("Registrar recebimento: dar entrada no estoque do que chegou de um pedido de compra aprovado.");
        ModuleCatalog.IsKnownPermission("purchasing.receive").Should().BeTrue();
    }

    [Fact]
    public void Nenhum_endpoint_da_CMP_01_usa_purchasing_receive() =>
        File.ReadAllText(Endpoints).Should().NotContain("\"purchasing.receive\"", "o recebimento é da CMP-04");

    [Fact]
    public void Enviar_solicitacao_exige_Idempotency_Key_e_todo_endpoint_tem_permissao()
    {
        var text = File.ReadAllText(Endpoints);
        text.Should().Contain(".RequireModule(\"purchasing\")");
        var statements = text.Split(';').Where(s => s.Contains(".WithName(\"", StringComparison.Ordinal)).ToList();
        statements.Should().NotBeEmpty();
        foreach (var statement in statements)
        {
            statement.Should().MatchRegex(@"\.Require(Any)?Permission(Of)?\(", "todo endpoint de Compras tem perm:");
        }

        statements.Single(s => s.Contains(".WithName(\"CreatePurchaseRequest\")", StringComparison.Ordinal)).Should().Contain(".RequireIdempotencyKey()");
    }

    /// <summary>CA 5 (ADR 0023 §6): o aviso de falta tem o consumidor do Purchasing, que recompõe pela fonte.</summary>
    [Fact]
    public void StockShortageChanged_tem_o_consumidor_do_Purchasing()
    {
        var registry = new IntegrationEventRegistry();
        HostEvents.Register(registry);
        registry.TryGet(IntegrationEventRegistry.NameOf(typeof(StockShortageChanged)), out var registered).Should().BeTrue();
        registered!.Handlers.Select(h => h.HandlerType).Should().Contain(typeof(BemAqui.Purchasing.Application.StockShortageChangedHandler));
    }
}
