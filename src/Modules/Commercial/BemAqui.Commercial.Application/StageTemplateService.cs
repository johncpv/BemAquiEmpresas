using BemAqui.BuildingBlocks.Paging;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.HR.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Modelos de etapas do projeto de serviço (EXE-02): cadastro com as etapas e os marcos sugeridos, padrão por segmento, vínculo com o item de
/// serviço e o pacote do perfil Serviços. O cargo e o item são só ids, validados pelo <see cref="IPositions"/> e pelo <see cref="IItems"/>
/// (ADR 0006). Implementa o <see cref="IStageTemplates"/> para a EXE-03a e a EXE-05b; nunca expõe custo.
/// </summary>
public sealed partial class StageTemplateService(ICommercialDb db, IPositions positions, IItems items, TimeProvider clock) : IStageTemplates
{
    public async Task<PagedList<StageTemplateSummary>> ListAsync(StageTemplateListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var templates = db.StageTemplates.AsNoTracking();
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
            templates = templates.Where(t => EF.Functions.ILike(t.Name, term, "\\"));
        }

        var result = await templates.OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name).ToPagedListAsync(page, cancellationToken);
        var ids = result.Items.Select(t => t.Id).ToList();
        var steps = (await db.StageTemplateSteps.AsNoTracking().Where(s => ids.Contains(s.TemplateId)).ToListAsync(cancellationToken)).ToLookup(s => s.TemplateId);
        var linked = await db.StageTemplateItems.AsNoTracking().Where(i => ids.Contains(i.TemplateId)).GroupBy(i => i.TemplateId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var usage = await UsageAsync(ids, cancellationToken);
        var list = result.Items.Select(t =>
        {
            var ordered = steps[t.Id].OrderBy(s => s.Sequence).ToList();
            return new StageTemplateSummary(t.Id, t.Name, t.Segment, SegmentName(t.Segment), t.IsDefault, IsSuggested(t), t.IsActive, ordered.Select(s => s.Code).ToList(),
                ordered.Select(s => s.Name).ToList(), ordered.Sum(s => s.StandardHoursPerUnit), linked.GetValueOrDefault(t.Id), usage.GetValueOrDefault(t.Id));
        }).ToList();
        return new PagedList<StageTemplateSummary>(list, result.Page, result.PageSize, result.Total);
    }

    public async Task<Result<StageTemplateDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await db.StageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return template is null ? NotFound() : await DetailAsync(template, cancellationToken);
    }

    /// <summary>Cargos ativos para o seletor da etapa (CA 4): só id e nome, pelo <see cref="IPositions"/>; nunca o custo-hora.</summary>
    public async Task<IReadOnlyList<StagePositionOption>> PositionsAsync(CancellationToken cancellationToken = default) =>
        (await positions.ActiveAsync(cancellationToken)).Select(p => new StagePositionOption(p.Id, p.Name)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static IReadOnlyList<StageSegmentOption> SegmentOptions() => StageTemplateRules.Segments.Select(s => new StageSegmentOption(s.Code, s.Name)).ToList();

    private async Task<StageTemplateDetail> DetailAsync(StageTemplate template, CancellationToken ct)
    {
        var steps = await db.StageTemplateSteps.AsNoTracking().Where(s => s.TemplateId == template.Id).OrderBy(s => s.Sequence).ToListAsync(ct);
        var itemCount = await db.StageTemplateItems.CountAsync(i => i.TemplateId == template.Id, ct);
        var usage = (await UsageAsync([template.Id], ct)).GetValueOrDefault(template.Id);
        var found = (await positions.FindAsync(steps.Where(s => s.PositionId != null).Select(s => s.PositionId!.Value).Distinct().ToList(), ct)).ToDictionary(p => p.Id);
        var views = steps.Select(s =>
        {
            var position = s.PositionId is { } p ? found.GetValueOrDefault(p) : null;
            return new StageStepView(s.Id, s.Sequence, s.Code, s.Name, s.Role, s.StandardHoursPerUnit, s.PositionId, position?.Name, position?.IsActive ?? false, s.RequiresApproval, s.Checklist.ToList());
        }).ToList();
        return new StageTemplateDetail(template.Id, template.Name, template.Segment, SegmentName(template.Segment), template.IsDefault, IsSuggested(template), template.IsActive,
            template.Revision, steps.Sum(s => s.StandardHoursPerUnit), itemCount, usage, views, template.SuggestedMilestones.ToList(), template.Version, template.CreatedAt);
    }

    /// <summary>Projetos que copiaram as etapas de cada modelo (CA 6): o modelo fica gravado no projeto na cópia (EXE-03a).</summary>
    private async Task<Dictionary<Guid, int>> UsageAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.Projects.AsNoTracking().Where(p => p.StageTemplateId != null && ids.Contains(p.StageTemplateId.Value)).GroupBy(p => p.StageTemplateId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    /// <summary>Chip "Sugerido — ajuste ao seu jeito" (CA 11): modelo do pacote do perfil que o tenant ainda não editou.</summary>
    private static bool IsSuggested(StageTemplate t) => t.SeedKey is not null && t.Revision == 1;

    private static string? SegmentName(string? code) => code is null ? null : StageTemplateRules.Segments.FirstOrDefault(s => s.Code == code)?.Name ?? code;

    // ----- IStageTemplates (CA 13) -----

    async Task<StageTemplateInfo?> IStageTemplates.GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await db.StageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return template is null ? null : await InfoAsync(template, cancellationToken);
    }

    public async Task<IReadOnlyList<StageTemplateOption>> ActiveAsync(CancellationToken cancellationToken = default) =>
        await db.StageTemplates.AsNoTracking().Where(t => t.IsActive).OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name)
            .Select(t => new StageTemplateOption(t.Id, t.Name, t.Segment, t.IsDefault)).ToListAsync(cancellationToken);

    public async Task<StageTemplateInfo?> SuggestAsync(IReadOnlyList<Guid> itemIds, string? segment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var linked = await (from l in db.StageTemplateItems.AsNoTracking()
                            join t in db.StageTemplates.AsNoTracking() on l.TemplateId equals t.Id
                            where itemIds.Contains(l.ItemId) && t.IsActive
                            select new { l.ItemId, Template = t }).ToListAsync(cancellationToken);
        var byItem = itemIds.Select(id => linked.FirstOrDefault(l => l.ItemId == id)?.Template).FirstOrDefault(t => t is not null);
        var chosen = byItem
            ?? (segment is null ? null : await db.StageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.IsActive && t.IsDefault && t.Segment == segment, cancellationToken))
            ?? await db.StageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.IsActive && t.IsDefault && t.Segment == null, cancellationToken);
        return chosen is null ? null : await InfoAsync(chosen, cancellationToken);
    }

    private async Task<StageTemplateInfo> InfoAsync(StageTemplate template, CancellationToken ct)
    {
        var steps = await db.StageTemplateSteps.AsNoTracking().Where(s => s.TemplateId == template.Id).OrderBy(s => s.Sequence).ToListAsync(ct);
        return new StageTemplateInfo(template.Id, template.Name, template.Segment, template.IsDefault, template.IsActive, template.Revision,
            steps.Select(s => new StageTemplateStepInfo(s.Code, s.Name, s.Role, s.StandardHoursPerUnit, s.PositionId, s.RequiresApproval, s.Checklist.ToList())).ToList(),
            template.SuggestedMilestones.ToList());
    }

    /// <summary>% e _ do texto valem como texto no ILIKE.</summary>
    internal static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    internal static Error NotFound() => Error.NotFound("stage_template_not_found", "Modelo de etapas não encontrado.");

    private static Error Stale() => Error.Conflict("concurrency_conflict", "O modelo de etapas mudou desde que você abriu. Recarregue.");
}
