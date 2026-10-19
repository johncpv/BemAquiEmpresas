using BemAqui.Finance.Contracts;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Divisão de um valor pelas parcelas da condição (CAD-05b CA 11 a 14). Cada parcela é
/// <c>round(valor × pct / 100, 2, AwayFromZero)</c> e a última recebe o que falta; o vencimento é a data base mais os dias,
/// em dias corridos, sem ajuste de dia não útil (revisto na FIN-01).
/// </summary>
public static class PaymentTermSplitter
{
    public const decimal Total = 100m;

    /// <summary>Mensagem PT-BR do problema das parcelas, ou null se estão válidas (CA 12).</summary>
    public static string? Validate(IReadOnlyList<Installment>? installments)
    {
        if (installments is null || installments.Count == 0)
        {
            return "Informe ao menos uma parcela.";
        }

        if (installments.Any(i => i.Days < 0))
        {
            return "Os dias das parcelas precisam ser 0 ou mais.";
        }

        if (installments.Zip(installments.Skip(1)).Any(p => p.Second.Days <= p.First.Days))
        {
            return "Os dias das parcelas precisam ser crescentes.";
        }

        if (installments.Any(i => i.Percent <= 0 || decimal.Round(i.Percent, 4) != i.Percent))
        {
            return "Cada parcela precisa de um percentual maior que zero, com até 4 casas.";
        }

        var sum = installments.Sum(i => i.Percent);
        return sum == Total ? null : $"A soma das parcelas precisa ser 100%. Hoje está em {sum.ToString("0.####", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}%.";
    }

    public static IReadOnlyList<InstallmentDue> Split(IReadOnlyList<Installment> installments, decimal amount, DateOnly baseDate)
    {
        ArgumentNullException.ThrowIfNull(installments);
        var result = new List<InstallmentDue>(installments.Count);
        var allocated = 0m;
        for (var i = 0; i < installments.Count; i++)
        {
            var value = i == installments.Count - 1
                ? amount - allocated
                : Math.Round(amount * installments[i].Percent / Total, 2, MidpointRounding.AwayFromZero);
            allocated += value;
            result.Add(new InstallmentDue(i + 1, baseDate.AddDays(installments[i].Days), value));
        }

        return result;
    }

    /// <summary>"Dividir igualmente" (CA 13): percentuais iguais com 4 casas; a última absorve a diferença (33,3333 + 33,3333 + 33,3334).</summary>
    public static IReadOnlyList<decimal> EqualPercents(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var each = Math.Round(Total / count, 4, MidpointRounding.ToZero);
        return [.. Enumerable.Repeat(each, count - 1), Total - (each * (count - 1))];
    }
}
