using System.Text.RegularExpressions;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Parties.Application;

/// <summary>Regras de entrada do terceiro, do endereço e do contato (CA 3, 9, 10, 12, 14, 15). Mensagens em PT-BR por campo.</summary>
public static partial class PartyValidation
{
    public static readonly string[] States =
        ["AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"];

    public const string LastRoleMessage = "O terceiro precisa de ao menos um papel.";

    public static Error? Party(PartyKind kind, string? document, string legalName, string? tradeName, IReadOnlyList<PartyRoleKind>? roles, string? stateRegistration, string? municipalRegistration, string? email, string? phone, string? notes)
    {
        var fields = new Dictionary<string, string[]>();
        var doc = BrazilianDocuments.Normalize(document);
        if (doc.Length > 0)
        {
            var docKind = BrazilianDocuments.KindOf(doc);
            if (kind == PartyKind.PF && docKind != DocumentKind.Cpf)
            {
                fields["document"] = ["Para pessoa física, informe um CPF com 11 dígitos."];
            }
            else if (kind == PartyKind.PJ && docKind != DocumentKind.Cnpj)
            {
                fields["document"] = ["Para pessoa jurídica, informe um CNPJ com 14 caracteres."];
            }
            else if (!BrazilianDocuments.IsValid(doc))
            {
                fields["document"] = [kind == PartyKind.PF ? "CPF inválido." : "CNPJ inválido."];
            }
        }

        if (string.IsNullOrWhiteSpace(legalName) || legalName.Trim().Length is < 2 or > 200)
        {
            fields["legalName"] = [kind == PartyKind.PF ? "Informe o nome (de 2 a 200 caracteres)." : "Informe a razão social (de 2 a 200 caracteres)."];
        }

        if (tradeName?.Trim().Length > 200)
        {
            fields["tradeName"] = ["Use até 200 caracteres no nome fantasia."];
        }

        if (roles is null || roles.Count == 0)
        {
            fields["roles"] = [LastRoleMessage];
        }

        if (!string.IsNullOrWhiteSpace(stateRegistration) && !StateRegistrationPattern().IsMatch(stateRegistration.Trim()))
        {
            fields["stateRegistration"] = ["Informe só os dígitos da inscrição estadual, ou ISENTO."];
        }

        if (municipalRegistration?.Trim().Length > 20)
        {
            fields["municipalRegistration"] = ["Use até 20 caracteres na inscrição municipal."];
        }

        if (!IsEmail(email))
        {
            fields["email"] = ["E-mail inválido."];
        }

        if (!IsPhone(phone))
        {
            fields["phone"] = ["Telefone com DDD: 10 ou 11 dígitos."];
        }

        if (notes?.Length > 2000)
        {
            fields["notes"] = ["Use até 2.000 caracteres nas observações."];
        }

        return Result(fields);
    }

    public static Error? Address(AddressInput? address, string prefix = "")
    {
        if (address is null)
        {
            return null;
        }

        var fields = new Dictionary<string, string[]>();
        if (!ZipPattern().IsMatch(address.ZipCode ?? string.Empty))
        {
            fields[prefix + "zipCode"] = ["CEP com 8 dígitos."];
        }

        if (string.IsNullOrWhiteSpace(address.Street))
        {
            fields[prefix + "street"] = ["Informe o logradouro."];
        }

        if (string.IsNullOrWhiteSpace(address.Number))
        {
            fields[prefix + "number"] = ["Informe o número (ou S/N)."];
        }

        if (string.IsNullOrWhiteSpace(address.District))
        {
            fields[prefix + "district"] = ["Informe o bairro."];
        }

        if (string.IsNullOrWhiteSpace(address.City))
        {
            fields[prefix + "city"] = ["Informe a cidade."];
        }

        if (!States.Contains(address.State?.Trim().ToUpperInvariant()))
        {
            fields[prefix + "state"] = ["Escolha a UF."];
        }

        if (!string.IsNullOrWhiteSpace(address.IbgeCode) && !IbgePattern().IsMatch(address.IbgeCode))
        {
            fields[prefix + "ibgeCode"] = ["Código IBGE com 7 dígitos."];
        }

        return Result(fields);
    }

    public static Error? Contact(ContactInput? contact, string prefix = "")
    {
        if (contact is null)
        {
            return null;
        }

        var fields = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(contact.Name) || contact.Name.Trim().Length > 150)
        {
            fields[prefix + "name"] = ["Informe o nome do contato."];
        }

        if (!IsEmail(contact.Email))
        {
            fields[prefix + "email"] = ["E-mail inválido."];
        }

        if (!IsPhone(contact.Phone))
        {
            fields[prefix + "phone"] = ["Telefone com DDD: 10 ou 11 dígitos."];
        }

        return Result(fields);
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? Digits(string? value) => string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(char.IsAsciiDigit).ToArray());

    private static bool IsEmail(string? value) => string.IsNullOrWhiteSpace(value) || (value.Length <= 254 && EmailPattern().IsMatch(value.Trim()));

    private static bool IsPhone(string? value) => string.IsNullOrWhiteSpace(value) || Digits(value)!.Length is 10 or 11;

    private static Error? Result(Dictionary<string, string[]> fields) =>
        fields.Count == 0 ? null : Error.Validation(Error.Codes.Validation, fields.Values.First()[0], fields);

    [GeneratedRegex(@"^(ISENTO|\d{2,14})$", RegexOptions.IgnoreCase)]
    private static partial Regex StateRegistrationPattern();

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex ZipPattern();

    [GeneratedRegex(@"^\d{7}$")]
    private static partial Regex IbgePattern();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();
}
