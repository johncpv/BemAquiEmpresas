using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// PLN-05 sem banco: a montagem do projeto virtual do orçamento (CA 1 e 2): itens que geram OS (D14 da Fase 3), uma etapa por centro com
/// horas × quantidade, ordem pelo roteiro e, sem roteiro, pela ordem das linhas; e os dias corridos do prazo (CA 4 e 6).
/// </summary>
public class QuoteScheduleSimulationTests
{
    private static readonly Guid Cut = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Weld = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid Assembly = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static SimulationItem Item(int sequence, QuoteItemKind kind, decimal quantity, params Guid[] routing) =>
        new(Guid.CreateVersion7(), sequence, kind, quantity, routing);

    [Fact]
    public void Gera_uma_OS_por_item_com_horas_e_uma_etapa_por_centro_com_horas_vezes_quantidade()
    {
        var equipment = Item(1, QuoteItemKind.Equipment, 2m);
        var product = Item(2, QuoteItemKind.Product, 3m);
        var service = Item(3, QuoteItemKind.Service, 1m);
        var productWithoutHours = Item(4, QuoteItemKind.Product, 1m);
        var labor = new[]
        {
            new SimulationLaborLine(equipment.Id, Assembly, 4m),
            new SimulationLaborLine(equipment.Id, Cut, 1.5m),
            new SimulationLaborLine(equipment.Id, Assembly, 2m),
            new SimulationLaborLine(product.Id, Weld, 0.3333m),
            new SimulationLaborLine(service.Id, Assembly, 10m),
        };

        var orders = QuoteScheduleSimulation.Build([service, product, equipment, productWithoutHours], labor);

        orders.Select(o => o.QuoteItemId).Should().Equal([equipment.Id, product.Id], "pela sequência dos itens; Serviço nunca e Produto sem horas não geram OS");
        orders[0].Steps.Should().Equal(new SimulationStep(Assembly, 12m), new SimulationStep(Cut, 3m));
        orders[1].Steps.Should().Equal(new SimulationStep(Weld, 0.9999m));
    }

    [Fact]
    public void Ordem_das_etapas_segue_o_roteiro_e_os_centros_fora_dele_vao_para_o_fim_na_ordem_das_linhas()
    {
        var withRouting = Item(1, QuoteItemKind.Equipment, 1m, Cut, Weld, Assembly);
        var partial = Item(2, QuoteItemKind.Equipment, 1m, Assembly);
        var labor = new[]
        {
            new SimulationLaborLine(withRouting.Id, Assembly, 3m),
            new SimulationLaborLine(withRouting.Id, Cut, 2m),
            new SimulationLaborLine(partial.Id, Weld, 1m),
            new SimulationLaborLine(partial.Id, Cut, 1m),
            new SimulationLaborLine(partial.Id, Assembly, 5m),
        };

        var orders = QuoteScheduleSimulation.Build([withRouting, partial], labor);

        orders[0].Steps.Select(s => s.WorkCenterId).Should().Equal([Cut, Assembly], "o roteiro manda; Solda do roteiro sem horas fica fora");
        orders[1].Steps.Select(s => s.WorkCenterId).Should().Equal([Assembly, Weld, Cut], "Montagem do roteiro primeiro; Solda e Corte na ordem das linhas");
    }

    [Fact]
    public void Sem_roteiro_vale_a_ordem_das_linhas_e_sem_horas_nao_ha_OS()
    {
        var item = Item(1, QuoteItemKind.Product, 1m);
        QuoteScheduleSimulation.Build([item], [new(item.Id, Weld, 1m), new(item.Id, Cut, 2m), new(item.Id, Weld, 1m)])[0].Steps
            .Should().Equal(new SimulationStep(Weld, 2m), new SimulationStep(Cut, 2m));
        QuoteScheduleSimulation.Build([item], [new(item.Id, Cut, 0m)]).Should().BeEmpty("CA 7: hora zero não vira etapa");
        QuoteScheduleSimulation.Build([item], []).Should().BeEmpty();
        QuoteScheduleSimulation.Build([Item(1, QuoteItemKind.Equipment, 0m)], []).Should().BeEmpty();
    }

    [Fact]
    public void Dias_corridos_e_dias_alem_do_prazo()
    {
        var today = new DateOnly(2026, 10, 30);
        QuoteScheduleSimulation.CalendarDays(today, new DateOnly(2026, 11, 12)).Should().Be(13);
        QuoteScheduleSimulation.DaysBeyond(today.AddDays(13), new DateOnly(2026, 11, 12)).Should().Be(0, "cabe no prazo");
        QuoteScheduleSimulation.DaysBeyond(today.AddDays(10), new DateOnly(2026, 11, 12)).Should().Be(3);
        QuoteScheduleSimulation.DaysBeyond(null, new DateOnly(2026, 11, 12)).Should().BeNull();
        QuoteScheduleSimulation.DaysBeyond(today, null).Should().BeNull();
    }
}
