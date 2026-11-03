using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>
/// Exportação de Fiscal → Notas de entrada (EXP-05 CA 16): a mesma lista da tela (<see cref="IncomingInvoiceService.ListAsync"/>) com o filtro
/// de situação, as colunas do cartão (número, série, emitente e CNPJ, emissão, total, situação, pedido) e a chave. Sem XML em lote.
/// </summary>
public static class IncomingInvoiceExports
{
    public static readonly IReadOnlyDictionary<IncomingInvoiceStatus, string> StatusLabels = new Dictionary<IncomingInvoiceStatus, string>
    {
        [IncomingInvoiceStatus.Imported] = "Importada",
        [IncomingInvoiceStatus.Checked] = "Conferida",
        [IncomingInvoiceStatus.Received] = "Recebida",
        [IncomingInvoiceStatus.Rejected] = "Recusada",
    };

    private static readonly IReadOnlyDictionary<IncomingInvoiceStatus, string> FilterLabels = new Dictionary<IncomingInvoiceStatus, string>
    {
        [IncomingInvoiceStatus.Imported] = "A conferir",
        [IncomingInvoiceStatus.Checked] = "Conferidas",
        [IncomingInvoiceStatus.Received] = "Recebidas",
        [IncomingInvoiceStatus.Rejected] = "Recusadas",
    };

    public static readonly ExportSpec<IncomingInvoiceSummary> Spec = new ExportSpec<IncomingInvoiceSummary>("IncomingInvoice", "notas-de-entrada", "Notas de entrada")
        .Text("Número", i => i.Number.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .Text("Série", i => i.Series.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .Text("Chave", i => i.AccessKey).NotInPdf()
        .Date("Emissão", i => DateOnly.FromDateTime(BusinessTime.ToBusiness(i.IssuedAt).DateTime))
        .Text("Emitente", i => i.IssuerName)
        .Text("CNPJ", i => BrazilianDocuments.Format(i.IssuerDocument))
        .Money("Total", i => i.Total)
        .Text("Pedido", i => i.HasOrder ? "Com pedido" : i.WithoutOrder ? "Sem pedido" : null)
        .Text("Situação", i => StatusLabels.GetValueOrDefault(i.Status, i.Status.ToString()));

    public static async Task<ExportRows<IncomingInvoiceSummary>> RowsAsync(IncomingInvoiceService service, IncomingInvoiceStatus? status, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(service);
        var list = await service.ListAsync(status, new PageRequest(1, Math.Max(1, max)), ct);
        return new ExportRows<IncomingInvoiceSummary>(list.Total, list.Items);
    }

    public static IReadOnlyList<ExportFilter> Filters(IncomingInvoiceStatus? status) => [new("Situação", status is { } s ? FilterLabels.GetValueOrDefault(s) : null)];
}
