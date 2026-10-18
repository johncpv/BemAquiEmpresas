using BemAqui.Parties.Contracts;

namespace BemAqui.Parties.Application;

/// <summary>Linha da lista (CA 16). <c>Document</c> vem formatado; CPF sempre mascarado na lista (D9, CA 17).</summary>
public sealed record PartyListItem(Guid Id, PartyKind Kind, string LegalName, string? TradeName, string? Document, IReadOnlyList<PartyRoleKind> Roles, string? City, string? State, bool IsActive);

public sealed record PartyAddressDto(Guid Id, AddressKind Kind, string ZipCode, string Street, string Number, string? Complement, string District, string City, string? IbgeCode, string State, bool IsDefault);

public sealed record PartyContactDto(Guid Id, string Name, string? Role, string? Email, string? Phone, bool ReceivesBilling);

/// <summary>Detalhe do terceiro: o documento completo só aqui.</summary>
public sealed record PartyDetail(
    Guid Id,
    PartyKind Kind,
    string LegalName,
    string? TradeName,
    string? Document,
    IReadOnlyList<PartyRoleKind> Roles,
    string? StateRegistration,
    string? MunicipalRegistration,
    TaxRegime TaxRegime,
    string? Email,
    string? Phone,
    string? Notes,
    bool IsActive,
    IReadOnlyList<PartyAddressDto> Addresses,
    IReadOnlyList<PartyContactDto> Contacts,
    DateTimeOffset CreatedAt);

public sealed record AddressInput(AddressKind Kind, string ZipCode, string Street, string Number, string? Complement, string District, string City, string? IbgeCode, string State, bool IsDefault = false);

public sealed record ContactInput(string Name, string? Role, string? Email, string? Phone, bool ReceivesBilling);

/// <summary>Criação (CA 2 a 9): o endereço de cobrança é opcional e, se vier, vira o padrão.</summary>
public sealed record CreatePartyRequest(
    PartyKind Kind,
    string? Document,
    string LegalName,
    string? TradeName,
    IReadOnlyList<PartyRoleKind> Roles,
    string? StateRegistration = null,
    string? MunicipalRegistration = null,
    TaxRegime? TaxRegime = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null,
    AddressInput? BillingAddress = null,
    ContactInput? MainContact = null);

public sealed record UpdatePartyRequest(
    PartyKind Kind,
    string? Document,
    string LegalName,
    string? TradeName,
    IReadOnlyList<PartyRoleKind> Roles,
    string? StateRegistration,
    string? MunicipalRegistration,
    TaxRegime TaxRegime,
    string? Email,
    string? Phone,
    string? Notes);

/// <summary>Conferência do documento ao sair do campo (CA 4 e 6), antes de qualquer outra ação.</summary>
public sealed record DocumentCheck(bool Valid, bool Exists, Guid? Id, string? Name, bool Deleted, IReadOnlyList<PartyRoleKind> Roles);

/// <summary>Resultado da busca da topbar (CA 19): no máximo 5, CPF mascarado.</summary>
public sealed record PartySearchHit(Guid Id, string Name, string? Document, IReadOnlyList<PartyRoleKind> Roles);

public sealed record PartyListQuery(string? Q, PartyRoleKind? Role, bool IncludeInactive);
