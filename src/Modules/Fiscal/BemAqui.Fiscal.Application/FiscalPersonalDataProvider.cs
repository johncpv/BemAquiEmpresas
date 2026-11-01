using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application;

/// <summary>
/// Fiscal no arquivo do titular (LGPD-01a CA 8, ADR 0037 §2): as NF-e de saída (<c>fiscal.outgoing_invoices</c>) em que o titular é o
/// destinatário, achadas pelo terceiro da nota (<c>party_id</c>) ou pelo documento copiado (CPF do titular). Sai o que identifica a nota
/// (número, série, chave, data, valor e situação) e o destinatário que a nota guarda (nome, documento, IE, e-mail, telefone e endereço); a
/// cópia do e-mail de outras pessoas sai mascarada. Rascunho descartado também sai, porque o dado continua guardado. A nota é documento
/// fiscal com guarda legal e nunca é apagada; o que pode ser anonimizado está em <see cref="RunAsync"/> (LGPD-01b). A NF-e de entrada guarda só o
/// emitente (fornecedor PJ) e não tem <c>[PersonalData]</c>.
/// </summary>
[PersonalDataCovers(typeof(OutgoingInvoice))]
public sealed class FiscalPersonalDataProvider(IFiscalDb db) : IPersonalDataProvider
{
    public string Module => "fiscal";

    public string Title => "Fiscal";

    public async Task<PersonalDataSection?> ExportAsync(PersonalDataExportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var party = subject.PartyId;
        var document = string.IsNullOrWhiteSpace(subject.Document) ? null : subject.Document;
        if (party is null && document is null)
        {
            return null;
        }

        var invoices = await db.OutgoingInvoices.AsNoTracking()
            .Where(i => (party != null && i.PartyId == party) || (document != null && i.RecipientDocument == document))
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        var records = invoices.Select(i => PersonalDataRecord.Of(i.AuthorizedAt ?? i.CreatedAt,
            ("Tipo", i.Type.ToString()),
            ("Número", i.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Série", i.Series?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Chave de acesso", i.AccessKey),
            ("Data da nota", i.IssuedOn is { } issued ? PersonalDataMask.Date(issued) : PersonalDataMask.At(i.CreatedAt)),
            ("Valor total", PersonalDataMask.Money(i.TotalAmount)),
            ("Situação", i.DiscardedAt is null ? i.Status.ToString() : "Rascunho descartado"),
            ("Romaneio", i.ShipmentNumber),
            ("Destinatário", i.RecipientName),
            ("Documento", string.IsNullOrWhiteSpace(i.RecipientDocument) ? null : BrazilianDocuments.Format(i.RecipientDocument)),
            ("Inscrição estadual", i.RecipientStateRegistration),
            ("E-mail", i.RecipientEmail),
            ("Telefone", i.RecipientPhone),
            ("Endereço", Address(i)),
            ("Cópia do e-mail", PersonalDataMask.Recipients(i.EmailCc, subject.Emails)),
            ("Enviada ao cliente em", PersonalDataMask.At(i.EmailedAt)))).ToList();

        return new PersonalDataSection(Module, Title,
            [new PersonalDataBlock("Notas fiscais de saída em que é o destinatário", "fiscal.outgoing_invoices", records)],
            Note: records.Count == 0 ? null : "Documento fiscal tem guarda legal: a nota não é apagada.");
    }

    /// <summary>Data da última NF-e emitida para o titular (CA 3): a base do fim do exercício.</summary>
    public async Task<IReadOnlyDictionary<string, DateOnly>> RetentionAnchorsAsync(PersonalDataSubject subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var issued = (await InvoicesAsync(subject, true, cancellationToken)).Where(IsIssued).Select(IssueDate).ToList();
        return issued.Count == 0 ? new Dictionary<string, DateOnly>() : new Dictionary<string, DateOnly> { [RetentionAnchors.LastDocument] = issued.Max() };
    }

