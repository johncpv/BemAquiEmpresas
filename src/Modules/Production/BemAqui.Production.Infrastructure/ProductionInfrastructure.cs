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

        // OS de fabricação (PRD-01): serviço, contrato IProductionOrders e uso de roteiro e de centro.
        services.TryAddScoped<FabricationOrderService>();
        services.TryAddScoped<IProductionOrders, ProductionOrdersContract>();
        services.TryAddScoped<FabricationUsage>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.Engineering.Contracts.IRoutingTemplateUsageProvider, FabricationUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkCenterUsageProvider, FabricationUsage>());

        // Kanban (PRD-02): quadro, aviso depois do commit (o Host troca o envio pelo SignalR) e o job "OS atrasada".
        services.TryAddScoped<ProductionBoardService>();
        services.TryAddScoped<ProductionBoardNotifier>();
        services.TryAddSingleton<IProductionBoardPush, NoProductionBoardPush>();
        services.TryAddScoped<LateOrdersNotifier>();

        // App do operador e apontamento (PRD-03a): fila, comandos, supervisor; o bloqueio por BOM liga na ENG-02 (IBomGate).
        services.TryAddScoped<LaborWorkflow>();
        // Custo de mão de obra (PRD-04): snapshot no fechamento, correção, recálculo, consultas de custo e a recomposição (D3).
        services.TryAddScoped<LaborCosting>();
        services.TryAddScoped<LaborCostService>();
        services.TryAddScoped<LaborCostBackfill>();
        // Parâmetros da Produção (decisões do PO de 22/10/2026): custo-hora por apontamento e limite do apontamento aberto.
        services.TryAddScoped<ProductionParametersService>();
        services.AddAuditableEntity<ProductionParameter>("settings");
        services.TryAddScoped<ShopfloorService>();
        services.TryAddScoped<SupervisorLaborService>();
        services.TryAddScoped<IBomGate, NoBomGate>();
        services.AddAuditableEntity<LaborEntry>("production");
        services.AddAuditableEntity<StepPause>("production");

        // Dono ProductionOrder (ADR 0019 §7 e adendo de 21/10/2026, D16): fabricação com pdf, png, jpg, dwg e dxf até 25 MB;
        // instalação continua com png, jpg e pdf até 10 MB (CA 12).
        services.AddAttachmentOwner(InstallationOrderService.AttachmentOwner, new AttachmentOwnerRules(
            "production", [.. InstallationTypes, "application/acad", "application/dxf"], 25 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IProductionDb>().ProductionOrders.AnyAsync(o => o.Id == id, ct),
            ReadAction: "view", WriteAction: "edit", CheckFile: CheckOrderFileAsync));
        return services;
    }

    private static readonly string[] InstallationTypes = ["application/pdf", "image/png", "image/jpeg"];

    /// <summary>Regra do anexo pelo tipo da OS (CA 12): a de instalação só aceita png, jpg e pdf até 10 MB.</summary>
    private static async Task<BemAqui.SharedKernel.Error?> CheckOrderFileAsync(IServiceProvider sp, Guid id, AttachmentFile file, CancellationToken ct)
    {
        var type = await sp.GetRequiredService<IProductionDb>().ProductionOrders.AsNoTracking().Where(o => o.Id == id).Select(o => o.Type).FirstAsync(ct);
        if (type != ProductionOrderType.Installation)
        {
            return null;
        }

        if (!InstallationTypes.Contains(file.ContentType))
        {
            return BemAqui.SharedKernel.Error.Validation("attachment.type_not_allowed", "Tipo de arquivo não permitido aqui. Envie PDF, PNG, JPEG.");
        }

        return file.Size > 10 * 1024 * 1024 ? BemAqui.SharedKernel.Error.Validation("attachment.size", "O arquivo precisa ter até 10 MB.") : null;
    }
}
