using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Modelo da OS de instalação (COM-06 CA 4, P-22): etapas e checklist que toda OS nova copia. O seed traz o padrão sugerido;
/// o tenant edita em Produção → Modelo da OS de instalação, e as OS já criadas não mudam.
/// </summary>
public sealed class InstallationTemplateService(IProductionDb db)
{
    public static readonly string[] DefaultSteps = ["Preparação da obra", "Montagem na obra", "Testes e ajustes", "Entrega técnica"];

    public static readonly string[] DefaultChecklist =
    [
        "Equipamento instalado conforme projeto", "Testes de segurança feitos", "Cliente orientado", "Documentação entregue", "Área limpa",
    ];

    public async Task<InstallationTemplateView> GetAsync(CancellationToken cancellationToken = default)
    {
        var lines = await db.InstallationTemplate.AsNoTracking().OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        return lines.Count == 0
            ? new InstallationTemplateView(DefaultSteps, DefaultChecklist)
            : new InstallationTemplateView(lines.Where(l => l.Kind == TemplateLineKind.Step).Select(l => l.Text).ToList(), lines.Where(l => l.Kind == TemplateLineKind.Checklist).Select(l => l.Text).ToList());
    }

    /// <summary>Troca o modelo inteiro; a última etapa é sempre "Entrega técnica", que só conclui pelo aceite.</summary>
    public async Task<Result<InstallationTemplateView>> SaveAsync(InstallationTemplateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var steps = input.Steps.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var checklist = input.Checklist.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (steps.Count == 0 || steps[^1] != DefaultSteps[^1])
        {
            return Invalid("steps", "A última etapa é \"Entrega técnica\", concluída pelo aceite.");
        }

        if (steps.Concat(checklist).Any(t => t.Length > 200) || steps.Count > 20 || checklist.Count > 40)
        {
            return Invalid("steps", "Até 20 etapas e 40 itens de checklist, com até 200 caracteres cada.");
        }

        if (checklist.Count == 0)
        {
            return Invalid("checklist", "Inclua ao menos um item no checklist de entrega técnica.");
        }

        db.InstallationTemplate.RemoveRange(await db.InstallationTemplate.ToListAsync(cancellationToken));
        Add(steps, checklist);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }

    /// <summary>Seed idempotente: grava o padrão só se o tenant ainda não tem modelo.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.InstallationTemplate.AnyAsync(cancellationToken))
        {
            return;
        }

        Add(DefaultSteps, DefaultChecklist);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void Add(IReadOnlyList<string> steps, IReadOnlyList<string> checklist)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            db.InstallationTemplate.Add(new InstallationTemplateLine { Kind = TemplateLineKind.Step, Sequence = i + 1, Text = steps[i] });
        }

        for (var i = 0; i < checklist.Count; i++)
        {
            db.InstallationTemplate.Add(new InstallationTemplateLine { Kind = TemplateLineKind.Checklist, Sequence = 100 + i + 1, Text = checklist[i] });
        }
    }

    internal static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });
}
