using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices.Nfse;

/// <summary>
/// Pedido de NFS-e a partir da nota gravada (FIS-06 CA 3): emitente da integração, a série do RPS, o tomador copiado (o mesmo destinatário da
/// NF-e), o serviço com o ISS, as retenções e o IBS/CBS já calculados pelo BemAqui (o Emissor transporta e assina) e a chave da NF-e do
/// equipamento vinculada (<c>nfeVinculada</c>). O <see cref="Hash"/> é o <c>payload_hash</c> gravado ao entrar em <c>Processing</c>.
/// </summary>
public sealed class NfsePayloads(IFiscalDb db)
{
    private static readonly JsonSerializerOptions Canonical = new()
    {
        Converters = { new CanonicalDecimalConverter(), new JsonStringEnumConverter(allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>O pedido de uma nota gravada (a conferência de pendentes reenvia com a mesma chave e o mesmo payload); nulo se não é NFS-e.</summary>
    public async Task<NfseIssueRequest?> BuildAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is not { Type: InvoiceType.NFSe, Service: not null })
        {
            return null;
        }

        var items = await db.OutgoingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == invoiceId).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        return await BuildAsync(invoice, items, cancellationToken);
    }

    public async Task<NfseIssueRequest> BuildAsync(OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var issuer = await db.FiscalIntegrationSettings.AsNoTracking().Select(s => s.IssuerDocument).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var linked = invoice.LinkedInvoiceId is { } l
            ? await db.OutgoingInvoices.AsNoTracking().Where(i => i.Id == l).Select(i => i.AccessKey).FirstOrDefaultAsync(cancellationToken)
            : null;
        return Build(invoice, items, issuer, linked);
    }

    /// <summary>Sem banco (teste de unidade): a forma do pedido a partir da nota.</summary>
    public static NfseIssueRequest Build(OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, string issuerDocument, string? linkedAccessKey)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(items);
        var service = invoice.Service ?? throw new InvalidOperationException("NFS-e sem o serviço montado.");
        var lines = items.SelectMany(i => i.Taxes).ToList();
        var issBase = lines.Where(t => t.Code == TaxCodes.Iss).Sum(t => t.Base);
        var ibs = service.IbsCbsCst is { Length: > 0 } cst ? new NfseIbsCbs(cst, service.IbsCbsClassification ?? "000001", NfseRules.IbsCbsOperationCode) : null;
        return new NfseIssueRequest(issuerDocument, NfseRules.Series, OutgoingNfePayloads.Recipient(invoice),
            new NfseService(service.ServiceItem, service.MunicipalCode ?? string.Empty, service.Ctn ?? string.Empty, service.Nbs ?? string.Empty, service.Description,
                service.ServiceCityCode ?? string.Empty, NfePayload.Money(invoice.TotalAmount), NfePayload.Money(issBase), NfePayload.Rate(service.IssRate), NfePayload.Money(service.IssAmount),
                service.IssWithheld, NfePayload.Money(service.NetAmount), null, ibs),
            null, linkedAccessKey);
    }

    /// <summary>SHA-256 (hex minúsculo) da forma canônica: o mesmo pedido dá sempre o mesmo hash, com 1 ou 1,00.</summary>
    public static string Hash(NfseIssueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var json = JsonSerializer.Serialize(request, Canonical);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>Decimal sem zeros à direita (1,00 e 1 são o mesmo valor no hash).</summary>
    private sealed class CanonicalDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue((value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture));
    }
}
