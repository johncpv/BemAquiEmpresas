using BemAqui.SharedKernel;
using FluentValidation.Results;

namespace BemAqui.BuildingBlocks.Validation;

public static class ValidationResultExtensions
{
    /// <summary>Erros do FluentValidation viram um único <see cref="Error"/> de validação com erros por campo.</summary>
    public static Error ToError(this ValidationResult result, string message = "Revise os campos destacados.")
    {
        ArgumentNullException.ThrowIfNull(result);
        var fields = result.Errors
            .GroupBy(e => JsonFieldName(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        return Error.Validation(Error.Codes.Validation, message, fields);
    }

    // "Address.ZipCode" → "address.zipCode", igual ao JSON (camelCase) que o front envia.
    private static string JsonFieldName(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
