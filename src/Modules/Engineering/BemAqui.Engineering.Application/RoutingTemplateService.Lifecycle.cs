using BemAqui.Engineering.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Engineering.Application;

public sealed partial class RoutingTemplateService
{
    /// <summary>Inativar (CA 11): some dos seletores e da sugestão pelo produto; continua visível em quem já usa.</summary>
    public async Task<Result<RoutingTemplateDetail>> InactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await db.RoutingTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        template.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(template, cancellationToken);
    }

    /// <summary>Ativar (CA 8): toda etapa com centro ativo e o nome sem repetir entre os ativos.</summary>
    public async Task<Result<RoutingTemplateDetail>> ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await db.RoutingTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        var steps = await db.RoutingTemplateSteps.AsNoTracking().Where(s => s.TemplateId == id).ToListAsync(cancellationToken);
        var ids = steps.Where(s => s.WorkCenterId != null).Select(s => s.WorkCenterId!.Value).Distinct().ToList();
        var active = (await workCenters.FindAsync(ids, cancellationToken)).Where(c => c.IsActive).Select(c => c.Id).ToHashSet();
        if (steps.Count == 0 || steps.Any(s => s.WorkCenterId is not { } c || !active.Contains(c)))
        {
            return RoutingRules.Invalid("steps", "Escolha o centro de trabalho de cada etapa para ativar.");
        }

        if (await NameTakenAsync(id, template.Name, cancellationToken))
        {
            return RoutingRules.Invalid("name", "Já existe um roteiro ativo com este nome. Renomeie antes de ativar.");
        }

        template.IsActive = true;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(template, cancellationToken);
    }

    /// <summary>
    /// Exclusão lógica só sem uso (CA 10, ADR 0016 D10): os vínculos de produto são apagados; com uso, 409 e o diálogo oferece
    /// inativar. Até a PRD-01 não há provedor de uso.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await db.RoutingTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        if ((await UsageAsync([id], cancellationToken)).GetValueOrDefault(id) is { Count: > 0 } used)
        {
            return Error.Conflict("routing_template_in_use", $"O roteiro é usado em {used.Text}. Inative-o.");
        }

        db.RoutingTemplateItems.RemoveRange(await db.RoutingTemplateItems.Where(i => i.TemplateId == id).ToListAsync(cancellationToken));
        db.RoutingTemplates.Remove(template);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Roteiro de exemplo (CA 8, P-04, D4): Projeto → Corte → Montagem → Qualidade → Expedição, horas 0, com o centro quando
    /// só há um ativo; sem centro, nasce inativo. Pela <c>seed_key</c>, não recria o que o tenant excluiu ou alterou.
    /// </summary>
    public async Task SeedExampleAsync(CancellationToken cancellationToken = default)
    {
        if (await db.RoutingTemplates.IgnoreQueryFilters().AnyAsync(t => t.SeedKey == RoutingTemplate.ExampleSeedKey, cancellationToken))
        {
            return;
        }

        var centers = await workCenters.ActiveAsync(cancellationToken);
        Guid? center = centers.Count == 1 ? centers[0].Id : null;
        var template = new RoutingTemplate { Name = ExampleName, IsActive = center is not null, SeedKey = RoutingTemplate.ExampleSeedKey, CreatedAt = clock.GetUtcNow() };
        db.RoutingTemplates.Add(template);
        foreach (var (step, i) in ExampleSteps.Select((s, i) => (s, i)))
        {
            db.RoutingTemplateSteps.Add(new RoutingTemplateStep
            {
                TemplateId = template.Id, Sequence = i + 1, Code = step.Code, Name = step.Name, WorkCenterId = center, StandardHours = 0, IsQualityStep = step.Quality,
                Checklist = step.Checklist.Select((t, n) => new BemAqui.Engineering.Contracts.RoutingChecklistItem(n + 1, t)).ToList(),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public const string ExampleName = "Roteiro padrão (exemplo)";

    /// <summary>Etapas do exemplo (P-04); a Expedição é "preparar e embalar" e a baixa do estoque é do romaneio (D4, PRD-06b).</summary>
    public static readonly IReadOnlyList<(string Code, string Name, bool Quality, string[] Checklist)> ExampleSteps =
    [
        ("P", "Projeto", false, []),
        ("C", "Corte", false, []),
        ("M", "Montagem", false, []),
        ("Q", "Qualidade", true, []),
        ("E", "Expedição", false, ["Preparar e embalar para o romaneio"]),
    ];
}