using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class ProjectService
{
    /// <summary>
    /// Linha do tempo (CA 12): aprovação, assinatura, fabricação, entrega, cancelamento e conclusão; parcelas e títulos só com
    /// <c>finance.view</c>. Montada dos dados do Commercial e dos contratos do Finance, em ordem cronológica (até 200).
    /// </summary>
    public async Task<Result<IReadOnlyList<ProjectTimelineEntry>>> TimelineAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var contract = await db.Contracts.AsNoTracking().FirstAsync(c => c.Id == project.ContractId, cancellationToken);
        var quote = await db.Quotes.IgnoreQueryFilters().AsNoTracking().Where(q => q.Id == contract.QuoteId).Select(q => new { q.Id, q.Number, q.Revision, q.ApprovedAt, q.ApprovedBy }).FirstAsync(cancellationToken);
        var entries = new List<ProjectTimelineEntry>();
        if (quote.ApprovedAt is { } approvedAt)
        {
            entries.Add(new(approvedAt, "quote_approved", $"Orçamento {quote.Number}{(quote.Revision > 0 ? $" rev. {quote.Revision}" : string.Empty)} aprovado", await WhoAsync(quote.ApprovedBy, cancellationToken), "/app/orcamentos/" + quote.Id));
        }

        entries.Add(new(At(contract.SignedAt), "contract_signed", $"Contrato {contract.Number} assinado", await WhoAsync(contract.CreatedBy, cancellationToken), "/app/contratos/" + contract.Id));
        foreach (var a in await db.ContractAmendments.AsNoTracking().Where(a => a.ContractId == contract.Id && a.Status == AmendmentStatus.Approved).ToListAsync(cancellationToken))
        {
            entries.Add(new(a.ApprovedAt ?? a.CreatedAt, "amendment_approved", $"Aditivo {a.Number} aprovado: {a.Description}", await WhoAsync(a.ApprovedBy, cancellationToken), $"/app/contratos/{contract.Id}/aditivos/{a.Id}"));
        }

        if (project.FabricatedOn is { } fabricated)
        {
            // PRD-06a1 CA 12: o projeto fabricado pelas OS mostra a origem; o registro manual continua como na COM-04b.
            entries.Add(project.FabricatedByOrders
                ? new(At(fabricated), "fabricated", "Fabricação concluída pelas OS", "Sistema", "/app/producao/os?projeto=" + id)
                : new(At(fabricated), "fabricated", "Fabricação concluída registrada", null, null));
        }

        // PRD-06a1 CA 15: conclusão e último estorno da conclusão de cada OS de fabricação, com quem e o motivo (o histórico da OS tem todos).
        foreach (var order in await productionOrders.ByProjectAsync(id, cancellationToken))
        {
            if (order.CompletedAt is { } completed)
            {
                entries.Add(new(completed, "production_order_completed", $"{order.Number} concluída", await WhoAsync(order.CompletedBy, cancellationToken), "/app/producao/os/" + order.Id));
            }

            if (order.CompletionReversedAt is { } reversed)
            {
                entries.Add(new(reversed, "production_order_completion_reversed", $"Conclusão da {order.Number} estornada: {order.CompletionReversalReason}",
                    await WhoAsync(order.CompletionReversedBy, cancellationToken), "/app/producao/os/" + order.Id + "?aba=historico"));
            }
        }

        // EST-03 CA 17: material baixado e devolvido por OS e dia, pela razão do próprio Commercial, só com projects.view_cost.
        if (await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken))
        {
            var material = await db.ProjectCosts.AsNoTracking()
                .Where(c => c.ProjectId == id && (c.Source == ProjectCostSource.StockIssue || c.Source == ProjectCostSource.StockReturn))
                .GroupBy(c => new { c.ProductionOrderId, c.Source, c.OccurredOn, c.IsBackfill })
                .Select(g => new { g.Key.ProductionOrderId, g.Key.Source, g.Key.OccurredOn, g.Key.IsBackfill, Amount = g.Sum(c => c.Amount), At = g.Min(c => c.RecordedAt) })
                .ToListAsync(cancellationToken);
            var numbers = (await productionOrders.ByProjectAsync(id, cancellationToken)).ToDictionary(o => o.Id, o => o.Number);
            foreach (var m in material.Where(m => m.Amount != 0))
            {
                entries.Add(MaterialEntry(m.ProductionOrderId, m.ProductionOrderId is { } o ? numbers.GetValueOrDefault(o) : null, m.Source, m.Amount, m.IsBackfill ? At(m.OccurredOn) : m.At, m.IsBackfill));
            }
        }

        if (project.DeliveredOn is { } delivered)
        {
            entries.Add(new(At(delivered), "delivered", project.DeliveredManually ? "Entrega registrada (sem instalação)" : "Entrega registrada", null, null));
        }

        if (contract.CancelledAt is { } cancelledAt)
        {
            entries.Add(new(cancelledAt, "contract_cancelled", $"Contrato cancelado{(contract.CancelReason is null ? string.Empty : ": " + contract.CancelReason)}", await WhoAsync(contract.CancelledBy, cancellationToken), "/app/contratos/" + contract.Id));
        }

        if (contract.CompletedAt is { } completedAt)
        {
            entries.Add(new(completedAt, "contract_completed", "Contrato concluído: projeto entregue e parcelas quitadas", "Sistema", "/app/contratos/" + contract.Id));
        }

        if (await permissions.HasPermissionAsync("finance", "view", cancellationToken))
        {
            foreach (var r in await receivables.ByProjectAsync(id, cancellationToken))
            {
                entries.Add(new(r.CreatedAt ?? At(contract.SignedAt), "receivable_created", $"Parcela {r.Number} gerada" + (r.DueDate is { } due ? $", vence {due:dd/MM/yyyy}" : ", aguarda o marco"), "Sistema", "/app/financeiro/receber/" + r.Id));
            }

            foreach (var p in await payables.ByProjectAsync(id, cancellationToken))
            {
                entries.Add(new(p.CreatedAt, "payable_created", $"Título a pagar {p.Number} lançado no projeto", null, "/app/financeiro/pagar/" + p.Id));
            }
        }

        return entries.OrderBy(e => e.At).Take(200).ToList();
    }

    /// <summary>
    /// "Material baixado para a OS-2026-00012: R$ 1.200,00" e "Material devolvido da OS-2026-00012: R$ 200,00" (EST-03 CA 17); o
    /// recomposto leva "(calculado na implantação)".
    /// </summary>
    public static ProjectTimelineEntry MaterialEntry(Guid? orderId, string? orderNumber, ProjectCostSource source, decimal amount, DateTimeOffset at, bool backfill)
    {
        var order = orderNumber ?? "OS";
        var money = "R$ " + Math.Abs(amount).ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        var title = source == ProjectCostSource.StockReturn || amount < 0 ? $"Material devolvido da {order}: {money}" : $"Material baixado para a {order}: {money}";
        return new ProjectTimelineEntry(at, "material_cost", backfill ? title + " (calculado na implantação)" : title, null,
            orderId is { } o ? "/app/producao/os/" + o + "?aba=material" : null);
    }

    /// <summary>Data de negócio às 00h de São Paulo, para ordenar junto com os instantes.</summary>
    private static DateTimeOffset At(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, BusinessTime.Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private async Task<string?> WhoAsync(Guid? userId, CancellationToken ct) => userId is { } id ? await users.DisplayNameAsync(id, ct) : null;
}
