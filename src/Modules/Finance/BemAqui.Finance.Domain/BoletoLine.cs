namespace BemAqui.Finance.Domain;

/// <summary>Linha digitável lida (FIN-02 CA 6): valor e vencimento quando a linha os traz.</summary>
public sealed record BoletoInfo(decimal? Amount, DateOnly? DueDate);

/// <summary>
/// Linha digitável do boleto (FEBRABAN): 47 dígitos (bancário, campos no módulo 10 e dígito geral no módulo 11) ou 48
/// (arrecadação/concessionária, blocos no módulo 10 ou 11 pelo terceiro dígito).
/// </summary>
public static class BoletoLine
{
    private static readonly DateOnly FactorBase = new(1997, 10, 7);

    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    /// <summary>Null quando a linha é inválida (tamanho ou dígito verificador).</summary>
    public static BoletoInfo? Parse(string? value, DateOnly today)
    {
        var d = Digits(value);
        return d.Length switch
        {
            47 => Bank(d, today),
            48 => Utility(d),
            _ => null,
        };
    }

    private static BoletoInfo? Bank(string d, DateOnly today)
    {
        if (Mod10(d[..9]) != d[9] - '0' || Mod10(d[10..20]) != d[20] - '0' || Mod10(d[21..31]) != d[31] - '0')
        {
            return null;
        }

        var barcode = d[..4] + d[32] + d[33..47] + d[4..9] + d[10..20] + d[21..31];
        if (BankGeneralDv(barcode[..4] + barcode[5..]) != barcode[4] - '0')
        {
            return null;
        }

        var factor = int.Parse(d[33..37], System.Globalization.CultureInfo.InvariantCulture);
        var amount = long.Parse(d[37..47], System.Globalization.CultureInfo.InvariantCulture) / 100m;
        DateOnly? due = null;
        if (factor > 0)
        {
            // O fator recomeçou em 1000 em 22/02/2025: das duas datas possíveis, a mais perto de hoje.
            var first = FactorBase.AddDays(factor);
            var second = first.AddDays(9000);
            due = Math.Abs(first.DayNumber - today.DayNumber) <= Math.Abs(second.DayNumber - today.DayNumber) ? first : second;
        }

        return new BoletoInfo(amount > 0 ? amount : null, due);
    }

    private static BoletoInfo? Utility(string d)
    {
        if (d[0] != '8')
        {
            return null;
        }

        var useMod10 = d[2] is '6' or '7';
        for (var block = 0; block < 4; block++)
        {
            var part = d.Substring(block * 12, 11);
            var dv = d[(block * 12) + 11] - '0';
            if ((useMod10 ? Mod10(part) : UtilityMod11(part)) != dv)
            {
                return null;
            }
        }

        var barcode = d[..11] + d[12..23] + d[24..35] + d[36..47];
        var amount = d[2] is '6' or '8' ? long.Parse(barcode[4..15], System.Globalization.CultureInfo.InvariantCulture) / 100m : 0m;
        return new BoletoInfo(amount > 0 ? amount : null, null);
    }

    /// <summary>Módulo 10: pesos 2 e 1 da direita para a esquerda, somando os algarismos do produto.</summary>
    public static int Mod10(string digits)
    {
        var sum = 0;
        var weight = 2;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var product = (digits[i] - '0') * weight;
            sum += product > 9 ? product - 9 : product;
            weight = weight == 2 ? 1 : 2;
        }

        return (10 - (sum % 10)) % 10;
    }

    private static int Mod11Sum(string digits)
    {
        var sum = 0;
        var weight = 2;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            sum += (digits[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        return sum;
    }

    /// <summary>Dígito geral do código de barras bancário: 0, 10 e 11 viram 1.</summary>
    public static int BankGeneralDv(string barcodeWithoutDv)
    {
        var dv = 11 - (Mod11Sum(barcodeWithoutDv) % 11);
        return dv is 0 or 10 or 11 ? 1 : dv;
    }

    private static int UtilityMod11(string digits)
    {
        var rest = Mod11Sum(digits) % 11;
        return rest is 0 or 1 ? 0 : 11 - rest;
    }
}
