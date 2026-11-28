using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// O que o resumo do projeto em PDF mostra (EXP-05 CA 5): número, nome, cliente, responsável, obra, a etapa (P E F I T C), prometido e atraso,
/// itens e OS, cronograma (os marcos do contrato) e, <b>só para quem vê na tela</b>, os blocos Custos (<c>projects.view_cost</c>) e Financeiro
/// (<c>finance.view</c>): o bloco sem a permissão vem nulo e nem é lido (o serviço não chama a consulta).
/// </summary>
public sealed record ProjectSummaryPdfModel(
    string Number, string Name, ProjectStatus Status, string StatusLabel, string Customer, string? Manager, string? SiteAddress, int StageIndex, DateOnly? PromisedDate,
    DateOnly? ForecastDate, int? DelayDays, string ContractNumber, IReadOnlyList<ProjectSummaryPdfItem> Items, IReadOnlyList<ProjectSummaryPdfOrder>? Orders,
    IReadOnlyList<ProjectSummaryPdfMilestone> Milestones, ProjectSummaryPdfCosts? Costs, ProjectSummaryPdfFinance? Finance, DateOnly IssueDate)
{
    /// <summary>Letras do indicador pelo tipo (EXE-01b CA 14): P E F I T C na fabricação, P I E A C no serviço.</summary>
    public IReadOnlyList<string> Letters { get; init; } = ProjectStageMap.FabricationLetters;

    /// <summary>Só a data do aceite do cliente (EXE-01b CA 14): nunca quem aceitou (LGPD).</summary>
    public DateOnly? AcceptedOn { get; init; }

    /// <summary>Etapa atual do modelo (EXE-03a CA 9): letra, nome e posição; nunca o responsável.</summary>
    public ProjectCurrentStage? CurrentStage { get; init; }
}

public sealed record ProjectSummaryPdfItem(int Sequence, string Description, decimal Quantity);

public sealed record ProjectSummaryPdfOrder(string Number, string Product, string? CurrentStep, string Status);

public sealed record ProjectSummaryPdfMilestone(int Sequence, string Description, decimal Percent, decimal Amount, string When);

public sealed record ProjectSummaryPdfCostLine(string Nature, decimal Budgeted, decimal Realized, decimal Projected, decimal Deviation);

public sealed record ProjectSummaryPdfCosts(
    IReadOnlyList<ProjectSummaryPdfCostLine> Lines, ProjectSummaryPdfCostLine Total, decimal? BudgetedMarginPct, decimal? RealizedMarginPct, decimal? ProjectedMarginPct,
    bool NoCostBudget = false);

public sealed record ProjectSummaryPdfFinance(decimal ToReceive, decimal Received, decimal ReceivedPct);

/// <summary>Montagem pura do resumo do projeto (EXP-05 CA 5): etapa, atraso, marca "Cancelado" e os blocos por permissão.</summary>
public static class ProjectSummaryPdf
{
    // EXE-01a CA 29: etapas pela função única ProjectStageMap (o PDF do projeto de serviço é da EXE-01b).
    public static readonly IReadOnlyList<string> Letters = ProjectStageMap.FabricationLetters;

    public static int StageIndex(ProjectStatus status) => ProjectStageMap.For(ExecutionKind.Fabrication, status).Index;

    public static string StatusLabel(ProjectStatus status) => ProjectStageMap.For(ExecutionKind.Fabrication, status).Label;

    /// <summary>"Etapa atual: T Treinamento (2 de 5)" (EXE-03a CA 9).</summary>
    public static string CurrentStageText(ProjectCurrentStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return $"Etapa atual: {stage.Code} {stage.Name} ({stage.Sequence} de {stage.Total})";
    }

    public static string OrderStatusLabel(ProductionOrderState status) => status switch
    {
        ProductionOrderState.Open => "Aberta",
        ProductionOrderState.InProgress => "Em produção",
        ProductionOrderState.Paused => "Pausada",
        ProductionOrderState.Quality => "Aguardando qualidade",
        ProductionOrderState.ReadyToComplete => "Pronta para concluir",
        ProductionOrderState.Completed => "Concluída",
        _ => "Cancelada",
    };

    public static string CostLabel(CostType type) => type switch
    {
        CostType.Material => "Material",
        CostType.Labor => "Mão de obra",
        CostType.Installation => "Instalação",
        CostType.Freight => "Frete",
        CostType.Tax => "Impostos",
        _ => "Outros",
    };

    /// <summary>Atraso em dias: o da previsão; sem ela, prometido já passado e o projeto ainda não entregue.</summary>
    public static int? Delay(ProjectDetail project, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Forecast?.DelayDays is { } days)
        {
            return days > 0 ? days : null;
        }

