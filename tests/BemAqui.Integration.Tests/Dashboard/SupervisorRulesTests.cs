using BemAqui.Dashboard.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.Purchasing.Contracts;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-03, regras sem banco: corte de 5 dias úteis e ordem das faltas (CA 5), situação da compra (CA 5, ponto 53), horas paradas e taxa de NC
/// (CA 6, ponto 51).
/// </summary>
public class SupervisorRulesTests
{
    // Sexta-feira, 23/10/2026; feriado na segunda 02/11 (Finados).
    private static readonly DateOnly Friday = new(2026, 10, 23);

    private static IReadOnlyList<DateOnly> WorkingDates()
    {
        var holidays = new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 11, 2)] = 0m };
        return BusinessDays.Dates([Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday], holidays, Friday.AddDays(1), Friday.AddDays(31));
    }

    [Theory]
    [InlineData(2026, 10, 23, 0)] // hoje
    [InlineData(2026, 10, 20, 0)] // já passou: bloqueia hoje
    [InlineData(2026, 10, 26, 1)] // segunda: o fim de semana não conta
    [InlineData(2026, 10, 30, 5)] // 5 dias úteis: ainda bloqueia
    [InlineData(2026, 11, 3, 6)] // feriado 02/11 não conta: 6 dias úteis
    public void Dias_uteis_ate_o_inicio_pelo_calendario(int year, int month, int day, int expected)
    {
        SupervisorBlocks.WorkingDaysUntil(Friday, new DateOnly(year, month, day), WorkingDates()).Should().Be(expected);
    }

    [Fact]
    public void Corte_de_5_dias_uteis_sem_previsao_e_alem_da_janela_nao_bloqueiam()
    {
        var dates = WorkingDates();
        SupervisorBlocks.Blocks(SupervisorBlocks.WorkingDaysUntil(Friday, new DateOnly(2026, 10, 30), dates)).Should().BeTrue();
        SupervisorBlocks.Blocks(SupervisorBlocks.WorkingDaysUntil(Friday, new DateOnly(2026, 11, 2), dates)).Should().BeTrue("o feriado de 02/11 é o mesmo 5º dia útil da sexta 30/10");
        SupervisorBlocks.Blocks(SupervisorBlocks.WorkingDaysUntil(Friday, new DateOnly(2026, 11, 3), dates)).Should().BeFalse();
        SupervisorBlocks.WorkingDaysUntil(Friday, null, dates).Should().BeNull();
        SupervisorBlocks.Blocks(null).Should().BeFalse();
        SupervisorBlocks.WorkingDaysUntil(Friday, Friday.AddDays(SupervisorBlocks.CalendarWindowDays + 1), dates).Should().BeNull("além da janela lida do calendário");
    }

    [Fact]
    public void Faltas_que_bloqueiam_vem_primeiro_a_mais_proxima_antes_depois_pela_data()
    {
        var rows = new (string Number, int? BlocksIn, DateOnly? Needed)[]
        {
            ("OS-5", null, null),
            ("OS-4", 12, new DateOnly(2026, 11, 12)),
            ("OS-3", 5, new DateOnly(2026, 10, 30)),
            ("OS-2", 0, new DateOnly(2026, 10, 23)),
            ("OS-6", 7, new DateOnly(2026, 11, 4)),
            ("OS-1", 0, new DateOnly(2026, 10, 23)),
        };
        SupervisorBlocks.Order(rows, r => r.BlocksIn, r => r.Needed, r => r.Number).Select(r => r.Number).Should().Equal("OS-1", "OS-2", "OS-3", "OS-6", "OS-4", "OS-5");
    }

    private static PurchaseRequestLineInfo Line(string number, PurchaseRequestStatus status, Guid? lineId = null) =>
        new(Guid.CreateVersion7(), number, lineId ?? Guid.CreateVersion7(), Guid.CreateVersion7(), 2m, Friday, status, PurchaseRequestOrigin.BomShortage, Guid.CreateVersion7(), null);

    [Fact]
    public void Situacao_da_compra_pela_SC_e_pelo_pedido()
    {
        SupervisorBlocks.Purchase([], []).Should().Be((PurchaseSituation.None, "Sem solicitação", (Guid?)null, (Guid?)null));

        var open = Line("SC-2026-0007", PurchaseRequestStatus.Open);
        var requested = SupervisorBlocks.Purchase([open], []);
        requested.Situation.Should().Be(PurchaseSituation.Requested);
        requested.Label.Should().Be("Solicitada SC-2026-0007");
        requested.RequestId.Should().Be(open.RequestId);

        SupervisorBlocks.Purchase([open, Line("SC-2026-0008", PurchaseRequestStatus.Quoting)], []).Label.Should().Be("Em cotação");

        var ordered = Line("SC-2026-0009", PurchaseRequestStatus.Ordered);
        var po = new PurchaseOrderForRequestLine(ordered.LineId, Guid.CreateVersion7(), "PC-2026-0003", PurchaseOrderStatus.Sent, new DateOnly(2026, 11, 12));
        var later = po with { PurchaseOrderId = Guid.CreateVersion7(), Number = "PC-2026-0004", ExpectedDate = new DateOnly(2026, 12, 1) };
        var withOrder = SupervisorBlocks.Purchase([open, ordered], [later, po]);
        withOrder.Situation.Should().Be(PurchaseSituation.Ordered);
        withOrder.Label.Should().Be("Pedido PC-2026-0003 previsto para 12/11", "o pedido previsto mais cedo");
        withOrder.OrderId.Should().Be(po.PurchaseOrderId);

        SupervisorBlocks.Purchase([Line("SC-2026-0010", PurchaseRequestStatus.Cancelled)], []).Situation.Should().Be(PurchaseSituation.None, "linha cancelada não conta");
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(3, 1, 25.0)]
    [InlineData(2, 1, 33.3)]
    [InlineData(0, 4, 100.0)]
    [InlineData(1, 2, 66.7)]
    public void Taxa_de_NC_e_reprovacoes_sobre_decisoes_com_uma_casa(int approvals, int rejections, double? expected)
    {
        ProductionIndicatorRules.NcRate(approvals, rejections).Should().Be(expected is null ? null : (decimal)expected.Value);
    }

    [Fact]
    public void Horas_paradas_contam_so_a_parte_da_pausa_dentro_do_periodo()
    {
        var from = new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);
        var now = to;
        ProductionIndicatorRules.MinutesWithin(from.AddHours(1), from.AddHours(2.5), now, from, to).Should().Be(90m);
        ProductionIndicatorRules.MinutesWithin(from.AddHours(-1), from.AddMinutes(30), now, from, to).Should().Be(30m, "começou antes do período");
        ProductionIndicatorRules.MinutesWithin(to.AddMinutes(-45), null, now, from, to).Should().Be(45m, "a pausa aberta conta até agora");
        ProductionIndicatorRules.MinutesWithin(from.AddHours(-3), from.AddHours(-2), now, from, to).Should().Be(0m);
        ProductionIndicatorRules.Hours(90m + 30m + 45m).Should().Be(2.75m);
        ProductionIndicatorRules.Hours(20m).Should().Be(0.33m);
    }

    [Theory]
    [InlineData(PauseReason.Material, StoppageReason.Material)]
    [InlineData(PauseReason.Machine, StoppageReason.Machine)]
    [InlineData(PauseReason.Other, StoppageReason.Other)]
    [InlineData(PauseReason.Break, null)]
    [InlineData(PauseReason.EndOfShift, null)]
    public void Intervalo_e_fim_de_turno_nao_sao_parada(PauseReason reason, StoppageReason? expected)
    {
        ProductionIndicatorRules.StoppageOf(reason).Should().Be(expected);
        if (expected is { } r)
        {
            SupervisorBlocks.StoppageLabel(r).Should().NotBeNullOrWhiteSpace();
        }
    }
}
