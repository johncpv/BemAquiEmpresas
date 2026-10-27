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
        // Dias úteis do calendário padrão para os outros módulos (DSH-01a CA 9; a RH-01c reaproveita).
        services.TryAddScoped<IBusinessCalendar, BusinessCalendarContract>();
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
        // Produto acabado na conclusão (PRD-06a2): entrada, séries, lotes e reservas pelos contratos do Inventory, na transação da OS.
        services.TryAddScoped<FinishedGoods>();
        services.TryAddScoped<IProductionOrders, ProductionOrdersContract>();
        services.TryAddScoped<FabricationUsage>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.Engineering.Contracts.IRoutingTemplateUsageProvider, FabricationUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkCenterUsageProvider, FabricationUsage>());

        // Kanban (PRD-02): quadro, aviso depois do commit (o Host troca o envio pelo SignalR) e o job "OS atrasada".
        services.TryAddScoped<ProductionBoardService>();
        services.TryAddScoped<ProductionBoardNotifier>();
        services.TryAddSingleton<IProductionBoardPush, NoProductionBoardPush>();
        services.TryAddScoped<LateOrdersNotifier>();

        // App do operador e apontamento (PRD-03a): fila, comandos, supervisor; o bloqueio por BOM (IBomGate) ligou na ENG-02.
        services.TryAddScoped<LaborWorkflow>();
        // Custo de mão de obra (PRD-04): snapshot no fechamento, correção, recálculo, consultas de custo e a recomposição (D3).
        services.TryAddScoped<LaborCosting>();
        services.TryAddScoped<LaborCostService>();
        services.TryAddScoped<LaborCostBackfill>();
        // Drill-down de horas e NC do projeto para o Commercial (DSH-02 CA 12 e 14).
        services.TryAddScoped<IProjectProductionCosts, ProjectProductionCostsContract>();
        // Parâmetros da Produção (decisões do PO de 22/10/2026): custo-hora por apontamento e limite do apontamento aberto.
        services.TryAddScoped<ProductionParametersService>();
        services.AddAuditableEntity<ProductionParameter>("settings");
        services.TryAddScoped<ShopfloorService>();
        services.TryAddScoped<SupervisorLaborService>();
        // ENG-02 CA 16 (D12): a etapa depois da primeira só inicia ou conclui com a BOM da OS liberada (IBoms da Engenharia).
        services.TryAddScoped<IBomGate, EngineeringBomGate>();
        services.AddAuditableEntity<LaborEntry>("production");
        services.AddAuditableEntity<StepPause>("production");

        // Qualidade e não conformidade (PRD-05): fila e decisão separadas da supervisão (ADR 0026 §4), NC com anexos.
        services.TryAddScoped<QualityService>();
        services.TryAddScoped<NonConformityService>();

        // Minha pauta (DSH-04): qualidade para decidir, OS atrasadas e NC sob responsabilidade; o "Sem custo-hora" por cargo para o RH.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Agenda.IAgendaSource, ProductionAgendaSource>());
        services.TryAddScoped<ILaborCostGaps, LaborCostGapsContract>();
        services.AddAuditableEntity<NonConformity>("production");
        services.AddAuditableEntity<QualityDecision>("production");

        // Dono NonConformity (ADR 0019 §7 adendo): fotos e evidências png, jpg e pdf até 10 MB; ler com production.view, gravar com
        // production.edit ou production.approve_quality; encerrada não recebe anexo. O limite de envios por hora é o geral (429).
        services.AddAttachmentOwner(NonConformityService.AttachmentOwner, new AttachmentOwnerRules(
            "production", InstallationTypes, 10 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<NonConformityService>().ExistsAsync(id, ct),
            ReadAction: "view", WriteAction: "edit", CanAttach: async (sp, id, ct) => await sp.GetRequiredService<NonConformityService>().CanAttachAsync(id, ct),
            AlternateWriteAction: "approve_quality"));

        // Dono ProductionOrder (ADR 0019 §7 e adendo de 21/10/2026, D16): fabricação com pdf, png, jpg, dwg e dxf até 25 MB;
        // instalação continua com png, jpg e pdf até 10 MB (CA 12).
        services.AddAttachmentOwner(InstallationOrderService.AttachmentOwner, new AttachmentOwnerRules(
            "production", [.. InstallationTypes, "application/acad", "application/dxf"], 25 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IProductionDb>().ProductionOrders.AnyAsync(o => o.Id == id, ct),
            ReadAction: "view", WriteAction: "edit", CheckFile: CheckOrderFileAsync));

        // Planejamento finito (PLN-01a, ADR 0029): motor puro, escritor único do plano (fora da auditoria), leitura e simulação pelo
        // IProductionSchedule e o recálculo manual e de hora em hora.
        services.TryAddScoped<ScheduleInputBuilder>();
        services.TryAddScoped<ScheduleService>();
        // PLN-01b: pedido de recálculo por evento (depois do commit, agrupado pela marca "pendente") e a previsão de entrega de cada projeto.
        services.TryAddScoped<IScheduleTrigger, ScheduleRecalculationTrigger>();
        services.TryAddScoped<ProjectForecastBuilder>();
        services.TryAddScoped<IProductionSchedule, ProductionScheduleContract>();
        services.TryAddScoped<PlanningService>();
        // PLN-02: fila de prioridade dos projetos (auditada), mantida no recálculo e no "Salvar ordem"; IPlanningQueue para o motor, a TV e a PLN-05.
        services.TryAddScoped<PlanningQueueStore>();
        services.TryAddScoped<IPlanningQueue>(sp => sp.GetRequiredService<PlanningQueueStore>());
        services.TryAddScoped<PlanningQueueService>();
        services.AddAuditableEntity<PlanningQueueEntry>("production");
        services.AddAuditableEntity<PlanningQueueState>("production");
        // PLN-04: ocupação da fábrica por semana e centro (lê o plano gravado e a capacidade agora); IFactoryLoad para os painéis (DSH-01b, DSH-03).
        services.TryAddScoped<FactoryLoadService>();
        services.TryAddScoped<IFactoryLoad>(sp => sp.GetRequiredService<FactoryLoadService>());
        // DSH-03: contratos de leitura do painel do supervisor (quadro compacto, qualidade e NC, indicadores), compostos pelo módulo Dashboard.
        services.TryAddScoped<IProductionBoard, ProductionBoardContract>();
        services.TryAddScoped<IQualityOverview, QualityOverviewContract>();
        services.TryAddScoped<IProductionIndicators, ProductionIndicatorsContract>();

        // PLN-03: cronograma por projeto e por centro (lê o plano gravado, a fila e a capacidade agora).
        services.TryAddScoped<GanttService>();

        // Expedição (PRD-06b): romaneio, a saída síncrona pelo IStockMovements e o contrato IShipments (Commercial, Fase 5 e EST-04).
        services.TryAddScoped<ShipmentService>();
        services.TryAddScoped<IShipments, ShipmentsContract>();
        services.AddAuditableEntity<Shipment>("production");
        services.AddAuditableEntity<ShipmentLine>("production");
        services.AddAuditableEntity<ShipmentLineSerial>("production");

        // Dono Shipment (ADR 0019 §7 adendo de 21/10/2026): o PDF gerado e os comprovantes pdf, png e jpg até 10 MB; ler com
        // production.view, gravar com production.create. O limite de envios por hora é o geral (429).
        services.AddAttachmentOwner(ShipmentRules.AttachmentOwner, new AttachmentOwnerRules(
            "production", InstallationTypes, 10 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IProductionDb>().Shipments.AnyAsync(s => s.Id == id, ct),
            ReadAction: "view", WriteAction: "create"));
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
