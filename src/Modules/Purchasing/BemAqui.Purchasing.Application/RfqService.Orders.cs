using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

public sealed partial class RfqService
{
    /// <summary>
    /// "Gerar pedidos de compra" (CA 9, 11): um pedido <c>Draft</c> por fornecedor vencedor, pelo serviço de criação da CMP-03a (as mesmas
    /// validações), com as linhas vencedoras (preço na unidade de estoque), o frete e o IPI da resposta, a condição, a previsão hoje + o
    /// maior prazo das linhas vencedoras, o destino pelas SC (R-CMP-6), <c>rfq_id</c> e <c>quotes_count</c>; as linhas de SC passam a
    /// <c>Ordered</c>. Tudo numa transação. Idempotente por cotação e fornecedor: o pedido não cancelado já gerado é devolvido, não duplica.
    /// </summary>
    public async Task<Result<RfqOrdersResult>> GenerateOrdersAsync(Guid id, RfqVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rfq is null)
        {
            return NotFound();
        }

        if (rfq.Status != RfqStatus.Closed)
        {
            return Error.Conflict(NotClosedCode, rfq.Status == RfqStatus.Cancelled ? "A cotação está cancelada." : "Conclua a cotação antes de gerar os pedidos.");
        }

        var lines = await db.RfqLines.AsNoTracking().Where(l => l.RfqId == id && l.SelectedSupplierId != null).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var suppliers = await db.RfqSuppliers.Where(s => s.RfqId == id).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var quotes = await db.RfqQuotes.AsNoTracking().Where(q => q.RfqId == id).ToListAsync(cancellationToken);
        var links = await db.RfqLineRequests.AsNoTracking().Where(r => r.RfqId == id).ToListAsync(cancellationToken);
        var existing = await GeneratedOrdersAsync(id, cancellationToken);
        var winners = suppliers.Where(s => lines.Any(l => l.SelectedSupplierId == s.Id)).ToList();
        var missing = winners.Where(s => !existing.ContainsKey(s.PartyId)).ToList();
        var created = new List<PurchaseOrder>();
        if (missing.Count > 0)
        {
            if (rfq.Version != input.Version)
            {
                return Stale();
            }

            var quotesCount = QuotesCount(suppliers, quotes);
            var rfqRequestLines = links.Select(l => l.RequestLineId).ToHashSet();
            var now = clock.GetUtcNow();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            Touch(rfq, input.Version, now);
            var touched = new HashSet<Guid>();
            foreach (var supplier in missing)
            {
                var won = lines.Where(l => l.SelectedSupplierId == supplier.Id).ToList();
                var wonQuotes = won.Select(l => quotes.Single(q => q.RfqLineId == l.Id && q.RfqSupplierId == supplier.Id)).ToList();
                var orderLines = won.Select(l => new PurchaseOrderLineInput(
                    l.ItemId, l.Quantity, null, quotes.Single(q => q.RfqLineId == l.Id && q.RfqSupplierId == supplier.Id).UnitPrice,
                    links.Where(r => r.RfqLineId == l.Id).Select(r => r.RequestLineId).ToList())).ToList();
                var order = await orders.AddFromRfqAsync(
                    new PurchaseOrderInput(supplier.PartyId, null, supplier.PaymentTermId, null, supplier.Freight, supplier.IpiPct, $"Cotação {rfq.Number}", orderLines, rfq.Id, quotesCount,
                        wonQuotes.Max(q => q.LeadDays)),
                    rfqRequestLines, cancellationToken);
                if (order.IsFailure)
                {
                    return order.Error!;
                }

                supplier.PurchaseOrderId = order.Value.Order.Id;
                supplier.OrderSubmittedAt = null;
                supplier.OrderSubmitError = null;
                created.Add(order.Value.Order);
                touched.UnionWith(order.Value.Touched);
            }

            await PurchaseRequestUpdates.RefreshStatusAsync(db, touched, now, cancellationToken, currentUser.UserId);
            if (await SaveAsync(cancellationToken) is { } conflict)
            {
                return conflict;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var all = await GeneratedOrdersAsync(id, cancellationToken);
        var names = await PartyNamesAsync(all.Keys, cancellationToken);
        var createdIds = created.Select(o => o.Id).ToHashSet();
        return new RfqOrdersResult(
            winners.Where(s => all.ContainsKey(s.PartyId)).Select(s => all[s.PartyId])
                .Select(o => new RfqOrderView(o.Id, o.Number, o.PartyId, names.GetValueOrDefault(o.PartyId) ?? "Fornecedor removido", o.Status, o.Total, createdIds.Contains(o.Id))).ToList(),
            created.Count);
    }

    /// <summary>
    /// "Enviar todos para aprovação" (CA 10): cada pedido em rascunho da cotação passa pelo envio da CMP-03a (alçada resolvida por pedido),
    /// de forma independente; o resultado lista cada um como enviado (com o nível) ou não enviado com o motivo. Um envio recusado não deixa
    /// nada pendente para o próximo. O resultado fica no fornecedor da cotação (histórico, CA 15).
    /// </summary>
    public async Task<Result<RfqSubmitResult>> SubmitOrdersAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Rfqs.AnyAsync(r => r.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var drafts = (await GeneratedOrdersAsync(id, cancellationToken)).Values.Where(o => o.Status == PurchaseOrderStatus.Draft).OrderBy(o => o.Number).ToList();
        if (drafts.Count == 0)
        {
            return Error.Conflict("rfq_no_draft_orders", "Não há pedido em rascunho desta cotação para enviar. Gere os pedidos ou abra cada um.");
        }

        var names = await PartyNamesAsync(drafts.Select(o => o.PartyId).ToList(), cancellationToken);
        var results = new List<RfqSubmitItem>();
        foreach (var order in drafts)
        {
            var submitted = await orders.SubmitAsync(order.Id, new PurchaseOrderVersionInput(order.Version), cancellationToken);
            var name = names.GetValueOrDefault(order.PartyId) ?? "Fornecedor removido";
            if (submitted.IsFailure)
            {
                db.ChangeTracker.Clear();
                results.Add(new RfqSubmitItem(order.Id, order.Number, name, order.Total, false, null, null, submitted.Error!.Code, submitted.Error.Message));
                continue;
            }

            var levels = submitted.Value.Levels;
            results.Add(new RfqSubmitItem(order.Id, order.Number, name, submitted.Value.Total, true, levels.Count, levels.OrderBy(l => l.Level).Select(l => l.ApproverName).FirstOrDefault(), null, null));
        }

        // Histórico da cotação (CA 15): quando cada pedido foi enviado, ou por que não foi.
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();
        var rows = await db.RfqSuppliers.Where(s => s.RfqId == id).ToListAsync(cancellationToken);
        foreach (var result in results)
        {
            var party = drafts.Single(o => o.Id == result.PurchaseOrderId).PartyId;
            foreach (var row in rows.Where(r => r.PartyId == party))
            {
                row.OrderSubmittedAt = result.Sent ? now : row.OrderSubmittedAt;
                row.OrderSubmitError = result.Sent ? null : Cut(result.Message ?? result.Code ?? "Não enviado", RfqRules.MaxSubmitError);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var sent = results.Count(r => r.Sent);
        return new RfqSubmitResult(results, sent, results.Count, $"Pedidos enviados para aprovação ({sent} de {results.Count})");
    }

    private async Task<Dictionary<Guid, string>> PartyNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : (await parties.FindManyAsync(ids.Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
