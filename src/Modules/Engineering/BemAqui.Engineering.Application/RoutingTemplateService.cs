using BemAqui.BuildingBlocks.Paging;
using BemAqui.Engineering.Contracts;
using BemAqui.Engineering.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Engineering.Application;

/// <summary>
/// Roteiros de fabricação (ENG-01): cadastro com as etapas, vínculo ao produto, duplicar, inativar e excluir conforme o uso
/// dos <see cref="IRoutingTemplateUsageProvider"/>. O centro e o produto são só ids, validados pelos contratos da Produção e
/// do Estoque (ADR 0006).
/// </summary>
public sealed partial class RoutingTemplateService(IEngineeringDb db, IWorkCenters workCenters, IItems items, IEnumerable<IRoutingTemplateUsageProvider> usages, TimeProvider clock)
{
    public async Task<PagedList<RoutingTemplateSummary>> ListAsync(RoutingTemplateListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var templates = db.RoutingTemplates.AsNoTracking();
        if (query.Situation == "active")
        {
            templates = templates.Where(t => t.IsActive);
        }
        else if (query.Situation == "inactive")
        {
            templates = templates.Where(t => !t.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = "%" + Escape(query.Q.Trim()) + "%";
            templates = templates.Where(t => EF.Functions.ILike(t.Name, term, "\\") || (t.ProductType != null && EF.Functions.ILike(t.ProductType, term, "\\")));
        }

        if (!string.IsNullOrWhiteSpace(query.ProductType))
        {
            var type = Escape(query.ProductType.Trim());
            templates = templates.Where(t => t.ProductType != null && EF.Functions.ILike(t.ProductType, type, "\\"));
        }

        var result = await templates.OrderBy(t => t.Name).ToPagedListAsync(page, cancellationToken);
        var ids = result.Items.Select(t => t.Id).ToList();
        var steps = (await db.RoutingTemplateSteps.AsNoTracking().Where(s => ids.Contains(s.TemplateId)).ToListAsync(cancellationToken)).ToLookup(s => s.TemplateId);
        var linked = await db.RoutingTemplateItems.AsNoTracking().Where(i => ids.Contains(i.TemplateId)).GroupBy(i => i.TemplateId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var usage = await UsageAsync(ids, cancellationToken);
        var items = result.Items.Select(t =>
        {
            var ordered = steps[t.Id].OrderBy(s => s.Sequence).ToList();
            var (count, text) = usage.GetValueOrDefault(t.Id);
            return new RoutingTemplateSummary(t.Id, t.Name, t.ProductType, t.IsActive, ordered.Select(s => s.Code).ToList(), ordered.Select(s => s.Name).ToList(),
                ordered.Sum(s => s.StandardHours), linked.GetValueOrDefault(t.Id), count, text);
        }).ToList();
        return new PagedList<RoutingTemplateSummary>(items, result.Page, result.PageSize, result.Total);
    }

    public async Task<Result<RoutingTemplateDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await db.RoutingTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return template is null ? NotFound() : await DetailAsync(template, cancellationToken);
    }

    /// <summary>Etapa do roteiro (CA 16): etapa de outro roteiro responde 404.</summary>
    public async Task<Result<RoutingStepView>> GetStepAsync(Guid id, Guid stepId, CancellationToken cancellationToken = default)
    {
        if (!await db.RoutingTemplates.AnyAsync(t => t.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var step = await db.RoutingTemplateSteps.AsNoTracking().FirstOrDefaultAsync(s => s.Id == stepId && s.TemplateId == id, cancellationToken);
        if (step is null)
        {
            return Error.NotFound("routing_step_not_found", "Etapa não encontrada neste roteiro.");
        }

        return (await StepViewsAsync([step], cancellationToken))[0];
    }

    /// <summary>Tipos de produto já usados no tenant, para as sugestões do campo (CA 2).</summary>
    public async Task<IReadOnlyList<string>> ProductTypesAsync(CancellationToken cancellationToken = default) =>
        (await db.RoutingTemplates.AsNoTracking().Where(t => t.ProductType != null).Select(t => t.ProductType!).Distinct().ToListAsync(cancellationToken))
            .Order(StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Centros ativos para o seletor da etapa (CA 3), pelo <see cref="IWorkCenters"/>.</summary>
    public async Task<IReadOnlyList<RoutingWorkCenterOption>> WorkCentersAsync(CancellationToken cancellationToken = default) =>
        (await workCenters.ActiveAsync(cancellationToken)).Select(w => new RoutingWorkCenterOption(w.Id, w.Name, w.Line)).ToList();

    /// <summary>Último centro ativo usado em cada nome de etapa (propositivo): ids Guid v7 crescem no tempo.</summary>
    public async Task<IReadOnlyList<SuggestedWorkCenter>> SuggestedWorkCentersAsync(CancellationToken cancellationToken = default)
    {
        var used = await db.RoutingTemplateSteps.AsNoTracking()
            .Where(s => s.WorkCenterId != null && db.RoutingTemplates.Any(t => t.Id == s.TemplateId))
            .OrderByDescending(s => s.Id).Select(s => new { s.Name, s.WorkCenterId }).Take(500).ToListAsync(cancellationToken);
        var active = (await workCenters.ActiveAsync(cancellationToken)).Select(w => w.Id).ToHashSet();
        return used.Where(u => active.Contains(u.WorkCenterId!.Value)).GroupBy(u => u.Name.ToUpperInvariant())
            .Select(g => new SuggestedWorkCenter(g.First().Name, g.First().WorkCenterId!.Value)).ToList();
    }

    /// <summary>Produtos acabados e componentes ativos para vincular (CA 7), com o roteiro que cada um já usa.</summary>
    public async Task<IReadOnlyList<RoutingProductOption>> ProductOptionsAsync(string? q, CancellationToken cancellationToken = default)
    {
        var found = (await items.SearchAsync(ItemType.Finished, q, 20, cancellationToken)).Concat(await items.SearchAsync(ItemType.Component, q, 20, cancellationToken))
            .Where(i => i.IsActive && !i.IsDeleted).OrderBy(i => i.Code, StringComparer.Ordinal).Take(20).ToList();
        var ids = found.Select(i => i.Id).ToList();
        var links = await (from l in db.RoutingTemplateItems.AsNoTracking()
                           join t in db.RoutingTemplates.AsNoTracking() on l.TemplateId equals t.Id
                           where ids.Contains(l.ItemId)
                           select new { l.ItemId, t.Id, t.Name }).ToDictionaryAsync(x => x.ItemId, cancellationToken);
        return found.Select(i => new RoutingProductOption(i.Id, i.Code, i.Name, i.Type, links.GetValueOrDefault(i.Id)?.Id, links.GetValueOrDefault(i.Id)?.Name)).ToList();
    }

    /// <summary>Criação preenchida a partir de um roteiro (CA 9, R-PRP-3): etapas, letras e checklists, sem os produtos.</summary>
    public async Task<Result<RoutingTemplateInput>> DuplicateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await db.RoutingTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        var steps = await db.RoutingTemplateSteps.AsNoTracking().Where(s => s.TemplateId == id).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var name = template.Name + " (cópia)";
        return new RoutingTemplateInput(name.Length > RoutingRules.MaxName ? template.Name[..(RoutingRules.MaxName - 8)] + " (cópia)" : name, template.ProductType,
            steps.Select(s => new RoutingStepInput(null, s.Name, s.Code, s.WorkCenterId, s.StandardHours, s.IsQualityStep, s.RequiresApproval, s.Checklist.OrderBy(c => c.Sequence).Select(c => c.Text).ToList())).ToList());
    }

    private async Task<RoutingTemplateDetail> DetailAsync(RoutingTemplate template, CancellationToken ct)
    {
        var steps = await db.RoutingTemplateSteps.AsNoTracking().Where(s => s.TemplateId == template.Id).OrderBy(s => s.Sequence).ToListAsync(ct);
        var itemCount = await db.RoutingTemplateItems.CountAsync(i => i.TemplateId == template.Id, ct);
        var (count, text) = (await UsageAsync([template.Id], ct)).GetValueOrDefault(template.Id);
        return new RoutingTemplateDetail(template.Id, template.Name, template.ProductType, template.IsActive, template.SeedKey == RoutingTemplate.ExampleSeedKey,
            steps.Sum(s => s.StandardHours), itemCount, count, text, await StepViewsAsync(steps, ct), template.Version, template.CreatedAt);
    }

    private async Task<IReadOnlyList<RoutingStepView>> StepViewsAsync(IReadOnlyList<RoutingTemplateStep> steps, CancellationToken ct)
    {
        var centers = (await workCenters.FindAsync(steps.Where(s => s.WorkCenterId != null).Select(s => s.WorkCenterId!.Value).Distinct().ToList(), ct)).ToDictionary(w => w.Id);
        return steps.Select(s =>
        {
            var center = s.WorkCenterId is { } c ? centers.GetValueOrDefault(c) : null;
            return new RoutingStepView(s.Id, s.Sequence, s.Code, s.Name, s.WorkCenterId, center?.Name, center?.IsActive ?? false, s.StandardHours, s.IsQualityStep, s.RequiresApproval,
                s.Checklist.OrderBy(i => i.Sequence).ToList());
        }).ToList();
    }

    /// <summary>Uso somado dos provedores (CA 12): total e o texto "{n} OS, {m} itens de orçamento".</summary>
    private async Task<Dictionary<Guid, (int Count, string? Text)>> UsageAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var parts = new Dictionary<Guid, List<(int Count, string Noun)>>();
        foreach (var usage in usages)
        {
            foreach (var (id, count) in await usage.CountAsync(ids, ct))
            {
                if (count > 0)
                {
                    (parts.TryGetValue(id, out var list) ? list : parts[id] = []).Add((count, usage.Noun));
                }
            }
        }

        return parts.ToDictionary(p => p.Key, p => (p.Value.Sum(x => x.Count), (string?)string.Join(", ", p.Value.Select(x => $"{x.Count} {x.Noun}"))));
    }

    /// <summary>% e _ do texto valem como texto no ILIKE.</summary>
    internal static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    internal static Error NotFound() => Error.NotFound("routing_template_not_found", "Roteiro não encontrado.");
}