using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Assets;

/// <summary>PAT-03a: regras puras do plano (próxima data e próximo horímetro a partir da conclusão) e transições da OS e do bem.</summary>
public class MaintenanceRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    [Fact]
    public void Plano_por_dias_vence_N_dias_depois_da_ultima_execucao_e_recalcula_da_conclusao()
    {
        var plan = new MaintenancePlan { FrequencyType = MaintenanceFrequency.Days, FrequencyValue = 30, LastDoneAt = new DateOnly(2026, 10, 1) };
        plan.Recalculate([]);
        plan.NextDueAt.Should().Be(new DateOnly(2026, 10, 31));
        plan.NextDueMeter.Should().BeNull();
        MaintenanceRules.IsDue(plan.FrequencyType, plan.NextDueAt, null, null, Today).Should().BeTrue();
        MaintenanceRules.IsOverdue(plan.FrequencyType, plan.NextDueAt, null, null, Today).Should().BeTrue("passou do dia 31");
        MaintenanceRules.IsOverdue(plan.FrequencyType, Today, null, null, Today).Should().BeFalse("vence hoje: ainda não está vencida");

        // A conclusão em 01/11 grava a última execução; o próximo conta dela, não do vencimento antigo.
        plan.LastDoneAt = Today;
        plan.Recalculate([]);
        plan.NextDueAt.Should().Be(new DateOnly(2026, 12, 1));
        MaintenanceRules.InLead(plan.NextDueAt, 7, Today).Should().BeFalse();
        MaintenanceRules.InLead(plan.NextDueAt, 30, Today).Should().BeTrue();
        MaintenanceRules.DueKey(plan.FrequencyType, plan.NextDueAt, null).Should().Be("d:2026-12-01");
    }

    [Fact]
    public void Plano_por_horas_vence_pelo_horimetro_e_estima_a_data_pela_media_das_ultimas_leituras()
    {
        var plan = new MaintenancePlan { FrequencyType = MaintenanceFrequency.Hours, FrequencyValue = 250, LastDoneMeter = 1000m };
        plan.Recalculate([]);
        plan.NextDueMeter.Should().Be(1250m);
        plan.NextDueAt.Should().BeNull("sem leituras, sem estimativa");

        MeterPoint[] readings = [new(new DateOnly(2026, 10, 1), 1000m), new(new DateOnly(2026, 10, 11), 1100m), new(new DateOnly(2026, 10, 21), 1200m)];
        plan.Recalculate(readings);
        plan.NextDueAt.Should().Be(new DateOnly(2026, 10, 26), "10 h por dia: faltam 50 h depois de 21/10");
        MaintenanceRules.IsDue(plan.FrequencyType, plan.NextDueAt, plan.NextDueMeter, 1200m, Today).Should().BeFalse("a data estimada não vence o plano; só a leitura");
        MaintenanceRules.IsDue(plan.FrequencyType, plan.NextDueAt, plan.NextDueMeter, 1250m, Today).Should().BeTrue();
        MaintenanceRules.DueKey(plan.FrequencyType, null, plan.NextDueMeter).Should().Be("h:1250");

        MaintenanceRules.EstimateDate([new(Today, 1000m)], 1250m).Should().BeNull("uma leitura só não dá média");
        MaintenanceRules.EstimateDate([new(Today, 1300m)], 1250m).Should().Be(Today, "já alcançado");
        MaintenanceRules.EstimateDate([new(Today.AddDays(-5), 1000m), new(Today, 1000m)], 1250m).Should().BeNull("sem uso");

        // Só as últimas 5 leituras entram na média.
        var old = new MeterPoint(new DateOnly(2025, 1, 1), 0m);
        MaintenanceRules.EstimateDate([old, .. readings, new(new DateOnly(2026, 10, 31), 1300m), new(Today, 1310m)], 1500m)
            .Should().Be(Today.AddDays(19), "(1310 − 1000) / 31 dias = 10 h por dia; faltam 190 h");
    }

    [Fact]
    public void A_OS_segura_o_bem_em_andamento_ou_parada_e_as_transicoes_sao_as_da_maquina_de_estados()
    {
        MaintenanceRules.HoldsAsset(MaintenanceOrderStatus.Open, machineStopped: true).Should().BeTrue();
        MaintenanceRules.HoldsAsset(MaintenanceOrderStatus.Open, machineStopped: false).Should().BeFalse();
        MaintenanceRules.HoldsAsset(MaintenanceOrderStatus.InProgress, machineStopped: false).Should().BeTrue();
        MaintenanceRules.HoldsAsset(MaintenanceOrderStatus.Done, machineStopped: true).Should().BeFalse();
        MaintenanceRules.AssetStatusAfter(AssetStatus.Maintenance, anyHolding: false).Should().Be(AssetStatus.Active);
        MaintenanceRules.AssetStatusAfter(AssetStatus.Active, anyHolding: true).Should().Be(AssetStatus.Maintenance);
        MaintenanceRules.AssetStatusAfter(AssetStatus.Disposed, anyHolding: true).Should().Be(AssetStatus.Disposed);

        var order = new MaintenanceOrder { Problem = "Ruído" };
        order.Invoking(o => o.Complete("Feito", null, DateTimeOffset.UtcNow)).Should().Throw<InvalidOperationException>("Open não vai direto a Done");
        order.Start(null, DateTimeOffset.UtcNow);
        order.Status.Should().Be(MaintenanceOrderStatus.InProgress);
        order.Invoking(o => o.Start(null, DateTimeOffset.UtcNow)).Should().Throw<InvalidOperationException>();
        order.Complete("Troquei o rolamento", null, DateTimeOffset.UtcNow);
        order.Status.Should().Be(MaintenanceOrderStatus.Done);
        order.Invoking(o => o.Cancel("Tarde demais", null, DateTimeOffset.UtcNow)).Should().Throw<InvalidOperationException>("Done não é cancelada");

        MaintenanceRules.Missing([MaintenanceChecklistResult.Done, null, null]).Should().Be(2);
        MaintenanceRules.MissingMessage(2).Should().Be("Marque os 2 itens que faltam");
        MaintenanceRules.MeterText(1100m).Should().Be("1.100");
        MaintenanceRules.MeterText(1234.5m).Should().Be("1.234,5");
    }
}
