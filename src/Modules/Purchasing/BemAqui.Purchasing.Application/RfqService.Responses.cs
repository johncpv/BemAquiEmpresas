using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

public sealed partial class RfqService
{
    /// <summary>
    /// "Salvar resposta" (CA 3, 5, 6): por linha, o preço (na unidade de estoque ou na do fornecedor, convertido pelo fator), a quantidade
    /// disponível e o prazo; por fornecedor, frete, IPI %, condição e validade. Grava quem lançou e quando (o histórico guarda cada
    /// alteração). Custo total acima do limite sem o anexo da proposta deste fornecedor: 422. Linha de outra cotação: 404 (IDOR).
    /// </summary>
    public async Task<Result<RfqDetail>> RespondAsync(Guid id, Guid supplierId, RfqResponseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var supplier = rfq is null ? null : await db.RfqSuppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.RfqId == id, cancellationToken);
        if (rfq is null || supplier is null)
        {
            return SupplierNotFound();
        }

        if (OpenError(rfq, input.Version) is { } refused)
        {
            return refused;
        }

        if (input.Freight < 0m || input.Freight > PurchaseOrderRules.MaxMoney || decimal.Round(input.Freight, 2) != input.Freight)
        {
            return Invalid("freight", "Informe o frete em reais, de zero para cima.");
        }

        if (input.IpiPct < 0m || input.IpiPct > PurchaseOrderRules.MaxIpiPct || decimal.Round(input.IpiPct, 4) != input.IpiPct)
        {
            return Invalid("ipiPct", "Informe o IPI de 0% a 100%, com até 4 casas.");
        }

        if (input.ValidUntil is { } valid && valid < Today && valid != supplier.ValidUntil)
        {
            return Invalid("validUntil", "A validade da proposta não pode ser antes de hoje.");
        }

        var notes = Clean(input.Notes);
        if (notes is { Length: > RfqRules.MaxNotes })
        {
            return Invalid("notes", $"Escreva a observação com até {RfqRules.MaxNotes} caracteres.");
        }

        var party = await parties.FindAsync(supplier.PartyId, cancellationToken);
        var termId = input.PaymentTermId ?? supplier.PaymentTermId ?? party?.DefaultPaymentTermId;
        var term = termId is { } t ? await terms.FindAsync(t, cancellationToken) : null;
        if (term is null || (!term.IsActive && term.Id != supplier.PaymentTermId))
        {
            return Invalid("paymentTermId", "Escolha uma condição de pagamento ativa.");
        }

        var quotes = input.Quotes ?? [];
        if (quotes.Count == 0)
        {
            return Invalid("quotes", "Informe o preço de pelo menos um item. Se o fornecedor não cotou, use Não cotou.");
        }

        if (quotes.Select(q => q.LineId).Distinct().Count() != quotes.Count)
        {
            return Invalid("quotes", "Cada item entra uma vez na resposta.");
        }

        var lines = await db.RfqLines.Where(l => l.RfqId == id).ToDictionaryAsync(l => l.Id, cancellationToken);
        if (quotes.Any(q => !lines.ContainsKey(q.LineId)))
        {
            return LineNotFound();
        }

        var codes = await supplierCodes.ForItemsAsync(supplier.PartyId, lines.Values.Select(l => l.ItemId).Distinct().ToList(), cancellationToken);
        var prepared = new List<(RfqQuoteInput Input, decimal UnitPrice, string? Unit)>();
        for (var i = 0; i < quotes.Count; i++)
        {
            var quote = quotes[i];
            var prefix = $"quotes[{i}].";
            var line = lines[quote.LineId];
            if (quote.UnitPrice is <= 0m or > PurchaseOrderRules.MaxUnitPrice || decimal.Round(quote.UnitPrice, 6) != quote.UnitPrice)
            {
                return Invalid(prefix + "unitPrice", "Informe o preço unitário, maior que zero, com até 6 casas.");
            }

            if (quote.AvailableQuantity is <= 0m or > PurchaseOrderRules.MaxQuantity)
            {
                return Invalid(prefix + "availableQuantity", "Informe a quantidade disponível, maior que zero.");
            }

            if (quote.LeadDays is < PurchaseRequestRules.MinLeadDays or > PurchaseRequestRules.MaxLeadDays)
            {
                return Invalid(prefix + "leadDays", "O prazo vai de 0 a 365 dias.");
            }

            var price = quote.UnitPrice;
            string? unit = null;
            if (quote.InSupplierUnit)
            {
                if (!codes.TryGetValue(line.ItemId, out var code) || code.ConversionFactor <= 0m || string.IsNullOrWhiteSpace(code.SupplierUnit))
                {
                    return Invalid(prefix + "inSupplierUnit", "Este item não tem a unidade do fornecedor cadastrada. Informe o preço na unidade de estoque.");
                }

                price = RfqRules.ToStockUnitPrice(quote.UnitPrice, code.ConversionFactor);
                unit = code.SupplierUnit;
                if (price <= 0m)
                {
                    return Invalid(prefix + "unitPrice", "O preço convertido para a unidade de estoque ficou zero. Confira o preço e a unidade.");
                }
            }

            prepared.Add((quote, price, unit));
        }

