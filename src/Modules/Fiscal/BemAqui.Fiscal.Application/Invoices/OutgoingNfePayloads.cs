using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using BemAqui.Parties.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// Pedido de NF-e a partir da nota gravada (FIS-03a): emitente da integração, série exclusiva (FIS-02a), natureza da regra, o destinatário
/// copiado, os itens com os tributos já calculados pelo BemAqui (o Emissor transporta e assina) e as informações complementares. Sem
/// duplicatas (FIS-02a: "Duplicatas na NF-e: não", D4): o pagamento vai como "sem pagamento" (a parcela do contrato é só cobrança). Usado na
/// emissão e na conferência de pendentes (reenvio com a mesma chave e o mesmo payload).
/// </summary>
public sealed class OutgoingNfePayloads(IFiscalDb db, IFiscalSettings settings, IParties parties) : IOutgoingNfePayloads
{
    public async Task<NfeIssueRequest?> BuildAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice?.ShipmentId is null)
        {
            return null; // nota da FIS-01 sem itens: a conferência só alerta
        }

        var items = await db.OutgoingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == invoiceId).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        return await BuildAsync(invoice, items, cancellationToken);
    }

    public async Task<NfeIssueRequest> BuildAsync(OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(items);
        var current = await settings.CurrentAsync(cancellationToken);
        var issuer = await db.FiscalIntegrationSettings.AsNoTracking().Select(s => s.IssuerDocument).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var carrier = invoice.CarrierPartyId is { } c ? (await parties.FindAsync(c, cancellationToken))?.LegalName : null;
        return Build(invoice, items, issuer, current.NfeSeries ?? 1, carrier);
    }

    /// <summary>Sem banco (teste de unidade): a forma do pedido a partir da nota.</summary>
    public static NfeIssueRequest Build(OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, string issuerDocument, int series, string? carrierName)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(items);
        var ordered = items.OrderBy(i => i.Sequence).ToList();
        var document = new string((invoice.RecipientDocument ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        var recipient = new NfeRecipient(
            document.Length == 14 ? document : null, document.Length == 11 ? document : null, NfeTextRules.Clean(invoice.RecipientName) ?? string.Empty,
            invoice.RecipientType switch { RecipientType.Contributor => NfeStateRegistration.Taxpayer, RecipientType.Exempt => NfeStateRegistration.Exempt, _ => NfeStateRegistration.NonTaxpayer },
            invoice.RecipientType == RecipientType.Contributor ? invoice.RecipientStateRegistration : null, invoice.RecipientEmail,
            new NfeAddress(NfeTextRules.Clean(invoice.RecipientStreet) ?? string.Empty, NfeTextRules.Clean(invoice.RecipientNumber) ?? "S/N", NfeTextRules.Clean(invoice.RecipientComplement),
                NfeTextRules.Clean(invoice.RecipientDistrict) ?? string.Empty, invoice.RecipientCityCode ?? string.Empty, NfeTextRules.Clean(invoice.RecipientCityName) ?? string.Empty,
                invoice.RecipientState ?? string.Empty, new string((invoice.RecipientPostalCode ?? string.Empty).Where(char.IsAsciiDigit).ToArray()), invoice.RecipientPhone));
        var nfeItems = ordered.Select(Item).ToList();
        var complementary = OutgoingInvoiceRules.ComplementaryForEmitter(invoice.AdditionalInformation, ordered.Select(i => (i.Sequence, i.AdditionalInfo)), invoice.Volumes,
            invoice.GrossWeight, invoice.VolumeSpecies, carrierName);
        return new NfeIssueRequest(issuerDocument, series, (invoice.NatureOfOperation ?? "Venda de produção do estabelecimento").ToUpperInvariant(), NfeOperationType.Outbound, NfePurpose.Normal,
            invoice.FinalConsumer, NfePresence.InPersonOutside, Enum.IsDefined((NfeFreightMode)invoice.FreightMode) ? (NfeFreightMode)invoice.FreightMode : NfeFreightMode.Issuer, recipient, nfeItems,
            [new NfePayment(NfePaymentMethod.NoPayment, 0m)], complementary);
    }

    /// <summary>Tributos do item no formato do pedido: ICMS (com ST e FCP), PIS, COFINS, IPI e IBS/CBS das linhas do cálculo.</summary>
    public static NfeItem Item(OutgoingInvoiceItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        TaxLine? Line(string code) => item.Taxes.FirstOrDefault(t => t.Code == code);
        var icms = Line(TaxCodes.Icms);
        var st = Line(TaxCodes.IcmsSt);
        var fcp = Line(TaxCodes.Fcp);
        var nfeIcms = new NfeIcms(icms?.Cst ?? "41", icms?.Base ?? 0m, icms?.Rate ?? 0m, icms?.Amount ?? 0m,
            StBase: st?.Base, StRate: st?.Rate, StAmount: st?.Amount, FcpBase: fcp?.Base, FcpRate: fcp?.Rate, FcpAmount: fcp?.Amount);
        NfePisCofins PisCofins(string code) => Line(code) is { } l ? new NfePisCofins(l.Cst ?? "01", l.Base, l.Rate, l.Amount) : new NfePisCofins("07", 0m, 0m, 0m);
        var ipi = Line(TaxCodes.Ipi) is { } i ? new NfeIpi(i.Cst ?? "99", i.Base, i.Rate, i.Amount, item.IpiFramework ?? "999") : null;
        var cbs = Line(TaxCodes.Cbs);
        var ibsState = Line(TaxCodes.IbsState);
        var ibsCity = Line(TaxCodes.IbsCity);
        var ibsCbs = cbs is null ? null : new NfeIbsCbs(cbs.Cst ?? "000", item.IbsCbsClassification ?? "000001", cbs.Base,
            ibsState is null ? null : new NfeTaxRate(ibsState.Rate, ibsState.Amount), ibsCity is null ? null : new NfeTaxRate(ibsCity.Rate, ibsCity.Amount), new NfeTaxRate(cbs.Rate, cbs.Amount));
        return new NfeItem(item.Code, NfeTextRules.Clean(item.Description) ?? item.Code, item.Ncm ?? string.Empty, item.Cest, item.Cfop ?? string.Empty, item.Unit, item.Quantity, item.UnitPrice,
            item.GrossAmount, new NfeItemTaxes(item.Origin, nfeIcms, PisCofins(TaxCodes.Pis), PisCofins(TaxCodes.Cofins), ipi, ibsCbs), item.Discount, item.Freight, 0m, item.OtherExpenses);
    }
}
