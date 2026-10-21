using BemAqui.Commercial.Domain;

namespace BemAqui.Commercial.Application;

// Funil (COM-08): só valores de preço, nunca custo ou margem (CA 8, D7).

/// <summary>Situação da revisão corrente no funil, com o parâmetro da lista para o drill-down (CA 5).</summary>
public sealed record FunnelStage(QuoteStatus Status, string Situation, int Count, decimal Amount);

/// <summary>Aprovados ÷ decididos, em % com uma casa; nulo sem orçamento decidido (CA 2).</summary>
public sealed record FunnelConversion(decimal? ByCount, decimal? ByAmount, int Decided);

public sealed record FunnelLossReason(QuoteLostReason Reason, int Count, decimal Amount, decimal Pct);

public sealed record FunnelOtherLoss(Guid Id, string Number, int Revision, string? Detail, decimal TotalPrice);

public sealed record FunnelExpiring(Guid Id, string Number, int Revision, Guid PartyId, string PartyName, decimal TotalPrice, DateOnly ValidUntil, int DaysLeft);

public sealed record FunnelAuthor(Guid Id, string Name);

public sealed record QuoteFunnel(
    DateOnly From, DateOnly To, Guid? AuthorId, IReadOnlyList<FunnelStage> Stages, FunnelConversion Conversion, IReadOnlyList<FunnelLossReason> LossReasons,
    IReadOnlyList<FunnelOtherLoss> OtherLosses, IReadOnlyList<FunnelExpiring> Expiring, IReadOnlyList<FunnelAuthor> Authors);
