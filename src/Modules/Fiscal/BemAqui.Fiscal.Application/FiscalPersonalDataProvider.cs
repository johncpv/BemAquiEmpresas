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
/// fiscal com guarda legal e nunca é apagada; a anonimização do que puder ser anonimizado é da LGPD-01b. A NF-e de entrada guarda só o
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
