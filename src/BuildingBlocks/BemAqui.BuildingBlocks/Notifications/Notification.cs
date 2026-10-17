namespace BemAqui.BuildingBlocks.Notifications;

/// <summary>Notificação do sino (<c>shared.notifications</c>). Sempre leva à ação (<see cref="Link"/> obrigatório).</summary>
public sealed class Notification
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    public required string Type { get; init; }

    public required string Title { get; init; }

    /// <summary>Rota relativa do app (ex.: <c>/app/finance/payment-orders/…</c>) onde a ação acontece.</summary>
    public required string Link { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; set; }
}
