using BemAqui.Assets.Application;
using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Assets;

/// <summary>
/// PAT-03b: regras puras do título do prestador (vencimento estimado, descrição), da parada (horas sugeridas, período em dias úteis, início,
/// cancelamento, estado da fonte), do desconto na capacidade e do custo da mão de obra.
/// </summary>
public class MaintenanceCostRulesTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);

    [Fact]
    public void Vencimento_estimado_e_a_data_mais_a_primeira_parcela_e_a_descricao_leva_numero_e_bem()
    {
        MaintenanceRules.EstimatedDue(Monday, 28).Should().Be(new DateOnly(2026, 11, 30), "CA 1: data agendada + a primeira parcela");
        MaintenanceRules.EstimatedDue(Monday, null).Should().Be(Monday, "sem condição padrão, a própria data");
        MaintenanceRules.PayableDescription("MAN-2026-0007", "PAT-00012", "Torno CNC").Should().Be("Manutenção MAN-2026-0007 — PAT-00012 Torno CNC");
        MaintenanceRules.PayableDescription("MAN-2026-0007", "PAT-00012", new string('x', 300)).Should().HaveLength(200);
        MaintenanceRules.IsValidMoney(0m).Should().BeFalse();
        MaintenanceRules.IsValidMoney(10.005m).Should().BeFalse("em centavos");
        MaintenanceRules.IsValidMoney(1500.5m).Should().BeTrue();
    }

    [Fact]
    public void Horas_paradas_sugeridas_dividem_o_centro_pelas_maquinas_e_o_periodo_conta_dias_uteis()
    {
        MaintenanceRules.SuggestedHoursPerDay(9m, 2).Should().Be(4.5m, "CA 8: horas do centro ÷ máquinas ativas");
        MaintenanceRules.SuggestedHoursPerDay(8m, 3).Should().Be(2.66m, "para baixo, em centésimos");
        MaintenanceRules.SuggestedHoursPerDay(8m, 0).Should().Be(8m, "sem máquina cadastrada, o centro inteiro");
        MaintenanceRules.IsValidHoursPerDay(9m, 9m).Should().BeTrue();
        MaintenanceRules.IsValidHoursPerDay(9.5m, 9m).Should().BeFalse("até as horas do centro");
        MaintenanceRules.IsValidHoursPerDay(0m, 9m).Should().BeFalse();

        MaintenanceRules.DowntimeDays(8m, 4m).Should().Be(2);
        MaintenanceRules.DowntimeDays(9m, 4m).Should().Be(3, "para cima");
        MaintenanceRules.DowntimeDays(1m, 4m).Should().Be(1);

        // Sexta, 06/11: três dias úteis vão até a terça seguinte (o fim de semana não conta).
        var friday = new DateOnly(2026, 11, 6);
        DateOnly[] working = [friday, new(2026, 11, 9), new(2026, 11, 10), new(2026, 11, 11)];
        MaintenanceRules.DowntimeEnd(friday, 3, working).Should().Be(new DateOnly(2026, 11, 10));
        MaintenanceRules.DowntimeEnd(friday, 10, working).Should().Be(friday.AddDays(9), "sem dias úteis suficientes, dias corridos");
    }

    [Fact]
    public void Parada_comeca_na_abertura_com_maquina_parada_e_cancelada_antes_de_comecar_sai()
    {
        MaintenanceRules.DowntimeStart(true, Monday, Monday.AddDays(5), null).Should().Be(Monday, "CA 9: máquina parada começa na abertura");
        MaintenanceRules.DowntimeStart(false, Monday, Monday.AddDays(5), null).Should().Be(Monday.AddDays(5), "CA 8: a data agendada");
        MaintenanceRules.DowntimeStart(false, Monday, Monday.AddDays(5), Monday.AddDays(2)).Should().Be(Monday.AddDays(2), "iniciada antes do agendado");
        MaintenanceRules.CancelledDowntimeEnd(Monday.AddDays(3), Monday).Should().BeNull("não começou: a parada sai");
        MaintenanceRules.CancelledDowntimeEnd(Monday, Monday.AddDays(1)).Should().Be(Monday.AddDays(1), "começou: termina no cancelamento");
    }

    [Fact]
    public void Estado_da_fonte_segue_a_OS_e_a_capacidade_desconta_as_horas_da_parada()
    {
        var order = new MaintenanceOrder { Number = "MAN-2026-0001", ScheduledOn = Monday, HoursPerDay = 4m, ExpectedHours = 8m, DowntimeFrom = Monday, DowntimeTo = Monday.AddDays(1), DowntimeRevision = 1 };
        var center = Guid.NewGuid();
        var open = MaintenanceDowntimesContract.State(order, center);
        open.Removed.Should().BeFalse();
        open.OpenEnded.Should().BeFalse("aberta sem máquina parada segue o período previsto");
        open.EndedOn.Should().BeNull();
        MaintenanceDowntimesContract.State(order, null).Removed.Should().BeTrue("bem sem centro: sem parada");

        order.Start(null, new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
        MaintenanceDowntimesContract.State(order, center).OpenEnded.Should().BeTrue("em andamento, até a conclusão");
        order.Complete("Feito", null, new DateTimeOffset(2026, 11, 5, 15, 0, 0, TimeSpan.Zero));
        var done = MaintenanceDowntimesContract.State(order, center);
        done.EndedOn.Should().Be(new DateOnly(2026, 11, 5), "CA 8: encerrada na conclusão");
        done.OpenEnded.Should().BeFalse();

        // Fim efetivo e desconto (CA 8 e 9): a máquina parada sem fim real segue até hoje; o desconto nunca deixa o dia negativo.
        DowntimeRules.EffectiveEnd(Monday, null, true, Monday.AddDays(4)).Should().Be(Monday.AddDays(4));
        DowntimeRules.EffectiveEnd(Monday, null, false, Monday.AddDays(4)).Should().Be(Monday);
        DowntimeRules.EffectiveEnd(Monday.AddDays(5), Monday.AddDays(2), true, Monday.AddDays(4)).Should().Be(Monday.AddDays(2), "o fim real vale");
        DowntimeRules.Covers(Monday, Monday.AddDays(1), null, false, Monday.AddDays(2), Monday).Should().BeFalse();
        DowntimeRules.Discount(9m, 4m).Should().Be(5m);
        DowntimeRules.Discount(9m, 12m).Should().Be(0m);
        DowntimeRules.Discount(0m, 4m).Should().Be(0m, "dia sem capacidade fica zero");
    }

    [Fact]
    public void Mao_de_obra_interna_e_horas_vezes_o_custo_hora_em_centavos_e_o_dialogo_mostra_o_impacto()
    {
        MaintenanceRules.LaborAmount(2.5m, 48.333333m).Should().Be(120.83m);
        MaintenanceRules.LaborAmount(3m, 50m).Should().Be(150m);
        var text = MaintenanceService.ConfirmationText(1800m, "Manutenção Industrial", new DateOnly(2026, 11, 30));
        text.Should().StartWith("Confirma o título a pagar de R$").And.Contain("1.800,00 ao prestador Manutenção Industrial, com vencimento em 30/11/2026, que segue para ordem de pagamento.");
    }
}
