using BemAqui.BuildingBlocks.Privacy;
using BemAqui.SharedKernel;
using BemAqui.Commercial.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Onde o titular aparece no Comercial (LGPD-01a CA 9, ADR 0037 §2): os orçamentos, contratos e projetos do terceiro PF (ou os orçamentos
/// em que o contato é o contato), só número, tipo, situação e data, sem o conteúdo comercial; e os envios do orçamento por e-mail
/// (<c>commercial.quote_dispatches</c>) a ele, com o destinatário de outras pessoas mascarado.
/// </summary>
[PersonalDataCovers(typeof(QuoteDispatch))]
public sealed class CommercialPersonalDataProvider(ICommercialDb db) : IPersonalDataProvider
{
    public string Module => "commercial";

    public string Title => "Comercial";

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var documents = new List<PersonalDataRecord>();
        if (subject.Kind == PersonalDataSubjectKind.Party && subject.PartyId is { } party)
        {
            documents.AddRange((await db.Quotes.IgnoreQueryFilters().AsNoTracking().Where(q => q.PartyId == party).OrderBy(q => q.CreatedAt).Select(q => new { q.Number, q.Status, q.CreatedAt }).ToListAsync(cancellationToken))
                .Select(q => Document("Orçamento", q.Number, q.Status.ToString(), q.CreatedAt)));
            documents.AddRange((await db.Contracts.AsNoTracking().Where(c => c.PartyId == party).OrderBy(c => c.CreatedAt).Select(c => new { c.Number, c.Status, c.CreatedAt }).ToListAsync(cancellationToken))
                .Select(c => Document("Contrato", c.Number, c.Status.ToString(), c.CreatedAt)));
            documents.AddRange((await db.Projects.AsNoTracking().Where(p => p.PartyId == party).OrderBy(p => p.CreatedAt).Select(p => new { p.Number, p.Status, p.CreatedAt }).ToListAsync(cancellationToken))
                .Select(p => Document("Projeto", p.Number, p.Status.ToString(), p.CreatedAt)));
        }
        else if (subject.Kind == PersonalDataSubjectKind.Contact)
        {
            documents.AddRange((await db.Quotes.IgnoreQueryFilters().AsNoTracking().Where(q => q.ContactId == subject.Id).OrderBy(q => q.CreatedAt).Select(q => new { q.Number, q.Status, q.CreatedAt }).ToListAsync(cancellationToken))
                .Select(q => Document("Orçamento (como contato)", q.Number, q.Status.ToString(), q.CreatedAt)));
        }

        var dispatches = new List<PersonalDataRecord>();
        var seen = new HashSet<Guid>();
        foreach (var email in subject.Emails)
        {
            var pattern = "%" + email.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var rows = await (from d in db.QuoteDispatches.AsNoTracking()
                              join q in db.Quotes.IgnoreQueryFilters().AsNoTracking() on d.QuoteId equals q.Id
                              where EF.Functions.ILike(d.To, PersonalDataQuery.Exact(email)) || (d.Cc != null && EF.Functions.ILike(d.Cc, pattern))
                              orderby d.CreatedAt
                              select new { d.Id, d.To, d.Cc, d.CreatedAt, q.Number }).ToListAsync(cancellationToken);
            dispatches.AddRange(rows.Where(r => seen.Add(r.Id)).Select(r => PersonalDataRecord.Of(r.CreatedAt,
                ("Orçamento", r.Number), ("Destinatário", PersonalDataMask.Recipients(r.To, subject.Emails)), ("Cópia", PersonalDataMask.Recipients(r.Cc, subject.Emails)),
                ("Enviado em", PersonalDataMask.At(r.CreatedAt)))));
        }

        return new PersonalDataSection(Module, Title,
        [
            new PersonalDataBlock("Documentos em que aparece", "commercial.quotes, commercial.contracts, commercial.projects", documents),
            new PersonalDataBlock("Orçamentos enviados por e-mail", "commercial.quote_dispatches", dispatches),
        ]);
    }

    private static PersonalDataRecord Document(string kind, string number, string status, DateTimeOffset at) =>
        PersonalDataRecord.Of(at, ("Tipo", kind), ("Número", number), ("Situação", status), ("Criado em", PersonalDataMask.At(at)));
}
