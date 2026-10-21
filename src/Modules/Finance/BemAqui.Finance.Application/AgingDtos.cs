namespace BemAqui.Finance.Application;

/// <summary>Faixa de atraso (FIN-06a CA 1): rótulo do PRD ("0–30", "31–60", "61–90", "90+") e o saldo vencido.</summary>
public sealed record AgingBucket(string Label, int FromDays, int? ToDays, decimal Total, int Count);

/// <summary>Cliente no aging (CA 2): saldo vencido por faixa, total, maior atraso e quantidade de títulos.</summary>
public sealed record AgingCustomer(Guid PartyId, string PartyName, IReadOnlyList<decimal> Buckets, decimal Total, int MaxDaysLate, int Count, decimal? AvgDelayDays = null, decimal? PctPaidLate = null);

/// <summary>Vencido por projeto (FIN-06b CA 1); sem projeto, a linha "Sem projeto".</summary>
public sealed record AgingProject(Guid? ProjectId, string? ProjectNumber, string? ProjectName, Guid? PartyId, string? PartyName, IReadOnlyList<decimal> Buckets, decimal Total, int MaxDaysLate, int Count);

public sealed record AgingProjectReport(DateOnly Today, decimal Total, IReadOnlyList<AgingProject> Rows);

/// <summary>Títulos do terceiro (FIN-06b CA 5): aberto, vencido e pago nos últimos 12 meses, a receber e a pagar.</summary>
public sealed record PartyFinanceTotals(decimal Open, decimal Overdue, decimal PaidLast12m);

public sealed record PartyFinanceSummary(PartyFinanceTotals Receivables, PartyFinanceTotals Payables, BemAqui.Finance.Contracts.CustomerPaymentStats PaymentStats);

public sealed record AgingReport(DateOnly Today, decimal Total, int Count, int Customers, IReadOnlyList<AgingBucket> Buckets, IReadOnlyList<AgingCustomer> Rows);
