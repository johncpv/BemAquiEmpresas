using BemAqui.Purchasing.Contracts;

namespace BemAqui.Purchasing.Domain;

/// <summary>Rateio de um valor pelas linhas (P-16).</summary>
public static class CostAllocator
{
    /// <summary>
    /// Rateia <paramref name="total"/> proporcionalmente aos <paramref name="weights"/> (o valor de cada linha), 2 casas <c>AwayFromZero</c>;
    /// a última linha absorve a diferença de arredondamento (CA 6: frete de R$ 100,00 sobre R$ 300,00 e R$ 700,00 vai R$ 30,00 e R$ 70,00).
    /// Sem peso (tudo zero), vai tudo na última linha.
    /// </summary>
    public static IReadOnlyList<decimal> Allocate(decimal total, IReadOnlyList<decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0)
        {
            return [];
        }

        var sum = weights.Sum();
        var shares = new decimal[weights.Count];
        var used = 0m;
        for (var i = 0; i < weights.Count - 1; i++)
        {
            shares[i] = sum == 0m ? 0m : Math.Round(total * weights[i] / sum, 2, MidpointRounding.AwayFromZero);
            used += shares[i];
        }

        shares[^1] = total - used;
        return shares;
    }
}

/// <summary>Regras puras do recebimento manual (CMP-04), testadas sem banco.</summary>
public static class GoodsReceiptRules
{
    public const string DocType = "REC";
    public const int MaxNotes = 500;
    public const int MaxReason = 500;
    public const int MaxLotNumber = 60;
    public const decimal MaxTolerancePct = 50m;
    public const string SoleOwnerLabel = "Recebido pelo Proprietário único";

    /// <summary>Valor da linha: quantidade × preço do pedido, 2 casas <c>AwayFromZero</c> (a mesma conta do pedido).</summary>
    public static decimal LineAmount(decimal quantity, decimal unitPrice) => PurchaseOrderRules.LineTotal(quantity, unitPrice);

    /// <summary>Custo unitário de entrada (P-16): (valor da linha + rateio do frete + rateio do IPI) ÷ quantidade, 6 casas.</summary>
    public static decimal EntryUnitCost(decimal lineAmount, decimal freightShare, decimal ipiShare, decimal quantity) =>
        quantity <= 0m ? 0m : Math.Round((lineAmount + freightShare + ipiShare) / quantity, 6, MidpointRounding.AwayFromZero);

    /// <summary>Valor do recebimento (títulos): Σ valores das linhas + frete + IPI deste recebimento, 2 casas.</summary>
    public static decimal Amount(IEnumerable<decimal> lineAmounts, decimal freight, decimal ipi)
    {
        ArgumentNullException.ThrowIfNull(lineAmounts);
        return Math.Round(lineAmounts.Sum() + freight + ipi, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Quanto ainda pode entrar na linha (CA 3): a quantidade pedida × (1 + tolerância) menos o já recebido (inclusive o excedente), 4 casas;
    /// nunca negativo.
    /// </summary>
    public static decimal MaxQuantity(decimal ordered, decimal receivedTotal, decimal tolerancePct) =>
        Math.Max(0m, Math.Round(ordered * (1m + tolerancePct / 100m), 4, MidpointRounding.ToZero) - receivedTotal);

    /// <summary>Teto (D8, CA 4): total aprovado + a tolerância, 2 casas.</summary>
    public static decimal Ceiling(decimal approvedTotal, decimal tolerancePct) => Math.Round(approvedTotal * (1m + tolerancePct / 100m), 2, MidpointRounding.ToZero);

    /// <summary>
    /// Frete ou IPI proporcional ao que está sendo recebido (CA 1): o valor do pedido × (itens deste recebimento ÷ subtotal do pedido), 2 casas,
    /// até o que sobrou; o recebimento que completa o pedido leva todo o resto.
    /// </summary>
    public static decimal ProportionalShare(decimal orderAmount, decimal orderSubtotal, decimal receiptSubtotal, decimal alreadyUsed, bool completes)
    {
        var left = Math.Max(0m, orderAmount - alreadyUsed);
        if (completes || orderSubtotal <= 0m)
        {
            return left;
        }

        return Math.Min(left, Math.Round(orderAmount * receiptSubtotal / orderSubtotal, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Previsto do pedido depois dos recebimentos (CA 9): o total menos o que já virou título do recebimento, nunca negativo.</summary>
    public static decimal PlannedBalance(decimal orderTotal, decimal receivedAmount) => Math.Max(0m, orderTotal - receivedAmount);

    /// <summary>Descrição do título do recebimento (CA 8).</summary>
    public static string InstallmentDescription(string receiptNumber, string orderNumber, int installment, int count) =>
        $"Recebimento {receiptNumber} do pedido {orderNumber} — parcela {installment}/{count}";

    /// <summary>Quantidade na unidade de estoque a partir da do fornecedor (fator do <c>item_supplier_codes</c>), 4 casas <c>AwayFromZero</c>.</summary>
    public static decimal FromSupplierUnit(decimal supplierQuantity, decimal factor) => Math.Round(supplierQuantity * factor, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Divide o recebido entre a quantidade pedida e o excedente da tolerância (o <c>CHECK</c> segura <c>quantity_received ≤ quantity</c>).
    /// </summary>
    public static (decimal Received, decimal Over) Split(decimal ordered, decimal receivedTotal) =>
        receivedTotal <= ordered ? (Math.Max(0m, receivedTotal), 0m) : (ordered, receivedTotal - ordered);

    /// <summary>Situação do pedido depois de receber ou estornar (CA 5, 12): tudo recebido → <c>Received</c>; algo → <c>PartiallyReceived</c>; nada → a de antes.</summary>
    public static PurchaseOrderStatus StatusAfter(IReadOnlyCollection<(decimal Ordered, decimal Received)> lines, PurchaseOrderStatus before)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count > 0 && lines.All(l => l.Received >= l.Ordered))
        {
            return PurchaseOrderStatus.Received;
        }

        return lines.Any(l => l.Received > 0m) ? PurchaseOrderStatus.PartiallyReceived : before is PurchaseOrderStatus.Approved or PurchaseOrderStatus.Sent ? before : PurchaseOrderStatus.Approved;
    }

    /// <summary>
    /// Segregação (CA 2, 14): quem pediu, quem decidiu algum nível da aprovação ou quem corrigiu o preço do pedido não registra o recebimento
    /// dele.
    /// </summary>
    public static bool IsInvolved(Guid user, Guid? requestedBy, IEnumerable<Guid?> deciders, IEnumerable<Guid?> priceCorrectors)
    {
        ArgumentNullException.ThrowIfNull(deciders);
        ArgumentNullException.ThrowIfNull(priceCorrectors);
        return requestedBy == user || deciders.Contains(user) || priceCorrectors.Contains(user);
    }

    /// <summary>"Encerrado com saldo de {x}" (CA 13).</summary>
    public static string ClosedLabel(decimal balance) => $"Encerrado com saldo de {PurchaseOrderRules.Money(balance)}";
}
