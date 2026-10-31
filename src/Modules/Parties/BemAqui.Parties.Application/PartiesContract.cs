using BemAqui.Parties.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

/// <summary>Implementação do <see cref="IParties"/> (CA 21): a porta dos outros módulos para os terceiros.</summary>
public sealed class PartiesContract(IPartiesDb db) : IParties
{
    public async Task<PartySummary?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Parties.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PartySummary(p.Id, p.Kind, p.LegalName, p.TradeName, p.Document, p.Roles.Select(r => r.Role).ToList(), p.IsActive, p.IsDeleted, p.DefaultPaymentTermId, p.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PartySummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Parties.IgnoreQueryFilters().AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new PartySummary(p.Id, p.Kind, p.LegalName, p.TradeName, p.Document, p.Roles.Select(r => r.Role).ToList(), p.IsActive, p.IsDeleted, p.DefaultPaymentTermId, p.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PartySummary>> SearchAsync(string? term, PartyRoleKind? role, int limit = 20, CancellationToken cancellationToken = default)
    {
        var text = Domain.Party.Normalize(term);
        var parties = db.Parties.AsNoTracking().Where(p => p.IsActive);
        if (text.Length > 0)
        {
            parties = parties.Where(p => p.SearchText.Contains(text));
        }

        if (role is { } r)
        {
            parties = parties.Where(p => p.Roles.Any(x => x.Role == r));
        }

        return await parties.OrderBy(p => p.SearchText).Take(Math.Clamp(limit, 1, 100))
            .Select(p => new PartySummary(p.Id, p.Kind, p.LegalName, p.TradeName, p.Document, p.Roles.Select(x => x.Role).ToList(), p.IsActive, false, p.DefaultPaymentTermId, p.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<PartySummary?> FindByDocumentAsync(string document, CancellationToken cancellationToken = default)
    {
        var doc = BemAqui.SharedKernel.BrazilianDocuments.Normalize(document);
        if (!BemAqui.SharedKernel.BrazilianDocuments.IsValid(doc))
        {
            return null;
        }

        return await db.Parties.AsNoTracking().Where(p => p.Document == doc).OrderByDescending(p => p.IsActive)
            .Select(p => new PartySummary(p.Id, p.Kind, p.LegalName, p.TradeName, p.Document, p.Roles.Select(r => r.Role).ToList(), p.IsActive, false, p.DefaultPaymentTermId, p.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> HasRoleAsync(Guid id, PartyRoleKind role, CancellationToken cancellationToken = default) =>
        db.PartyRoles.AnyAsync(r => r.PartyId == id && r.Role == role, cancellationToken);

    public async Task<PartyAddressInfo?> DefaultAddressAsync(Guid id, AddressKind kind, CancellationToken cancellationToken = default) =>
        await db.PartyAddresses.AsNoTracking().Where(a => a.PartyId == id && a.Kind == kind).OrderByDescending(a => a.IsDefault)
            .Select(a => new PartyAddressInfo(a.Id, a.Kind, a.ZipCode, a.Street, a.Number, a.Complement, a.District, a.City, a.IbgeCode, a.State))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PartyContactInfo?> BillingContactAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.PartyContacts.AsNoTracking().Where(c => c.PartyId == id).OrderByDescending(c => c.ReceivesBilling).ThenBy(c => c.Name)
            .Select(c => new PartyContactInfo(c.Id, c.Name, c.Role, c.Email, c.Phone, c.ReceivesBilling))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PartyContactInfo>> ContactsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.PartyContacts.AsNoTracking().Where(c => c.PartyId == id).OrderByDescending(c => c.ReceivesBilling).ThenBy(c => c.Name)
            .Select(c => new PartyContactInfo(c.Id, c.Name, c.Role, c.Email, c.Phone, c.ReceivesBilling)).ToListAsync(cancellationToken);

    public async Task<PartyFiscalInfo?> FiscalInfoAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Parties.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PartyFiscalInfo(p.Id, p.Kind, p.LegalName, p.Document, p.StateRegistration, p.Email, p.Phone))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PartyAddressInfo>> AddressesAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.PartyAddresses.AsNoTracking().Where(a => a.PartyId == id).OrderBy(a => a.Kind).ThenByDescending(a => a.IsDefault)
            .Select(a => new PartyAddressInfo(a.Id, a.Kind, a.ZipCode, a.Street, a.Number, a.Complement, a.District, a.City, a.IbgeCode, a.State)).ToListAsync(cancellationToken);
}
