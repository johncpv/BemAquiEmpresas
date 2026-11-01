using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Commercial.Infrastructure;

public static class CommercialInfrastructure
{
    /// <summary>Orçamentos (COM-01a), contratos e projetos (COM-04a): serviços, parâmetros, uso dos cadastros, histórico e anexos.</summary>
    public static IServiceCollection AddCommercialInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<QuoteService>();
        // PLN-05: simulação de prazo do orçamento pelo motor do Production (IProductionSchedule), sem gravar.
        services.TryAddScoped<QuoteScheduleSimulationService>();
        // Resumo do funil para o painel do proprietário (DSH-01a, ADR 0028 §1).
        services.TryAddScoped<IQuoteFunnel, QuoteFunnelContract>();
        services.TryAddScoped<QuoteLookups>();
        services.TryAddScoped<QuoteExpiry>();
        // PDF e envio (COM-03): anexos do orçamento por revisão; o PDF gerado sai como anexo do sistema.
        services.TryAddSingleton<BemAqui.BuildingBlocks.Pdf.IPdfRenderer, BemAqui.BuildingBlocks.Pdf.QuestPdfRenderer>();
        services.TryAddScoped<BemAqui.Commercial.Application.Documents.QuoteDocumentService>();
        services.AddAuditableEntity<QuoteDispatch>("quotes");
        // LGPD-01a: onde o titular aparece no Comercial (documentos e envios por e-mail).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Privacy.IPersonalDataProvider, CommercialPersonalDataProvider>());
        services.AddAttachmentOwner(BemAqui.Commercial.Application.Documents.QuoteDocumentService.OwnerType, new AttachmentOwnerRules("quotes",
            ["application/pdf", "image/png", "image/jpeg", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/acad", "application/dxf"],
            25 * 1024 * 1024, OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<ICommercialDb>().Quotes.AnyAsync(q => q.Id == id, ct)));
        services.TryAddScoped<CommercialParametersService>();
        services.TryAddScoped<ICommercialParameters>(sp => sp.GetRequiredService<CommercialParametersService>());
        services.TryAddScoped<QuoteApprovalService>();
        services.TryAddScoped<ContractService>();
        services.TryAddScoped<ProjectService>();
        // Orçado × realizado e margem (DSH-02): um cálculo só para a aba Custos, o relatório e o painel do dono.
        services.TryAddScoped<ProjectMarginService>();
        services.TryAddScoped<IProjectMargins>(sp => sp.GetRequiredService<ProjectMarginService>());
        // EST-03 CA 11: recomposição do custo de material das saídas com projeto anteriores ao consumidor (D3).
        services.TryAddScoped<MaterialCostBackfill>();
        services.TryAddScoped<AmendmentService>();
        // Minha pauta (DSH-04): aditivo aguardando aprovação (projects) e orçamentos vencendo (quotes).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Agenda.IAgendaSource, ProjectsAgendaSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Agenda.IAgendaSource, QuotesAgendaSource>());
        services.TryAddScoped<IProjects>(sp => sp.GetRequiredService<ProjectService>());
        services.TryAddScoped<IContracts>(sp => sp.GetRequiredService<ProjectService>());
        services.TryAddScoped<CommercialUsage>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPartyUsageProvider, CommercialUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IItemUsageProvider, CommercialUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkCenterUsageProvider, CommercialUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.Engineering.Contracts.IRoutingTemplateUsageProvider, CommercialUsage>());
        services.AddAuditableEntity<Quote>("quotes");
        services.AddAuditableEntity<QuoteItem>("quotes");
        services.AddAuditableEntity<QuoteItemCost>("quotes", "view_cost");
        services.AddAuditableEntity<QuoteMilestone>("quotes");
        services.AddAuditableEntity<CommercialParameter>("settings");
        services.AddAuditableEntity<Contract>("projects");
        services.AddAuditableEntity<ContractMilestone>("projects");
        services.AddAuditableEntity<Project>("projects");
        services.AddAuditableEntity<ProjectItem>("projects");
        services.AddAuditableEntity<ProjectBudget>("projects", "view_cost");
        services.AddAuditableEntity<ContractAmendment>("projects");
        services.AddAuditableEntity<ContractAmendmentMilestone>("projects");
        services.AddAuditableEntity<ContractAmendmentReduction>("projects");

        // Contrato assinado (CA 15): só PDF até 25 MB; o anterior fica no histórico de anexos.
        services.AddAttachmentOwner(ContractService.AttachmentOwner, new AttachmentOwnerRules("projects", ["application/pdf"], 25 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<ICommercialDb>().Contracts.AnyAsync(c => c.Id == id, ct),
            ReadAction: "view", WriteAction: "edit"));

        // Documentos do projeto (COM-05 CA 11): tipos e tamanho da ADR 0019 §7.
        services.AddAttachmentOwner(ProjectService.AttachmentOwner, new AttachmentOwnerRules("projects", ["application/pdf", "image/png", "image/jpeg"], 25 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<ICommercialDb>().Projects.AnyAsync(p => p.Id == id, ct),
            ReadAction: "view", WriteAction: "edit"));
        return services;
    }
}
