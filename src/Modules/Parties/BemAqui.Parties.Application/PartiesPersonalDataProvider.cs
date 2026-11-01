using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

/// <summary>
/// Titular terceiro PF ou contato (LGPD-01a CA 1 e 9, ADR 0037 §1): o Parties identifica o titular, responde ao seletor e exporta o cadastro,
/// os endereços e os contatos. Os contatos de um terceiro PF são outras pessoas: saem com o primeiro nome e o e-mail e o telefone
/// mascarados; o terceiro de um contato aparece pelo nome só quando é PJ.
/// </summary>
[PersonalDataCovers(typeof(Party), typeof(PartyContact))]
public sealed class PartiesPersonalDataProvider(IPartiesDb db) : IPersonalDataProvider
{
    public string Module => "parties";

    public string Title => "Terceiros";

    public async Task<PersonalDataSubject?> ResolveAsync(PersonalDataSubjectKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        if (kind == PersonalDataSubjectKind.Party)
        {
            var party = await db.Parties.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.Kind == PartyKind.PF, cancellationToken);
            return party is null ? null : new PersonalDataSubject(kind, id, party.LegalName, Emails(party.Email), party.Document, PartyId: party.Id);
        }

        if (kind == PersonalDataSubjectKind.Contact)
        {
            var contact = await db.PartyContacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            return contact is null ? null : new PersonalDataSubject(kind, id, contact.Name, Emails(contact.Email), PartyId: contact.PartyId);
        }

        return null;
    }

    public async Task<IReadOnlyList<PersonalDataSubjectMatch>> SearchAsync(PersonalDataSubjectKind kind, string term, CancellationToken cancellationToken = default)
    {
        var text = Party.Normalize(term);
        if (text.Length < 2)
        {
            return [];
        }

        var digits = BrazilianDocuments.Normalize(term);
        if (kind == PersonalDataSubjectKind.Party)
        {
            var parties = await db.Parties.AsNoTracking().Where(p => p.Kind == PartyKind.PF && (p.SearchText.Contains(text) || (digits.Length >= 6 && p.Document != null && p.Document.Contains(digits))))
                .OrderBy(p => p.SearchText).Take(20).ToListAsync(cancellationToken);
            return parties.Select(p => new PersonalDataSubjectMatch(kind, p.Id, p.LegalName, p.Document is null ? PersonalDataMask.Email(p.Email) : PersonalDataMask.Cpf(p.Document))).ToList();
        }

        if (kind == PersonalDataSubjectKind.Contact)
        {
            var like = "%" + term.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var contacts = await (from c in db.PartyContacts.AsNoTracking()
                                  join p in db.Parties.AsNoTracking() on c.PartyId equals p.Id
                                  where EF.Functions.ILike(c.Name, like) || (c.Email != null && EF.Functions.ILike(c.Email, like))
                                  orderby c.Name
                                  select new { c.Id, c.Name, c.Email, p.LegalName, p.Kind }).Take(20).ToListAsync(cancellationToken);
            return contacts.Select(c => new PersonalDataSubjectMatch(kind, c.Id, c.Name, (c.Kind == PartyKind.PJ ? c.LegalName + " · " : string.Empty) + PersonalDataMask.Email(c.Email))).ToList();
        }

        return [];
    }

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        return subject.Kind switch
        {
            PersonalDataSubjectKind.Party => await PartyAsync(subject.Id, cancellationToken),
            PersonalDataSubjectKind.Contact => await ContactAsync(subject.Id, cancellationToken),
            _ => null,
        };
    }

    private async Task<PersonalDataSection?> PartyAsync(Guid id, CancellationToken ct)
    {
        var party = await db.Parties.IgnoreQueryFilters().AsNoTracking().Include(p => p.Roles).Include(p => p.Addresses).Include(p => p.Contacts)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (party is null)
        {
            return null;
        }

        var record = PersonalDataRecord.Of(party.CreatedAt,
            ("Nome", party.LegalName), ("Nome fantasia", party.TradeName), ("CPF", party.Document is null ? null : BrazilianDocuments.Format(party.Document)),
            ("E-mail", party.Email), ("Telefone", party.Phone), ("Observações", party.Notes),
            ("Papéis", string.Join(", ", party.Roles.Select(r => RoleLabel(r.Role)))), ("Ativo", PersonalDataMask.YesNo(party.IsActive)),
            ("Excluído", PersonalDataMask.YesNo(party.IsDeleted)), ("Cadastrado em", PersonalDataMask.At(party.CreatedAt)));
        var addresses = party.Addresses.Select(a => PersonalDataRecord.Of(
            ("Tipo", a.Kind switch { AddressKind.Billing => "Cobrança", AddressKind.Delivery => "Entrega", _ => "Obra" }), ("CEP", a.ZipCode), ("Logradouro", a.Street),
            ("Número", a.Number), ("Complemento", a.Complement), ("Bairro", a.District), ("Cidade", a.City), ("UF", a.State), ("Padrão", PersonalDataMask.YesNo(a.IsDefault)))).ToList();
        var contacts = party.Contacts.Select(c => PersonalDataRecord.Of(
            ("Nome", PersonalDataMask.Name(c.Name)), ("Função", c.Role), ("E-mail", PersonalDataMask.Email(c.Email)), ("Telefone", PersonalDataMask.Phone(c.Phone)))).ToList();
        return new PersonalDataSection(Module, Title,
        [
            new PersonalDataBlock("Cadastro", "parties.parties", [record]),
            new PersonalDataBlock("Endereços", "parties.party_addresses", addresses),
            new PersonalDataBlock("Contatos (outras pessoas, mascarados)", "parties.party_contacts", contacts),
        ]);
    }

    private async Task<PersonalDataSection?> ContactAsync(Guid id, CancellationToken ct)
    {
        var contact = await db.PartyContacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return null;
        }

        var party = await db.Parties.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == contact.PartyId).Select(p => new { p.Kind, p.LegalName }).FirstOrDefaultAsync(ct);
        var record = PersonalDataRecord.Of(
            ("Nome", contact.Name), ("Função", contact.Role), ("E-mail", contact.Email), ("Telefone", contact.Phone),
            ("Recebe cobrança", PersonalDataMask.YesNo(contact.ReceivesBilling)),
            ("Contato de", party is null ? null : party.Kind == PartyKind.PJ ? party.LegalName : "Terceiro pessoa física"));
        return new PersonalDataSection(Module, Title, [new PersonalDataBlock("Contato", "parties.party_contacts", [record])]);
    }

    private static string[] Emails(string? email) => string.IsNullOrWhiteSpace(email) ? [] : [email.Trim().ToLowerInvariant()];

    private static string RoleLabel(PartyRoleKind role) => role switch
    {
        PartyRoleKind.Customer => "Cliente",
        PartyRoleKind.Supplier => "Fornecedor",
        PartyRoleKind.Installer => "Instalador",
        _ => "Transportadora",
    };
}
