using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

public sealed partial class PartyService
{
    public async Task<Result<ImportWrite>> ImportAddContactAsync(Guid partyId, ContactImportData data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!await db.Parties.AnyAsync(p => p.Id == partyId, cancellationToken))
        {
            return NotFound();
        }

        var input = new ContactInput(data.Name, data.Role, data.Email, data.Phone, false);
        if (PartyValidation.Contact(input) is { } invalid)
        {
            return invalid;
        }

        var contact = Apply(new PartyContact { PartyId = partyId }, input);
        db.PartyContacts.Add(contact);
        await db.SaveChangesAsync(cancellationToken);
        return new ImportWrite(contact.Id, await ContactXminAsync(contact.Id, cancellationToken) ?? 0);
    }

    public async Task<Result<ImportWrite>> ImportUpdateContactAsync(Guid contactId, ContactImportData data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        var contact = await db.PartyContacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null)
        {
            return ContactNotFound();
        }

        var input = new ContactInput(data.Name, data.Role, data.Email, data.Phone, contact.ReceivesBilling);
        if (PartyValidation.Contact(input) is { } invalid)
        {
            return invalid;
        }

        Apply(contact, input);
        await db.SaveChangesAsync(cancellationToken);
        return new ImportWrite(contact.Id, await ContactXminAsync(contact.Id, cancellationToken) ?? 0);
    }

    public async Task<ContactImportData?> ImportCurrentContactAsync(Guid contactId, CancellationToken cancellationToken) =>
        await db.PartyContacts.AsNoTracking().Where(c => c.Id == contactId).Select(c => new ContactImportData(c.Name, c.Role, c.Email, c.Phone)).FirstOrDefaultAsync(cancellationToken);

    public Task<long?> ContactXminAsync(Guid contactId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<long?>($"SELECT xmin::text::bigint AS \"Value\" FROM parties.party_contacts WHERE id = {contactId}").SingleOrDefaultAsync(cancellationToken);
}

/// <summary><see cref="IPartyImport"/> (MIG-01b): a porta da importação para os terceiros, sobre o <see cref="PartyService"/>.</summary>
public sealed class PartyImport(PartyService service) : IPartyImport
{
    public Task<Guid?> FindByDocumentAsync(string document, CancellationToken cancellationToken = default) => service.FindByDocumentForImportAsync(document, cancellationToken);

    public Task<Result<ImportWrite>> CreateAsync(PartyImportData data, CancellationToken cancellationToken = default) => service.ImportCreateAsync(data, cancellationToken);

    public Task<Result<ImportWrite>> UpdateAsync(Guid partyId, PartyImportData data, CancellationToken cancellationToken = default) => service.ImportUpdateAsync(partyId, data, cancellationToken);

    public Task<Result<ImportWrite>> LinkAsync(Guid partyId, IReadOnlyList<PartyRoleKind> roles, string importSource, CancellationToken cancellationToken = default) =>
        service.ImportLinkAsync(partyId, roles, importSource, cancellationToken);

    public Task<PartyImportData?> CurrentAsync(Guid partyId, CancellationToken cancellationToken = default) => service.ImportCurrentAsync(partyId, cancellationToken);

    public Task<long?> VersionAsync(Guid partyId, CancellationToken cancellationToken = default) => service.PartyXminAsync(partyId, cancellationToken);

    public Task<Result<ImportWrite>> AddContactAsync(Guid partyId, ContactImportData data, CancellationToken cancellationToken = default) => service.ImportAddContactAsync(partyId, data, cancellationToken);

    public Task<Result<ImportWrite>> UpdateContactAsync(Guid contactId, ContactImportData data, CancellationToken cancellationToken = default) => service.ImportUpdateContactAsync(contactId, data, cancellationToken);

    public Task<ContactImportData?> CurrentContactAsync(Guid contactId, CancellationToken cancellationToken = default) => service.ImportCurrentContactAsync(contactId, cancellationToken);

    public Task<long?> ContactVersionAsync(Guid contactId, CancellationToken cancellationToken = default) => service.ContactXminAsync(contactId, cancellationToken);
}