    public Task<PersonalDataAnonymizationPlan?> PlanAnonymizationAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, false, cancellationToken);

    public Task<PersonalDataAnonymizationPlan?> AnonymizeAsync(PersonalDataAnonymizationContext context, CancellationToken cancellationToken = default) =>
        RunAsync(context, true, cancellationToken);

    /// <summary>
    /// LGPD-01b CA 2, 4 e 9. A NF-e emitida (autorizada, cancelada ou denegada) é documento fiscal com guarda legal: continua existindo, com
    /// número, chave, valores e datas, e o XML e o DANFE guardados ficam. Nela, o nome, o documento e a IE do destinatário seguem o grupo
    /// <c>identification</c> do terceiro e o endereço, o grupo <c>address</c>, os dois contados do fim do exercício da data da nota. O e-mail e o
    /// telefone do destinatário (opcionais no XML) e o endereço do titular na cópia do e-mail seguem o grupo de contato (pela tabela, saem já).
    /// O rascunho descartado não tem obrigação: o destinatário inteiro sai. A nota em andamento (rascunho, processando, contingência ou
    /// rejeitada à espera da correção) ainda pode ser emitida e fica retida até o desfecho.
    /// </summary>
    private async Task<PersonalDataAnonymizationPlan?> RunAsync(PersonalDataAnonymizationContext context, bool apply, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var subject = context.Subject;
        var invoices = await InvoicesAsync(subject, false, ct);
        if (invoices.Count == 0)
        {
            return null;
        }

        var marker = context.Marker;
        var plan = new PersonalDataPlanBuilder(Module, Title);
        var issued = invoices.Where(IsIssued).ToList();
        var discarded = invoices.Where(i => i.DiscardedAt is not null).ToList();
        var ongoing = invoices.Except(issued).Except(discarded).ToList();

        plan.Keep("Notas fiscais de saída emitidas: mantidas, com número, chave, valores, datas, XML e DANFE", "fiscal.outgoing_invoices", issued.Count);
        foreach (var group in issued.GroupBy(i => context.Decide(PersonalDataSubjectKind.Party, RetentionGroups.Identification, IssueDate(i))))
        {
            if (apply && !group.Key.Retained)
            {
                foreach (var i in group)
                {
                    RemoveIdentity(i, marker);
                }
            }

            plan.Fields(group.Key, "Nome, CPF e inscrição estadual do destinatário nas notas emitidas", "fiscal.outgoing_invoices", group.Count());
        }

        foreach (var group in issued.GroupBy(i => context.Decide(PersonalDataSubjectKind.Party, RetentionGroups.Address, IssueDate(i))))
        {
            if (apply && !group.Key.Retained)
            {
                foreach (var i in group)
                {
                    RemoveAddress(i);
                }
            }

            plan.Fields(group.Key, "Endereço do destinatário nas notas emitidas", "fiscal.outgoing_invoices", group.Count());
        }

        var contact = context.Decide(context.ContactGroup);
        var withContact = issued.Where(i => i.RecipientEmail is not null || i.RecipientPhone is not null || PersonalDataMarker.HasAny(i.EmailCc, subject.Emails)).ToList();
        if (apply && !contact.Retained)
        {
            withContact.ForEach(i => RemoveContact(i, subject.Emails, marker));
        }

        plan.Fields(contact, "E-mail e telefone do destinatário e cópia do e-mail nas notas emitidas", "fiscal.outgoing_invoices", withContact.Count);

        if (apply)
        {
            foreach (var i in discarded)
            {
                RemoveIdentity(i, marker);
                RemoveAddress(i);
                RemoveContact(i, subject.Emails, marker);
            }
        }

        plan.Anonymize("Destinatário dos rascunhos de nota descartados", "fiscal.outgoing_invoices", discarded.Count);
        plan.Fields(new RetentionDecision(true, null, "nota fiscal em andamento (emita ou descarte o rascunho)"), "Destinatário das notas em andamento",
            "fiscal.outgoing_invoices", ongoing.Count);

        if (apply)
        {
            await db.SaveChangesAsync(ct);
        }

        var built = plan.Build();
        return built.Items.Count == 0 ? null : built;
    }

    /// <summary>As notas do titular: pelo terceiro da nota ou pelo CPF copiado (o funcionário que comprou como pessoa física).</summary>
    private async Task<List<OutgoingInvoice>> InvoicesAsync(PersonalDataSubject subject, bool readOnly, CancellationToken ct)
    {
        var party = subject.PartyId;
        var document = string.IsNullOrWhiteSpace(subject.Document) ? null : subject.Document;
        if (party is null && document is null)
        {
            return [];
        }

        var query = db.OutgoingInvoices.Where(i => (party != null && i.PartyId == party) || (document != null && i.RecipientDocument == document));
        return await (readOnly ? query.AsNoTracking() : query).ToListAsync(ct);
    }

    /// <summary>Nota com número na SEFAZ: autorizada, cancelada ou denegada (a denegada também é guardada).</summary>
    private static bool IsIssued(OutgoingInvoice i) =>
        i.DiscardedAt is null && i.Status is OutgoingInvoiceStatus.Authorized or OutgoingInvoiceStatus.Cancelled or OutgoingInvoiceStatus.Denied;

    private static DateOnly IssueDate(OutgoingInvoice i) =>
        i.IssuedOn ?? DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(i.AuthorizedAt ?? i.CreatedAt).DateTime);

    private static void RemoveIdentity(OutgoingInvoice i, string marker)
    {
        i.RecipientName = i.RecipientName is null ? null : marker;
        i.RecipientDocument = null;
        i.RecipientStateRegistration = null;
    }

    private static void RemoveAddress(OutgoingInvoice i)
    {
        i.RecipientStreet = null;
        i.RecipientNumber = null;
        i.RecipientComplement = null;
        i.RecipientDistrict = null;
        i.RecipientPostalCode = null;
    }

    private static void RemoveContact(OutgoingInvoice i, IReadOnlyCollection<string> emails, string marker)
    {
        i.RecipientEmail = null;
        i.RecipientPhone = null;
        i.EmailCc = PersonalDataMarker.Recipients(i.EmailCc, emails, marker);
    }

    private static string? Address(OutgoingInvoice i)
    {
        var parts = new[]
        {
            string.Join(", ", new[] { i.RecipientStreet, i.RecipientNumber, i.RecipientComplement }.Where(p => !string.IsNullOrWhiteSpace(p))),
            i.RecipientDistrict,
            string.Join("/", new[] { i.RecipientCityName, i.RecipientState }.Where(p => !string.IsNullOrWhiteSpace(p))),
            i.RecipientPostalCode,
        }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return parts.Count == 0 ? null : string.Join(" - ", parts);
    }
}
