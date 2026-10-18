namespace BemAqui.Platform.Application.Tenants;

/// <summary>CNPJ com dígitos verificadores (FND-03b CA 3).</summary>
public static class Cnpj
{
    public static bool IsValid(string? value)
    {
        var digits = (value ?? string.Empty).Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (digits.Length != 14 || digits.Distinct().Count() == 1)
        {
            return false;
        }

        return digits[12] == Check(digits, 12) && digits[13] == Check(digits, 13);
    }

    private static int Check(int[] digits, int length)
    {
        var weights = length == 12 ? new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 } : new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var sum = weights.Select((w, i) => w * digits[i]).Sum();
        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }
}
