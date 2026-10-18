using System.Globalization;
using System.Text;
using BemAqui.BuildingBlocks.Time;

namespace BemAqui.Migrations.Application;

/// <summary>
/// Conversões comuns dos valores do Dolibarr (MIG-01a CA 20): número de ponto flutuante para <c>decimal</c> com
/// arredondamento <see cref="MidpointRounding.AwayFromZero"/>, datas locais de São Paulo para UTC e texto saneado.
/// </summary>
public static class DolibarrConvert
{
    /// <summary>Quantidade (<c>numeric(18,4)</c>).</summary>
    public static decimal? Quantity(object? value) => Number(value, 4);

    /// <summary>Valor com <paramref name="scale"/> casas. <c>double</c> passa pela representação mais curta (0.1 vira 0.1, e não 0.1000000000000000055).</summary>
    public static decimal? Number(object? value, int scale) => value switch
    {
        null or DBNull => null,
        decimal d => Math.Round(d, scale, MidpointRounding.AwayFromZero),
        double d when double.IsFinite(d) => Math.Round(decimal.Parse(d.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture), scale, MidpointRounding.AwayFromZero),
        float f when float.IsFinite(f) => Math.Round(decimal.Parse(f.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture), scale, MidpointRounding.AwayFromZero),
        int or long or short or byte or sbyte => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        string s when decimal.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => Math.Round(d, scale, MidpointRounding.AwayFromZero),
        _ => throw new FormatException("Número inválido: " + value),
    };

    /// <summary>Data sem hora (<c>date</c>): só a parte da data, sem conversão de fuso.</summary>
    public static DateOnly? Date(object? value) => value switch
    {
        null or DBNull => null,
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        _ => throw new FormatException("Data inválida: " + value),
    };

    /// <summary>Data com hora gravada pelo Dolibarr sem fuso: lida como horário de São Paulo e devolvida em UTC.</summary>
    public static DateTimeOffset? LocalDateTime(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        var local = value is DateTime dt ? DateTime.SpecifyKind(dt, DateTimeKind.Unspecified) : throw new FormatException("Data e hora inválida: " + value);
        if (BusinessTime.Zone.IsInvalidTime(local))
        {
            local = local.AddHours(1); // hora pulada no início do horário de verão
        }

        return new DateTimeOffset(local, BusinessTime.Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>
    /// Texto em UTF-8, em forma NFC, sem caracteres de controle (exceto quebra de linha e tabulação) e sem marcas de
    /// direção bidi; recupera o acento de texto gravado duas vezes em latin1 ("JoÃ£o" vira "João"). Vazio vira null.
    /// </summary>
    public static string? Text(object? value)
    {
        var text = value switch
        {
            null or DBNull => null,
            string s => s,
            byte[] bytes => DecodeBytes(bytes),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        if (text is null)
        {
            return null;
        }

        text = FixDoubleEncoding(text);
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormC))
        {
            if (ch is '\n' or '\t')
            {
                builder.Append(ch);
            }
            else if (!char.IsControl(ch) && !IsBidi(ch))
            {
                builder.Append(ch);
            }
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static bool IsBidi(char ch) => ch is '؜' or '‎' or '‏' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');

    private static string DecodeBytes(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string FixDoubleEncoding(string text)
    {
        if (text.All(c => c < 0x80) || text.Any(c => c > 0xFF) || !text.Any(c => c is 'Â' or 'Ã'))
        {
            return text;
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(Encoding.Latin1.GetBytes(text));
        }
        catch (DecoderFallbackException)
        {
            return text;
        }
    }
}
