using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Host;
using BemAqui.Inventory.Contracts;
using BemAqui.Purchasing.Contracts;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// CMP-01 e CMP-03a (ADR 0006, ADR 0023 §6, ADR 0024): o Purchasing só conversa com o Estoque, o Comercial, a Produção, o Financeiro e
/// os Terceiros pelos Contracts e nunca lê tabela do Finance; a ação <c>purchasing.receive</c> está no catálogo e nenhum endpoint desta
/// fase a usa; o aviso <see cref="StockShortageChanged"/> tem o consumidor do Purchasing; a SC e o pedido exigem <c>Idempotency-Key</c>;
/// os eventos do pedido são só aviso (nenhum consumidor cria título).
/// </summary>
public class PurchasingRulesTests
{
    private static string EndpointsDir => Path.Combine(Solution.RootDirectory, "src", "Modules", "Purchasing", "BemAqui.Purchasing.Endpoints");

    private static string EndpointsText => string.Join("\n", Directory.GetFiles(EndpointsDir, "PurchasingEndpoints*.cs").Select(File.ReadAllText));

    private static IEnumerable<(string File, string Text)> PurchasingSources()
    {
        var root = Path.Combine(Solution.RootDirectory, "src", "Modules", "Purchasing");
        var sep = Path.DirectorySeparatorChar;
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(root, f), File.ReadAllText(f)));
    }

    [Fact]
    public void Purchasing_so_referencia_os_Contracts_dos_outros_modulos()
    {
        var others = Solution.Projects.Where(p => p.Module == "Purchasing")
            .SelectMany(p => p.ProjectReferences.Select(Solution.Classify))
            .Where(t => t.Module is not null && t.Module != "Purchasing")
            .Select(t => $"BemAqui.{t.Module}.{t.Layer}")
            .Distinct()
            .ToList();
        others.Should().BeEquivalentTo([
            "BemAqui.Inventory.Contracts", "BemAqui.Commercial.Contracts", "BemAqui.Production.Contracts", "BemAqui.Finance.Contracts", "BemAqui.Parties.Contracts",
        ]);
    }

    /// <summary>CMP-03a (ADR 0024 §4): o Purchasing guarda os próprios níveis e decisões; nada lê <c>finance.*</c> nem o banco do Finance.</summary>
    [Fact]
    public void Nada_do_Purchasing_le_tabela_do_Finance()
    {
        var offending = PurchasingSources()
            .Where(s => s.Text.Contains("IFinanceDb", StringComparison.Ordinal) || s.Text.Contains("\"finance.", StringComparison.Ordinal) && s.Text.Contains("ToTable", StringComparison.Ordinal)
                || s.Text.Contains("finance.payables", StringComparison.Ordinal) || s.Text.Contains("finance.approval_rules", StringComparison.Ordinal))
            .Select(s => s.File)
            .ToList();
        offending.Should().BeEmpty();
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

    /// <summary>CMP-03a: as permissões do pedido vêm do catálogo (view, create, edit, approve).</summary>
    [Fact]
    public void Catalogo_declara_as_acoes_do_pedido()
    {
        foreach (var permission in new[] { "purchasing.view", "purchasing.create", "purchasing.edit", "purchasing.approve" })
        {
            ModuleCatalog.IsKnownPermission(permission).Should().BeTrue(permission);
        }
    }

    [Fact]
    public void Nenhum_endpoint_desta_fase_usa_purchasing_receive() =>
        EndpointsText.Should().NotContain("\"purchasing.receive\"", "o recebimento é da CMP-04");

    [Fact]
    public void Criar_exige_Idempotency_Key_e_todo_endpoint_tem_permissao()
    {
        var text = EndpointsText;
        text.Should().Contain(".RequireModule(\"purchasing\")");
        var statements = text.Split(';').Where(s => s.Contains(".WithName(\"", StringComparison.Ordinal)).ToList();
        statements.Should().NotBeEmpty();
        foreach (var statement in statements)
        {
            statement.Should().MatchRegex(@"\.Require(Any)?Permission(Of)?\(", "todo endpoint de Compras tem perm:");
        }

        statements.Single(s => s.Contains(".WithName(\"CreatePurchaseRequest\")", StringComparison.Ordinal)).Should().Contain(".RequireIdempotencyKey()");
        statements.Single(s => s.Contains(".WithName(\"CreatePurchaseOrder\")", StringComparison.Ordinal)).Should().Contain(".RequireIdempotencyKey()");
        statements.Single(s => s.Contains(".WithName(\"ApprovePurchaseOrder\")", StringComparison.Ordinal)).Should().Contain("\"purchasing.approve\"");
        statements.Single(s => s.Contains(".WithName(\"RejectPurchaseOrder\")", StringComparison.Ordinal)).Should().Contain("\"purchasing.approve\"");
        statements.Single(s => s.Contains(".WithName(\"CancelPurchaseOrder\")", StringComparison.Ordinal)).Should().Contain("\"purchasing.edit\"");
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

    /// <summary>CMP-03a CA 12 (ADR 0024 §1): o previsto nasce na transação da aprovação; os eventos do pedido não têm consumidor que crie título.</summary>
    [Theory]
    [InlineData(typeof(PurchaseOrderSubmitted))]
    [InlineData(typeof(PurchaseOrderLevelReleased))]
    [InlineData(typeof(PurchaseOrderApproved))]
    [InlineData(typeof(PurchaseOrderRejected))]
    [InlineData(typeof(PurchaseOrderChanged))]
    [InlineData(typeof(PurchaseOrderCancelled))]
    public void Eventos_do_pedido_sao_so_aviso(Type eventType)
    {
        var registry = new IntegrationEventRegistry();
        HostEvents.Register(registry);
        registry.TryGet(IntegrationEventRegistry.NameOf(eventType), out var registered).Should().BeTrue();
        registered!.Handlers.Where(h => h.HandlerType.Namespace?.StartsWith("BemAqui.Finance", StringComparison.Ordinal) == true).Should().BeEmpty();
    }

    /// <summary>CMP-03a (ADR 0024 §4): o <c>IApprovalRouting</c> usa o núcleo do <c>ApprovalResolver</c>, sem repetir a regra de alçada.</summary>
    [Fact]
    public void IApprovalRouting_nao_duplica_a_regra_do_ApprovalResolver()
    {
        var routing = typeof(BemAqui.Finance.Application.ApprovalRoutingContract);
        routing.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).Should().Contain(typeof(BemAqui.Finance.Application.ApprovalResolver));
        var source = File.ReadAllText(Path.Combine(Solution.RootDirectory, "src", "Modules", "Finance", "BemAqui.Finance.Application", "ApprovalRoutingContract.cs"));
        source.Should().NotContain("ApprovalRules.Resolve(").And.NotContain("UsersWithPermissionAsync").And.Contain("resolver.ResolveRulesAsync(");
    }
}
