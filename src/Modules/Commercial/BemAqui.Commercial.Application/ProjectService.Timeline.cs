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
            entries.Add(new(At(fabricated), "fabricated", "Fabricação concluída registrada", null, null));
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

    /// <summary>Data de negócio às 00h de São Paulo, para ordenar junto com os instantes.</summary>
    private static DateTimeOffset At(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, BusinessTime.Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private async Task<string?> WhoAsync(Guid? userId, CancellationToken ct) => userId is { } id ? await users.DisplayNameAsync(id, ct) : null;
}
