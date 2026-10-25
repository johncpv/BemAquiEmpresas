using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

public sealed partial class RfqService
{
    /// <summary>
    /// "Concluir cotação" (CA 6, 8): o vencedor de cada linha cotada (um fornecedor que cotou a linha); quando não é o de menor custo total
    /// da linha, o motivo é obrigatório (Outro com o texto). O servidor confere de novo o anexo de cada vencedor acima do limite. A cotação
    /// fica <c>Closed</c>; linha sem nenhuma resposta fica sem vencedor e as linhas de SC dela voltam a <c>Open</c>. Uma resposta basta.
    /// </summary>
    public async Task<Result<RfqDetail>> CloseAsync(Guid id, RfqCloseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (OpenError(rfq, input.Version) is { } refused)
        {
            return refused;
        }

        var lines = await db.RfqLines.Where(l => l.RfqId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var suppliers = await db.RfqSuppliers.Where(s => s.RfqId == id).ToListAsync(cancellationToken);
        var quotes = await db.RfqQuotes.AsNoTracking().Where(q => q.RfqId == id).ToListAsync(cancellationToken);
        if (quotes.Count == 0)
        {
            return Invalid("selections", "Registre a resposta de ao menos um fornecedor.");
        }

        var comparison = Compare(lines, suppliers, quotes);
        var selections = input.Selections ?? [];
        if (selections.Select(s => s.LineId).Distinct().Count() != selections.Count)
        {
            return Invalid("selections", "Escolha um vencedor só por item.");
        }

        if (selections.Any(s => lines.All(l => l.Id != s.LineId)))
        {
            return LineNotFound();
        }

        if (selections.Any(s => suppliers.All(x => x.Id != s.SupplierId)))
        {
            return SupplierNotFound();
        }

        var found = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        string ItemOf(RfqLine line) => found.GetValueOrDefault(line.ItemId)?.Code ?? "o item";
        var chosen = new Dictionary<Guid, (RfqSelectionInput Selection, Guid? Cheapest)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var selection = selections.FirstOrDefault(s => s.LineId == line.Id);
            var quoted = quotes.Any(q => q.RfqLineId == line.Id);
            if (selection is null)
            {
                if (quoted)
                {
                    return Invalid($"selections[{i}]", $"Escolha o vencedor de {ItemOf(line)}.");
                }

                continue;
            }

            if (!quotes.Any(q => q.RfqLineId == line.Id && q.RfqSupplierId == selection.SupplierId))
            {
                return Invalid($"selections[{i}].supplierId", $"O fornecedor escolhido não cotou {ItemOf(line)}.");
            }

            var cheapest = comparison.CheapestByLine.TryGetValue(line.Id, out var c) ? c : (Guid?)null;
            var note = Clean(selection.Note);
            if (RfqRules.NeedsReason(selection.SupplierId, cheapest))
            {
                if (selection.Reason is not { } reason || !Enum.IsDefined(reason))
                {
                    return Invalid($"selections[{i}].reason", $"Informe o motivo da escolha de {ItemOf(line)}: o vencedor não é o de menor custo total.");
                }

                if (reason == RfqSelectionReason.Other && note is null)
                {
                    return Invalid($"selections[{i}].note", "Escreva o motivo da escolha.");
                }
            }

            if (note is { Length: > RfqRules.MaxSelectionNote })
            {
                return Invalid($"selections[{i}].note", $"Escreva o motivo com até {RfqRules.MaxSelectionNote} caracteres.");
            }

            chosen[line.Id] = (selection with { Note = note }, cheapest);
        }

        // CA 6: o anexo pode ter sido recusado pelo antivírus (ou retirado) depois da resposta; confere de novo cada vencedor acima do limite.
        var limit = await parameters.QuoteAttachmentAmountAsync(cancellationToken);
        foreach (var winner in chosen.Values.Select(v => v.Selection.SupplierId).Distinct())
        {
            var totals = comparison.Suppliers.FirstOrDefault(s => s.SupplierId == winner);
            if (totals is not null && RfqRules.NeedsAttachment(totals.Total, limit) && await AttachmentsCountAsync(winner, cancellationToken) == 0)
            {
                return AttachmentRequired(limit);
            }
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Touch(rfq!, input.Version, now);
        foreach (var line in lines)
        {
            if (chosen.TryGetValue(line.Id, out var pick))
            {
                var needsReason = RfqRules.NeedsReason(pick.Selection.SupplierId, pick.Cheapest);
                line.SelectedSupplierId = pick.Selection.SupplierId;
                line.SuggestedSupplierId = pick.Cheapest;
                line.SelectionReason = needsReason ? pick.Selection.Reason : null;
                line.SelectionNote = needsReason ? pick.Selection.Note : null;
            }
        }

        rfq!.Status = RfqStatus.Closed;
        rfq.ClosedAt = now;
        rfq.ClosedBy = currentUser.UserId;
        var touched = await ReleaseRequestsAsync(lines.Where(l => !chosen.ContainsKey(l.Id)).Select(l => l.Id).ToList(), now, cancellationToken);
        await PurchaseRequestUpdates.RefreshStatusAsync(db, touched, now, cancellationToken, currentUser.UserId);
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Comparativo das respostas gravadas (CA 7): só os fornecedores com preço.</summary>
    private static RfqComparison Compare(IReadOnlyCollection<RfqLine> lines, IReadOnlyCollection<RfqSupplier> suppliers, IReadOnlyCollection<RfqQuote> quotes) =>
        RfqRules.Compare(
            lines.Select(l => new RfqComparisonLine(l.Id, l.Quantity)).ToList(),
            suppliers.Where(s => s.Responded).Select(s => new RfqComparisonSupplier(s.Id, s.Sequence, s.Freight, s.IpiPct)).ToList(),
            quotes.Select(q => new RfqComparisonQuote(q.RfqLineId, q.RfqSupplierId, q.UnitPrice, q.LeadDays)).ToList());

    /// <summary>Respostas com preço (o "1 cotação" e o <c>quotes_count</c> do pedido, CA 11).</summary>
    private static int QuotesCount(IEnumerable<RfqSupplier> suppliers, IEnumerable<RfqQuote> quotes)
    {
        var withPrice = quotes.Select(q => q.RfqSupplierId).ToHashSet();
        return suppliers.Count(s => s.Responded && withPrice.Contains(s.Id));
    }

    /// <summary>Pedidos não cancelados da cotação, por fornecedor (terceiro): a fonte da verdade do "já gerado" (CA 9).</summary>
    private async Task<Dictionary<Guid, PurchaseOrder>> GeneratedOrdersAsync(Guid rfqId, CancellationToken ct) =>
        (await db.PurchaseOrders.AsNoTracking().Where(o => o.RfqId == rfqId && o.Status != PurchaseOrderStatus.Cancelled).OrderByDescending(o => o.CreatedAt).ToListAsync(ct))
        .GroupBy(o => o.PartyId).ToDictionary(g => g.Key, g => g.First());
}
