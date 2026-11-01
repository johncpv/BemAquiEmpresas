using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Commercial.Domain;
using BemAqui.Fiscal.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Reverso do imposto da nota cancelada (FIS-03b CA 5, ADR 0025 adendo): o <see cref="InvoiceCancelled"/> vira uma linha
/// <c>InvoiceTaxReversal</c> na razão <c>commercial.project_costs</c> (<c>source_id</c> = nota, <c>seq</c> 1, valor negativo igual ao da
/// <c>InvoiceTax</c> já lançada, <c>invoice_amount</c> negativo, <c>occurred_on</c> = data do cancelamento). A linha original nunca é apagada.
/// Sem a <c>InvoiceTax</c> (nota de homologação ou antes da data de corte), nada; se o cancelamento chegar antes da autorização, o reverso
/// usa os valores do evento e a soma das duas fecha em zero. Idempotente pela inbox, pela conferência e pelo índice único
/// <c>(source, source_id, seq)</c> (a segunda gravação concorrente para nele). Nenhum movimento de estoque (D12).
/// </summary>
public sealed class InvoiceTaxReversalConsumer(ICommercialDb db, TimeProvider clock) : IIntegrationEventConsumer<InvoiceCancelled>
{
    public const int Seq = 1;

    public async Task HandleAsync(InvoiceCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.ProjectId is not { } projectId || !await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return;
        }

        if (await db.ProjectCosts.AnyAsync(c => c.Source == ProjectCostSource.InvoiceTaxReversal && c.SourceId == integrationEvent.InvoiceId && c.Seq == Seq, cancellationToken))
        {
            return;
        }

        var original = await db.ProjectCosts.AsNoTracking().Where(c => c.Source == ProjectCostSource.InvoiceTax && c.SourceId == integrationEvent.InvoiceId)
            .GroupBy(c => c.SourceId).Select(g => new { Amount = g.Sum(c => c.Amount), Invoice = g.Sum(c => c.InvoiceAmount ?? 0m) }).FirstOrDefaultAsync(cancellationToken);
        if (original is null && !integrationEvent.ProjectTaxApplies)
        {
            return;
        }

        db.ProjectCosts.Add(new ProjectCost
        {
            ProjectId = projectId, CostType = CostType.Tax, Source = ProjectCostSource.InvoiceTaxReversal, SourceId = integrationEvent.InvoiceId, Seq = Seq,
            Amount = -(original?.Amount ?? Math.Round(integrationEvent.CostTaxAmount, 2, MidpointRounding.AwayFromZero)),
            InvoiceAmount = -(original?.Invoice ?? Math.Round(integrationEvent.TotalAmount, 2, MidpointRounding.AwayFromZero)),
            OccurredOn = integrationEvent.CancelledOn, RecordedAt = clock.GetUtcNow(),
        });
        // Duas entregas ao mesmo tempo: o índice único deixa uma; a outra falha, volta pela inbox e para na conferência acima.
        await db.SaveChangesAsync(cancellationToken);
    }
}
