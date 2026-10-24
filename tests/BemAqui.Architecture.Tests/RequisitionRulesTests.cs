using System.Reflection;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Application;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// EST-03 (ADR 0023 §1, revisão pré-Fase 3 §2.2): o pedido de material do operador nunca baixa estoque (nenhum caminho do
/// <c>shopfloor</c> conhece o motor), nenhum DTO do pedido traz custo, valor ou lote, e todo comando da requisição que cria movimento
/// exige <c>Idempotency-Key</c> e <c>perm:</c>.
/// </summary>
public class RequisitionRulesTests
{
    private static readonly string[] Forbidden = ["cost", "price", "amount", "salary", "value", "valor", "custo", "lot"];

    private static string Modules => Path.Combine(Solution.RootDirectory, "src", "Modules");

    private static IEnumerable<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (!seen.Add(type))
            {
                continue;
            }

            yield return type;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var inner = t.IsGenericType ? t.GetGenericArguments() : [t];
                foreach (var candidate in inner.Where(c => c.Assembly == root.Assembly))
                {
                    pending.Push(candidate);
                }
            }
        }
    }

    [Fact]
    public void Nenhum_caminho_do_shopfloor_chama_o_motor_de_estoque()
    {
        var sep = Path.DirectorySeparatorChar;
        var shopfloor = Directory.GetFiles(Path.Combine(Modules, "Production"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && Path.GetFileName(f).Contains("Shopfloor", StringComparison.Ordinal))
            .ToList();
        shopfloor.Should().Contain(f => f.EndsWith("ShopfloorService.Material.cs", StringComparison.Ordinal), "a regra precisa enxergar o pedido do operador");
        shopfloor.Where(f => File.ReadAllText(f).Contains("IStockMovements", StringComparison.Ordinal) || File.ReadAllText(f).Contains("IStockReservations", StringComparison.Ordinal))
            .Should().BeEmpty("o pedido do operador nunca baixa estoque (CA 6)");

        var contract = File.ReadAllText(Path.Combine(Modules, "Inventory", "BemAqui.Inventory.Application", "MaterialRequisitionsContract.cs"));
        contract.Should().NotContain("IStockMovements").And.NotContain("ReservationAllocator").And.NotContain("PostManyAsync");
        typeof(MaterialRequisitionsContract).GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType)
            .Should().NotContain([typeof(IStockMovements), typeof(ReservationAllocator), typeof(MaterialRequisitionService)], "o contrato do pedido só grava a requisição Requested");
    }

    [Fact]
    public void DTOs_do_pedido_de_material_nao_trazem_custo_valor_nem_lote()
    {
        Type[] production = [typeof(ShopfloorMaterialView), typeof(ShopfloorMaterialRequest), typeof(ShopfloorMaterialInput)];
        Type[] contracts = [typeof(OrderMaterialOptions), typeof(MaterialRequestInfo), typeof(MaterialRequestCommand)];
        var violations = production.Concat(contracts).SelectMany(Reachable).Distinct()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (Type: t, Property: p.Name)))
            .Where(x => Forbidden.Any(f => x.Property.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{x.Type.Name}.{x.Property}")
            .ToList();
        violations.Should().BeEmpty("o app do operador nunca mostra custo (EST-03 CA 6, EST-01a CA 29)");
        Reachable(typeof(ShopfloorMaterialView)).Should().Contain(typeof(ShopfloorMaterialRequestLine), "a regra enxerga os tipos aninhados");
    }

    [Fact]
    public void Comandos_da_requisicao_exigem_Idempotency_Key_e_permissao()
    {
        var text = File.ReadAllText(Path.Combine(Modules, "Inventory", "BemAqui.Inventory.Endpoints", "RequisitionEndpoints.cs"));
        var statements = text.Split(';');
        string[] commands = ["CreateMaterialRequisition", "ConfirmMaterialRequest", "ApproveMaterialRequisition", "ReverseMaterialRequisition", "ReturnMaterialToStock", "RecordMaterialLoss"];
        foreach (var name in commands)
        {
            var statement = statements.Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal));
            statement.Should().Contain(".RequireIdempotencyKey()", $"{name} cria movimento ou documento (CA 2)");
            statement.Should().MatchRegex(@"\.Require(Any)?Permission(Of)?\(", $"{name} tem perm:");
        }

        statements.Should().OnlyContain(s => !s.Contains(".MapPost(", StringComparison.Ordinal) || s.Contains(".RequirePermission(", StringComparison.Ordinal) || s.Contains(".RequireAnyPermission(", StringComparison.Ordinal),
            "todo comando da requisição tem perm: (CA 15)");
        statements.Should().NotContain(s => s.Contains(".AllowPinSession()", StringComparison.Ordinal), "o backoffice da requisição exige login completo");
    }

    [Fact]
    public void Pedido_do_operador_fica_na_lista_do_PIN_e_exige_shopfloor_create()
    {
        var text = File.ReadAllText(Path.Combine(Modules, "Production", "BemAqui.Production.Endpoints", "ProductionEndpoints.Shopfloor.cs"));
        var statement = text.Split(';').Single(s => s.Contains(".WithName(\"RequestShopfloorMaterial\")", StringComparison.Ordinal));
        statement.Should().Contain(".RequirePermission(\"shopfloor.create\")").And.Contain(".AllowPinSession()").And.Contain(".RequireIdempotencyKey()");
        PinSessionAllowList.Reviewed.Keys.Should().Contain(["RequestShopfloorMaterial", "GetShopfloorStepMaterial"]);
    }
}
