using System.Runtime.CompilerServices;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.BuildingBlocks.Modules;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// Minha pauta (DSH-04 CA 3 e 15): toda fonte (<see cref="IAgendaSource"/>) mora no Application de um módulo, declara o módulo do
/// catálogo e, por categoria, uma permissão <c>modulo.acao</c> do próprio módulo (camadas 1 a 3); códigos únicos. A declaração é
/// estática: o teste lê sem construir a fonte.
/// </summary>
public class AgendaRulesTests
{
    private static IReadOnlyList<IAgendaSource> Sources() =>
        [.. Solution.Assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IAgendaSource).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .Select(t => (IAgendaSource)RuntimeHelpers.GetUninitializedObject(t))];

    [Fact]
    public void Toda_fonte_declara_modulo_e_permissao_do_catalogo()
    {
        var problems = new List<string>();
        foreach (var source in Sources())
        {
            var type = source.GetType();
            if (ModuleCatalog.Find(source.Module) is null)
            {
                problems.Add($"{type.Name}: módulo '{source.Module}' fora do catálogo");
            }

            if (!(type.Assembly.GetName().Name ?? string.Empty).EndsWith(".Application", StringComparison.Ordinal))
            {
                problems.Add($"{type.Name}: fonte fora do Application do módulo");
            }

            if (source.Categories.Count == 0)
            {
                problems.Add($"{type.Name}: sem categoria");
            }

            foreach (var category in source.Categories)
            {
                if (category.Module != source.Module || !category.Code.StartsWith(source.Module + ".", StringComparison.Ordinal))
                {
                    problems.Add($"{type.Name}: categoria {category.Code} não é do módulo {source.Module}");
                }

                if (!ModuleCatalog.IsKnownPermission(category.Permission) || !category.Permission.StartsWith(source.Module + ".", StringComparison.Ordinal))
                {
                    problems.Add($"{type.Name}: {category.Code} com permissão '{category.Permission}' fora do catálogo ou de outro módulo");
                }

                if (string.IsNullOrWhiteSpace(category.Label) || !Enum.IsDefined(category.Tier))
                {
                    problems.Add($"{type.Name}: {category.Code} sem rótulo ou nível");
                }
            }
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void Codigos_de_categoria_sao_unicos() =>
        Sources().SelectMany(s => s.Categories).GroupBy(c => c.Code).Where(g => g.Count() > 1).Select(g => g.Key).Should().BeEmpty();

    /// <summary>CA 3: as categorias das fontes já entregues. Story nova acrescenta a sua (EST-01b, FIN-07, Fases 5 e 6).</summary>
    [Fact]
    public void Fontes_registradas_e_categorias() =>
        Sources().SelectMany(s => s.Categories).Select(c => c.Code).Should().Contain(
        [
            "finance.payment_orders_awaiting", "finance.payment_orders_to_pay", "finance.receivables_overdue", "finance.payables_due_soon",
            "projects.amendments_pending", "quotes.expiring",
            "production.quality_pending", "production.orders_late", "production.non_conformities_mine",
            "purchasing.orders_awaiting", "purchasing.shortages_without_request",
            "hr.positions_without_cost", "hr.time_adjustments_pending",
            "fiscal.certificate_expiring", "fiscal.invoices_to_issue", "fiscal.cancellation_requests", "fiscal.numbers_to_invalidate",
            "assets.maintenance_overdue", "assets.machines_stopped",
        ]);

    /// <summary>CA 15 (ADR 0026): a sessão de PIN do /fab não chama a pauta.</summary>
    [Fact]
    public void Pauta_fora_da_sessao_de_pin() => PinSessionAllowList.Reviewed.Should().NotContainKey("GetMyAgenda");
}
