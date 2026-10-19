using System.Globalization;
using System.Text;

namespace BemAqui.SharedKernel;

/// <summary>Texto para busca com pg_trgm: minúsculas, sem acento e sem espaço nas pontas ("Pé-direito" vira "pe-direito").</summary>
public static class SearchText
{
    public static string Normalize(string? text)
    {
        var decomposed = (text ?? string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }
}
