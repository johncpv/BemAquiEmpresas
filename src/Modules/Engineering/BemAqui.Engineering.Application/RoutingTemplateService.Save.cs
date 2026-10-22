using BemAqui.Engineering.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Engineering.Application;

public sealed partial class RoutingTemplateService
{
    /// <summary>Novo roteiro (CA 2 a 6): gravado ativo, com a sequência 1..n e o centro de cada etapa validado.</summary>
    public async Task<Result<RoutingTemplateDetail>> CreateAsync(RoutingTemplateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (steps, errors) = RoutingRules.Validate(input.Name, input.ProductType, input.Steps);
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Id is not null)
            {
                errors.TryAdd($"steps[{i}].id", ["Etapa não encontrada neste roteiro."]);
            }
        }

        if (await ValidateAsync(null, input.Name, true, steps, errors, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var template = new RoutingTemplate { CreatedAt = clock.GetUtcNow() };
        Apply(template, input.Name, input.ProductType);
        db.RoutingTemplates.Add(template);
        foreach (var step in steps)
        {
            var entity = new RoutingTemplateStep { TemplateId = template.Id };
            Apply(entity, step);
            db.RoutingTemplateSteps.Add(entity);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(template, cancellationToken);
    }

    /// <summary>
    /// Edição (CA 3 a 6, 12, 17): etapas com id são as deste roteiro (id de outro roteiro é recusado, CA 16), as que saíram da
    /// lista são removidas e a sequência é regravada; a versão (xmin) do roteiro sobe a cada gravação, mesmo quando só as
    /// etapas mudam. As OS já criadas não mudam: a OS guarda a própria cópia (PRD-01).
    /// </summary>
    public async Task<Result<RoutingTemplateDetail>> UpdateAsync(Guid id, RoutingTemplateUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var template = await db.RoutingTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if (template.Version != input.Version)
        {
            return Stale();
        }

        var existing = await db.RoutingTemplateSteps.Where(s => s.TemplateId == id).ToDictionaryAsync(s => s.Id, cancellationToken);
        var (steps, errors) = RoutingRules.Validate(input.Name, input.ProductType, input.Steps);
        var seen = new HashSet<Guid>();
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Id is { } stepId && (!existing.ContainsKey(stepId) || !seen.Add(stepId)))
            {
                errors.TryAdd($"steps[{i}].id", ["Etapa não encontrada neste roteiro."]);
            }
        }

        if (await ValidateAsync(id, input.Name, template.IsActive, steps, errors, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.Entry(template).Property(t => t.Version).OriginalValue = input.Version;
        Apply(template, input.Name, input.ProductType);
        foreach (var step in steps)
        {
            var entity = step.Id is { } stepId ? existing[stepId] : new RoutingTemplateStep { TemplateId = id };
            Apply(entity, step);
            if (step.Id is null)
            {
                db.RoutingTemplateSteps.Add(entity);
            }
        }

        db.RoutingTemplateSteps.RemoveRange(existing.Values.Where(s => !seen.Contains(s.Id)));
        // Toda gravação passa pelo xmin do roteiro, inclusive a que só muda etapas (CA 17).
        db.Entry(template).State = EntityState.Modified;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(template, cancellationToken);
    }

    /// <summary>Regras com banco: nome único entre os ativos (sem diferenciar maiúsculas) e centro ativo e existente (CA 2, 5).</summary>
    private async Task<Error?> ValidateAsync(Guid? id, string name, bool active, IReadOnlyList<NormalizedStep> steps, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        RoutingRules.RequireWorkCenters(steps, errors);
        var ids = steps.Where(s => s.WorkCenterId != null).Select(s => s.WorkCenterId!.Value).Distinct().ToList();
        var centers = (await workCenters.FindAsync(ids, ct)).Where(c => c.IsActive).Select(c => c.Id).ToHashSet();
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].WorkCenterId is { } center && !centers.Contains(center))
            {
                errors.TryAdd($"steps[{i}].workCenterId", ["Escolha um centro de trabalho ativo."]);
            }
        }

        if (!errors.ContainsKey("name") && active && await NameTakenAsync(id, name, ct))
        {
            errors["name"] = ["Já existe um roteiro com este nome."];
        }

        return RoutingRules.ToError(errors);
    }

    private Task<bool> NameTakenAsync(Guid? id, string name, CancellationToken ct)
    {
        var pattern = Escape(name.Trim());
        return db.RoutingTemplates.AnyAsync(t => t.Id != id && t.IsActive && EF.Functions.ILike(t.Name, pattern, "\\"), ct);
    }

    private static void Apply(RoutingTemplate template, string name, string? productType)
    {
        template.Name = name.Trim();
        template.ProductType = string.IsNullOrWhiteSpace(productType) ? null : productType.Trim();
    }

    private static void Apply(RoutingTemplateStep entity, NormalizedStep step)
    {
        entity.Sequence = step.Sequence;
        entity.Name = step.Name;
        entity.Code = step.Code;
        entity.WorkCenterId = step.WorkCenterId;
        entity.StandardHours = step.StandardHours;
        entity.IsQualityStep = step.IsQualityStep;
        entity.RequiresApproval = step.RequiresApproval;
        if (!entity.Checklist.SequenceEqual(step.Checklist))
        {
            entity.Checklist = step.Checklist.ToList();
        }
    }

    private static Error Stale() => Error.Conflict("concurrency_conflict", "O roteiro mudou desde que você abriu. Recarregue.");
}