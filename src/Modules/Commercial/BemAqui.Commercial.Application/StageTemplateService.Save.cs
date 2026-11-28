using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class StageTemplateService
{
    public const string NameIndex = "ux_stage_templates_name_active";
    public const string DefaultIndex = "ux_stage_templates_default_segment";

    /// <summary>Novo modelo (CA 2, 3 e 5): ativo, revisão 1, sequência 1..n e o cargo de cada etapa validado.</summary>
    public async Task<Result<StageTemplateDetail>> CreateAsync(StageTemplateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (steps, milestones, errors) = StageTemplateRules.Validate(input);
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Id is not null)
            {
                errors.TryAdd($"steps[{i}].id", ["Etapa não encontrada neste modelo."]);
            }
        }

        if (await ValidateAsync(null, input.Name, true, steps, [], errors, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var template = new StageTemplate { CreatedAt = clock.GetUtcNow(), Revision = 1 };
        Apply(template, input, milestones);
        db.StageTemplates.Add(template);
        foreach (var step in steps)
        {
            var entity = new StageTemplateStep { TemplateId = template.Id };
            Apply(entity, step);
            db.StageTemplateSteps.Add(entity);
        }

        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await DetailAsync(template, cancellationToken);
    }

    /// <summary>
    /// Edição (CA 2, 3, 5 e 6): etapas com id são as deste modelo; as que saíram da lista são removidas e a sequência é regravada. A revisão sobe e
    /// o xmin do modelo é conferido em toda gravação, mesmo quando só as etapas mudam. Os projetos que já copiaram as etapas não mudam.
    /// </summary>
    public async Task<Result<StageTemplateDetail>> UpdateAsync(Guid id, StageTemplateUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var template = await db.StageTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.Version != input.Version)
        {
            return Stale();
        }

        var existing = await db.StageTemplateSteps.Where(s => s.TemplateId == id).ToDictionaryAsync(s => s.Id, cancellationToken);
        var (steps, milestones, errors) = StageTemplateRules.Validate(input.AsInput());
        var seen = new HashSet<Guid>();
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Id is { } stepId && (!existing.ContainsKey(stepId) || !seen.Add(stepId)))
            {
                errors.TryAdd($"steps[{i}].id", ["Etapa não encontrada neste modelo."]);
            }
        }

        var segment = string.IsNullOrWhiteSpace(input.Segment) ? null : input.Segment.Trim();
        if (template.IsDefault && segment != template.Segment)
        {
            errors.TryAdd("segment", ["Este é o modelo padrão. Escolha outro modelo padrão antes de mudar o segmento deste."]);
        }

        // Cargo inativo é aceito só se já estava gravado no modelo (CA 3).
        var stored = existing.Values.Where(s => s.PositionId != null).Select(s => s.PositionId!.Value).ToHashSet();
        if (await ValidateAsync(id, input.Name, template.IsActive, steps, stored, errors, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.Entry(template).Property(t => t.Version).OriginalValue = input.Version;
        Apply(template, input.AsInput(), milestones);
        template.Revision++;
        foreach (var step in steps)
        {
            var entity = step.Id is { } stepId ? existing[stepId] : new StageTemplateStep { TemplateId = id };
            Apply(entity, step);
            if (step.Id is null)
            {
                db.StageTemplateSteps.Add(entity);
            }
        }

        db.StageTemplateSteps.RemoveRange(existing.Values.Where(s => !seen.Contains(s.Id)));
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await DetailAsync(template, cancellationToken);
    }

    /// <summary>Inativar: some dos seletores e da sugestão. O padrão não pode ser inativado (409).</summary>
    public async Task<Result<StageTemplateDetail>> InactivateAsync(Guid id, StageTemplateVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var template = await db.StageTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.Version != input.Version)
        {
            return Stale();
        }

        if (template.IsDefault)
        {
            return Error.Conflict("stage_template_is_default", "Escolha outro modelo padrão antes de inativar este.");
        }

        db.Entry(template).Property(t => t.Version).OriginalValue = input.Version;
        template.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(template, cancellationToken);
    }

    /// <summary>Ativar: o nome não pode repetir entre os ativos.</summary>
    public async Task<Result<StageTemplateDetail>> ActivateAsync(Guid id, StageTemplateVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var template = await db.StageTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.Version != input.Version)
        {
            return Stale();
        }

        if (await NameTakenAsync(id, template.Name, cancellationToken))
        {
            return StageTemplateRules.Invalid("name", "Já existe um modelo ativo com este nome. Renomeie antes de ativar.");
        }

        db.Entry(template).Property(t => t.Version).OriginalValue = input.Version;
        template.IsActive = true;
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await DetailAsync(template, cancellationToken);
    }

    /// <summary>
    /// "Usar como padrão" (CA 7): numa transação só, trava a linha do padrão atual do mesmo segmento (<c>FOR UPDATE</c>), tira o padrão dele e
    /// marca este, conferindo o xmin. Dois pedidos simultâneos: o segundo encontra o índice único do padrão ocupado e recebe 409
    /// <c>concurrency_conflict</c>.
    /// </summary>
    public async Task<Result<StageTemplateDetail>> SetDefaultAsync(Guid id, StageTemplateVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var template = await db.StageTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.Version != input.Version)
        {
            return Stale();
        }

        if (template.IsDefault)
        {
            return await DetailAsync(template, cancellationToken);
        }

        if (!template.IsActive)
        {
            return StageTemplateRules.Invalid("isActive", "Ative o modelo antes de usá-lo como padrão.");
        }

        var segment = template.Segment ?? string.Empty;
        var current = await db.StageTemplates
            .FromSql($"SELECT t.*, t.xmin FROM commercial.stage_templates t WHERE t.is_default AND NOT t.is_deleted AND coalesce(t.segment, '') = {segment} FOR UPDATE")
            .ToListAsync(cancellationToken);
        foreach (var previous in current)
        {
            await db.Entry(previous).ReloadAsync(cancellationToken);
            previous.IsDefault = false;
        }

        try
        {
            // Duas gravações: o padrão anterior sai antes de este entrar, para o índice único parcial não ver dois ao mesmo tempo.
            await db.SaveChangesAsync(cancellationToken);
            db.Entry(template).Property(t => t.Version).OriginalValue = input.Version;
            template.IsDefault = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, DefaultIndex))
        {
            return Error.Conflict("concurrency_conflict", "Outro modelo acabou de virar o padrão. Recarregue.");
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(template, cancellationToken);
    }

    /// <summary>
    /// Exclusão lógica (CA 8 e 9) com <c>projects.delete</c>: na mesma transação remove os vínculos com itens (com auditoria) e marca o modelo. O
    /// padrão não pode ser excluído (409). A <c>seed_key</c> fica gravada, então o pacote do perfil não recria o modelo.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id, uint version, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var template = await db.StageTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.Version != version)
        {
            return Stale();
        }

        if (template.IsDefault)
        {
            return Error.Conflict("stage_template_is_default", "Escolha outro modelo padrão antes de excluir este.");
        }

        db.StageTemplateItems.RemoveRange(await db.StageTemplateItems.Where(i => i.TemplateId == id).ToListAsync(cancellationToken));
        db.Entry(template).Property(t => t.Version).OriginalValue = version;
        db.StageTemplates.Remove(template);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Regras com banco: nome único entre os ativos (sem diferenciar maiúsculas) e cargo existente e ativo (o inativo só se já gravado).</summary>
    private async Task<Error?> ValidateAsync(Guid? id, string name, bool active, IReadOnlyList<NormalizedStageStep> steps, HashSet<Guid> storedPositions, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        var ids = steps.Where(s => s.PositionId != null).Select(s => s.PositionId!.Value).Distinct().ToList();
        var found = (await positions.FindAsync(ids, ct)).ToDictionary(p => p.Id);
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].PositionId is { } position && (!found.TryGetValue(position, out var p) || (!p.IsActive && !storedPositions.Contains(position))))
            {
                errors.TryAdd($"steps[{i}].positionId", ["Escolha um cargo ativo."]);
            }
        }

        if (!errors.ContainsKey("name") && active && await NameTakenAsync(id, name, ct))
        {
            errors["name"] = ["Já existe um modelo com este nome."];
        }

        return StageTemplateRules.ToError(errors);
    }

    private Task<bool> NameTakenAsync(Guid? id, string name, CancellationToken ct)
    {
        var pattern = Escape(name.Trim());
        return db.StageTemplates.AnyAsync(t => t.Id != id && t.IsActive && EF.Functions.ILike(t.Name, pattern, "\\"), ct);
    }

    /// <summary>Grava; o índice único de nome (corrida entre dois salvar) vira o 422 no campo.</summary>
    private async Task<Error?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, NameIndex))
        {
            return StageTemplateRules.Invalid("name", "Já existe um modelo com este nome.");
        }
    }

    private static void Apply(StageTemplate template, StageTemplateInput input, IReadOnlyList<BemAqui.Commercial.Contracts.SuggestedMilestone> milestones)
    {
        template.Name = input.Name.Trim();
        template.Segment = string.IsNullOrWhiteSpace(input.Segment) ? null : input.Segment.Trim();
        if (!template.SuggestedMilestones.SequenceEqual(milestones))
        {
            template.SuggestedMilestones = milestones.ToList();
        }
    }

    private static void Apply(StageTemplateStep entity, NormalizedStageStep step)
    {
        entity.Sequence = step.Sequence;
        entity.Name = step.Name;
        entity.Code = step.Code;
        entity.Role = step.Role;
        entity.StandardHoursPerUnit = step.StandardHoursPerUnit;
        entity.PositionId = step.PositionId;
        entity.RequiresApproval = step.RequiresApproval;
        if (!entity.Checklist.SequenceEqual(step.Checklist))
        {
            entity.Checklist = step.Checklist.ToList();
        }
    }
}
