using BemAqui.SharedKernel;

namespace BemAqui.Identity.Application;

public sealed partial class CompanySettings
{
    private static readonly string[] States =
        ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];

    /// <summary>Regras do perfil (CA 2), com mensagem PT-BR por campo; o endereço é opcional, mas sem campo fora do formato.</summary>
    private static Error? Validate(CompanyProfileInput input)
    {
        var fields = new Dictionary<string, string[]>();
        void Add(string field, string message) => fields[field] = [message];
        if (string.IsNullOrWhiteSpace(input.LegalName) || input.LegalName.Trim().Length is < 2 or > 200)
        {
            Add("legalName", "Informe a razão social (de 2 a 200 caracteres).");
        }

        if (input.TradeName?.Trim().Length > 200)
        {
            Add("tradeName", "Use até 200 caracteres no nome fantasia.");
        }

        if (Clean(input.StateRegistration) is { } ie && !StateRegistrationPattern().IsMatch(ie))
        {
            Add("stateRegistration", "Informe só os dígitos da inscrição estadual, ou ISENTO.");
        }

        if (input.MunicipalRegistration?.Trim().Length > 20)
        {
            Add("municipalRegistration", "Use até 20 caracteres na inscrição municipal.");
        }

        if (Digits(input.ZipCode) is { } zip && zip.Length != 8)
        {
            Add("zipCode", "CEP com 8 dígitos.");
        }

        if (Clean(input.State) is { } state && !States.Contains(state.ToUpperInvariant()))
        {
            Add("state", "Escolha a UF.");
        }

        if (Digits(input.CityIbge) is { } ibge && ibge.Length != 7)
        {
            Add("cityIbge", "Código IBGE com 7 dígitos.");
        }

        if (Digits(input.Phone) is { } phone && phone.Length is not (10 or 11))
        {
            Add("phone", "Telefone com DDD: 10 ou 11 dígitos.");
        }

        if (Clean(input.Email) is { } email && !EmailPattern().IsMatch(email))
        {
            Add("email", "E-mail inválido.");
        }

        foreach (var (field, value, max) in new[] { ("street", input.Street, 200), ("number", input.Number, 20), ("complement", input.Complement, 100), ("district", input.District, 100), ("city", input.City, 100) })
        {
            if (value?.Trim().Length > max)
            {
                Add(field, $"Use até {max} caracteres.");
            }
        }

        return fields.Count == 0 ? null : Error.Validation("validation", fields.First().Value[0], fields);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Digits(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
