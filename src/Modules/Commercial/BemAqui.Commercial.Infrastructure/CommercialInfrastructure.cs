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
        services.TryAddScoped<QuoteLookups>();
        services.TryAddScoped<QuoteExpiry>();
        services.TryAddScoped<CommercialParametersService>();
        services.TryAddScoped<ICommercialParameters>(sp => sp.GetRequiredService<CommercialParametersService>());
        services.TryAddScoped<QuoteApprovalService>();
        services.TryAddScoped<ContractService>();
        services.TryAddScoped<ProjectService>();
        services.TryAddScoped<IProjects>(sp => sp.GetRequiredService<ProjectService>());
        services.TryAddScoped<IContracts>(sp => sp.GetRequiredService<ProjectService>());
        services.TryAddScoped<CommercialUsage>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPartyUsageProvider, CommercialUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IItemUsageProvider, CommercialUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkCenterUsageProvider, CommercialUsage>());
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

        // Contrato assinado (CA 15): só PDF até 25 MB; o anterior fica no histórico de anexos.
        services.AddAttachmentOwner(ContractService.AttachmentOwner, new AttachmentOwnerRules("projects", ["application/pdf"], 25 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<ICommercialDb>().Contracts.AnyAsync(c => c.Id == id, ct),
            ReadAction: "view", WriteAction: "edit"));
        return services;
    }
}
