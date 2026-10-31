namespace BemAqui.Finance.Domain;

/// <summary>Condições gravadas no boleto, base da divisão do valor pago (BNK-02): valor, vencimento, multa, juros e desconto.</summary>
public sealed record BillingSlipPaymentTerms(decimal Amount, DateOnly DueDate, DateOnly FineFrom, decimal FinePct, decimal InterestMonthPct, decimal DiscountAmount, DateOnly? DiscountUntil)
{
    public static BillingSlipPaymentTerms Of(BillingSlip slip)
    {
        ArgumentNullException.ThrowIfNull(slip);
        return new BillingSlipPaymentTerms(slip.Amount, slip.DueDate, slip.FineFrom, slip.FinePct, slip.InterestMonthPct, slip.DiscountAmount, slip.DiscountUntil);
    }
}

/// <summary>
/// Resultado da divisão: baixa automática (principal, juros, multa e desconto, com total = valor pago) ou divergência com o motivo, que
/// vai para a conferência humana (CA 9, R-PRP-7).
/// </summary>
public sealed record BillingSlipSplit(bool Automatic, decimal Principal, decimal Interest, decimal Fine, decimal Discount, string? Divergence)
{
    public static BillingSlipSplit Divergent(string reason) => new(false, 0, 0, 0, 0, reason);
}

/// <summary>
/// Divisão do valor pago no banco em principal, juros, multa e desconto (BNK-02 CA 7 e 9), pura. Na baixa, o principal é o que sai do
/// saldo e o total que entra na conta é principal + juros + multa − desconto (FIN-01), igual ao valor pago:
/// <list type="bullet">
/// <item>Pagou o saldo: principal = saldo.</item>
/// <item>Pagou a mais depois do vencimento: principal = saldo; multa (percentual sobre o valor) e juros (taxa mensal ÷ 30 por dia,
/// contados de <c>fine_from</c> até a data do crédito) até o calculado pelos percentuais do boleto. Acima disso, ou antes do vencimento,
/// divergência.</item>
/// <item>Pagou a menos até a data do desconto: principal = saldo e desconto = a diferença, até o desconto do boleto. Fora da data, ou
/// além do desconto, divergência.</item>
/// </list>
/// Saldo do título diferente do valor do boleto (o título mudou depois da emissão) é sempre divergência. Diferença de até
/// <see cref="Tolerance"/> no arredondamento do banco é aceita.
/// </summary>
public static class BillingSlipSettlement
{
    /// <summary>Arredondamento do banco aceito na conferência dos juros, da multa e do desconto.</summary>
    public const decimal Tolerance = 0.02m;

    public const string BalanceChanged = "O saldo do título mudou depois da emissão do boleto.";
    public const string Underpaid = "Pago a menor fora da data do desconto.";
    public const string UnderpaidBeyondDiscount = "Pago a menor além do desconto do boleto.";
    public const string Overpaid = "Pago a maior além dos juros e da multa calculados.";
    public const string OverpaidOnTime = "Pago a maior antes do vencimento.";
    public const string NothingPaid = "O banco não informou o valor pago.";

    public static decimal MaxFine(BillingSlipPaymentTerms terms, DateOnly creditDate)
    {
        ArgumentNullException.ThrowIfNull(terms);
        return creditDate > terms.DueDate ? Round(terms.Amount * terms.FinePct / 100m) : 0m;
    }

    /// <summary>Juros de mora pela taxa mensal (÷ 30 por dia corrido) de <c>fine_from</c> até o crédito, só depois do vencimento.</summary>
    public static decimal MaxInterest(BillingSlipPaymentTerms terms, DateOnly creditDate)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (creditDate <= terms.DueDate)
        {
            return 0m;
        }

        var days = Math.Max(0, creditDate.DayNumber - terms.FineFrom.DayNumber);
        return Round(terms.Amount * terms.InterestMonthPct / 100m / 30m * days);
    }

    public static BillingSlipSplit Split(BillingSlipPaymentTerms terms, decimal balance, decimal paid, DateOnly creditDate)
    {
        ArgumentNullException.ThrowIfNull(terms);
        paid = Round(paid);
        balance = Round(balance);
        if (paid <= 0)
        {
            return BillingSlipSplit.Divergent(NothingPaid);
        }

        if (balance != Round(terms.Amount))
        {
            return BillingSlipSplit.Divergent(BalanceChanged);
        }

        if (paid == balance)
        {
            return new BillingSlipSplit(true, balance, 0, 0, 0, null);
        }

        if (paid > balance)
        {
            if (creditDate <= terms.DueDate)
            {
                return BillingSlipSplit.Divergent(OverpaidOnTime);
            }

            var excess = paid - balance;
            var maxFine = MaxFine(terms, creditDate);
            var maxInterest = MaxInterest(terms, creditDate);
            if (excess > maxFine + maxInterest + Tolerance)
            {
                return BillingSlipSplit.Divergent(Overpaid);
            }

            var fine = Math.Min(excess, maxFine);
            return new BillingSlipSplit(true, balance, excess - fine, fine, 0, null);
        }

        var shortfall = balance - paid;
        if (terms.DiscountAmount <= 0 || terms.DiscountUntil is not { } until || creditDate > until)
        {
            return BillingSlipSplit.Divergent(Underpaid);
        }

        return shortfall <= terms.DiscountAmount + Tolerance
            ? new BillingSlipSplit(true, balance, 0, 0, shortfall, null)
            : BillingSlipSplit.Divergent(UnderpaidBeyondDiscount);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
