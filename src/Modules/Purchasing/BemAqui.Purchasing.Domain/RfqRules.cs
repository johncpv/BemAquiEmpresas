using System.Globalization;

namespace BemAqui.Purchasing.Domain;

/// <summary>Linha da cotação para o comparativo: o id e a quantidade pedida (unidade de estoque).</summary>
public sealed record RfqComparisonLine(Guid LineId, decimal Quantity);

/// <summary>Fornecedor para o comparativo: a ordem na cotação (desempate), o frete e o IPI % da resposta.</summary>
public sealed record RfqComparisonSupplier(Guid SupplierId, int Sequence, decimal Freight, decimal IpiPct);

/// <summary>Preço de um fornecedor numa linha (unidade de estoque) e o prazo.</summary>
public sealed record RfqComparisonQuote(Guid LineId, Guid SupplierId, decimal UnitPrice, int LeadDays);

/// <summary>
/// Custo de uma linha num fornecedor (CA 7): total da linha (qtd × preço, 2 casas) e o custo total da linha com o IPI e o frete rateado
/// pelo valor (P-16), só para comparar.
/// </summary>
public sealed record RfqLineCost(Guid LineId, Guid SupplierId, decimal LineTotal, decimal LandedCost, int LeadDays);

/// <summary>Totais do fornecedor (CA 7): subtotal, frete, IPI e custo total, o maior prazo e se cotou todas as linhas.</summary>
public sealed record RfqSupplierTotals(Guid SupplierId, decimal Subtotal, decimal Freight, decimal IpiAmount, decimal Total, int MaxLeadDays, int QuotedLines, bool CoversAll);

/// <summary>Resultado do comparativo: custos por linha e fornecedor, totais, o menor custo de cada linha e o menor total geral.</summary>
public sealed record RfqComparison(
    IReadOnlyList<RfqLineCost> Costs, IReadOnlyList<RfqSupplierTotals> Suppliers, IReadOnlyDictionary<Guid, Guid> CheapestByLine, Guid? CheapestOverall);

/// <summary>Regras puras da cotação (CMP-02), testadas sem banco.</summary>
public static class RfqRules
{
    public const string DocType = "COT";
    public const int MaxLines = 50;
    public const int MaxSuppliers = 20;
    public const int MaxSuggestedSuppliers = 3;
    public const int MaxNotes = 500;
    public const int MaxReason = 500;
    public const int MaxSelectionNote = 200;
    public const int MaxSubmitError = 200;

    /// <summary>Limite padrão do anexo obrigatório da proposta (D8, ADR 0024 §5).</summary>
    public const decimal DefaultAttachmentAmount = 5_000m;

