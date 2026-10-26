using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

/// <summary>
/// Atraso e risco da OS de fabricação pela função única <see cref="DeliveryRiskRules"/> (PRD-02 CA 4; PLN-01b CA 9): o Kanban, a lista
/// e o detalhe da OS e o job "OS atrasada" passam a data prometida do projeto e a data prevista de entrega do projeto (o plano, PLN-01b);
/// a OS concluída ou cancelada não tem prazo. A regra provisória da Fase 3 ("prometido em até 7 dias e fora da última etapa") saiu.
/// </summary>
public static class ProductionOrderRisk
{
    public static DeliveryAssessment Evaluate(DateOnly? promised, DateOnly? forecast, DateOnly today, ProductionOrderStatus status) =>
        DeliveryRiskRules.Evaluate(promised, forecast, today, status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled);
}
