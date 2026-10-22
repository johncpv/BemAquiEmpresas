namespace BemAqui.Production.Infrastructure;

/// <summary>Ajustes de dado das migrations da Produção, em SQL nomeado para o teste de integração rodar o mesmo texto.</summary>
public static class ProductionDataFixes
{
    /// <summary>
    /// PRD-06a1 CA 1 (D12): as OS de fabricação que já estão com todas as etapas <c>Done</c> (e ao menos uma etapa) passam a
    /// <c>ReadyToComplete</c> ("Pronta para concluir"). Concluídas e canceladas não mudam. Idempotente.
    /// </summary>
    public const string ReadyToComplete = """
        UPDATE production.production_orders o
           SET status = 'ReadyToComplete'
         WHERE o.type = 'Fabrication'
           AND o.status IN ('Open', 'InProgress', 'Paused', 'Quality')
           AND EXISTS (SELECT 1 FROM production.production_order_steps s WHERE s.order_id = o.id)
           AND NOT EXISTS (SELECT 1 FROM production.production_order_steps s WHERE s.order_id = o.id AND (s.status IS NULL OR s.status <> 'Done'));
        """;
}