    public const decimal MaxAttachmentAmount = 1_000_000m;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Preço na unidade de estoque a partir do preço na unidade do fornecedor (CA 3): 1 unidade do fornecedor = <paramref name="factor"/>
    /// na de estoque, então o preço é dividido pelo fator, 6 casas <c>AwayFromZero</c>.
    /// </summary>
    public static decimal ToStockUnitPrice(decimal supplierUnitPrice, decimal factor)
    {
        if (factor <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), "O fator precisa ser maior que zero.");
        }

        return Math.Round(supplierUnitPrice / factor, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>Custo total do fornecedor: Σ (qtd × preço) + frete + IPI sobre o subtotal, com a mesma conta do pedido (CA 7, P-16).</summary>
    public static PurchaseOrderTotals SupplierTotal(IEnumerable<(decimal Quantity, decimal UnitPrice)> lines, decimal freight, decimal ipiPct) =>
        PurchaseOrderRules.Totals(lines, freight, ipiPct);

    /// <summary>Acima do limite (estritamente), a resposta exige o anexo da proposta do fornecedor (CA 6).</summary>
    public static bool NeedsAttachment(decimal supplierTotal, decimal limit) => supplierTotal > limit;

    public static string AttachmentRequiredMessage(decimal limit) =>
        $"Respostas acima de {Money(limit)} exigem o anexo da proposta do fornecedor.";

    /// <summary>
    /// Comparativo (CA 7 e 8): por linha e fornecedor, o total da linha e o custo total da linha (com o IPI e o frete do fornecedor
    /// rateado pelo valor dos itens dele); o menor custo de cada linha (desempate: menor prazo, depois a ordem na cotação) e o menor total
    /// geral entre os que cotaram todas as linhas cotadas por alguém.
    /// </summary>
    public static RfqComparison Compare(
        IReadOnlyCollection<RfqComparisonLine> lines, IReadOnlyCollection<RfqComparisonSupplier> suppliers, IReadOnlyCollection<RfqComparisonQuote> quotes)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(suppliers);
        ArgumentNullException.ThrowIfNull(quotes);
        var quantity = lines.ToDictionary(l => l.LineId, l => l.Quantity);
        var quotedLines = quotes.Where(q => quantity.ContainsKey(q.LineId)).Select(q => q.LineId).ToHashSet();
        var costs = new List<RfqLineCost>();
        var totals = new List<RfqSupplierTotals>();
        foreach (var supplier in suppliers)
        {
            var mine = quotes.Where(q => q.SupplierId == supplier.SupplierId && quantity.ContainsKey(q.LineId)).ToList();
            if (mine.Count == 0)
            {
                continue;
            }

            var sum = SupplierTotal(mine.Select(q => (quantity[q.LineId], q.UnitPrice)), supplier.Freight, supplier.IpiPct);
            foreach (var quote in mine)
            {
                var lineTotal = PurchaseOrderRules.LineTotal(quantity[quote.LineId], quote.UnitPrice);
                var freightShare = sum.Subtotal == 0m ? 0m : supplier.Freight * lineTotal / sum.Subtotal;
                var landed = lineTotal + (lineTotal * supplier.IpiPct / 100m) + freightShare;
                costs.Add(new RfqLineCost(quote.LineId, supplier.SupplierId, lineTotal, Math.Round(landed, 2, MidpointRounding.AwayFromZero), quote.LeadDays));
            }

            totals.Add(new RfqSupplierTotals(supplier.SupplierId, sum.Subtotal, supplier.Freight, sum.IpiAmount, sum.Total, mine.Max(q => q.LeadDays), mine.Count,
                quotedLines.All(l => mine.Any(q => q.LineId == l))));
        }

        var order = suppliers.ToDictionary(s => s.SupplierId, s => s.Sequence);
        var cheapest = costs.GroupBy(c => c.LineId)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.LandedCost).ThenBy(c => c.LeadDays).ThenBy(c => order[c.SupplierId]).First().SupplierId);
        var overall = totals.Where(t => t.CoversAll).OrderBy(t => t.Total).ThenBy(t => t.MaxLeadDays).ThenBy(t => order[t.SupplierId]).Select(t => (Guid?)t.SupplierId).FirstOrDefault();
        return new RfqComparison(costs, totals, cheapest, overall);
    }

    /// <summary>Quando o vencedor não é o de menor custo total da linha, o motivo é obrigatório (CA 8).</summary>
    public static bool NeedsReason(Guid selectedSupplierId, Guid? cheapestSupplierId) => cheapestSupplierId is { } cheapest && cheapest != selectedSupplierId;

    /// <summary>Aviso "1 cotação" da cotação concluída (CA 11, D8): só um fornecedor respondeu com preço; não bloqueia.</summary>
    public static string? SingleQuoteWarning(RfqStatus status, int quotesCount) =>
        status == RfqStatus.Closed && quotesCount == 1 ? "1 cotação: só um fornecedor respondeu a esta cotação." : null;

    /// <summary>Rótulo do motivo da escolha (lista do CA 8).</summary>
    public static string ReasonLabel(RfqSelectionReason reason) => reason switch
    {
        RfqSelectionReason.LeadTime => "Prazo",
        RfqSelectionReason.Quality => "Qualidade",
        RfqSelectionReason.PaymentTerm => "Condição de pagamento",
        RfqSelectionReason.ApprovedSupplier => "Fornecedor homologado",
        _ => "Outro",
    };

    public static string Money(decimal value) => value.ToString("C", PtBr);
}
