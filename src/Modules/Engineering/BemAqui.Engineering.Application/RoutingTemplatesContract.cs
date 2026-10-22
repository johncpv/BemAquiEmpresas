using BemAqui.Engineering.Contracts;
using BemAqui.Engineering.Domain;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Engineering.Application;

/// <summary>Implementação de <see cref="IRoutingTemplates"/> (CA 13): só as tabelas do Engineering.</summary>
public sealed class RoutingTemplatesContract(IEngineeringDb db) : IRoutingTemplates
{
    public async Task<IReadOnlyList<RoutingTemplateInfo>> ActiveAsync(CancellationToken cancellationToken = default) =>
        await InfosAsync(db.RoutingTemplates.AsNoTracking().Where(t => t.IsActive), cancellationToken);

    public async Task<IReadOnlyList<RoutingTemplateInfo>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return await InfosAsync(db.RoutingTemplates.AsNoTracking().Where(t => ids.Contains(t.Id)), cancellationToken);
    }

    public async Task<IReadOnlyList<RoutingStepInfo>> StepsAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        (await FindAsync([templateId], cancellationToken)) is [var template, ..] ? template.Steps : [];

    public async Task<RoutingTemplateInfo?> ForItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var templates = db.RoutingTemplates.AsNoTracking().Where(t => t.IsActive && db.RoutingTemplateItems.Any(i => i.ItemId == itemId && i.TemplateId == t.Id));
        return (await InfosAsync(templates, cancellationToken)) is [var template, ..] ? template : null;
    }

    public async Task<IReadOnlyList<WorkCenterStandardHours>> TotalStandardHoursAsync(Guid templateId, CancellationToken cancellationToken = default) =>
        (await StepsAsync(templateId, cancellationToken)).GroupBy(s => s.WorkCenterId).Select(g => new WorkCenterStandardHours(g.Key, g.Sum(s => s.StandardHours))).ToList();

    private async Task<IReadOnlyList<RoutingTemplateInfo>> InfosAsync(IQueryable<RoutingTemplate> query, CancellationToken ct)
    {
        var templates = await query.OrderBy(t => t.Name).ToListAsync(ct);
        var ids = templates.Select(t => t.Id).ToList();
        var steps = (await db.RoutingTemplateSteps.AsNoTracking().Where(s => ids.Contains(s.TemplateId)).ToListAsync(ct)).ToLookup(s => s.TemplateId);
        return templates.Select(t => new RoutingTemplateInfo(t.Id, t.Name, t.ProductType, t.IsActive,
            steps[t.Id].OrderBy(s => s.Sequence).Select(s => new RoutingStepInfo(s.Id, s.Sequence, s.Code, s.Name, s.WorkCenterId, s.StandardHours, s.IsQualityStep, s.RequiresApproval,
                s.Checklist.OrderBy(c => c.Sequence).ToList())).ToList())).ToList();
    }
}

/// <summary>Centro usado em etapa de roteiro não excluído (CAD-03 CA 14): em uso, o centro só inativa.</summary>
public sealed class RoutingWorkCenterUsage(IEngineeringDb db) : IWorkCenterUsageProvider
{
    public async Task<string?> UsageAsync(Guid workCenterId, CancellationToken cancellationToken = default) =>
        await db.RoutingTemplateSteps.AnyAsync(s => s.WorkCenterId == workCenterId && db.RoutingTemplates.Any(t => t.Id == s.TemplateId), cancellationToken) ? "roteiros" : null;
}