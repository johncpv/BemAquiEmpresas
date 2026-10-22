using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PRD-03a, regras sem banco: minutos líquidos com pausas, situação da etapa e da OS com pausa, saída, vários operadores,
/// aprovação e qualidade (CA 7 a 17), ordem da fila e filtro por centro (CA 1, 2), sobreposição e retroativo (CA 19).
/// </summary>
public class LaborRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Minutos_liquidos_descontam_as_pausas_e_o_aberto_conta_ate_agora()
    {
        var pauses = new[] { new PauseSpan(T0.AddMinutes(30), T0.AddMinutes(45)), new PauseSpan(T0.AddMinutes(60), T0.AddMinutes(70)) };
        LaborRules.NetMinutes(T0, T0.AddHours(2), pauses, T0.AddHours(5)).Should().Be(95, "120 − 15 − 10");
        LaborRules.PausedMinutes(pauses, T0.AddHours(5)).Should().Be(25);
        LaborRules.NetMinutes(T0, null, [new PauseSpan(T0.AddMinutes(10), null)], T0.AddMinutes(40)).Should().Be(10, "a pausa aberta para o cronômetro");
        LaborRules.NetMinutes(T0, T0.AddMinutes(5), [new PauseSpan(T0, T0.AddMinutes(9))], T0).Should().Be(0, "nunca negativo");
        LaborRules.Hours(95).Should().Be(1.5833m);
        LaborRules.Hours(0).Should().Be(0m);
    }

    [Theory]
    [InlineData(true, false, ProductionStepStatus.InProgress)]
    [InlineData(false, false, ProductionStepStatus.Paused)]
    [InlineData(false, true, ProductionStepStatus.InProgress)]
    public void Etapa_iniciada_fica_pausada_quando_ninguem_trabalha_nela(bool working, bool awaiting, ProductionStepStatus expected) =>
        LaborRules.StartedStepStatus(working, awaiting).Should().Be(expected);

    [Fact]
    public void Situacao_da_OS_pelas_etapas()
    {
        static StepState S(ProductionStepStatus status, bool quality = false) => new(status, quality);
        var W = ProductionStepStatus.Waiting;
        var R = ProductionStepStatus.Ready;
        var D = ProductionStepStatus.Done;
        LaborRules.OrderStatus(ProductionOrderStatus.Open, [S(R), S(W)]).Should().Be(ProductionOrderStatus.Open, "nada começou");
        LaborRules.OrderStatus(ProductionOrderStatus.Open, [S(ProductionStepStatus.InProgress), S(W)]).Should().Be(ProductionOrderStatus.InProgress, "CA 7");
        LaborRules.OrderStatus(ProductionOrderStatus.InProgress, [S(ProductionStepStatus.Paused), S(W)]).Should().Be(ProductionOrderStatus.Paused, "CA 11");
        LaborRules.OrderStatus(ProductionOrderStatus.Paused, [S(ProductionStepStatus.InProgress), S(W)]).Should().Be(ProductionOrderStatus.InProgress, "CA 12");
        LaborRules.OrderStatus(ProductionOrderStatus.InProgress, [S(D), S(R), S(W, true)]).Should().Be(ProductionOrderStatus.InProgress, "a próxima pronta");
        LaborRules.OrderStatus(ProductionOrderStatus.InProgress, [S(D), S(R, true)]).Should().Be(ProductionOrderStatus.Quality, "CA 16: Aguardando qualidade");
        LaborRules.OrderStatus(ProductionOrderStatus.InProgress, [S(D), S(D)]).Should().Be(ProductionOrderStatus.InProgress, "CA 17: Pronta para concluir");
        LaborRules.OrderStatus(ProductionOrderStatus.Cancelled, [S(ProductionStepStatus.InProgress)]).Should().Be(ProductionOrderStatus.Cancelled);
        LaborRules.OrderStatus(ProductionOrderStatus.Completed, [S(D)]).Should().Be(ProductionOrderStatus.Completed);
        LaborRules.OrderStatus(ProductionOrderStatus.Open, []).Should().Be(ProductionOrderStatus.Open, "sem roteiro");
    }

    [Fact]
    public void Fila_com_o_centro_do_operador_primeiro_depois_prioridade_e_prometido()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var d = Guid.CreateVersion7();
        var e = Guid.CreateVersion7();
        var order = LaborRules.QueueOrder([
            new QueueKey(a, false, 0, new DateOnly(2026, 11, 1), "OS-2026-00001", 1),
            new QueueKey(b, true, 5, new DateOnly(2026, 12, 1), "OS-2026-00002", 1),
            new QueueKey(c, true, 0, null, "OS-2026-00003", 1),
            new QueueKey(d, true, 0, new DateOnly(2026, 12, 1), "OS-2026-00004", 2),
            new QueueKey(e, false, 0, null, "OS-2026-00005", 1),
        ]);
        order.Should().Equal(d, c, b, a, e);
    }

    [Fact]
    public void Sobreposicao_e_retroativo_do_supervisor()
    {
        var x = Guid.CreateVersion7();
        var spans = new[] { new EntrySpan(x, T0, T0.AddHours(2)), new EntrySpan(Guid.CreateVersion7(), T0.AddHours(5), null) };
        LaborRules.Overlapping(T0.AddHours(1), T0.AddHours(3), spans).Should().NotBeNull();
        LaborRules.Overlapping(T0.AddHours(2), T0.AddHours(3), spans).Should().BeNull("encostar no fim não cruza");
        LaborRules.Overlapping(T0.AddHours(1), T0.AddHours(3), spans, except: x).Should().BeNull("a correção ignora o próprio apontamento");
        LaborRules.Overlapping(T0.AddHours(6), T0.AddHours(7), spans).Should().NotBeNull("o aberto vale até agora");

        var now = T0.AddDays(1);
        LaborRules.ValidateInterval(now.AddMinutes(10), now.AddHours(1), [], now)!.Fields!.Should().ContainKey("startedAt");
        LaborRules.ValidateInterval(T0.AddHours(2), T0.AddHours(1), [], now)!.Fields!.Should().ContainKey("endedAt");
        LaborRules.ValidateInterval(T0, now.AddHours(1), [], now)!.Fields!.Should().ContainKey("endedAt", "fim no futuro");
        var old = LaborRules.ValidateInterval(now.AddDays(-8), now.AddDays(-8).AddHours(1), [], now)!;
        old.Code.Should().Be("labor_backdate_limit");
        old.Message.Should().Be("Apontamento retroativo só até 7 dias.");
        LaborRules.ValidateInterval(T0, T0.AddHours(2), [(PauseReason.Break, null, T0.AddMinutes(10), T0.AddMinutes(20))], now).Should().BeNull();
        LaborRules.ValidateInterval(T0, T0.AddHours(2), [(PauseReason.Break, null, T0.AddMinutes(-10), T0.AddMinutes(20))], now)!.Fields!.Should().ContainKey("pauses");
        LaborRules.ValidateInterval(T0, T0.AddHours(2), [(PauseReason.Break, null, T0.AddMinutes(10), T0.AddMinutes(30)), (PauseReason.Other, null, T0.AddMinutes(20), T0.AddMinutes(40))], now)
            !.Fields!.Should().ContainKey("pauses", "pausas que se cruzam");
        LaborRules.ValidateInterval(T0, T0.AddHours(2), [(PauseReason.Other, new string('x', 121), T0.AddMinutes(10), T0.AddMinutes(20))], now)!.Fields!.Should().ContainKey("pauses");
    }

    [Fact]
    public void Mensagens_do_checklist_e_regra_da_BOM()
    {
        LaborRules.MissingChecklist(1).Should().Be("Marque o item que falta.");
        LaborRules.MissingChecklist(3).Should().Be("Marque os 3 itens que faltam.");
        LaborRules.NeedsBom(0).Should().BeFalse("D12: a primeira etapa inicia sem BOM");
        LaborRules.NeedsBom(1).Should().BeTrue();
        ShopfloorService.PauseLabel(PauseReason.EndOfShift).Should().Be("Fim do turno");
        ProductionBoardService.Initials("Diego Santos Lima").Should().Be("DL");
        ProductionBoardService.Initials("Rafael").Should().Be("R");
    }
}