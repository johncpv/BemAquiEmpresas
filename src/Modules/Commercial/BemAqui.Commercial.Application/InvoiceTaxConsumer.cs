using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Commercial.Domain;
using BemAqui.Fiscal.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Imposto realizado do projeto pela nota (FIS-03a CA 11, ADR 0025 adendo D8): o <see cref="InvoiceAuthorized"/> vira uma linha
/// <c>InvoiceTax</c> na razão <c>commercial.project_costs</c> (<c>CostType.Tax</c>, <c>source_id</c> = nota, <c>seq</c> 1, valor = tributos
/// marcados como custo na regra, <c>invoice_amount</c> = valor da nota, <c>occurred_on</c> = data da nota). Só a nota que conta para o custo
/// (<see cref="InvoiceAuthorized.ProjectTaxApplies"/>: produção, a partir da data de corte); homologação nunca entra. Idempotente pela
/// inbox, pela conferência da linha e pelo índice único <c>(source, source_id, seq)</c>. Lê só ids, valores e data.
/// </summary>
public sealed class InvoiceTaxConsumer(ICommercialDb db, TimeProvider clock) : IIntegrationEventConsumer<InvoiceAuthorized>
{
    public const int Seq = 1;

    public async Task HandleAsync(InvoiceAuthorized integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (!integrationEvent.ProjectTaxApplies || integrationEvent.ProjectId is not { } projectId || !await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken))
        {
            return;
        }

        if (await db.ProjectCosts.AnyAsync(c => c.Source == ProjectCostSource.InvoiceTax && c.SourceId == integrationEvent.InvoiceId && c.Seq == Seq, cancellationToken))
        {
            return;
        }

        db.ProjectCosts.Add(new ProjectCost
        {
            ProjectId = projectId, CostType = CostType.Tax, Source = ProjectCostSource.InvoiceTax, SourceId = integrationEvent.InvoiceId, Seq = Seq,
            Amount = Math.Round(integrationEvent.CostTaxAmount, 2, MidpointRounding.AwayFromZero), OccurredOn = integrationEvent.IssuedOn, RecordedAt = clock.GetUtcNow(),
            InvoiceAmount = Math.Round(integrationEvent.TotalAmount, 2, MidpointRounding.AwayFromZero),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
