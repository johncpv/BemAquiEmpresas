namespace BemAqui.BuildingBlocks.Messaging;

/// <summary>Seção <c>Outbox</c>.</summary>
public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    public int BatchSize { get; set; } = 20;

    public int MaxAttempts { get; set; } = 10;

    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Espera após a tentativa <paramref name="attempts"/>: base × 2^(n−1), até o máximo.</summary>
    public TimeSpan BackoffFor(int attempts)
    {
        var factor = Math.Pow(2, Math.Max(0, attempts - 1));
        var delay = TimeSpan.FromTicks((long)Math.Min(BaseDelay.Ticks * factor, MaxDelay.Ticks));
        return delay;
    }
}
