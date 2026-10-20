using BemAqui.Commercial.Domain;

namespace BemAqui.Commercial.Application;

/// <summary>Revisão do número (COM-02 CA 8), sem custo: a margem vem por <c>/quotes/costs</c> para quem tem <c>quotes.view_cost</c>.</summary>
public sealed record QuoteRevisionSummary(Guid Id, int Revision, QuoteStatus Status, bool IsCurrent, DateTimeOffset CreatedAt, string? CreatedByName, decimal TotalPrice, DateOnly? ValidUntil);

public sealed record ExtendValidityInput(DateOnly ValidUntil, uint Version);
