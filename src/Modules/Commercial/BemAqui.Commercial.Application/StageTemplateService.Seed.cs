using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>Modelo sugerido do pacote do perfil Serviços (proposta §6).</summary>
public sealed record StageTemplateSeed(string Key, string Name, IReadOnlyList<StageStepSeed> Steps, IReadOnlyList<SuggestedMilestone> Milestones);

public sealed record StageStepSeed(string Code, string Name, StageRole Role, decimal Hours, string[] Checklist);

public sealed partial class StageTemplateService
{
    public const string SoftwareSeedKey = "service-software-rollout";

    /// <summary>
    /// Os quatro modelos da proposta §6 com os marcos sugeridos. Horas que a proposta não dá ficam 0. No modelo de consultoria, o "P Plano de
    /// ação" (entrega parcial na proposta) é Execução, porque o modelo tem uma Entrega só e nada de Execução depois dela (CA 3); o marco de 30%
    /// continua ligado à conclusão do P.
    /// </summary>
    public static readonly IReadOnlyList<StageTemplateSeed> Seeds =
    [
        new(SoftwareSeedKey, "Implantação de software",
            [
                new("K", "Kickoff", StageRole.Execution, 4m, []),
                new("L", "Levantamento e configuração", StageRole.Execution, 16m, []),
                new("I", "Integrações e dados", StageRole.Execution, 16m, []),
                new("T", "Treinamento", StageRole.Execution, 6m, []),
                new("G", "Go-live", StageRole.Delivery, 4m, []),
                new("A", "Aceite do cliente", StageRole.Acceptance, 1m, []),
            ],
            [new(50m, SuggestedMilestoneTrigger.Signature, null, 0), new(50m, SuggestedMilestoneTrigger.TechnicalAcceptance, null, 0)]),
        new("service-consulting", "Consultoria / projeto com entregáveis",
            [
                new("D", "Diagnóstico", StageRole.Execution, 8m, []),
                new("P", "Plano de ação", StageRole.Execution, 8m, []),
                new("E", "Execução acompanhada", StageRole.Execution, 24m, []),
                new("R", "Relatório final", StageRole.Delivery, 6m, []),
                new("A", "Aceite", StageRole.Acceptance, 0m, []),
            ],
            [new(30m, SuggestedMilestoneTrigger.Signature, null, 0), new(30m, SuggestedMilestoneTrigger.StageCompleted, "P", 0), new(40m, SuggestedMilestoneTrigger.TechnicalAcceptance, null, 0)]),
        new("service-installation", "Obra / instalação",
            [
                new("V", "Vistoria", StageRole.Execution, 2m, []),
                new("M", "Mobilização", StageRole.Execution, 4m, []),
                new("X", "Execução", StageRole.Execution, 40m, ["Equipe com EPI conferido", "Área isolada e sinalizada", "Ferramentas e equipamentos inspecionados", "Permissão de trabalho emitida quando exigida"]),
                new("T", "Testes", StageRole.Execution, 4m, []),
                new("E", "Entrega técnica", StageRole.Delivery, 2m, []),
                new("A", "Termo de aceite", StageRole.Acceptance, 0m, []),
            ],
            [new(40m, SuggestedMilestoneTrigger.Signature, null, 0), new(40m, SuggestedMilestoneTrigger.StageCompleted, "X", 0), new(20m, SuggestedMilestoneTrigger.TechnicalAcceptance, null, 0)]),
        new("service-maintenance", "Manutenção programada",
            [
                new("P", "Programação", StageRole.Execution, 1m, []),
                new("V", "Visita e checklist", StageRole.Execution, 3m, []),
                new("R", "Relatório", StageRole.Delivery, 1m, []),
            ],
            [new(100m, SuggestedMilestoneTrigger.Delivered, null, 0)]),
    ];

    /// <summary>
    /// Pacote <c>projects.services</c> e o botão "Usar os sugeridos" (CA 11): cria só o que falta pela <c>seed_key</c> (inclusive entre os
    /// excluídos, que não voltam), não duplica um nome ativo do tenant, nunca altera o que existe e só dá o padrão do tenant à "Implantação de
    /// software" se ainda não houver padrão. Devolve quantos criou.
    /// </summary>
    public async Task<int> SeedSuggestedAsync(CancellationToken cancellationToken = default)
    {
        var seeded = (await db.StageTemplates.IgnoreQueryFilters().Where(t => t.SeedKey != null).Select(t => t.SeedKey!).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var names = (await db.StageTemplates.Where(t => t.IsActive).Select(t => t.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasDefault = await db.StageTemplates.AnyAsync(t => t.IsDefault && t.Segment == null, cancellationToken);
        var created = 0;
        foreach (var seed in Seeds.Where(s => !seeded.Contains(s.Key) && !names.Contains(s.Name)))
        {
            var template = new StageTemplate
            {
                Name = seed.Name, SeedKey = seed.Key, Revision = 1, CreatedAt = clock.GetUtcNow(), SuggestedMilestones = seed.Milestones.ToList(),
                IsDefault = seed.Key == SoftwareSeedKey && !hasDefault,
            };
            db.StageTemplates.Add(template);
            foreach (var (step, i) in seed.Steps.Select((s, i) => (s, i)))
            {
                db.StageTemplateSteps.Add(new StageTemplateStep
                {
                    TemplateId = template.Id, Sequence = i + 1, Code = step.Code, Name = step.Name, Role = step.Role, StandardHoursPerUnit = step.Hours, Checklist = step.Checklist.ToList(),
                });
            }

            created++;
        }

        if (created == 0)
        {
            return 0;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Outra rodada do pacote (deploy e aba Perfis ao mesmo tempo) criou antes: nada a fazer, a próxima rodada confere de novo.
            return 0;
        }

        return created;
    }
}
