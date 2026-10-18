using System.Text.Json.Serialization;

namespace BemAqui.Parties.Contracts;

/// <summary>Pessoa física ou jurídica.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartyKind>))]
public enum PartyKind
{
    PF,
    PJ,
}

/// <summary>Papel do terceiro (R-TER-1): um mesmo documento pode ter vários.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartyRoleKind>))]
public enum PartyRoleKind
{
    Customer,
    Supplier,
    Installer,
    Carrier,
}

[JsonConverter(typeof(JsonStringEnumConverter<AddressKind>))]
public enum AddressKind
{
    Billing,
    Delivery,
    Site,
}

[JsonConverter(typeof(JsonStringEnumConverter<TaxRegime>))]
public enum TaxRegime
{
    NotInformed,
    SimplesNacional,
    Mei,
    LucroPresumido,
    LucroReal,
    Exempt,
}

/// <summary>Resumo para outros módulos (CAD-01a CA 21). <c>Document</c> é o normalizado, para o documento fiscal.</summary>
public sealed record PartySummary(Guid Id, PartyKind Kind, string LegalName, string? TradeName, string? Document, IReadOnlyList<PartyRoleKind> Roles, bool IsActive, bool IsDeleted);

public sealed record PartyAddressInfo(Guid Id, AddressKind Kind, string ZipCode, string Street, string Number, string? Complement, string District, string City, string? IbgeCode, string State);

public sealed record PartyContactInfo(Guid Id, string Name, string? Role, string? Email, string? Phone, bool ReceivesBilling);

/// <summary>
/// Terceiros para os outros módulos (Commercial, Purchasing, Finance, Inventory): a única porta de entrada, nunca a
/// tabela. A busca devolve só ativos; o resumo por id devolve até excluído (documento antigo continua mostrando o nome).
/// </summary>
public interface IParties
{
    Task<PartySummary?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PartySummary>> SearchAsync(string? term, PartyRoleKind? role, int limit = 20, CancellationToken cancellationToken = default);

    Task<bool> HasRoleAsync(Guid id, PartyRoleKind role, CancellationToken cancellationToken = default);

    Task<PartyAddressInfo?> DefaultAddressAsync(Guid id, AddressKind kind, CancellationToken cancellationToken = default);

    Task<PartyContactInfo?> BillingContactAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Quem usa um terceiro (CAD-01a CA 20): cada módulo que guarda o id de um terceiro implementa e registra no DI.
/// Em uso, o terceiro não é excluído, só inativado.
/// </summary>
public interface IPartyUsageProvider
{
    /// <summary>Descrição curta do uso ("código de fornecedor", "orçamentos") ou null se não usa.</summary>
    Task<string?> UsageAsync(Guid partyId, CancellationToken cancellationToken = default);
}
