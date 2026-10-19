using BemAqui.SharedKernel;

namespace BemAqui.Parties.Contracts;

/// <summary>Endereço de cobrança vindo da importação (MIG-01b CA 8).</summary>
public sealed record PartyImportAddress(string ZipCode, string Street, string Number, string? Complement, string District, string City, string State);

/// <summary>Terceiro como a importação monta a partir da origem (MIG-01b CA 2 a 10 e 14).</summary>
public sealed record PartyImportData(
    PartyKind Kind,
    string? Document,
    string LegalName,
    string? TradeName,
    IReadOnlyList<PartyRoleKind> Roles,
    string? Email,
    string? Phone,
    string? Notes,
    bool IsActive,
    Guid? DefaultPaymentTermId,
    PartyImportAddress? BillingAddress,
    string ImportSource);

public sealed record ContactImportData(string Name, string? Role, string? Email, string? Phone);

/// <summary>Registro gravado: id e <c>xmin</c> da linha de destino logo depois (idempotência da MIG-01a).</summary>
public sealed record ImportWrite(Guid Id, long Xmin);

/// <summary>
/// Escrita da importação nos terceiros (ADR 0017): só o módulo Migrations usa. Passa pelas mesmas validações da tela
/// (CAD-01a) e grava o <c>import_source</c>. Nunca apaga papel nem endereço; o vínculo com cadastro existente não
/// sobrescreve os dados (MIG-01b CA 7).
/// </summary>
public interface IPartyImport
{
    Task<Guid?> FindByDocumentAsync(string document, CancellationToken cancellationToken = default);

    Task<Result<ImportWrite>> CreateAsync(PartyImportData data, CancellationToken cancellationToken = default);

    /// <summary>Atualiza os dados principais com a origem e soma os papéis (nunca remove); o endereço novo entra se ainda não existe.</summary>
    Task<Result<ImportWrite>> UpdateAsync(Guid partyId, PartyImportData data, CancellationToken cancellationToken = default);

    /// <summary>Só acrescenta os papéis que faltam e grava o <c>import_source</c> se estiver vazio (CA 7).</summary>
    Task<Result<ImportWrite>> LinkAsync(Guid partyId, IReadOnlyList<PartyRoleKind> roles, string importSource, CancellationToken cancellationToken = default);

    /// <summary>Dados atuais, para mostrar o conflito; null se o terceiro não existe mais.</summary>
    Task<PartyImportData?> CurrentAsync(Guid partyId, CancellationToken cancellationToken = default);

    Task<long?> VersionAsync(Guid partyId, CancellationToken cancellationToken = default);

    Task<Result<ImportWrite>> AddContactAsync(Guid partyId, ContactImportData data, CancellationToken cancellationToken = default);

    Task<Result<ImportWrite>> UpdateContactAsync(Guid contactId, ContactImportData data, CancellationToken cancellationToken = default);

    Task<ContactImportData?> CurrentContactAsync(Guid contactId, CancellationToken cancellationToken = default);

    Task<long?> ContactVersionAsync(Guid contactId, CancellationToken cancellationToken = default);
}
