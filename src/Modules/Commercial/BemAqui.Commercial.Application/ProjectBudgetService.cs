using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>Custo orçado de uma natureza na edição direta (COM-09).</summary>
public sealed record ProjectBudgetEditLine(CostType CostType, decimal Amount);

/// <summary>Edição direta do custo orçado (COM-09): as naturezas com o valor novo (as que não vêm ficam como estão), o motivo e a versão do projeto.</summary>
public sealed record ProjectBudgetEditInput(IReadOnlyList<ProjectBudgetEditLine>? Lines, string? Reason, uint Version);

public sealed record ProjectBudgetRevisionLine(CostType CostType, decimal PreviousAmount, decimal NewAmount);

/// <summary>Uma edição do custo orçado (COM-09 CA 6): quem, quando, o motivo e as naturezas alteradas, com o valor anterior e o novo.</summary>
public sealed record ProjectBudgetRevisionView(DateTimeOffset ChangedAt, Guid? ChangedBy, string? ChangedByName, string Reason, IReadOnlyList<ProjectBudgetRevisionLine> Lines);

/// <summary>
/// Edição direta do custo orçado do projeto (COM-09, decisão do PO de 15/11/2026): sem fluxo de aprovação, protegida por
/// <c>projects.edit_budget</c> (rota) mais <c>projects.view_cost</c> (o orçado é sigilo comercial, D7), com motivo obrigatório, conferência
/// da versão do projeto e a trilha em <c>commercial.project_budget_revisions</c> (valor anterior, novo, quem e quando). Projeto encerrado
/// ou cancelado não é editado. A aba Custos, o relatório "Orçado × realizado" e o painel leem o orçado na hora: não há o que recalcular.
/// </summary>
public sealed class ProjectBudgetService(ICommercialDb db, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, TimeProvider clock)
{
    /// <summary>Teto de um valor orçado: cabe em <c>numeric(18,2)</c> com folga para a soma das naturezas.</summary>
    public const decimal MaxAmount = 999_999_999_999.99m;

    /// <summary>Quantas edições o histórico devolve (as mais recentes).</summary>
    public const int HistoryLimit = 100;

    public async Task<Result<IReadOnlyList<ProjectBudgetLine>>> EditAsync(Guid id, ProjectBudgetEditInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Editar o custo orçado exige também a permissão Ver custo e margem. Fale com o administrador.");
        }

        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null)
        {
            return ProjectService.NotFound();
        }

        if (project.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O projeto mudou desde que você abriu. Recarregue.");
        }

        if (project.Status == ProjectStatus.Closed)
        {
            return Error.Conflict("project_closed", "Projeto encerrado: o custo orçado não pode mais ser editado.");
        }

        if (project.Status == ProjectStatus.Cancelled)
        {
            return Error.Conflict("project_cancelled", "Projeto cancelado: o custo orçado não pode mais ser editado.");
        }

        var lines = input.Lines ?? [];
        if (lines.Count == 0)
        {
            return QuoteService.Invalid("lines", "Informe o custo orçado de ao menos uma natureza.");
        }

        if (lines.Any(l => !Enum.IsDefined(l.CostType)) || lines.GroupBy(l => l.CostType).Any(g => g.Count() > 1))
        {
            return QuoteService.Invalid("lines", "Cada natureza de custo aparece uma vez só.");
        }

        if (lines.Any(l => l.Amount < 0 || l.Amount > MaxAmount || decimal.Round(l.Amount, 2) != l.Amount))
        {
            return QuoteService.Invalid("lines", "O custo orçado de cada natureza precisa ser zero ou positivo, com até 2 casas decimais.");
        }

        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > ProjectBudgetRevision.ReasonMaxLength)
        {
            return QuoteService.Invalid("reason", "Informe o motivo da edição, com até 500 caracteres.");
        }

        var budget = await db.ProjectBudgets.Where(b => b.ProjectId == id).ToListAsync(cancellationToken);
        var changed = lines.Select(l => (l.CostType, Line: budget.FirstOrDefault(b => b.CostType == l.CostType), l.Amount))
            .Where(c => (c.Line?.BudgetedAmount ?? 0m) != c.Amount)
            .ToList();
        if (changed.Count == 0)
        {
            return QuoteService.Invalid("lines", "Nenhum valor mudou. Altere o custo orçado de ao menos uma natureza.");
        }

        var now = clock.GetUtcNow();
        foreach (var (type, line, amount) in changed)
        {
            db.ProjectBudgetRevisions.Add(new ProjectBudgetRevision
            {
                ProjectId = id, CostType = type, PreviousAmount = line?.BudgetedAmount ?? 0m, NewAmount = amount, Reason = reason, ChangedAt = now, ChangedBy = currentUser.UserId,
            });
            if (line is null)
            {
                db.ProjectBudgets.Add(new ProjectBudget { ProjectId = id, CostType = type, BudgetedAmount = amount });
            }
            else
            {
                line.BudgetedAmount = amount;
            }
        }

        // A marca no projeto muda a versão (xmin): duas edições sobre a mesma versão dão 409, pelo tratador de concorrência (ADR 0019).
        db.Entry(project).Property(p => p.Version).OriginalValue = input.Version;
        project.BudgetRevisedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return await db.ProjectBudgets.AsNoTracking().Where(b => b.ProjectId == id).OrderBy(b => b.CostType)
            .Select(b => new ProjectBudgetLine(b.CostType, b.BudgetedAmount)).ToListAsync(cancellationToken);
    }

    /// <summary>Histórico das edições (COM-09 CA 6), da mais recente à mais antiga; só com <c>projects.view_cost</c>.</summary>
    public async Task<Result<IReadOnlyList<ProjectBudgetRevisionView>>> RevisionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "O orçado do projeto é restrito. Fale com o administrador.");
        }

        if (!await db.Projects.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return ProjectService.NotFound();
        }

        var rows = await db.ProjectBudgetRevisions.AsNoTracking().Where(r => r.ProjectId == id).OrderByDescending(r => r.ChangedAt).ToListAsync(cancellationToken);
        // Uma edição grava todas as naturezas com o mesmo instante: o grupo é a edição.
        var edits = rows.GroupBy(r => (r.ChangedAt, r.ChangedBy)).ToList();
        var names = new Dictionary<Guid, string?>();
        foreach (var userId in edits.Where(e => e.Key.ChangedBy is not null).Select(e => e.Key.ChangedBy!.Value).Distinct())
        {
            names[userId] = await users.DisplayNameAsync(userId, cancellationToken);
        }

        return edits.Take(HistoryLimit).Select(edit => new ProjectBudgetRevisionView(
            edit.Key.ChangedAt, edit.Key.ChangedBy, edit.Key.ChangedBy is { } by ? names.GetValueOrDefault(by) : null, edit.First().Reason,
            edit.OrderBy(r => r.CostType).Select(r => new ProjectBudgetRevisionLine(r.CostType, r.PreviousAmount, r.NewAmount)).ToList())).ToList();
    }
}
