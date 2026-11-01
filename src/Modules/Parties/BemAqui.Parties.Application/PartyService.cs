using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

/// <summary>
/// Cadastro de terceiros (CAD-01a): um cadastro só por documento, com papéis, endereços e contatos. O documento é
/// opcional; quando existe, é válido e único entre os não excluídos (índice único parcial, CA 5).
/// </summary>
public sealed partial class PartyService(IPartiesDb db, IEnumerable<IPartyUsageProvider> usages, BemAqui.Finance.Contracts.IPaymentTerms paymentTerms, TimeProvider clock)
{
    public const string DocumentIndex = "ix_parties_document_unique";
    public const string ExistsCode = "party_exists";
    public const string DeletedCode = "party_deleted";
    public const string InUseCode = "party_in_use";
    public const string AnonymizedCode = "party_anonymized";

    /// <summary>Conferência ao sair do campo de documento (CA 4 e 6): vem antes de qualquer outra ação.</summary>
    public async Task<DocumentCheck> CheckDocumentAsync(string? document, CancellationToken cancellationToken = default)
    {
        var doc = BrazilianDocuments.Normalize(document);
        if (!BrazilianDocuments.IsValid(doc))
        {
            return new DocumentCheck(false, false, null, null, false, []);
        }

        var found = await db.Parties.IgnoreQueryFilters().AsNoTracking().Where(p => p.Document == doc).OrderBy(p => p.IsDeleted)
            .Select(p => new { p.Id, p.LegalName, p.IsDeleted, Roles = p.Roles.Select(r => r.Role).ToList() }).FirstOrDefaultAsync(cancellationToken);
        return found is null
            ? new DocumentCheck(true, false, null, null, false, [])
            : new DocumentCheck(true, true, found.Id, found.LegalName, found.IsDeleted, found.Roles);
    }

    public async Task<Result<PartyDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var party = await Load(id).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return party is null ? NotFound() : ToDetail(party) with { DefaultPaymentTerm = await TermRefAsync(party.DefaultPaymentTermId, cancellationToken) };
    }

    public async Task<Result<PartyDetail>> CreateAsync(CreatePartyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invalid = PartyValidation.Party(request.Kind, request.Document, request.LegalName, request.TradeName, request.Roles, request.StateRegistration, request.MunicipalRegistration, request.Email, request.Phone, request.Notes)
            ?? PartyValidation.Address(request.BillingAddress, "billingAddress.")
            ?? PartyValidation.Contact(request.MainContact, "mainContact.");
        if (invalid is not null)
        {
            return invalid;
        }

        var document = NullIfEmpty(BrazilianDocuments.Normalize(request.Document));
        if (document is not null && await DuplicateAsync(document, null, cancellationToken) is { } duplicate)
        {
            return duplicate;
        }

        if (await InvalidTermAsync(request.DefaultPaymentTermId, null, cancellationToken) is { } badTerm)
        {
            return badTerm;
        }

        var party = new Party { Kind = request.Kind, CreatedAt = clock.GetUtcNow(), TaxRegime = request.TaxRegime ?? TaxRegime.NotInformed, DefaultPaymentTermId = request.DefaultPaymentTermId };
        Apply(party, request.Kind, document, request.LegalName, request.TradeName, request.StateRegistration, request.MunicipalRegistration, party.TaxRegime, request.Email, request.Phone, request.Notes);
        party.Roles.AddRange(request.Roles.Distinct().Select(r => new PartyRole { PartyId = party.Id, Role = r }));
        if (request.BillingAddress is { } billing)
        {
            party.Addresses.Add(NewAddress(party.Id, billing with { Kind = AddressKind.Billing }, isDefault: true));
        }

        if (request.MainContact is { } contact)
        {
            party.Contacts.Add(Apply(new PartyContact { PartyId = party.Id }, contact));
        }

        db.Parties.Add(party);
        return await SaveAsync(party.Id, document, cancellationToken);
    }

    public async Task<Result<PartyDetail>> UpdateAsync(Guid id, UpdatePartyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var party = await Load(id).FirstOrDefaultAsync(cancellationToken);
        if (party is null)
        {
            return NotFound();
        }

        if (party.AnonymizedAt is not null)
        {
            return Anonymized();
        }

        var invalid = PartyValidation.Party(request.Kind, request.Document, request.LegalName, request.TradeName, request.Roles, request.StateRegistration, request.MunicipalRegistration, request.Email, request.Phone, request.Notes);
        if (invalid is not null)
        {
            return invalid;
        }

        var document = NullIfEmpty(BrazilianDocuments.Normalize(request.Document));
        if (document is not null && document != party.Document && await DuplicateAsync(document, id, cancellationToken) is { } duplicate)
        {
            return duplicate;
        }

        if (await InvalidTermAsync(request.DefaultPaymentTermId, party.DefaultPaymentTermId, cancellationToken) is { } badTerm)
        {
            return badTerm;
        }

        Apply(party, request.Kind, document, request.LegalName, request.TradeName, request.StateRegistration, request.MunicipalRegistration, request.TaxRegime, request.Email, request.Phone, request.Notes);
        party.DefaultPaymentTermId = request.DefaultPaymentTermId;
        var wanted = request.Roles.Distinct().ToHashSet();
        db.PartyRoles.RemoveRange(party.Roles.Where(r => !wanted.Contains(r.Role)));
        foreach (var role in wanted.Where(w => party.Roles.All(r => r.Role != w)))
        {
            db.PartyRoles.Add(new PartyRole { PartyId = party.Id, Role = role });
        }

        return await SaveAsync(party.Id, document, cancellationToken);
    }

    private async Task<Result<PartyDetail>> SaveAsync(Guid id, string? document, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (document is not null && DbErrors.IsUniqueViolation(ex, DocumentIndex))
        {
            // Duas gravações do mesmo documento ao mesmo tempo (CA 5): só uma passa.
            return await DuplicateAsync(document, id, cancellationToken) ?? Error.Conflict(ExistsCode, "Este documento já está cadastrado.");
        }

        return await GetAsync(id, cancellationToken);
    }

    private async Task<Error?> DuplicateAsync(string document, Guid? except, CancellationToken cancellationToken)
    {
        var other = await db.Parties.IgnoreQueryFilters().AsNoTracking().Where(p => p.Document == document && p.Id != except).OrderBy(p => p.IsDeleted)
            .Select(p => new { p.Id, p.LegalName, p.IsDeleted, p.Kind }).FirstOrDefaultAsync(cancellationToken);
        if (other is null)
        {
            return null;
        }

        var label = other.Kind == PartyKind.PF ? "CPF" : "CNPJ";
        var data = new Dictionary<string, object?> { ["existingId"] = other.Id, ["existingName"] = other.LegalName };
        return other.IsDeleted
            ? Error.Conflict(DeletedCode, $"Este {label} pertence a um cadastro excluído. Restaure o cadastro.") with { Data = data }
            : Error.Conflict(ExistsCode, $"Este {label} já está cadastrado como {other.LegalName}.") with { Data = data };
    }

    private IQueryable<Party> Load(Guid id) =>
        db.Parties.Include(p => p.Roles).Include(p => p.Addresses).Include(p => p.Contacts).AsSplitQuery().Where(p => p.Id == id);

    private static Error NotFound() => Error.NotFound(Error.Codes.NotFound, "Terceiro não encontrado.");

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
