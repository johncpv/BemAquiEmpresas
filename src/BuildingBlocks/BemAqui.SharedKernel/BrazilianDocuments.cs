namespace BemAqui.SharedKernel;

public enum DocumentKind
{
    Cpf,
    Cnpj,
}

/// <summary>
/// CPF e CNPJ (CAD-01a CA 1), o mesmo validador do front (<c>src/Web/src/lib/masks.ts</c>). Aceita com ou sem máscara,
/// normaliza para maiúsculas sem pontuação e confere os dígitos verificadores. O CNPJ pode ser alfanumérico
/// (IN RFB 2.229/2024): cada caractere vale o código ASCII menos 48 ("0" = 0, "A" = 17), e os dois dígitos
/// verificadores continuam numéricos, pelo módulo 11.
/// </summary>
public static class BrazilianDocuments
{
    private static readonly int[] CnpjFirst = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] CnpjSecond = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    /// <summary>Só letras e dígitos, em maiúsculas ("47.225.005/0001-86" vira "47225005000186").</summary>
    public static string Normalize(string? value) =>
        new((value ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>CPF quando são 11 dígitos; CNPJ quando são 12 letras ou dígitos e 2 dígitos. Null para o resto.</summary>
    public static DocumentKind? KindOf(string? value)
    {
        var doc = Normalize(value);
        if (doc.Length == 11 && doc.All(char.IsAsciiDigit))
        {
            return DocumentKind.Cpf;
        }

        return doc.Length == 14 && doc[..12].All(char.IsAsciiLetterOrDigit) && doc[12..].All(char.IsAsciiDigit) ? DocumentKind.Cnpj : null;
    }

    public static bool IsValid(string? value) => KindOf(value) switch
    {
        DocumentKind.Cpf => IsValidCpf(value),
        DocumentKind.Cnpj => IsValidCnpj(value),
        _ => false,
    };

    public static bool IsValidCpf(string? value)
    {
        var cpf = Normalize(value);
        if (cpf.Length != 11 || !cpf.All(char.IsAsciiDigit) || AllSame(cpf))
        {
            return false;
        }

        int Digit(int size)
        {
            var sum = 0;
            for (var i = 0; i < size; i++)
            {
                sum += (cpf[i] - '0') * (size + 1 - i);
            }

            var rest = sum * 10 % 11;
            return rest == 10 ? 0 : rest;
        }

        return Digit(9) == cpf[9] - '0' && Digit(10) == cpf[10] - '0';
    }

    public static bool IsValidCnpj(string? value)
    {
        var cnpj = Normalize(value);
        if (KindOf(cnpj) != DocumentKind.Cnpj || AllSame(cnpj))
        {
            return false;
        }

        int Digit(int[] weights)
        {
            var sum = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                sum += (cnpj[i] - '0') * weights[i];
            }

            var rest = sum % 11;
            return rest < 2 ? 0 : 11 - rest;
        }

        return Digit(CnpjFirst) == cnpj[12] - '0' && Digit(CnpjSecond) == cnpj[13] - '0';
    }

    /// <summary>Exibição: "123.456.789-09" ou "12.ABC.345/01DE-35".</summary>
    public static string Format(string? value)
    {
        var doc = Normalize(value);
        return KindOf(doc) switch
        {
            DocumentKind.Cpf => $"{doc[..3]}.{doc[3..6]}.{doc[6..9]}-{doc[9..]}",
            DocumentKind.Cnpj => $"{doc[..2]}.{doc[2..5]}.{doc[5..8]}/{doc[8..12]}-{doc[12..]}",
            _ => doc,
        };
    }

    /// <summary>CPF para lista e busca (D9, LGPD): só os 6 dígitos do meio, "***.456.789-**".</summary>
    public static string MaskCpf(string? value)
    {
        var doc = Normalize(value);
        return doc.Length == 11 ? $"***.{doc[3..6]}.{doc[6..9]}-**" : "***";
    }

    private static bool AllSame(string value) => value.All(c => c == value[0]);
}
