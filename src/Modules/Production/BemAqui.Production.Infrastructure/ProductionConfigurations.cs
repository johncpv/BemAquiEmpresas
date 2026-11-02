using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Production.Infrastructure;

internal static class ProductionSchema
{
    public const string Name = "production";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static ValueComparer<List<T>> ListComparer<T>() =>
        new((x, y) => x!.SequenceEqual(y!), v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i!.GetHashCode())), v => v.ToList());
}

internal sealed class WorkCenterConfiguration : IEntityTypeConfiguration<WorkCenter>
{
    public void Configure(EntityTypeBuilder<WorkCenter> b)
    {
        b.ToTable("work_centers", ProductionSchema.Name);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.Line).HasMaxLength(60);
        b.Property(e => e.HoursPerDay).HasPrecision(5, 2);
        // Turnos em jsonb: [{ "start": "07:00", "end": "11:00" }], horário local, sem data.
        b.Property(e => e.Shifts).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, ProductionSchema.Json),
            s => JsonSerializer.Deserialize<List<Shift>>(s, ProductionSchema.Json) ?? new List<Shift>(),
            ProductionSchema.ListComparer<Shift>());
        b.HasOne<Calendar>().WithMany().HasForeignKey(e => e.CalendarId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CalendarConfiguration : IEntityTypeConfiguration<Calendar>
{
    public void Configure(EntityTypeBuilder<Calendar> b)
    {
        b.ToTable("calendars", ProductionSchema.Name);
        b.Property(e => e.Name).HasMaxLength(100);
        // Dias úteis em jsonb: ["Monday", ..., "Friday"].
        b.Property(e => e.WorkingDays).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, ProductionSchema.Json),
            s => JsonSerializer.Deserialize<List<Weekday>>(s, ProductionSchema.Json) ?? new List<Weekday>(),
            ProductionSchema.ListComparer<Weekday>());
        // Um padrão por tenant no MVP.
        b.HasIndex(e => e.IsDefault).IsUnique().HasFilter("is_default").HasDatabaseName("ix_calendars_default");
    }
}

internal sealed class CalendarExceptionConfiguration : IEntityTypeConfiguration<CalendarExceptionDay>
{
    public void Configure(EntityTypeBuilder<CalendarExceptionDay> b)
    {
        b.ToTable("calendar_exceptions", ProductionSchema.Name);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.HoursAvailable).HasPrecision(5, 2);
        b.Property(e => e.Description).HasMaxLength(100);
        b.HasOne<Calendar>().WithMany().HasForeignKey(e => e.CalendarId).OnDelete(DeleteBehavior.Cascade);
        // Uma exceção por calendário e data (CA 9).
        b.HasIndex(e => new { e.CalendarId, e.Date }).IsUnique().HasDatabaseName(CalendarService.DateIndex);
    }
}

/// <summary>
/// <c>production.downtimes</c> (PAT-03b, ADR 0038 §4): uma parada por origem (<c>(source_type, source_id)</c> único, CA 11), índice pelo centro e
/// período para a capacidade.
/// </summary>
internal sealed class DowntimeConfiguration : IEntityTypeConfiguration<Downtime>
{
    public void Configure(EntityTypeBuilder<Downtime> b)
    {
        b.ToTable("downtimes", ProductionSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_downtimes_hours_per_day", "hours_per_day > 0");
            t.HasCheckConstraint("ck_downtimes_period", "planned_end_on >= starts_on");
        });
        b.Property(e => e.Reason).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.SourceType).HasMaxLength(40);
        b.Property(e => e.HoursPerDay).HasPrecision(18, 4);
        b.HasIndex(e => new { e.SourceType, e.SourceId }).IsUnique().HasDatabaseName("ux_downtimes_source");
        b.HasIndex(e => new { e.WorkCenterId, e.StartsOn }).HasDatabaseName("ix_downtimes_work_center_id_starts_on");
    }
}
