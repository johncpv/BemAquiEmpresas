using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Application;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.HR.Infrastructure;

/// <summary>Jornada do seed (CAD-04a CA 16): "44 h — seg a sex 08:00–17:48", pela <c>seed_key</c>, sem recriar a excluída.</summary>
public sealed class WorkScheduleSeeder : ITenantSeeder
{
    public const string SeedKey = "44h-seg-sex";
    public const string Name = "44 h — seg a sex 08:00–17:48";

    public int Order => 40;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IHrDb>();
        if (await db.WorkSchedules.IgnoreQueryFilters().AnyAsync(s => s.SeedKey == SeedKey, cancellationToken) || await db.WorkSchedules.AnyAsync(s => s.Name == Name, cancellationToken))
        {
            return;
        }

        var schedule = new WorkSchedule { SeedKey = SeedKey, Name = Name, CreatedAt = tenantScope.GetRequiredService<TimeProvider>().GetUtcNow() };
        schedule.SetDays(Enumerable.Range(1, 5).Select(day => new ScheduleDay(day, "08:00", "17:48", 60)).ToList());
        db.WorkSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public static class HrInfrastructure
{
    /// <summary>
    /// Cargos, jornadas e sindicatos (CAD-04a): serviços, contrato <see cref="IHourlyCost"/>, seed e histórico. O histórico do
    /// HR só abre com <c>hr.view_sensitive</c> (CA 13).
    /// </summary>
    public static IServiceCollection AddHrInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<PositionService>();
        services.TryAddScoped<IHourlyCost>(sp => sp.GetRequiredService<PositionService>());
        services.TryAddScoped<ScheduleService>();
        services.TryAddScoped<UnionService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, WorkScheduleSeeder>());
        // Funcionários (CAD-04b).
        services.TryAddScoped<EmployeeService>();
        services.TryAddScoped<EmployeesContract>();
        services.TryAddScoped<IEmployees>(sp => sp.GetRequiredService<EmployeesContract>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPositionUsageProvider, EmployeesContract>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IUnionUsageProvider, EmployeesContract>());
        services.AddAuditableEntity<Employee>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<EmployeeHistoryEntry>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<JobPosition>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<WorkSchedule>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<LaborUnion>("hr", PositionService.SensitiveAction);
        return services;
    }
}
