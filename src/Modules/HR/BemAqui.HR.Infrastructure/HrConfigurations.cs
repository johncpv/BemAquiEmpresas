using System.Text.Json;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.HR.Infrastructure;

internal static class HrSchema
{
    public const string Name = "hr";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}

internal sealed class JobPositionConfiguration : IEntityTypeConfiguration<JobPosition>
{
    public void Configure(EntityTypeBuilder<JobPosition> b)
    {
        b.ToTable("job_positions", HrSchema.Name);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.Cbo).HasMaxLength(6);
        b.Property(e => e.MonthlyHours).HasPrecision(6, 2);
        b.Property(e => e.SalaryMin).HasPrecision(18, 2);
        b.Property(e => e.SalaryMax).HasPrecision(18, 2);
        b.Property(e => e.ReferenceSalary).HasPrecision(18, 2);
        b.Property(e => e.HourlyCost).HasPrecision(18, 6);
        b.HasOne<WorkSchedule>().WithMany().HasForeignKey(e => e.WorkScheduleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Name).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName("ix_job_positions_name");
    }
}

internal sealed class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> b)
    {
        b.ToTable("work_schedules", HrSchema.Name);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.WeeklyHours).HasPrecision(5, 2);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        // Dias em jsonb: [{ "weekday": 1, "start": "08:00", "end": "17:48", "breakMinutes": 60 }].
        b.Property(e => e.Days).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, HrSchema.Json),
            s => JsonSerializer.Deserialize<List<ScheduleDay>>(s, HrSchema.Json) ?? new List<ScheduleDay>(),
            new ValueComparer<List<ScheduleDay>>((x, y) => x!.SequenceEqual(y!), v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode())), v => v.ToList()));
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_work_schedules_seed_key");
    }
}

internal sealed class LaborUnionConfiguration : IEntityTypeConfiguration<LaborUnion>
{
    public void Configure(EntityTypeBuilder<LaborUnion> b)
    {
        b.ToTable("unions", HrSchema.Name);
        b.Property(e => e.Name).HasMaxLength(150);
        b.Property(e => e.Document).HasMaxLength(14);
    }
}
