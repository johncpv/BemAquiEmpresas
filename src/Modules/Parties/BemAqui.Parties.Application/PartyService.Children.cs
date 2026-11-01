using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

public sealed partial class PartyService
{
    /// <summary>Novo endereço (CA 12): o primeiro de cada tipo vira o padrão; marcar como padrão desmarca o anterior.</summary>
    public async Task<Result<PartyDetail>> AddAddressAsync(Guid partyId, AddressInput input, CancellationToken cancellationToken = default)
    {
        if (await AnonymizedAsync(partyId, cancellationToken) is { } anonymized)
        {
            return anonymized;
        }

        ArgumentNullException.ThrowIfNull(input);
        if (!await db.Parties.AnyAsync(p => p.Id == partyId, cancellationToken))
        {
            return NotFound();
        }

        if (PartyValidation.Address(input) is { } invalid)
        {
            return invalid;
        }

        var first = !await db.PartyAddresses.AnyAsync(a => a.PartyId == partyId && a.Kind == input.Kind, cancellationToken);
        var address = NewAddress(partyId, input, isDefault: false);
        db.PartyAddresses.Add(address);
        await SaveDefaultAsync(partyId, input.Kind, first || input.IsDefault ? address : null, cancellationToken);
        return await GetAsync(partyId, cancellationToken);
    }

    public async Task<Result<PartyDetail>> UpdateAddressAsync(Guid partyId, Guid addressId, AddressInput input, CancellationToken cancellationToken = default)
    {
        if (await AnonymizedAsync(partyId, cancellationToken) is { } anonymized)
        {
            return anonymized;
        }

        ArgumentNullException.ThrowIfNull(input);
        var address = await OwnAddressAsync(partyId, addressId, cancellationToken);
        if (address is null)
        {
            return AddressNotFound();
        }

        if (PartyValidation.Address(input) is { } invalid)
        {
            return invalid;
        }

        var kindChanged = address.Kind != input.Kind;
        Apply(address, input);
        if (kindChanged)
        {
            address.IsDefault = false;
        }

        await SaveDefaultAsync(partyId, input.Kind, input.IsDefault || (kindChanged && !await db.PartyAddresses.AnyAsync(a => a.PartyId == partyId && a.Kind == input.Kind && a.Id != addressId, cancellationToken)) ? address : null, cancellationToken);
        return await GetAsync(partyId, cancellationToken);
    }

    /// <summary>Remover endereço: se era o padrão do tipo, o mais antigo do mesmo tipo assume.</summary>
    public async Task<Result<PartyDetail>> RemoveAddressAsync(Guid partyId, Guid addressId, CancellationToken cancellationToken = default)
    {
        if (await AnonymizedAsync(partyId, cancellationToken) is { } anonymized)
        {
            return anonymized;
        }

        var address = await OwnAddressAsync(partyId, addressId, cancellationToken);
        if (address is null)
        {
            return AddressNotFound();
        }

        db.PartyAddresses.Remove(address);
        await db.SaveChangesAsync(cancellationToken);
        if (address.IsDefault && await db.PartyAddresses.Where(a => a.PartyId == partyId && a.Kind == address.Kind).OrderBy(a => a.Id).FirstOrDefaultAsync(cancellationToken) is { } next)
        {
            next.IsDefault = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(partyId, cancellationToken);
    }

    public async Task<Result<PartyDetail>> AddContactAsync(Guid partyId, ContactInput input, CancellationToken cancellationToken = default)
    {
        if (await AnonymizedAsync(partyId, cancellationToken) is { } anonymized)
        {
            return anonymized;
        }

        ArgumentNullException.ThrowIfNull(input);
        if (!await db.Parties.AnyAsync(p => p.Id == partyId, cancellationToken))
        {
            return NotFound();
        }

        if (PartyValidation.Contact(input) is { } invalid)
        {
            return invalid;
        }

        db.PartyContacts.Add(Apply(new PartyContact { PartyId = partyId }, input));
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(partyId, cancellationToken);
    }

    public async Task<Result<PartyDetail>> UpdateContactAsync(Guid partyId, Guid contactId, ContactInput input, CancellationToken cancellationToken = default)
    {
        if (await AnonymizedAsync(partyId, cancellationToken) is { } anonymized)
        {
            return anonymized;
        }

        ArgumentNullException.ThrowIfNull(input);
        var contact = await db.PartyContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.PartyId == partyId, cancellationToken);
        if (contact is null)
        {
            return ContactNotFound();
        }

        if (PartyValidation.Contact(input) is { } invalid)
        {
            return invalid;
        }

        Apply(contact, input);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(partyId, cancellationToken);
    }

    public async Task<Result<PartyDetail>> RemoveContactAsync(Guid partyId, Guid contactId, CancellationToken cancellationToken = default)
    {
        if (await AnonymizedAsync(partyId, cancellationToken) is { } anonymized)
        {
            return anonymized;
        }

        var contact = await db.PartyContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.PartyId == partyId, cancellationToken);
        if (contact is null)
        {
            return ContactNotFound();
        }

        db.PartyContacts.Remove(contact);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(partyId, cancellationToken);
    }

    /// <summary>Cadastro anonimizado (LGPD-01b) é só leitura: 409 <c>party_anonymized</c>.</summary>
    private async Task<Error?> AnonymizedAsync(Guid partyId, CancellationToken cancellationToken) =>
        await db.Parties.IgnoreQueryFilters().AnyAsync(p => p.Id == partyId && p.AnonymizedAt != null, cancellationToken) ? Anonymized() : null;

    public static Error Anonymized() => Error.Conflict(AnonymizedCode, "Cadastro anonimizado a pedido do titular: não pode ser alterado.");

    /// <summary>IDOR pai/filho (CA 13): o endereço só existe para a rota do próprio terceiro; o terceiro precisa existir.</summary>
    private Task<PartyAddress?> OwnAddressAsync(Guid partyId, Guid addressId, CancellationToken cancellationToken) =>
        db.PartyAddresses.FirstOrDefaultAsync(a => a.Id == addressId && a.PartyId == partyId && db.Parties.Any(p => p.Id == partyId), cancellationToken);

    /// <summary>Grava e, se houver novo padrão, troca em dois passos: desmarca os outros, depois marca o novo.</summary>
    private async Task SaveDefaultAsync(Guid partyId, AddressKind kind, PartyAddress? newDefault, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (newDefault is not null)
        {
            newDefault.IsDefault = false;
            foreach (var other in await db.PartyAddresses.Where(a => a.PartyId == partyId && a.Kind == kind && a.IsDefault && a.Id != newDefault.Id).ToListAsync(cancellationToken))
            {
                other.IsDefault = false;
            }

            await db.SaveChangesAsync(cancellationToken);
            newDefault.IsDefault = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    private static Error AddressNotFound() => Error.NotFound(Error.Codes.NotFound, "Endereço não encontrado.");

    private static Error ContactNotFound() => Error.NotFound(Error.Codes.NotFound, "Contato não encontrado.");
}
