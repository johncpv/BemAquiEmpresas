namespace BemAqui.Finance.Application;

/// <summary>Faixa de atraso (FIN-06a CA 1): rótulo do PRD ("0–30", "31–60", "61–90", "90+") e o saldo vencido.</summary>
public sealed record AgingBucket(string Label, int FromDays, int? ToDays, decimal Total, int Count);

/// <summary>Cliente no aging (CA 2): saldo vencido por faixa, total, maior atraso e quantidade de títulos.</summary>
public sealed record AgingCustomer(Guid PartyId, string PartyName, IReadOnlyList<decimal> Buckets, decimal Total, int MaxDaysLate, int Count);

public sealed record AgingReport(DateOnly Today, decimal Total, int Count, int Customers, IReadOnlyList<AgingBucket> Buckets, IReadOnlyList<AgingCustomer> Rows);
