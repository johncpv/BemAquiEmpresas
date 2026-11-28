using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

/// <summary>Implementação do <see cref="IParties"/> (CA 21): a porta dos outros módulos para os terceiros.</summary>
public sealed class PartiesContract(IPartiesDb db, PartyAddressIbge ibge) : IParties
{
    public const string BackofficeOnlyCode = "field_change_requires_backoffice";

    public Task<int> FillIbgeCodesAsync(Guid id, CancellationToken cancellationToken = default) => ibge.FillPartyAsync(id, cancellationToken);

    public async Task<IReadOnlyList<PartySummary>> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        var digits = PartyValidation.Digits(phone);
        if (digits is not { Length: 10 or 11 })
        {
            return [];
        }

        return await db.Parties.AsNoTracking().Where(p => p.IsActive && (p.Phone == digits || p.Contacts.Any(c => c.Phone == digits))).OrderBy(p => p.SearchText).Take(10)
            .Select(p => new PartySummary(p.Id, p.Kind, p.LegalName, p.TradeName, p.Document, p.Roles.Select(r => r.Role).ToList(), p.IsActive, false, p.DefaultPaymentTermId, p.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> PhoneSetByAppAsync(Guid id, string phone, CancellationToken cancellationToken = default)
    {
        var digits = PartyValidation.Digits(phone);
        return await db.Parties.AsNoTracking().AnyAsync(p => p.Id == id && p.PhoneSetByApp && p.Phone == digits && !p.Contacts.Any(c => c.Phone == digits), cancellationToken);
    }

    public async Task<Result> FillEmptyContactAsync(Guid id, PartyContactFill fill, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fill);
        var party = await db.Parties.Include(p => p.Addresses).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (party is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Terceiro não encontrado.");
        }

        if (party.AnonymizedAt is not null)
        {
            return Error.Conflict("party_anonymized", "Cadastro anonimizado: não pode ser alterado.");
        }

        var address = fill.BillingAddress is { } a
            ? new AddressInput(AddressKind.Billing, PartyValidation.Digits(a.ZipCode) ?? string.Empty, a.Street ?? string.Empty, a.Number ?? string.Empty, a.Complement,
                a.District ?? string.Empty, a.City ?? string.Empty, null, a.State ?? string.Empty, true)
            : null;
        var invalid = PartyValidation.Party(party.Kind, null, party.LegalName, null, [PartyRoleKind.Customer], null, null, fill.Email, fill.Phone, null)
            ?? PartyValidation.Address(address, "billingAddress.");
        if (invalid is not null)
        {
            return invalid;
        }

        var email = PartyValidation.Clean(fill.Email)?.ToLowerInvariant();
        var phone = PartyValidation.Digits(fill.Phone);
        var blocked = new Dictionary<string, string[]>();
        if (email is not null && party.Email is { Length: > 0 } && party.Email != email)
        {
            blocked["email"] = ["O e-mail já está preenchido. A troca é feita no ERP."];
        }

        if (phone is not null && party.Phone is { Length: > 0 } && party.Phone != phone)
        {
            blocked["phone"] = ["O telefone já está preenchido. A troca é feita no ERP."];
        }

        var billing = party.Addresses.Where(x => x.Kind == AddressKind.Billing).OrderByDescending(x => x.IsDefault).FirstOrDefault();
        if (address is not null && billing is not null && !SameAddress(billing, address))
        {
            blocked["billingAddress"] = ["O endereço de cobrança já está preenchido. A troca é feita no ERP."];
        }

        if (blocked.Count > 0)
        {
            return Error.Validation(BackofficeOnlyCode, "Trocar um contato existente é só no ERP, por uma pessoa.", blocked);
        }

        // INT-04 CA 11: o que o aplicativo preencheu fica marcado até uma pessoa confirmar no ERP.
        party.EmailSetByApp |= party.Email is null && email is not null;
        party.PhoneSetByApp |= party.Phone is null && phone is not null;
        party.Email ??= email;
        party.Phone ??= phone;
        if (address is not null && billing is null)
        {
            party.Addresses.Add(new PartyAddress
            {
                PartyId = party.Id, Kind = AddressKind.Billing, IsDefault = true, ZipCode = address.ZipCode, Street = address.Street.Trim(), Number = address.Number.Trim(),
                Complement = PartyValidation.Clean(address.Complement), District = address.District.Trim(), City = address.City.Trim(), State = address.State.Trim().ToUpperInvariant(),
            });
        }

        await ibge.FillTrackedAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static bool SameAddress(PartyAddress current, AddressInput input) =>
        current.ZipCode == input.ZipCode && Same(current.Street, input.Street) && Same(current.Number, input.Number) && Same(current.City, input.City)
        && Same(current.State, input.State) && Same(current.District, input.District);

    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

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

    public async Task<PartySummary?> FindBySeedKeyAsync(string seedKey, CancellationToken cancellationToken = default) =>
        await db.Parties.AsNoTracking().Where(p => p.SeedKey == seedKey)
            .Select(p => new PartySummary(p.Id, p.Kind, p.LegalName, p.TradeName, p.Document, p.Roles.Select(r => r.Role).ToList(), p.IsActive, false, p.DefaultPaymentTermId, p.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

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