        var open = project.Status is ProjectStatus.Planned or ProjectStatus.InProgress or ProjectStatus.Fabricated or ProjectStatus.Installing;
        return open && project.PromisedDate is { } promised && promised < today ? today.DayNumber - promised.DayNumber : null;
    }

    public static (PdfMark Mark, string? Notice) Mark(ProjectStatus status) => status == ProjectStatus.Cancelled ? (PdfMark.Cancelled, null) : (PdfMark.None, null);

    public static string When(ContractMilestoneView m, ExecutionKind kind = ExecutionKind.Fabrication)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (kind == ExecutionKind.Service && m.Trigger != MilestoneTrigger.Date)
        {
            return $"{(m.DaysAfterEvent is > 0 ? $"{m.DaysAfterEvent} dias após " : "na ")}{MilestoneEventLabels.Lower(m.TriggerEvent, kind)}";
        }

        return m.Trigger == MilestoneTrigger.Date
            ? m.DueDate is { } due ? "em " + PdfTheme.Date(due) : "data a definir"
            : $"{(m.DaysAfterEvent is > 0 ? $"{m.DaysAfterEvent} dias após " : "na ")}{m.TriggerEvent switch
            {
                TriggerEvent.Signature => "assinatura do contrato",
                TriggerEvent.FabricationDone => "fabricação concluída",
                TriggerEvent.Delivered => "entrega",
                _ => "aceite técnico",
            }}";
    }

    public static ProjectSummaryPdfCosts Costs(ProjectCostView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        static ProjectSummaryPdfCostLine Line(ProjectMarginLine l, string label) => new(label, l.Budgeted, l.Realized, l.Projected, l.Deviation);
        return new ProjectSummaryPdfCosts(view.Lines.Select(l => Line(l, CostLabel(l.CostType))).ToList(), Line(view.Total, "Total"), view.BudgetedMarginPct, view.RealizedMarginPct,
            view.ProjectedMarginPct, view.NoCostBudget);
    }

    /// <summary>% recebido do contratado (como a coluna da lista, 1 casa); contrato zerado, 0.</summary>
    public static ProjectSummaryPdfFinance Finance(ProjectFinancial financial, decimal contracted)
    {
        ArgumentNullException.ThrowIfNull(financial);
        return new ProjectSummaryPdfFinance(financial.ToReceive, financial.Received,
            contracted == 0 ? 0 : Math.Round(financial.Received / contracted * 100, 1, MidpointRounding.AwayFromZero));
    }

    public static ProjectSummaryPdfModel Build(
        ProjectDetail project, ContractDetail? contract, IReadOnlyList<ProductionOrderInfo>? orders, ProjectCostView? costs, ProjectFinancial? financial, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(project);
        var items = project.Items.OrderBy(i => i.Sequence).Select(i => new ProjectSummaryPdfItem(i.Sequence, i.Description, i.Quantity)).ToList();
        var descriptions = project.Items.ToDictionary(i => i.Id, i => i.Description);
        var orderRows = project.ExecutionKind == ExecutionKind.Service ? null : orders?.Where(o => o.Type == ProductionOrderKind.Fabrication).OrderBy(o => o.Number, StringComparer.Ordinal)
            .Select(o => new ProjectSummaryPdfOrder(o.Number, descriptions.GetValueOrDefault(o.ProjectItemId, "—"),
                o.CurrentStepCode is { } code ? $"{code} · {o.CurrentStepName}" : null, OrderStatusLabel(o.Status))).ToList();
        var milestones = contract?.Milestones.OrderBy(m => m.Sequence).Select(m => new ProjectSummaryPdfMilestone(m.Sequence, m.Description, m.Percent, m.Amount, When(m, project.ExecutionKind))).ToList() ?? [];
        var stage = project.Stage ?? ProjectStageMap.For(project.ExecutionKind, project.Status, project.AcceptedOn is not null, project.AcceptanceRequired);
        var contracted = contract is null ? 0 : contract.CurrentValue ?? contract.TotalValue;
        return new ProjectSummaryPdfModel(
            project.Number, project.Name, project.Status, stage.Label, project.PartyName, project.ManagerName, project.SiteAddress, stage.Index,
            project.PromisedDate, project.ForecastDate, Delay(project, today), project.ContractNumber, items, orderRows, milestones, costs is null ? null : Costs(costs),
            financial is null ? null : Finance(financial, contracted), today)
        {
            Letters = stage.Letters,
            AcceptedOn = project.ExecutionKind == ExecutionKind.Service ? project.AcceptedOn : null,
            CurrentStage = project.ExecutionKind == ExecutionKind.Service ? project.CurrentStage : null,
        };
    }
}
