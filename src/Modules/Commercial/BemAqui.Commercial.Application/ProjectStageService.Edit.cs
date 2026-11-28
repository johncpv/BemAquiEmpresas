using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class ProjectStageService
{
    /// <summary>
    /// "Usar um modelo de etapas" (CA 3) em projeto <c>Planned</c>, <c>InProgress</c> ou <c>Delivered</c>, e troca do modelo antes de começar (CA 5).
    /// No projeto entregue, as etapas nascem concluídas pela data da entrega (e do aceite), <b>sem evento e sem tocar em título</b>. Encerrado ou
    /// cancelado, 409.
    /// </summary>
    public async Task<Result<ProjectStagesView>> ApplyTemplateAsync(Guid id, ProjectStageTemplateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, tracking: true, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var project = loaded.Value;
        if (project.Version != input.Version)
        {
            return Stale();
        }

        if (!ServiceStagePlan.AcceptsStages(project.Status))
        {
            return Error.Conflict("project_stages_locked", ServiceStagePlan.ClosedRefusal);
        }

        var existing = await db.ProjectServiceStages.Where(s => s.ProjectId == id).ToListAsync(cancellationToken);
        if (existing.Any(s => s.IsStarted))
        {
            return Error.Conflict("project_stages_started", ServiceStagePlan.StartedRefusal);
        }

        var template = await templates.GetAsync(input.TemplateId, cancellationToken);
        if (template is null || !template.IsActive || template.Steps.Count == 0)
        {
            return StageTemplateRules.Invalid("templateId", "Escolha um modelo de etapas ativo.");
        }

        if (await MilestonesErrorAsync(id, existing.Select(s => s.Code).ToList(), cancellationToken) is { } blocked)
        {
            return blocked;
        }

        await RemoveAsync(existing, cancellationToken);
        var items = await db.ProjectItems.AsNoTracking().Where(i => i.ProjectId == id).OrderBy(i => i.Sequence).Select(i => new { i.ItemId, i.Quantity }).ToListAsync(cancellationToken);
        var quantity = await QuantityAsync(template.Id, items.Select(i => (i.ItemId, i.Quantity)).ToList(), cancellationToken);
        var status = project.Status is ProjectStatus.Fabricated or ProjectStatus.Installing ? ProjectStatus.InProgress : project.Status;
        Attach(project, template, ServiceStagePlan.Copy(template.Steps, quantity, status, project.DeliveredOn, project.AcceptedOn), Now);
        db.Entry(project).Property(p => p.Version).OriginalValue = input.Version;
        return await SaveAsync(project, cancellationToken);
    }

    /// <summary>
    /// Edição das etapas (CA 5). Antes de começar: incluir, remover, renomear, reordenar, mudar papel, horas, cargo, aprovação e checklist, com as
    /// validações da EXE-02 (422 por linha) e a checagem de marcos (<see cref="IStageMilestoneGuard"/>). Depois da primeira etapa iniciada: só
    /// responsável e datas previstas; o resto, 409.
    /// </summary>
    public async Task<Result<ProjectStagesView>> UpdateAsync(Guid id, ProjectStagesInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, tracking: true, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var project = loaded.Value;
        if (project.Version != input.Version)
        {
            return Stale();
        }

        if (!ServiceStagePlan.AcceptsStages(project.Status))
        {
            return Error.Conflict("project_stages_locked", ServiceStagePlan.ClosedRefusal);
        }

        var list = input.Stages ?? [];
        var existing = await db.ProjectServiceStages.Where(s => s.ProjectId == id).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var byId = existing.ToDictionary(s => s.Id);
        // IDOR (CA 6): etapa de outro projeto (ou inexistente) é 404, sem dizer de quem é.
        if (list.Any(s => s.Id is { } stageId && !byId.ContainsKey(stageId)) || list.Where(s => s.Id is not null).GroupBy(s => s.Id).Any(g => g.Count() > 1))
        {
            return StageNotFound();
        }

        var (steps, errors) = StageTemplateRules.ValidateSteps(list.Select(s =>
            new StageStepInput(s.Id, s.Name, s.Code, s.Role, 0m, s.PositionId, s.RequiresApproval, s.Checklist ?? [])).ToList());
        var renamed = errors.ToDictionary(e => e.Key.StartsWith("steps", StringComparison.Ordinal) ? "stages" + e.Key[5..] : e.Key, e => e.Value, StringComparer.Ordinal);
        await LineErrorsAsync(list, existing, renamed, cancellationToken);
        if (StageTemplateRules.ToError(renamed) is { } invalid)
        {
            return invalid;
        }

        if (existing.Any(s => s.IsStarted))
        {
            var ids = existing.Select(e => e.Id).ToList();
            var checklists = (await db.ProjectServiceStageChecks.AsNoTracking().Where(c => ids.Contains(c.StageId)).OrderBy(c => c.Sequence).ToListAsync(cancellationToken))
                .ToLookup(c => c.StageId, c => c.Text);
            if (!SameStructure(existing, list, steps, checklists))
            {
                return Error.Conflict("project_stages_started", ServiceStagePlan.StartedRefusal);
            }

            foreach (var s in list)
            {
                var stage = byId[s.Id!.Value];
                stage.ResponsibleUserId = s.ResponsibleUserId;
                stage.PlannedStart = s.PlannedStart;
                stage.PlannedEnd = s.PlannedEnd;
            }
        }
        else
        {
            // Marcos do contrato (CA 5): renomear, remover, trocar a letra ou mudar o papel de etapa apontada por marco StageCompleted, 409.
            var kept = list.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToHashSet();
            var touched = existing.Where(e => !kept.Contains(e.Id)).Select(e => e.Code)
                .Concat(list.Select((s, i) => (s, step: steps[i])).Where(x => x.s.Id is { } sid && Changed(byId[sid], x.step)).Select(x => byId[x.s.Id!.Value].Code))
                .Distinct(StringComparer.Ordinal).ToList();
            if (await MilestonesErrorAsync(id, touched, cancellationToken) is { } blocked)
            {
                return blocked;
            }

            await ReplaceAsync(project, existing, list, steps, cancellationToken);
        }

        project.StagesChangedAt = Now;
        db.Entry(project).Property(p => p.Version).OriginalValue = input.Version;
        return await SaveAsync(project, cancellationToken);
    }

    /// <summary>Horas, datas e responsável por linha (CA 5 e 6): responsável é usuário ativo do tenant com <c>projects.view</c>; cargo existente.</summary>
    private async Task LineErrorsAsync(IReadOnlyList<ProjectStageInput> list, IReadOnlyList<ProjectServiceStage> existing, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        void Add(string field, string message) => errors.TryAdd(field, [message]);
        HashSet<Guid> responsible = list.Any(s => s.ResponsibleUserId is not null) ? [.. await users.UsersWithPermissionAsync("projects.view", ct)] : [];
        var positionIds = list.Where(s => s.PositionId is not null).Select(s => s.PositionId!.Value).Distinct().ToList();
        HashSet<Guid> known = positionIds.Count == 0 ? [] : [.. (await positions.FindAsync(positionIds, ct)).Select(p => p.Id)];
        var current = existing.ToDictionary(s => s.Id);
        for (var i = 0; i < list.Count; i++)
        {
            var s = list[i];
            var field = $"stages[{i}]";
            if (s.PlannedHours < 0 || s.PlannedHours > MaxPlannedHours)
            {
                Add(field + ".plannedHours", "Informe as horas previstas entre 0 e 9.999.999.");
            }

            if (s.PlannedStart is { } start && s.PlannedEnd is { } end && end < start)
            {
                Add(field + ".plannedEnd", "O fim previsto não pode ser antes do início.");
            }

            var unchanged = s.Id is { } sid && current.TryGetValue(sid, out var before) && before.ResponsibleUserId == s.ResponsibleUserId;
            if (s.ResponsibleUserId is { } user && !unchanged && !responsible.Contains(user))
            {
                Add(field + ".responsibleUserId", "Escolha um usuário ativo com acesso a projetos.");
            }

            if (s.PositionId is { } position && !known.Contains(position))
            {
                Add(field + ".positionId", "Escolha um cargo do cadastro.");
            }
        }
    }

    /// <summary>Mesmas etapas, na mesma ordem, com os mesmos campos de estrutura (projeto já começou: só responsável e datas mudam).</summary>
    private static bool SameStructure(
        List<ProjectServiceStage> existing, IReadOnlyList<ProjectStageInput> list, IReadOnlyList<NormalizedStageStep> steps, ILookup<Guid, string> checklists)
    {
        if (existing.Count != list.Count)
        {
            return false;
        }

        for (var i = 0; i < list.Count; i++)
        {
            var stage = existing[i];
            if (list[i].Id != stage.Id || Changed(stage, steps[i]) || Round(list[i].PlannedHours) != stage.PlannedHours || list[i].PositionId != stage.PositionId
                || list[i].RequiresApproval != stage.RequiresApproval || !checklists[stage.Id].SequenceEqual(steps[i].Checklist, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Nome, letra ou papel mudaram (o que a checagem de marcos olha).</summary>
    private static bool Changed(ProjectServiceStage stage, NormalizedStageStep step) =>
        !string.Equals(stage.Name, step.Name, StringComparison.Ordinal) || !string.Equals(stage.Code, step.Code, StringComparison.Ordinal) || stage.Role != step.Role;

    /// <summary>Antes de começar: regrava a lista (atualiza pelo id, inclui as novas, remove as que saíram) e o checklist de cada etapa.</summary>
    private async Task ReplaceAsync(
        Project project, IReadOnlyList<ProjectServiceStage> existing, IReadOnlyList<ProjectStageInput> list, IReadOnlyList<NormalizedStageStep> steps, CancellationToken ct)
    {
        var kept = list.Where(s => s.Id is not null).Select(s => s.Id!.Value).ToHashSet();
        await RemoveAsync(existing.Where(e => !kept.Contains(e.Id)).ToList(), ct);
        var keptIds = kept.ToList();
        var oldChecks = (await db.ProjectServiceStageChecks.Where(c => keptIds.Contains(c.StageId)).ToListAsync(ct)).ToLookup(c => c.StageId);
        var byId = existing.ToDictionary(s => s.Id);
        for (var i = 0; i < list.Count; i++)
        {
            var input = list[i];
            var step = steps[i];
            ProjectServiceStage stage;
            if (input.Id is { } stageId)
            {
                stage = byId[stageId];
            }
            else
            {
                stage = new ProjectServiceStage { ProjectId = project.Id };
                db.ProjectServiceStages.Add(stage);
            }

            stage.Sequence = i + 1;
            stage.Code = step.Code;
            stage.Name = step.Name;
            stage.Role = step.Role;
            stage.PlannedHours = Round(input.PlannedHours);
            stage.PositionId = input.PositionId;
            stage.RequiresApproval = input.RequiresApproval;
            stage.ResponsibleUserId = input.ResponsibleUserId;
            stage.PlannedStart = input.PlannedStart;
            stage.PlannedEnd = input.PlannedEnd;
            // Projeto entregue sem etapas: as novas nascem como na cópia (CA 3), concluídas pela data da entrega e do aceite.
            var (status, completedOn) = ServiceStagePlan.Initial(i, step.Role, project.Status, project.DeliveredOn, project.AcceptedOn);
            stage.Status = status;
            stage.CompletedOn = completedOn;
            stage.CompletedAt = status == ProjectStageStatus.Done ? Now : null;
            var before = oldChecks[stage.Id].OrderBy(c => c.Sequence).Select(c => c.Text).ToList();
            if (!before.SequenceEqual(step.Checklist, StringComparer.Ordinal))
            {
                db.ProjectServiceStageChecks.RemoveRange(oldChecks[stage.Id]);
                db.ProjectServiceStageChecks.AddRange(step.Checklist.Select((text, n) => new ProjectServiceStageCheck { StageId = stage.Id, Sequence = n + 1, Text = text }));
            }
        }
    }

    /// <summary>Remove etapas não iniciadas e o checklist delas (cópia que ainda não teve movimento).</summary>
    private async Task RemoveAsync(List<ProjectServiceStage> stages, CancellationToken ct)
    {
        if (stages.Count == 0)
        {
            return;
        }

        var ids = stages.Select(s => s.Id).ToList();
        db.ProjectServiceStageChecks.RemoveRange(await db.ProjectServiceStageChecks.Where(c => ids.Contains(c.StageId)).ToListAsync(ct));
        db.ProjectServiceStages.RemoveRange(stages);
    }

    private async Task<Error?> MilestonesErrorAsync(Guid projectId, List<string> codes, CancellationToken ct)
    {
        if (codes.Count == 0)
        {
            return null;
        }

        var milestones = await milestoneGuard.MilestonesForAsync(projectId, codes, ct);
        return milestones.Count == 0
            ? null
            : Error.Conflict("stage_has_milestones", $"Estas etapas estão em marcos do contrato: {string.Join("; ", milestones)}. Ajuste os marcos por um aditivo antes.");
    }

    private static decimal Round(decimal hours) => Math.Round(hours, 4, MidpointRounding.AwayFromZero);
}
