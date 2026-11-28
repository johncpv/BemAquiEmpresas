using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.HR.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Etapas do projeto de serviço (EXE-03a): cópia do modelo na aprovação e em projeto existente ("Usar um modelo de etapas"), edição antes de
/// começar e leitura. Nenhum evento é publicado e nenhum título é tocado; a etapa do modelo é a <see cref="ProjectCurrentStage"/>, separada da etapa
/// do projeto (<see cref="ProjectStageMap"/>). Iniciar, checklist, concluir e aprovar: EXE-03b (<c>ProjectStageService.Progress.cs</c>).
/// </summary>
public sealed partial class ProjectStageService(
    ICommercialDb db, IStageTemplates templates, IStageMilestoneGuard milestoneGuard, IPositions positions, IUserDirectory users, ICurrentUser currentUser,
    TenantContextHolder tenant, ProjectExecutionService execution, CommercialParametersService parameters, IPermissionChecker permissions, IEventPublisher events,
    TimeProvider clock)
{
    /// <summary>Horas previstas de uma etapa do projeto: de 0 a 9.999.999 (as do modelo vão até 99.999 por unidade).</summary>
    public const decimal MaxPlannedHours = 9_999_999m;

    private bool Released => tenant.Resolved?.HasFeature(BusinessProfileCatalog.ServiceExecutionFeature) == true;

    /// <summary>Segmento do perfil Serviços do tenant (o primeiro deles), para o modelo padrão do segmento na sugestão.</summary>
    private string? Segment => tenant.Resolved?.Segments.FirstOrDefault(s => StageTemplateRules.Segments.Any(x => string.Equals(x.Code, s, StringComparison.Ordinal)));

    // ----- Aprovação (CA 2) -----

    /// <summary>
    /// Modelo sugerido para o orçamento aprovado (item → segmento → padrão do tenant) e a cópia das etapas, com as horas pela quantidade do item que
    /// originou a sugestão (sem item, × 1). Sem modelo, nulo: o projeto nasce sem etapas (EXE-01a).
    /// </summary>
    public async Task<(StageTemplateInfo Template, IReadOnlyList<StageCopy> Stages)?> ForApprovalAsync(IReadOnlyList<(Guid? ItemId, decimal Quantity)> items, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);
        var template = await templates.SuggestAsync(items.Where(i => i.ItemId is not null).Select(i => i.ItemId!.Value).Distinct().ToList(), Segment, ct);
        if (template is null || template.Steps.Count == 0)
        {
            return null;
        }

        var quantity = await QuantityAsync(template.Id, items, ct);
        return (template, ServiceStagePlan.Copy(template.Steps, quantity, ProjectStatus.Planned, null, null));
    }

    /// <summary>Grava as etapas copiadas e o modelo no projeto (CA 1, 2 e 4). Só monta; quem chama grava na transação dele.</summary>
    public void Attach(Project project, StageTemplateInfo template, IReadOnlyList<StageCopy> stages, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(stages);
        foreach (var copy in stages)
        {
            var stage = new ProjectServiceStage
            {
                ProjectId = project.Id, Sequence = copy.Sequence, Code = copy.Code, Name = copy.Name, Role = copy.Role, PlannedHours = copy.PlannedHours, PositionId = copy.PositionId,
                RequiresApproval = copy.RequiresApproval, Status = copy.Status, CompletedOn = copy.CompletedOn, CompletedAt = copy.Status == ProjectStageStatus.Done ? now : null,
            };
            db.ProjectServiceStages.Add(stage);
            db.ProjectServiceStageChecks.AddRange(copy.Checklist.Select((text, i) => new ProjectServiceStageCheck { StageId = stage.Id, Sequence = i + 1, Text = text }));
        }

        project.StageTemplateId = template.Id;
        project.StageTemplateRevision = template.Revision;
        project.StageTemplateName = template.Name;
        project.StagesAppliedAt = now;
        project.StagesAppliedBy = currentUser.UserId;
        project.StagesChangedAt = now;
    }

    /// <summary>Etapas congeladas no snapshot do contrato (CA 2): letra, nome e papel.</summary>
    public static IReadOnlyList<SnapshotStage> Snapshot(IReadOnlyList<StageCopy> stages) =>
        stages.Select(s => new SnapshotStage(s.Code, s.Name, s.Role.ToString())).ToList();

    // ----- Leitura (CA 7 a 9) -----

    public async Task<Result<ProjectStagesView>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(id, tracking: false, cancellationToken);
        return loaded.IsFailure ? loaded.Error! : await ViewAsync(loaded.Value, cancellationToken);
    }

    /// <summary>Etapa atual dos projetos informados (CA 9); sem etapas, fora do mapa. Quem chama confere o recurso <c>projects.service_execution</c>.</summary>
    internal static async Task<IReadOnlyDictionary<Guid, ProjectCurrentStage>> CurrentAsync(ICommercialDb db, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
        {
            return new Dictionary<Guid, ProjectCurrentStage>();
        }

        var rows = await db.ProjectServiceStages.AsNoTracking().Where(s => projectIds.Contains(s.ProjectId))
            .Select(s => new { s.ProjectId, s.Sequence, s.Code, s.Name, s.Status }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.ProjectId)
            .Select(g => (g.Key, Current: ServiceStagePlan.Current(g.Select(r => (r.Sequence, r.Code, r.Name, r.Status)).ToList())))
            .Where(x => x.Current is not null).ToDictionary(x => x.Key, x => x.Current!);
    }

    private async Task<ProjectStagesView> ViewAsync(Project project, CancellationToken ct)
    {
        var stages = await db.ProjectServiceStages.AsNoTracking().Where(s => s.ProjectId == project.Id).OrderBy(s => s.Sequence).ToListAsync(ct);
        var stageIds = stages.Select(s => s.Id).ToList();
        var checks = (await db.ProjectServiceStageChecks.AsNoTracking().Where(c => stageIds.Contains(c.StageId)).OrderBy(c => c.Sequence).ToListAsync(ct)).ToLookup(c => c.StageId);
        var positionNames = (await positions.FindAsync(stages.Where(s => s.PositionId != null).Select(s => s.PositionId!.Value).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.Name);
        var names = new Dictionary<Guid, string?>();
        var people = stages.SelectMany(s => new[] { s.ResponsibleUserId, s.CompletedBy, s.ApprovedBy }).Concat(checks.SelectMany(g => g.Select(c => c.MarkedBy)))
            .Where(u => u is not null).Select(u => u!.Value).Distinct();
        foreach (var userId in people)
        {
            names[userId] = await users.DisplayNameAsync(userId, ct);
        }

        string? Name(Guid? userId) => userId is { } u ? names.GetValueOrDefault(u) : null;
        // EXE-03b: marco financeiro por etapa (até a EXE-05a, nenhum); decide quem conclui e o bloco de efeito nas parcelas.
        var milestones = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in stages.Select(s => s.Code).Distinct(StringComparer.Ordinal))
        {
            if ((await milestoneGuard.MilestonesForAsync(project.Id, [code], ct)).Count > 0)
            {
                milestones.Add(code);
            }
        }

        var views = stages.Select(s =>
        {
            var list = checks[s.Id].Select(c => new ProjectStageCheckView(c.Id, c.Sequence, c.Text, c.Result, c.MarkedAt, Name(c.MarkedBy))).ToList();
            return new ProjectStageView(s.Id, s.Sequence, s.Code, s.Name, s.Role, s.Status, s.PlannedHours, s.PositionId,
                s.PositionId is { } p ? positionNames.GetValueOrDefault(p) : null, s.RequiresApproval, s.ResponsibleUserId,
                Name(s.ResponsibleUserId), s.PlannedStart, s.PlannedEnd, s.StartedAt, s.CompletedOn,
                list.Count(c => c.Result is not null), list.Count, list, milestones.Contains(s.Code), s.SelfApproved, Name(s.CompletedBy), Name(s.ApprovedBy),
                s.CompletionVersion);
        }).ToList();
        var started = stages.Any(s => s.IsStarted);
        var canApply = ServiceStagePlan.AcceptsStages(project.Status) && !started;
        Guid? suggested = null;
        IReadOnlyList<StageTemplateOption> options = [];
        if (canApply)
        {
            options = await templates.ActiveAsync(ct);
            var itemIds = await db.ProjectItems.AsNoTracking().Where(i => i.ProjectId == project.Id && i.ItemId != null).OrderBy(i => i.Sequence).Select(i => i.ItemId!.Value).ToListAsync(ct);
            suggested = (await templates.SuggestAsync(itemIds, Segment, ct))?.Id;
        }

        return new ProjectStagesView(project.Id, views, project.StageTemplateId, project.StageTemplateName, project.StageTemplateRevision, started,
            ServiceStagePlan.Current(stages.Select(s => (s.Sequence, s.Code, s.Name, s.Status)).ToList()), canApply, suggested, options, project.Version);
    }

    // ----- Apoio -----

    /// <summary>
    /// Projeto do tenant (404), com o recurso por perfil (403 <c>module_not_released</c>, CA 6) e de serviço (409). Mesma ordem do
    /// <see cref="ProjectExecutionService"/>.
    /// </summary>
    private async Task<Result<Project>> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.Projects : db.Projects.AsNoTracking();
        var project = await query.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
        {
            return ProjectService.NotFound();
        }

        if (!Released)
        {
            return Error.Forbidden(Error.Codes.ModuleNotReleased, "Funcionalidade não liberada nessa versão do sistema.");
        }

        return project.ExecutionKind != ExecutionKind.Service
            ? Error.Conflict("not_a_service_project", "As etapas do modelo são do projeto de serviço. No projeto de fabricação, as etapas vêm do roteiro da OS.")
            : project;
    }

    /// <summary>Quantidade do item que originou a sugestão: o primeiro item (na ordem) ligado ao modelo; sem item ligado, 1.</summary>
    private async Task<decimal> QuantityAsync(Guid templateId, IReadOnlyList<(Guid? ItemId, decimal Quantity)> items, CancellationToken ct)
    {
        var ids = items.Where(i => i.ItemId is not null).Select(i => i.ItemId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return 1m;
        }

        var linked = await db.StageTemplateItems.AsNoTracking().Where(l => l.TemplateId == templateId && ids.Contains(l.ItemId)).Select(l => l.ItemId).ToListAsync(ct);
        var origin = items.FirstOrDefault(i => i.ItemId is { } itemId && linked.Contains(itemId));
        return origin.ItemId is null ? 1m : origin.Quantity;
    }

    private static Error Stale() => Error.Conflict("concurrency_conflict", "O projeto mudou desde que você abriu. Recarregue.");

    private static Error StageNotFound() => Error.NotFound("project_stage_not_found", "Etapa não encontrada neste projeto.");

    private async Task<Result<ProjectStagesView>> SaveAsync(Project project, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Stale();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Stale();
        }

        var reloaded = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == project.Id, ct);
        return await ViewAsync(reloaded, ct);
    }

    private DateTimeOffset Now => clock.GetUtcNow();
}
