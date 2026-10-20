using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
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

        // OS de instalação (COM-06).
        services.TryAddScoped<InstallationTemplateService>();
        services.TryAddScoped<InstallationOrderService>();
        services.TryAddScoped<IInstallationOrders, InstallationOrdersContract>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, InstallationTemplateSeeder>());
        services.AddAuditableEntity<ProductionOrder>("production");
        services.AddAuditableEntity<ProductionOrderStep>("production");
        services.AddAuditableEntity<ProductionOrderChecklistItem>("production");
        services.AddAuditableEntity<InstallationTemplateLine>("production");
        services.AddAttachmentOwner(InstallationOrderService.AttachmentOwner, new AttachmentOwnerRules("production", ["application/pdf", "image/png", "image/jpeg"], 10 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IProductionDb>().ProductionOrders.AnyAsync(o => o.Id == id, ct),
            ReadAction: "view", WriteAction: "edit"));
        return services;
    }
}
