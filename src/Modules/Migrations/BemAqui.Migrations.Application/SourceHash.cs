using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BemAqui.Migrations.Application.Snapshot;
using BemAqui.SharedKernel;

namespace BemAqui.Migrations.Application;

/// <summary>
/// Hash estável da linha de origem (SHA-256 do JSON das colunas em ordem alfabética, sem <c>tms</c>): o mesmo conteúdo dá
/// o mesmo hash em qualquer execução, e só a data de alteração do Dolibarr não conta como mudança (CA 11).
/// </summary>
public static class SourceHash
{
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal) { "tms" };

    public static string Of(SourceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var canonical = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (column, value) in row.Values)
        {
            if (!Ignored.Contains(column))
            {
                canonical[column] = Canonical(value);
            }
        }

        var json = JsonSerializer.Serialize(new { t = row.Table.Name, v = canonical });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string? Canonical(object? value) => value switch
    {
        null or DBNull => null,
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}

/// <summary>
/// Máscara do dado pessoal na staging, no relatório e nas diferenças do conflito (CA 18 e 21, ADR 0018): CPF com
/// <see cref="BrazilianDocuments.MaskCpf"/>, e-mail com a primeira letra e o domínio, telefone com os 4 últimos dígitos.
/// CNPJ não é dado pessoal e sai inteiro.
/// </summary>
public static class PersonalMask
{
    public const string Hidden = "***";

    public static string? Mask(string column, object? value)
    {
        var text = value switch
        {
            null or DBNull => null,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (text.Contains('@', StringComparison.Ordinal) && !text.Any(char.IsWhiteSpace))
        {
            var at = text.IndexOf('@', StringComparison.Ordinal);
            return (at > 0 ? text[..1] : string.Empty) + Hidden + text[at..];
        }

        if (column.Contains("phone", StringComparison.OrdinalIgnoreCase) || column.Contains("mobile", StringComparison.OrdinalIgnoreCase))
        {
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            return digits.Length > 4 ? Hidden + digits[^4..] : Hidden;
        }

        // Só CNPJ válido sai inteiro: texto qualquer de 14 caracteres não pode escapar da máscara.
        if (BrazilianDocuments.KindOf(text) == DocumentKind.Cnpj && BrazilianDocuments.IsValidCnpj(text))
        {
            return BrazilianDocuments.Normalize(text);
        }

        if (BrazilianDocuments.KindOf(text) == DocumentKind.Cpf && text.All(c => char.IsAsciiDigit(c) || c is '.' or '-' or ' '))
        {
            return BrazilianDocuments.MaskCpf(text);
        }

        return Hidden;
    }

    private static readonly string[] PersonalFields = ["email", "phone", "mobile", "document", "cpf", "siren", "siret", "address", "street", "note"];

    /// <summary>Diferença do conflito com os dois valores mascarados quando o campo é pessoal (CA 18), qualquer que seja o carregador.</summary>
    public static FieldDifference Difference(FieldDifference difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        return PersonalFields.Any(p => difference.Field.Contains(p, StringComparison.OrdinalIgnoreCase))
            ? difference with { Source = Mask(difference.Field, difference.Source), Target = Mask(difference.Field, difference.Target) }
            : difference;
    }

    /// <summary>Conteúdo da linha em JSON para a staging, com as colunas pessoais mascaradas.</summary>
    public static string Payload(SourceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var values = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (column, value) in row.Values)
        {
            values[column] = row.Table.Column(column)?.Personal == true ? Mask(column, value) : value;
        }

        return JsonSerializer.Serialize(values);
    }
}
