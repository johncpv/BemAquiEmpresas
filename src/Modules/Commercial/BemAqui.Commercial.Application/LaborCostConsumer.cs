using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>Linha já gravada na razão para uma fonte: versão e valor (diferença) gravados.</summary>
public sealed record AppliedCost(int Seq, decimal Amount);

/// <summary>
/// Regras da razão de diferenças (ADR 0025 §2, PRD-04 CA 8), sem banco: a linha nova leva <c>newAmount</c> − Σ das linhas já gravadas
/// para a fonte; evento com <c>seq</c> menor ou igual ao maior já aplicado é ignorado (a linha mais nova já levou a fonte ao valor
/// certo); <c>seq</c> 0 (mensagem de antes da PRD-04) também.
/// </summary>
public static class ProjectCostLedger
{
    /// <summary>Diferença a gravar; null quando o evento deve ser ignorado (repetido, fora de ordem ou antigo).</summary>
    public static decimal? Difference(IReadOnlyCollection<AppliedCost> applied, int seq, decimal newAmount)
    {
        ArgumentNullException.ThrowIfNull(applied);
        if (seq <= 0 || (applied.Count > 0 && applied.Max(a => a.Seq) >= seq))
        {
            return null;
        }

        return newAmount - applied.Sum(a => a.Amount);
    }

    /// <summary>
    /// Fonte da linha de horas pela versão: <c>Labor</c> na primeira (fechamento ou implantação), <c>LaborCorrection</c> nas seguintes
    /// (correção e recálculo). Assim o índice único <c>(source, source_id, seq)</c> também barra duas linhas da mesma versão.
    /// </summary>
    public static ProjectCostSource LaborSource(int seq) => seq <= 1 ? ProjectCostSource.Labor : ProjectCostSource.LaborCorrection;
}

/// <summary>
/// Custo de mão de obra no projeto (PRD-04 CA 8 e 12): <see cref="LaborEntryClosed"/> e <see cref="LaborEntryCorrected"/> viram uma
/// linha <c>Labor</c> na razão <c>commercial.project_costs</c> com a diferença para o valor novo do apontamento. Idempotente pela inbox,
/// pela regra do <c>seq</c> e pelo índice único (corrida entre a recomposição e o consumidor ao vivo: a segunda gravação falha, a
/// mensagem volta e é ignorada). Lê só ids, valores e data do evento (CA 15).
/// </summary>
public sealed class LaborCostConsumer(ICommercialDb db, TimeProvider clock) : IIntegrationEventConsumer<LaborEntryClosed>, IIntegrationEventConsumer<LaborEntryCorrected>
{
    public Task HandleAsync(LaborEntryClosed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RecordAsync(
            integrationEvent.LaborEntryId, integrationEvent.ProductionOrderId, integrationEvent.ProjectId, integrationEvent.Version, integrationEvent.NewAmount, integrationEvent.OccurredOn,
            integrationEvent.Origin == LaborCostOrigin.Backfill, cancellationToken);
    }

    public Task HandleAsync(LaborEntryCorrected integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return RecordAsync(
            integrationEvent.LaborEntryId, integrationEvent.ProductionOrderId, integrationEvent.ProjectId, integrationEvent.Version, integrationEvent.NewAmount, integrationEvent.OccurredOn,
            integrationEvent.Origin == LaborCostOrigin.Backfill, cancellationToken);
    }

    private async Task RecordAsync(Guid entryId, Guid orderId, Guid projectId, int seq, decimal newAmount, DateOnly occurredOn, bool backfill, CancellationToken ct)
    {
        if (seq <= 0 || projectId == Guid.Empty || !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            return;
        }

        var applied = await db.ProjectCosts.AsNoTracking()
            .Where(c => c.SourceId == entryId && (c.Source == ProjectCostSource.Labor || c.Source == ProjectCostSource.LaborCorrection))
            .Select(c => new AppliedCost(c.Seq, c.Amount)).ToListAsync(ct);
        if (ProjectCostLedger.Difference(applied, seq, newAmount) is not { } difference)
        {
            return;
        }

        var now = clock.GetUtcNow();
        db.ProjectCosts.Add(new ProjectCost
        {
            ProjectId = projectId, ProductionOrderId = orderId == Guid.Empty ? null : orderId, CostType = CostType.Labor, Source = ProjectCostLedger.LaborSource(seq), SourceId = entryId,
            Seq = seq, Amount = difference, OccurredOn = occurredOn == default ? BusinessTime.Today(clock) : occurredOn, RecordedAt = now, IsBackfill = backfill,
        });
        await db.SaveChangesAsync(ct);
    }
}