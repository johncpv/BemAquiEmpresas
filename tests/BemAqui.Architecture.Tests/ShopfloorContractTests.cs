using System.Reflection;
using BemAqui.Production.Application;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PRD-03a CA 25 (D7, revisão pré-Fase 3 §2.2): nenhum DTO de <c>/api/v1/shopfloor/*</c> traz custo (custo-hora, custo do
/// apontamento, custo médio, valor do item ou da OS), inclusive nos tipos aninhados.
/// </summary>
public class ShopfloorContractTests
{
    private static readonly string[] Forbidden = ["cost", "price", "amount", "salary", "value", "valor", "custo"];

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
    public void DTOs_do_app_do_operador_nao_trazem_custo()
    {
        Type[] roots = [typeof(ShopfloorQueueView), typeof(ShopfloorStepDetail), typeof(ShopfloorActionResult), typeof(ShopfloorQueueStep), typeof(ShopfloorAttachment)];
        var violations = roots.SelectMany(Reachable).Distinct()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (Type: t, Property: p.Name)))
            .Where(x => Forbidden.Any(f => x.Property.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{x.Type.Name}.{x.Property}")
            .ToList();
        violations.Should().BeEmpty("o app do operador nunca mostra custo (CA 25)");
        Reachable(typeof(ShopfloorStepDetail)).Should().Contain(typeof(ShopfloorQueueStep), "a regra enxerga os tipos aninhados");
    }
}