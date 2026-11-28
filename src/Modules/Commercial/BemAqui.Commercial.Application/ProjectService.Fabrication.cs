using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class ProjectService
{
    public const string PastFabricationMessage = "O projeto já passou da fabricação.";

    /// <summary>EXE-01a: "Projeto de serviço não tem fabricação." (409 <c>not_a_fabrication_project</c> e motivo do estorno da OS).</summary>
    public const string NotAFabricationMessage = "Projeto de serviço não tem fabricação.";

    /// <summary>"A parcela {marco} já foi recebida. Fale com o Financeiro antes de estornar." (PRD-06a1 CA 11, ponto 83).</summary>
    public static string ReceivedMessage(string milestone) => $"A parcela {milestone} já foi recebida. Fale com o Financeiro antes de estornar.";

    /// <summary>"A fabricação deste projeto é concluída pelas OS ({n} abertas)." (PRD-06a1 CA 9, ponto 81).</summary>
    public static string ByOrdersMessage(int open) => $"A fabricação deste projeto é concluída pelas OS ({open} {(open == 1 ? "aberta" : "abertas")}).";

    /// <summary>
    /// Fabricação do projeto para a Produção (PRD-06a1 CA 2, 10 e 11): parcelas "fabricação concluída" do contrato e dos aditivos aprovados,
    /// com o título quando existe, e o motivo que recusa o estorno: projeto em <c>Installing</c>, <c>Delivered</c> ou <c>Closed</c>, ou projeto
    /// fabricado pelas OS com a parcela já recebida (baixa total ou parcial). Projeto fabricado à mão não muda com o estorno da OS.
    /// </summary>
    async Task<ProjectFabricationInfo?> IProjects.FabricationAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        // EXE-01a: projeto de serviço não tem fabricação nem parcela de fabricação a mostrar.
        if (project.ExecutionKind == ExecutionKind.Service)
        {
            return new ProjectFabricationInfo(project.Id, project.Number, project.Status.ToString(), project.FabricatedOn, false, [], NotAFabricationMessage);
        }

        var milestones = await FabricationMilestonesAsync(project.ContractId, cancellationToken);
        string? blocker = null;
        if (project.Status is ProjectStatus.Installing or ProjectStatus.Delivered or ProjectStatus.Closed)
        {
            blocker = PastFabricationMessage;
        }
        else if (project.Status == ProjectStatus.Fabricated && project.FabricatedByOrders && milestones.FirstOrDefault(m => m.Received) is { } received)
        {
            blocker = ReceivedMessage(received.Description);
        }

        return new ProjectFabricationInfo(project.Id, project.Number, project.Status.ToString(), project.FabricatedOn, project.FabricatedByOrders, milestones, blocker);
    }

    /// <summary>Marcos com gatilho "fabricação concluída" (contrato e aditivos aprovados) e os títulos deles no Finance.</summary>
    private async Task<IReadOnlyList<FabricationMilestoneInfo>> FabricationMilestonesAsync(Guid contractId, CancellationToken ct)
    {
        var own = await db.ContractMilestones.AsNoTracking().Where(m => m.ContractId == contractId && m.TriggerEvent == TriggerEvent.FabricationDone)
            .OrderBy(m => m.Sequence).Select(m => new { m.Id, m.Description, m.Amount, m.DaysAfterEvent }).ToListAsync(ct);
        var amended = await (from m in db.ContractAmendmentMilestones.AsNoTracking()
                             join a in db.ContractAmendments.AsNoTracking() on m.AmendmentId equals a.Id
                             where a.ContractId == contractId && a.Status == AmendmentStatus.Approved && m.TriggerEvent == TriggerEvent.FabricationDone
                             orderby a.Number
                             select new { m.Id, m.Description, m.Amount, m.DaysAfterEvent }).ToListAsync(ct);
        var all = own.Concat(amended).ToList();
        if (all.Count == 0)
        {
            return [];
        }

        var titles = (await receivables.ForContractAsync(contractId, ct)).Where(r => r.Status != ReceivableStatus.Cancelled).ToLookup(r => r.MilestoneId);
        return all.Select(m =>
        {
            var title = titles[m.Id].FirstOrDefault();
            var received = title is not null && (title.HasSettlement || title.Status is ReceivableStatus.PartiallyPaid or ReceivableStatus.Paid);
            return new FabricationMilestoneInfo(
                m.Id, m.Description, m.Amount, m.DaysAfterEvent ?? 0, title?.Id, title?.Number, title is not null && title.Status != ReceivableStatus.Planned, received);
        }).ToList();
    }

    /// <summary>OS de fabricação não canceladas do projeto (PRD-06a1 CA 9): quantas e quantas ainda não concluíram.</summary>
    private async Task<(int Total, int Open)> FabricationOrdersAsync(Guid projectId, CancellationToken ct)
    {
        var orders = (await productionOrders.ByProjectAsync(projectId, ct))
            .Where(o => o.Type == ProductionOrderKind.Fabrication && o.Status != ProductionOrderState.Cancelled).ToList();
        return (orders.Count, orders.Count(o => o.Status != ProductionOrderState.Completed));
    }

    public const string InstallationPendingMessage =
        "Este projeto tem instalação: a entrega e o aceite técnico vêm da OS de instalação. Abra a OS de instalação.";

    /// <summary>
    /// Instalação do projeto (COM-04b CA 10, COM-06): o projeto tem instalação quando o orçado de Instalação é maior que zero ou há OS de
    /// instalação não cancelada; <c>Open</c> conta as OS abertas ou em andamento.
    /// </summary>
    private async Task<(bool Has, int Open)> InstallationAsync(Guid projectId, CancellationToken ct)
    {
        var orders = (await installations.ByProjectAsync(projectId, ct)).Where(o => o.Status != InstallationState.Cancelled).ToList();
        var open = orders.Count(o => o.Status is InstallationState.Open or InstallationState.InProgress);
        var has = orders.Count > 0
            || await db.ProjectBudgets.AnyAsync(b => b.ProjectId == projectId && b.CostType == CostType.Installation && b.BudgetedAmount > 0m, ct);
        return (has, open);
    }
}