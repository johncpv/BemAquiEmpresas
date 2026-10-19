using System.Net;
using System.Text.RegularExpressions;

namespace BemAqui.Migrations.Application.Loaders;

/// <summary>Logradouro separado do número e do complemento (MIG-01b CA 8). Sem número reconhecido, <c>Number</c> é "S/N".</summary>
public sealed record SplitStreet(string Street, string Number, string? Complement, bool Separated);

/// <summary>Conversões de texto da carga de terceiros (MIG-01b CA 8, 10 e 11).</summary>
public static partial class DolibarrText
{
    [GeneratedRegex(@"<\s*(br|/p|/div|/li)\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"^\s*(?<street>.+?)\s*,?\s*(?:n[º°o.]*\s*)?(?<number>\d+[A-Za-z]?)\s*(?:[-,/]\s*(?<complement>.+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex StreetNumber();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    /// <summary>HTML do Dolibarr para texto: quebras viram linha, tags somem, entidades são decodificadas; sem controle nem bidi.</summary>
    public static string? HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = LineBreakTags().Replace(html, "\n");
        text = WebUtility.HtmlDecode(Tags().Replace(text, string.Empty)).Replace("\r", string.Empty, StringComparison.Ordinal).Replace('\u00A0', ' ');
        text = string.Join('\n', text.Split('\n').Select(l => Spaces().Replace(l, " ").Trim()));
        return DolibarrConvert.Text(BlankLines().Replace(text, "\n\n"));
    }

    /// <summary>"Rua das Flores, 120 - Sala 3" vira (Rua das Flores, 120, Sala 3); sem número, fica tudo no logradouro com "S/N".</summary>
    public static SplitStreet Street(string address)
    {
        var line = string.Join(", ", address.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var match = StreetNumber().Match(line);
        // "km 3" é parte da rodovia, não o número.
        return match.Success && match.Groups["street"].Value.Length >= 3 && !match.Groups["street"].Value.EndsWith("km", StringComparison.OrdinalIgnoreCase)
            ? new SplitStreet(match.Groups["street"].Value.TrimEnd(',', ' '), match.Groups["number"].Value, match.Groups["complement"].Success ? match.Groups["complement"].Value.Trim() : null, true)
            : new SplitStreet(line, "S/N", null, false);
    }

    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    public static string? Email(string? value) => value is { Length: > 0 } v && EmailPattern().IsMatch(v.Trim()) ? v.Trim() : null;

    /// <summary>Telefone com DDD (10 ou 11 dígitos); o +55 na frente é retirado. Fora disso, null.</summary>
    public static string? Phone(string? value)
    {
        var digits = Digits(value);
        if (digits.Length is 12 or 13 && digits.StartsWith("55", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        return digits.Length is 10 or 11 ? digits : null;
    }
}
