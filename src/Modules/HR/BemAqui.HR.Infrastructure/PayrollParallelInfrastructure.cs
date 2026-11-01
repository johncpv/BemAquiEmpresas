using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.HR.Infrastructure;

/// <summary>
/// Conferência paralela e virada da folha calculada (FLH-06): o serviço, a auditoria (a conferência e as diferenças só com
/// <c>hr.view_sensitive</c>; o de-para e a virada com <c>hr.view</c>), o <c>folha-calculada.csv</c> no pacote da contadora e o dono de anexo do
/// termo assinado (pdf, png e jpg até 10 MB, ler e gravar com <c>hr.view_sensitive</c>, cada download auditado; só pela rota da conferência,
/// que confere o Proprietário).
/// </summary>
public static class PayrollParallelInfrastructure
{
    public static IServiceCollection AddPayrollParallel(this IServiceCollection services)
    {
        services.TryAddScoped<PayrollParallelService>();
        services.AddAuditableEntity<PayrollParallelRun>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<PayrollParallelDifference>("hr", PositionService.SensitiveAction);
        services.AddAuditableEntity<PayrollItemMapping>("hr");
        services.AddAuditableEntity<PayrollModeChange>("hr");
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountantExportSection, AccountantCalculatedPayrollSection>());
        services.AddAttachmentOwner(PayrollParallelService.TermOwner, new AttachmentOwnerRules("hr", ["application/pdf", "image/png", "image/jpeg"], 10 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IHrDb>().PayrollParallelRuns.AnyAsync(r => r.Id == id, ct),
            ReadAction: PositionService.SensitiveAction, WriteAction: PositionService.SensitiveAction, AuditReads: true, CheckedOnly: true));
        return services;
    }
}
