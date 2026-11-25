using System.Security.Cryptography;
using System.Text;
using BemAqui.Sales.Contracts;

namespace BemAqui.Sales.Domain;

/// <summary>Regras puras da devolução e do vale-troca (PDV-06): quantidade devolvível, valor do item com o desconto rateado, prazo e código do vale.</summary>
public static class ReturnRules
{
    /// <summary>Vendas feitas: concluída e as já devolvidas em parte ou no todo (continuam receita, recebível e baixa).</summary>
    public static readonly SaleStatus[] Done = [SaleStatus.Completed, SaleStatus.PartiallyReturned, SaleStatus.Returned];

    public const string LateMessagePrefix = "Prazo de devolução encerrado em ";

    /// <summary>CA 2: o que ainda pode voltar da linha (vendido − já devolvido), nunca negativo.</summary>
    public static decimal Returnable(decimal sold, decimal alreadyReturned) => Math.Max(0m, SaleRules.Quantity(sold - alreadyReturned));

    /// <summary>
    /// Valor devolvido de uma quantidade da linha: o praticado na venda (total da linha, já com o desconto rateado), proporcional à quantidade. A última
    /// parte da linha leva o que falta (total − já devolvido), para os centavos fecharem.
    /// </summary>
    public static decimal Amount(decimal lineTotal, decimal lineQuantity, decimal quantity, decimal alreadyReturnedQuantity, decimal alreadyReturnedAmount)
    {
        if (lineQuantity <= 0 || quantity <= 0)
        {
            return 0m;
        }

        return SaleRules.Quantity(alreadyReturnedQuantity + quantity) >= lineQuantity
            ? Math.Max(0m, SaleRules.Money(lineTotal - alreadyReturnedAmount))
            : SaleRules.Money(lineTotal * quantity / lineQuantity);
    }

    /// <summary>CA 3: o último dia do prazo de troca (data da venda + dias da loja).</summary>
    public static DateOnly Deadline(DateOnly saleDate, int exchangeDays) => saleDate.AddDays(Math.Max(0, exchangeDays));

    public static bool IsLate(DateOnly today, DateOnly deadline) => today > deadline;

    public static string LateMessage(DateOnly deadline) => LateMessagePrefix + deadline.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture) + ".";

    /// <summary>A venda passa a devolvida quando nada mais pode voltar; senão, parcialmente devolvida.</summary>
    public static SaleStatus StatusAfter(IEnumerable<(decimal Sold, decimal Returned)> lines) =>
        lines.All(l => Returnable(l.Sold, l.Returned) == 0) ? SaleStatus.Returned : SaleStatus.PartiallyReturned;

    /// <summary>CA 8: o vale aceita o valor? Vencido, sem saldo ou valor acima do saldo: a mensagem; null quando aceita.</summary>
    public static string? RedeemProblem(decimal balance, DateOnly expiresOn, DateOnly today, decimal amount)
    {
        if (today > expiresOn)
        {
            return "Vale vencido em " + expiresOn.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) + ".";
        }

        if (balance <= 0)
        {
            return "Vale sem saldo.";
        }

        if (amount <= 0 || SaleRules.Money(amount) != amount)
        {
            return "Informe o valor a usar do vale, em reais.";
        }

        return amount > balance ? "O valor passa do saldo do vale (" + balance.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + ")." : null;
    }

    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ"; // sem 0/O, 1/I/L: o operador digita do papel

    /// <summary>Código do vale: 12 caracteres aleatórios (cerca de 59 bits) em 3 grupos (<c>VT-XXXX-XXXX-XXXX</c>).</summary>
    public static string NewCode()
    {
        Span<char> chars = stackalloc char[12];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"VT-{chars[..4]}-{chars[4..8]}-{chars[8..]}";
    }

    /// <summary>O código como o operador digitou: maiúsculas, sem espaço nem traço, sem o prefixo; null quando não tem o formato.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 40)
        {
            return null;
        }

        var clean = new string(code.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        if (clean.StartsWith("VT", StringComparison.Ordinal) && clean.Length == 14)
        {
            clean = clean[2..];
        }

        return clean.Length == 12 && clean.All(c => Alphabet.Contains(c, StringComparison.Ordinal)) ? clean : null;
    }

    /// <summary>Hash do código normalizado (o banco nunca guarda o código).</summary>
    public static string Hash(string normalized) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("bq-store-credit:" + normalized)));
}
