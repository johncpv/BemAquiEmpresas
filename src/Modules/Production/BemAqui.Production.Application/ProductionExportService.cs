using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>Linha exportada da lista de OS de fabricação (com o fim previsto do plano).</summary>
public sealed record FabricationOrderExportRow(FabricationOrderSummary Order, DateOnly? PlannedEnd);

/// <summary>Linha exportada da lista de NC (com o custo estimado, que some do arquivo sem <c>projects.view_cost</c>).</summary>
public sealed record NonConformityExportRow(NonConformitySummary Nc, decimal? Cost);

/// <summary>
/// Exportação das listas da produção (EXP-04 CA 15 e 16): OS de fabricação, OS de instalação e não conformidades, só em xlsx, com os mesmos
/// filtros da tela (inclusive "Aguardando você" das NC) pela mesma consulta da lista, sem paginação. Nunca custo da OS nem valor do
/// instalador; o custo estimado da NC sai só com <c>projects.view_cost</c> (coluna omitida sem a ação).
/// </summary>
public sealed class ProductionExportService(
    FabricationOrderService fabrication, InstallationOrderService installations, NonConformityService nonConformities, IProductionSchedule schedule, IProductionDb db)
{
    private static readonly ExportSensitivity ViewCost = ExportSensitivity.Omitted("projects.view_cost");

    public static readonly IReadOnlyDictionary<string, string> FabricationSituations = new Dictionary<string, string>
    {
        ["aberta"] = "Abertas", ["andamento"] = "Em produção", ["pronta"] = "Prontas para concluir", ["concluida"] = "Concluídas", ["cancelada"] = "Canceladas",
    };

    public static readonly IReadOnlyDictionary<string, string> InstallationSituations = new Dictionary<string, string>
    {
        ["aberta"] = "Abertas", ["andamento"] = "Em instalação", ["concluida"] = "Concluídas", ["cancelada"] = "Canceladas",
    };

    public static readonly IReadOnlyDictionary<string, string> NonConformitySituations = new Dictionary<string, string>
    {
        ["aberta"] = "Abertas", ["acao"] = "Em ação", ["encerrada"] = "Encerradas", ["pendente"] = "Não encerradas",
    };

    public static readonly ExportSpec<FabricationOrderExportRow> FabricationOrders = new ExportSpec<FabricationOrderExportRow>("ProductionOrder", "os-de-fabricacao", "OS de fabricação")
        .Text("Número", r => r.Order.Number)
        .Text("Projeto", r => r.Order.ProjectNumber)
        .Text("Cliente", r => r.Order.PartyName)
        .Text("Produto", r => r.Order.ProductName ?? r.Order.ItemDescription)
        .Quantity("Quantidade", r => r.Order.Quantity)
        .Text("Etapa atual", r => CurrentStep(r.Order))
        .Date("Data prometida", r => r.Order.PromisedDate)
        .Date("Fim previsto", r => r.PlannedEnd)
        .Text("Atrasada", r => r.Order.DelayDays is > 0 ? "Sim" : "Não")
        .Text("Situação", r => ProductionOrderPdf.StatusLabel(r.Order.Status));

    public static readonly ExportSpec<InstallationSummary> Installations = new ExportSpec<InstallationSummary>("ProductionOrder", "os-de-instalacao", "OS de instalação")
        .Text("Número", r => r.Number)
        .Text("Projeto", r => $"{r.ProjectNumber} · {r.ProjectName}")
        .Text("Instalador", r => r.InstallerName)
        .Date("Início previsto", r => r.PlannedStart)
        .Date("Fim previsto", r => r.PlannedEnd)
        .Text("Etapas concluídas", r => $"{r.StepsDone} de {r.StepsTotal}")
        .Text("Situação", r => InstallationPdf.StatusLabel(r.Status));

    public static readonly ExportSpec<NonConformityExportRow> NonConformities = new ExportSpec<NonConformityExportRow>("NonConformity", "nao-conformidades", "Não conformidades")
        .Text("Número", r => r.Nc.Number)
        .Text("OS", r => r.Nc.OrderNumber)
        .Text("Projeto", r => r.Nc.ProjectNumber)
        .Text("Etapa de qualidade", r => r.Nc.StepCode is { Length: > 0 } code ? $"{code} · {r.Nc.StepName}" : r.Nc.StepName)
        .Text("Responsável", r => r.Nc.ResponsibleName)
        .Text("Situação", r => NonConformityPdf.StatusLabel(r.Nc.Status))
        .Quantity("Idade (dias)", r => r.Nc.AgeDays)
        .Money("Custo estimado", r => r.Cost, ViewCost);

    /// <summary>"C · Corte"; sem roteiro, "Sem roteiro"; concluída, a última etapa.</summary>
    public static string CurrentStep(FabricationOrderSummary order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.NoRouting || order.Codes.Count == 0)
        {
            return "Sem roteiro";
        }

        var i = Math.Clamp(order.CurrentStepIndex, 0, order.Codes.Count - 1);
        return $"{order.Codes[i]} · {order.StepNames[i]}";
    }

    public async Task<Result<ExportData<FabricationOrderExportRow>>> FabricationOrdersAsync(FabricationListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await fabrication.ListAsync(query, new PageRequest(1, max), ct);
        IReadOnlyList<OrderForecastInfo> forecasts = page.Total > max || page.Items.Count == 0 ? [] : await schedule.ForOrdersAsync(page.Items.Select(o => o.Id).ToList(), ct);
        var ends = forecasts.ToDictionary(f => f.OrderId, f => f.PlannedEnd);
        ExportFilter[] filters =
        [
            new("Situação", query.Situation is { } s ? FabricationSituations.GetValueOrDefault(s, s) : null),
            new("Linha", string.IsNullOrWhiteSpace(query.Line) ? null : query.Line.Trim()),
            new("Projeto", query.ProjectId is null ? null : "Um projeto"),
            new("Cliente", query.PartyId is null ? null : "Um cliente"),
            new("Atrasadas", query.Late ? "Só as atrasadas" : null),
        ];
        var rows = page.Items.Select(o => new FabricationOrderExportRow(o, o.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled ? null : ends.GetValueOrDefault(o.Id))).ToList();
        return new ExportData<FabricationOrderExportRow>(FabricationOrders, filters, new ExportRows<FabricationOrderExportRow>(page.Total, rows));
    }

    public async Task<Result<ExportData<InstallationSummary>>> InstallationsAsync(string? situation, Guid? projectId, int max, CancellationToken ct)
    {
        var page = await installations.ListAsync(situation, projectId, new PageRequest(1, max), ct);
        ExportFilter[] filters =
        [
            new("Situação", situation is { } s ? InstallationSituations.GetValueOrDefault(s, s) : null),
            new("Projeto", projectId is null ? null : "Um projeto"),
        ];
        return new ExportData<InstallationSummary>(Installations, filters, new ExportRows<InstallationSummary>(page.Total, page.Items));
    }

    public async Task<Result<ExportData<NonConformityExportRow>>> NonConformitiesAsync(NonConformityListQuery query, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await nonConformities.ListAsync(query, new PageRequest(1, max), ct);
        var ids = page.Items.Select(n => n.Id).ToList();
        var costs = await db.NonConformities.AsNoTracking().Where(n => ids.Contains(n.Id)).ToDictionaryAsync(n => n.Id, n => n.Cost, ct);
        ExportFilter[] filters =
        [
            new("Mostrar", string.Equals(query.Filter, "todas", StringComparison.Ordinal) ? "Todas" : "Aguardando você"),
            new("Situação", query.Situation is { } s ? NonConformitySituations.GetValueOrDefault(s, s) : null),
            new("OS", query.OrderId is null ? null : "Uma OS"),
            new("Projeto", query.ProjectId is null ? null : "Um projeto"),
        ];
        var rows = page.Items.Select(n => new NonConformityExportRow(n, costs.GetValueOrDefault(n.Id))).ToList();
        return new ExportData<NonConformityExportRow>(NonConformities, filters, new ExportRows<NonConformityExportRow>(page.Total, rows));
    }
}
