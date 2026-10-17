namespace BemAqui.BuildingBlocks.Time;

/// <summary>Fuso de negócio: datas gravadas em UTC, exibidas e agendadas em America/Sao_Paulo.</summary>
public static class BusinessTime
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTimeOffset ToBusiness(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    public static DateOnly Today(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DateOnly.FromDateTime(ToBusiness(clock.GetUtcNow()).DateTime);
    }
}
