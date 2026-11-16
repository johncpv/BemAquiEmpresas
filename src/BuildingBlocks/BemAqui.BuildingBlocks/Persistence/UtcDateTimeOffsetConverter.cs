using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BemAqui.BuildingBlocks.Persistence;

/// <summary>
/// Leva todo <see cref="DateTimeOffset"/> para UTC (offset zero) ao gravar e ao usar como parâmetro de consulta. O Npgsql
/// recusa offset diferente de zero em <c>timestamptz</c>; sem isto, um cliente da API que manda "2026-11-13T08:00:00-03:00"
/// recebe 500. O instante não muda, só a representação; a coluna continua <c>timestamptz</c> (sem migration).
/// </summary>
public sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v.ToUniversalTime())
{
    /// <summary>Aplica a conversão a todas as propriedades <see cref="DateTimeOffset"/> (e anuláveis) do modelo.</summary>
    public static void Apply(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }
}