        var total = RfqRules.SupplierTotal(prepared.Select(p => (lines[p.Input.LineId].Quantity, p.UnitPrice)), input.Freight, input.IpiPct).Total;
        var limit = await parameters.QuoteAttachmentAmountAsync(cancellationToken);
        if (RfqRules.NeedsAttachment(total, limit) && await AttachmentsCountAsync(supplier.Id, cancellationToken) == 0)
        {
            return AttachmentRequired(limit);
        }

        var now = clock.GetUtcNow();
        Touch(rfq, input.Version, now);
        var current = await db.RfqQuotes.Where(q => q.RfqSupplierId == supplier.Id).ToListAsync(cancellationToken);
        foreach (var (quote, price, unit) in prepared)
        {
            var available = decimal.Round(quote.AvailableQuantity ?? lines[quote.LineId].Quantity, 4, MidpointRounding.AwayFromZero);
            if (current.FirstOrDefault(q => q.RfqLineId == quote.LineId) is { } same)
            {
                same.UnitPrice = price;
                same.SupplierUnitPrice = unit is null ? null : quote.UnitPrice;
                same.SupplierUnit = unit;
                same.AvailableQuantity = available;
                same.LeadDays = quote.LeadDays;
            }
            else
            {
                db.RfqQuotes.Add(new RfqQuote
                {
                    RfqId = id, RfqLineId = quote.LineId, RfqSupplierId = supplier.Id, UnitPrice = price, SupplierUnitPrice = unit is null ? null : quote.UnitPrice, SupplierUnit = unit,
                    AvailableQuantity = available, LeadDays = quote.LeadDays,
                });
            }
        }

        foreach (var gone in current.Where(q => prepared.All(p => p.Input.LineId != q.RfqLineId)))
        {
            db.RfqQuotes.Remove(gone);
        }

        supplier.Freight = input.Freight;
        supplier.IpiPct = input.IpiPct;
        supplier.PaymentTermId = term.Id;
        supplier.ValidUntil = input.ValidUntil;
        supplier.Notes = notes;
        supplier.Responded = true;
        supplier.Declined = false;
        supplier.RespondedBy = currentUser.UserId;
        supplier.RespondedAt = now;
        return await SavedAsync(id, cancellationToken);
    }

    /// <summary>"Não cotou" (CA 3): o fornecedor fica sem resposta (os preços lançados antes saem, e ficam no histórico), com quem marcou e quando.</summary>
    public async Task<Result<RfqDetail>> DeclineAsync(Guid id, Guid supplierId, RfqVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rfq = await db.Rfqs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        var supplier = rfq is null ? null : await db.RfqSuppliers.FirstOrDefaultAsync(s => s.Id == supplierId && s.RfqId == id, cancellationToken);
        if (rfq is null || supplier is null)
        {
            return SupplierNotFound();
        }

        if (OpenError(rfq, input.Version) is { } refused)
        {
            return refused;
        }

        var now = clock.GetUtcNow();
        Touch(rfq, input.Version, now);
        db.RfqQuotes.RemoveRange(await db.RfqQuotes.Where(q => q.RfqSupplierId == supplier.Id).ToListAsync(cancellationToken));
        supplier.Responded = false;
        supplier.Declined = true;
        supplier.RespondedBy = currentUser.UserId;
        supplier.RespondedAt = now;
        return await SavedAsync(id, cancellationToken);
    }

    /// <summary>Anexos da proposta do fornecedor (dono <c>Rfq</c>), fora os recusados pelo antivírus (CA 4, 6).</summary>
    private async Task<int> AttachmentsCountAsync(Guid rfqSupplierId, CancellationToken ct) => (await attachments.ListCheckedAsync(AttachmentOwner, rfqSupplierId, ct)).Count;

    private static Error AttachmentRequired(decimal limit)
    {
        var message = RfqRules.AttachmentRequiredMessage(limit);
        return Error.Validation(AttachmentRequiredCode, message, new Dictionary<string, string[]> { ["attachment"] = [message] });
    }
}
