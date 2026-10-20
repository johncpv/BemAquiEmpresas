using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Contracts;

/// <summary>Orçamento perdido (COM-01b CA 11), sem consumidor nesta fase; o funil (COM-08) lê a tabela.</summary>
public sealed record QuoteLost(Guid QuoteId, string Reason) : IIntegrationEvent;
