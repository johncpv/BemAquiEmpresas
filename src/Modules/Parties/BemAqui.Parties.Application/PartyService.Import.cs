using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

/// <summary>
/// Escrita da importação (MIG-01b, ADR 0017): mesmas validações e normalizações da tela, mais o <c>import_source</c>.
/// Nunca remove papel nem endereço; devolve o <c>xmin</c> da linha do terceiro para a idempotência da MIG-01a.
/// </summary>
public sealed partial class PartyService
{
    public async Task<Guid?> FindByDocumentForImportAsync(string document, CancellationToken cancellationToken)
    {
        var doc = BrazilianDocuments.Normalize(document);
        return doc.Length == 0 ? null : await db.Parties.Where(p => p.Document == doc).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Result<ImportWrite>> ImportCreateAsync(PartyImportData data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        var created = await CreateAsync(new CreatePartyRequest(data.Kind, data.Document, data.LegalName, data.TradeName, data.Roles,
            Email: data.Email, Phone: data.Phone, Notes: data.Notes, BillingAddress: ToInput(data.BillingAddress), DefaultPaymentTermId: data.DefaultPaymentTermId), cancellationToken);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var party = await db.Parties.FirstAsync(p => p.Id == created.Value.Id, cancellationToken);
        party.ImportSource = data.ImportSource;
        party.IsActive = data.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return new ImportWrite(party.Id, await PartyXminAsync(party.Id, cancellationToken) ?? 0);
    }

    public async Task<Result<ImportWrite>> ImportUpdateAsync(Guid partyId, PartyImportData data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        var party = await Load(partyId).FirstOrDefaultAsync(cancellationToken);
        if (party is null)
        {
            return NotFound();
        }

        var roles = party.Roles.Select(r => r.Role).Union(data.Roles).ToList();
        var invalid = PartyValidation.Party(data.Kind, data.Document, data.LegalName, data.TradeName, roles, party.StateRegistration, party.MunicipalRegistration, data.Email, data.Phone, data.Notes)
            ?? PartyValidation.Address(ToInput(data.BillingAddress));
        if (invalid is not null)
        {
            return invalid;
        }

        var document = NullIfEmpty(BrazilianDocuments.Normalize(data.Document));
        if (document is not null && document != party.Document && await DuplicateAsync(document, partyId, cancellationToken) is { } duplicate)
        {
            return duplicate;
        }

        Apply(party, data.Kind, document, data.LegalName, data.TradeName, party.StateRegistration, party.MunicipalRegistration, party.TaxRegime, data.Email, data.Phone, data.Notes);
        party.IsActive = data.IsActive;
        party.DefaultPaymentTermId = data.DefaultPaymentTermId ?? party.DefaultPaymentTermId;
        party.ImportSource ??= data.ImportSource;
        foreach (var role in data.Roles.Where(r => party.Roles.All(x => x.Role != r)).Distinct())
        {
            db.PartyRoles.Add(new PartyRole { PartyId = party.Id, Role = role });
        }

        AddAddressIfNew(party, data.BillingAddress);
        await db.SaveChangesAsync(cancellationToken);
        return new ImportWrite(party.Id, await PartyXminAsync(party.Id, cancellationToken) ?? 0);
    }

    public async Task<Result<ImportWrite>> ImportLinkAsync(Guid partyId, IReadOnlyList<PartyRoleKind> roles, string importSource, CancellationToken cancellationToken)
    {
        var party = await db.Parties.Include(p => p.Roles).FirstOrDefaultAsync(p => p.Id == partyId, cancellationToken);
        if (party is null)
        {
            return NotFound();
        }

        foreach (var role in roles.Where(r => party.Roles.All(x => x.Role != r)).Distinct())
        {
            db.PartyRoles.Add(new PartyRole { PartyId = party.Id, Role = role });
        }

        party.ImportSource ??= importSource;
        await db.SaveChangesAsync(cancellationToken);
        return new ImportWrite(party.Id, await PartyXminAsync(party.Id, cancellationToken) ?? 0);
    }

    public async Task<PartyImportData?> ImportCurrentAsync(Guid partyId, CancellationToken cancellationToken)
    {
        var p = await Load(partyId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return p is null ? null : new PartyImportData(p.Kind, p.Document, p.LegalName, p.TradeName, p.Roles.Select(r => r.Role).ToList(), p.Email, p.Phone, p.Notes,
            p.IsActive, p.DefaultPaymentTermId, null, p.ImportSource ?? string.Empty);
    }

    public Task<long?> PartyXminAsync(Guid partyId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<long?>($"SELECT xmin::text::bigint AS \"Value\" FROM parties.parties WHERE id = {partyId} AND NOT is_deleted").SingleOrDefaultAsync(cancellationToken);

    private void AddAddressIfNew(Party party, PartyImportAddress? address)
    {
        if (ToInput(address) is not { } input || party.Addresses.Any(a => a.ZipCode == input.ZipCode && a.Street == input.Street.Trim() && a.Number == input.Number.Trim()))
        {
            return;
        }

        var isDefault = party.Addresses.All(a => !(a.Kind == AddressKind.Billing && a.IsDefault));
        db.PartyAddresses.Add(NewAddress(party.Id, input, isDefault));
    }

    private static AddressInput? ToInput(PartyImportAddress? a) =>
        a is null ? null : new AddressInput(AddressKind.Billing, a.ZipCode, a.Street, a.Number, a.Complement, a.District, a.City, null, a.State);
}
