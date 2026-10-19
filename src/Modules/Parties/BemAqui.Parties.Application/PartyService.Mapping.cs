using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Parties.Application;

public sealed partial class PartyService
{
    /// <summary>Documento para exibição: CNPJ formatado; CPF mascarado fora do detalhe (D9, CA 17).</summary>
    public static string? DisplayDocument(string? document, bool full) =>
        document is null ? null
        : BrazilianDocuments.KindOf(document) == DocumentKind.Cpf && !full ? BrazilianDocuments.MaskCpf(document)
        : BrazilianDocuments.Format(document);

    private static void Apply(Party party, PartyKind kind, string? document, string legalName, string? tradeName, string? stateRegistration, string? municipalRegistration, TaxRegime taxRegime, string? email, string? phone, string? notes)
    {
        party.Kind = kind;
        party.Document = document;
        party.LegalName = legalName.Trim();
        // PF não tem nome fantasia (CA 9).
        party.TradeName = kind == PartyKind.PF ? null : PartyValidation.Clean(tradeName);
        party.StateRegistration = PartyValidation.Clean(stateRegistration)?.ToUpperInvariant();
        party.MunicipalRegistration = PartyValidation.Clean(municipalRegistration);
        party.TaxRegime = taxRegime;
        party.Email = PartyValidation.Clean(email)?.ToLowerInvariant();
        party.Phone = PartyValidation.Digits(phone);
        party.Notes = PartyValidation.Clean(notes);
    }

    private static PartyAddress NewAddress(Guid partyId, AddressInput input, bool isDefault) => Apply(new PartyAddress { PartyId = partyId, IsDefault = isDefault }, input);

    private static PartyAddress Apply(PartyAddress address, AddressInput input)
    {
        address.Kind = input.Kind;
        address.ZipCode = input.ZipCode;
        address.Street = input.Street.Trim();
        address.Number = input.Number.Trim();
        address.Complement = PartyValidation.Clean(input.Complement);
        address.District = input.District.Trim();
        address.City = input.City.Trim();
        address.IbgeCode = PartyValidation.Clean(input.IbgeCode);
        address.State = input.State.Trim().ToUpperInvariant();
        return address;
    }

    private static PartyContact Apply(PartyContact contact, ContactInput input)
    {
        contact.Name = input.Name.Trim();
        contact.Role = PartyValidation.Clean(input.Role);
        contact.Email = PartyValidation.Clean(input.Email)?.ToLowerInvariant();
        contact.Phone = PartyValidation.Digits(input.Phone);
        contact.ReceivesBilling = input.ReceivesBilling;
        return contact;
    }

    private static PartyAddressDto ToDto(PartyAddress a) => new(a.Id, a.Kind, a.ZipCode, a.Street, a.Number, a.Complement, a.District, a.City, a.IbgeCode, a.State, a.IsDefault);

    private static PartyContactDto ToDto(PartyContact c) => new(c.Id, c.Name, c.Role, c.Email, c.Phone, c.ReceivesBilling);

    private static PartyDetail ToDetail(Party p) => new(
        p.Id, p.Kind, p.LegalName, p.TradeName, DisplayDocument(p.Document, full: true), p.Roles.Select(r => r.Role).Order().ToList(),
        p.StateRegistration, p.MunicipalRegistration, p.TaxRegime, p.Email, p.Phone, p.Notes, p.IsActive,
        p.Addresses.OrderBy(a => a.Kind).ThenByDescending(a => a.IsDefault).Select(ToDto).ToList(),
        p.Contacts.OrderByDescending(c => c.ReceivesBilling).ThenBy(c => c.Name).Select(ToDto).ToList(),
        p.CreatedAt,
        ImportSource: p.ImportSource);
}
