using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Production.Infrastructure;

public static class ProductionInfrastructure
{
    /// <summary>Centros de trabalho e calendário (CAD-03): serviços, contratos de centros e de capacidade, seed e histórico.</summary>
    public static IServiceCollection AddProductionInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<CalendarService>();
        services.TryAddScoped<WorkCenterService>();
        services.TryAddScoped<WorkCentersContract>();
        services.TryAddScoped<IWorkCenters>(sp => sp.GetRequiredService<WorkCentersContract>());
        services.TryAddScoped<IWorkCenterCapacity>(sp => sp.GetRequiredService<WorkCentersContract>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, CalendarSeeder>());
        services.AddAuditableEntity<WorkCenter>("production");
        services.AddAuditableEntity<Calendar>("production");
        services.AddAuditableEntity<CalendarExceptionDay>("production");
        return services;
    }
}
