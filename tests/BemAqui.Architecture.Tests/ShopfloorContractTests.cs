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

    /// <summary>PRD-05: a fila e a decisão de qualidade também rodam no tablet (e por PIN, PRD-03b): nenhum DTO traz custo.</summary>
    [Fact]
    public void DTOs_da_qualidade_nao_trazem_custo()
    {
        Type[] roots = [typeof(QualityQueueView), typeof(QualityStepDetail), typeof(QualityDecisionResult)];
        roots.SelectMany(Reachable).Distinct()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => (Type: t, Property: p.Name)))
            .Where(x => Forbidden.Any(f => x.Property.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{x.Type.Name}.{x.Property}")
            .Should().BeEmpty("a qualidade no tablet não mostra custo");
    }

    /// <summary>
    /// PRD-05 CA 13 (D6, ADR 0026 §4): a <c>production.approve_quality</c> só abre os endpoints da qualidade, que ficam num arquivo
    /// próprio (separados da supervisão) para a PRD-03b pôr só eles na lista permitida da sessão por PIN.
    /// </summary>
    [Fact]
    public void A_acao_da_qualidade_so_abre_os_endpoints_da_qualidade()
    {
        var uses = PermissionRules.SolutionSources().Where(s => s.Content.Contains("RequirePermission(\"production.approve_quality\")", StringComparison.Ordinal)).Select(s => Path.GetFileName(s.File)).ToList();
        uses.Should().Equal(["ProductionEndpoints.Quality.cs"]);
    }
}