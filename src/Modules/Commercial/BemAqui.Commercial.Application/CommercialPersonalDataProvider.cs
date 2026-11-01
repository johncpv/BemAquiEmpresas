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

    /// <summary>Data do último contrato ou projeto do terceiro PF (documentos com obrigação; o orçamento não conta), para a retenção (CA 3).</summary>
    public async Task<IReadOnlyDictionary<string, DateOnly>> RetentionAnchorsAsync(PersonalDataSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.Kind != PersonalDataSubjectKind.Party || subject.PartyId is not { } party)
        {
            return new Dictionary<string, DateOnly>();
        }

        var dates = (await db.Contracts.Where(c => c.PartyId == party).Select(c => c.CreatedAt).ToListAsync(cancellationToken))
            .Concat(await db.Projects.Where(p => p.PartyId == party).Select(p => p.CreatedAt).ToListAsync(cancellationToken))
            .Select(at => DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(at).DateTime)).ToList();
        return dates.Count == 0 ? new Dictionary<string, DateOnly>() : new Dictionary<string, DateOnly> { [RetentionAnchors.LastDocument] = dates.Max() };
    }

    public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    /// <summary>
    /// LGPD-01b CA 2 e 9: orçamentos, contratos e projetos ficam (o nome mora no cadastro do terceiro); nos envios do orçamento por e-mail
    /// (<c>commercial.quote_dispatches</c>), o endereço do titular no destinatário e na cópia vira o marcador.
    /// </summary>
    private async Task<PersonalDataAnonymizationPlan?> RunAsync(PersonalDataAnonymizationContext context, bool apply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var plan = new PersonalDataPlanBuilder(Module, Title);
        if (subject.Kind == PersonalDataSubjectKind.Party && subject.PartyId is { } party)
        {
            plan.Keep("Orçamentos: mantidos", "commercial.quotes", await db.Quotes.IgnoreQueryFilters().CountAsync(q => q.PartyId == party, ct))
                .Keep("Contratos: mantidos", "commercial.contracts", await db.Contracts.CountAsync(c => c.PartyId == party, ct))
                .Keep("Projetos: mantidos", "commercial.projects", await db.Projects.CountAsync(p => p.PartyId == party, ct));
        }
        else if (subject.Kind == PersonalDataSubjectKind.Contact)
        {
            plan.Keep("Orçamentos em que é o contato: mantidos (o nome do contato vira o marcador no cadastro)", "commercial.quotes",
                await db.Quotes.IgnoreQueryFilters().CountAsync(q => q.ContactId == subject.Id, ct));
        }

        var dispatches = new Dictionary<Guid, QuoteDispatch>();
        foreach (var email in subject.Emails)
        {
            var pattern = PersonalDataQuery.Contains(email);
            foreach (var d in await db.QuoteDispatches.Where(d => EF.Functions.ILike(d.To, PersonalDataQuery.Exact(email)) || (d.Cc != null && EF.Functions.ILike(d.Cc, pattern))).ToListAsync(ct))
            {
                dispatches[d.Id] = d;
            }
        }

        var decision = context.Decide(context.ContactGroup);
        if (apply && !decision.Retained && dispatches.Count > 0)
        {
            foreach (var d in dispatches.Values)
            {
                var entry = db.Entry(d);
                entry.Property(x => x.To).CurrentValue = PersonalDataMarker.Recipients(d.To, subject.Emails, context.Marker)!;
                entry.Property(x => x.Cc).CurrentValue = PersonalDataMarker.HasAny(d.Cc, subject.Emails) ? PersonalDataMarker.Recipients(d.Cc, subject.Emails, context.Marker) : d.Cc;
            }

            await db.SaveChangesAsync(ct);
        }

        var built = plan.Fields(decision, "Destinatário dos orçamentos enviados por e-mail", "commercial.quote_dispatches", dispatches.Count).Build();
        return built.Items.Count == 0 ? null : built;
    }

    private static PersonalDataRecord Document(string kind, string number, string status, DateTimeOffset at) =>
        PersonalDataRecord.Of(at, ("Tipo", kind), ("Número", number), ("Situação", status), ("Criado em", PersonalDataMask.At(at)));
}
