namespace BemAqui.Purchasing.Domain;

/// <summary>Parcela de um título (número, vencimento e valor), antes de virar título no Finance.</summary>
public sealed record InvoiceInstallment(int Number, DateOnly DueDate, decimal Amount);

/// <summary>Parcelas de cada parte da nota (FIS-04b CA 6 a 9): a do recebimento (itens de estoque) e as da parte "Sem item de estoque", por conta.</summary>
public sealed record InvoiceInstallmentPlan(IReadOnlyList<InvoiceInstallment> Receipt, IReadOnlyList<(Guid AccountId, IReadOnlyList<InvoiceInstallment> Installments)> NoStock, bool Mismatch);

/// <summary>Regras puras do recebimento pela NF-e de entrada (FIS-04b), testadas sem banco.</summary>
public static class InvoiceReceiptRules
{
    /// <summary>Diferença de preço tolerada por unidade de estoque (CA 3): acima de R$ 0,01, o recebimento pela nota espera a correção do preço.</summary>
    public const decimal PriceTolerance = 0.01m;

    public const string PriceBlockedMessage = "O preço da nota é diferente do pedido. Peça a correção do preço a quem aprova compras.";
    public const string MismatchNotice = "As duplicatas não somam o total da nota";
    public const string NoDuplicatesHint = "A nota não trouxe duplicatas: os vencimentos vêm da condição do pedido.";
    public const string PriceDifferenceCode = "incoming_invoice_price_difference";

    /// <summary>O preço da nota (em unidade de estoque) é diferente do preço do pedido em mais de R$ 0,01 (CA 3).</summary>
    public static bool PriceDiffers(decimal orderPrice, decimal invoicePrice) => Math.Abs(invoicePrice - orderPrice) > PriceTolerance;

    /// <summary>
    /// Custo unitário de entrada pela nota (P-16, CA 5): (valor da linha + rateios de frete, IPI, seguro e outras despesas − crédito de
    /// impostos) ÷ quantidade, 6 casas <c>AwayFromZero</c>.
    /// </summary>
    public static decimal EntryUnitCost(decimal lineAmount, decimal freight, decimal ipi, decimal insurance, decimal other, decimal credit, decimal quantity) =>
        quantity <= 0m ? 0m : Math.Round((lineAmount + freight + ipi + insurance + other - credit) / quantity, 6, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Crédito de impostos da linha (CA 5): com o tributo recuperável no parâmetro, o ICMS, o PIS e a COFINS dos itens da nota ligados à linha e o
    /// IPI rateado saem do custo. Sem crédito (o padrão, P-01), zero: todos ficam no custo.
    /// </summary>
    public static decimal TaxCredit(IReadOnlyCollection<string> recoverable, decimal icms, decimal pis, decimal cofins, decimal ipiShare)
    {
        ArgumentNullException.ThrowIfNull(recoverable);
        var credit = 0m;
        credit += recoverable.Contains("ICMS") ? icms : 0m;
        credit += recoverable.Contains("PIS") ? pis : 0m;
        credit += recoverable.Contains("COFINS") ? cofins : 0m;
        credit += recoverable.Contains("IPI") ? ipiShare : 0m;
        return Math.Round(credit, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Parcelas das duas partes da nota (CA 6 a 9). A base são as duplicatas (uma parcela por duplicata, com o vencimento dela) ou, sem
    /// duplicatas, as parcelas da condição do pedido a partir da emissão (CA 7). O total = recebimento + parte sem item de estoque; cada
    /// parcela da base recebe a parte proporcional (a última absorve o centavo). Com as duplicatas somando o total, a soma das partes de uma
    /// parcela é exatamente a duplicata; somando outro valor, os títulos ficam pelo total com o aviso (CA 8, não bloqueia).
    /// </summary>
    public static InvoiceInstallmentPlan Plan(decimal receiptAmount, IReadOnlyList<(Guid AccountId, decimal Amount)> noStock, IReadOnlyList<(DateOnly DueDate, decimal Amount)> basis, bool fromDuplicates)
    {
        ArgumentNullException.ThrowIfNull(noStock);
        ArgumentNullException.ThrowIfNull(basis);
        if (basis.Count == 0)
        {
            return new InvoiceInstallmentPlan([], [], false);
        }

        var noStockTotal = noStock.Sum(n => n.Amount);
        var total = receiptAmount + noStockTotal;
        var weights = basis.Select(b => b.Amount).ToList();
        var mismatch = fromDuplicates && weights.Sum() != total;
        var totals = CostAllocator.Allocate(total, weights);
        var receipt = CostAllocator.Allocate(receiptAmount, weights);
        var rest = totals.Select((t, i) => t - receipt[i]).ToList();
        var parts = new List<(Guid, IReadOnlyList<InvoiceInstallment>)>();
        var used = new decimal[basis.Count];
        for (var k = 0; k < noStock.Count; k++)
        {
            var shares = k == noStock.Count - 1 ? rest.Select((r, i) => r - used[i]).ToList() : CostAllocator.Allocate(noStock[k].Amount, weights);
            for (var i = 0; i < shares.Count; i++)
            {
                used[i] += shares[i];
            }

            parts.Add((noStock[k].AccountId, Installments(shares, basis, k * basis.Count)));
        }

        return new InvoiceInstallmentPlan(Installments(receipt, basis, 0), parts, mismatch);
    }

    /// <summary>Descrição do título do recebimento pela nota (CA 6), com o aviso quando as duplicatas não fecham (CA 8).</summary>
    public static string InstallmentDescription(long invoiceNumber, string receiptNumber, int installment, int count, bool mismatch) =>
        $"NF-e {invoiceNumber} · recebimento {receiptNumber} — parcela {installment}/{count}" + (mismatch ? " · " + MismatchNotice : string.Empty);

    /// <summary>Descrição do título da parte "Sem item de estoque" (CA 9).</summary>
    public static string NoStockDescription(long invoiceNumber, string account, int installment, int count, bool mismatch) =>
        $"NF-e {invoiceNumber} · {account} — parcela {installment}/{count}" + (mismatch ? " · " + MismatchNotice : string.Empty);

    /// <summary>Parte sem item de estoque de um item da nota (CA 9): valor − desconto + IPI do item, 2 casas.</summary>
    public static decimal NoStockAmount(decimal amount, decimal discount, decimal ipi) => Math.Round(amount - discount + ipi, 2, MidpointRounding.AwayFromZero);

    private static List<InvoiceInstallment> Installments(IReadOnlyList<decimal> shares, IReadOnlyList<(DateOnly DueDate, decimal Amount)> basis, int offset) =>
        shares.Select((amount, i) => new InvoiceInstallment(offset + i + 1, basis[i].DueDate, amount)).Where(p => p.Amount > 0m).ToList();
}
