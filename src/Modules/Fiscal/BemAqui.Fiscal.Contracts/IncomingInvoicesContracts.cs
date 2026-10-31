namespace BemAqui.Fiscal.Contracts;

/// <summary>
/// NF-e de entrada para os outros módulos (PAT-01a CA 7): id, número, série, emitente (o fornecedor no <c>IParties</c>, quando
/// identificado), nome do emitente e data de emissão. Sem valor nem itens.
/// </summary>
public sealed record IncomingInvoiceSummary(Guid Id, string Number, string Series, Guid? IssuerPartyId, string IssuerName, DateOnly IssueDate);

/// <summary>
/// Notas de entrada importadas pela FIS-04a, para o seletor da NF de entrada do bem (PAT-01a CA 7, R-PRP-3: o fornecedor e a data
/// de aquisição vêm da nota). Só notas importadas; sem permissão do Fiscal no request (quem chama confere a própria).
/// </summary>
public interface IIncomingInvoices
{
    /// <summary>Busca por número, série ou emitente, das mais novas às mais antigas.</summary>
    Task<IReadOnlyList<IncomingInvoiceSummary>> SearchAsync(string? term, int limit = 20, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IncomingInvoiceSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
