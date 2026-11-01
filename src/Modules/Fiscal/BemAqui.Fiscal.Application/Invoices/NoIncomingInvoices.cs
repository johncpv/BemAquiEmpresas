using BemAqui.Fiscal.Contracts;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// <see cref="IIncomingInvoices"/> enquanto a FIS-04a (importação da NF-e de entrada) não chega: nenhuma nota importada. O bem antigo
/// usa a referência em texto livre (PAT-01a CA 7); a FIS-04a troca este registro pela leitura de <c>fiscal.incoming_invoices</c>.
/// </summary>
public sealed class NoIncomingInvoices : IIncomingInvoices
{
    public Task<IReadOnlyList<IncomingInvoiceSummary>> SearchAsync(string? term, int limit = 20, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IncomingInvoiceSummary>>([]);

    public Task<IReadOnlyList<IncomingInvoiceSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IncomingInvoiceSummary>>([]);
}
