using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.SharedKernel;

namespace BemAqui.Assets.Application;

/// <summary>Linha do custo de manutenção exportado (EXP-05 CA 17): um bem, um mês ou um centro; a última de cada aba é o total.</summary>
public sealed record MaintenanceCostExportRow(string Label, string? Center, decimal Preventive, decimal Corrective, decimal Total, int Orders, bool IsTotal);

/// <summary>
/// Exportação do patrimônio (EXP-05 CA 17, ADR 0045 §3): Bens, Manutenção e Custo de manutenção, cada uma pela <b>mesma consulta da tela</b>
/// com os mesmos filtros. O valor de aquisição só existe para quem tem <c>assets.edit</c>: sem a ação, a coluna nem entra no arquivo (como a
/// tela, que não a mostra). O custo de manutenção é a mesma tela (só com <c>assets.edit</c>, na rota).
/// </summary>
public sealed class AssetExportService(AssetService assets, MaintenanceService maintenance, MaintenanceCostService costs)
{
    public static readonly IReadOnlyDictionary<AssetType, string> TypeLabels = new Dictionary<AssetType, string>
    {
        [AssetType.Machine] = "Máquina", [AssetType.Tool] = "Ferramenta", [AssetType.Furniture] = "Mobiliário", [AssetType.Vehicle] = "Veículo", [AssetType.It] = "TI",
    };

    public static readonly IReadOnlyDictionary<AssetStatus, string> StatusLabels = new Dictionary<AssetStatus, string>
    {
        [AssetStatus.Active] = "Ativo", [AssetStatus.Maintenance] = "Em manutenção", [AssetStatus.Disposed] = "Baixado",
    };

    /// <summary>As colunas da lista de bens; o valor de aquisição some do arquivo sem <c>assets.edit</c> (<see cref="ExportSensitivity.Omitted"/>, EXP-04).</summary>
    public static readonly ExportSpec<AssetListItem> AssetsSpec = new ExportSpec<AssetListItem>("Asset", "bens", "Bens")
        .Text("Etiqueta", a => a.Tag)
        .Text("Nome", a => a.Name)
        .Text("Tipo", a => TypeLabels.GetValueOrDefault(a.Type, a.Type.ToString()))
        .Text("Série", a => a.SerialNumber).NotInPdf()
        .Text("Local", a => a.SiteName)
        .Text("Responsável", a => a.ResponsibleName is { } name ? (a.ResponsibleTerminated ? name + " (desligado)" : name) : null)
        .Text("Centro", a => a.WorkCenterName)
        .Money("Valor de aquisição", a => a.AcquisitionValue, ExportSensitivity.Omitted("assets.edit"))
        .Text("Situação", a => StatusLabels.GetValueOrDefault(a.Status, a.Status.ToString()));

    public static readonly ExportSpec<MaintenanceOrderListItem> OrdersSpec = new ExportSpec<MaintenanceOrderListItem>("MaintenanceOrder", "manutencao", "Manutenção")
        .Text("Número", o => o.Number)
        .Text("Bem", o => $"{o.AssetTag} · {o.AssetName}")
        .Text("Centro", o => o.WorkCenterName)
        .Text("Tipo", o => MaintenanceOrderPdf.TypeLabel(o.Type))
        .Text("Plano", o => o.PlanDescription).NotInPdf()
        .Date("Agendada", o => o.ScheduledOn)
        .Text("Executor", o => o.SupplierName ?? MaintenanceOrderPdf.InternalTeam)
        .Text("Máquina parada", o => o.MachineStopped ? "Sim" : "Não").NotInPdf()
        .Text("Situação", o => MaintenanceOrderPdf.StatusLabel(o.Status));

    public static readonly ExportSpec<MaintenanceDuePlan> DuePlansSpec = new ExportSpec<MaintenanceDuePlan>("MaintenancePlan", "preventivas", "Preventivas vencidas e a vencer")
        .Text("Plano", p => p.Description)
        .Text("Bem", p => $"{p.AssetTag} · {p.AssetName}")
        .Text("Centro", p => p.WorkCenterName)
        .Date("Vencimento", p => p.NextDueAt)
        .Quantity("Horímetro previsto", p => p.NextDueMeter)
        .Text("Situação", p => p.Overdue ? "Vencida" : "A vencer")
        .Text("OS aberta", p => p.OpenOrderNumber);

    public static ExportSpec<MaintenanceCostExportRow> CostSpec(string firstColumn) =>
        new ExportSpec<MaintenanceCostExportRow>("MaintenanceCost", "custo-de-manutencao", "Custo de manutenção")
            .Text(firstColumn, r => r.Label)
            .Text("Centro", r => r.Center)
            .Money("Preventiva", r => r.Preventive)
            .Money("Corretiva", r => r.Corrective)
            .Money("Total", r => r.Total)
            .Quantity("OS", r => r.Orders)
            .Style(r => r.IsTotal ? new ExportRowStyle(Bold: true) : null);

    private static readonly string[] Months = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    /// <summary>"mar/2026" a partir de "2026-03", como o cabeçalho do mês na tela.</summary>
    public static string MonthLabel(string month) =>
        month is { Length: 7 } && int.TryParse(month[5..], out var m) && m is >= 1 and <= 12 ? $"{Months[m - 1]}/{month[..4]}" : month;

    /// <summary>Bens (CA 17): a busca, os filtros e a ordem da tela; o valor de aquisição omitido sem <c>assets.edit</c>.</summary>
    public async Task<Result<ExportData<AssetListItem>>> AssetsAsync(AssetListQuery query, string? sort, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await assets.ListAsync(query, new PageRequest(1, Math.Max(1, max), sort), ct);
        ExportFilter[] filters =
        [
            new("Busca", string.IsNullOrWhiteSpace(query.Q) ? null : query.Q.Trim()),
            new("Tipo", query.Type is { } type ? TypeLabels.GetValueOrDefault(type) : null),
            new("Local", query.SiteId is null ? null : page.Items.Select(a => a.SiteName).FirstOrDefault(n => n is not null)),
            new("Centro", query.WorkCenterId is null ? null : page.Items.Select(a => a.WorkCenterName).FirstOrDefault(n => n is not null)),
            new("Responsável", query.ResponsibleEmployeeId is null ? null : page.Items.Select(a => a.ResponsibleName).FirstOrDefault(n => n is not null)),
            new("Situação", query.Status is { } status ? (status == AssetStatus.Disposed ? "Baixados" : StatusLabels.GetValueOrDefault(status)) : null),
            new("Responsável desligado", query.ResponsibleTerminated ? "Só os do desligado" : null),
        ];
        return new ExportData<AssetListItem>(AssetsSpec, filters, new ExportRows<AssetListItem>(page.Total, page.Items));
    }

    /// <summary>Manutenção (CA 17): as OS do filtro (sem situação, as abertas e em andamento) e a aba das preventivas vencidas e a vencer.</summary>
    public async Task<Result<ExportData<MaintenanceOrderListItem>>> OrdersAsync(MaintenanceListQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var overview = await maintenance.OverviewAsync(query, ct);
        ExportFilter[] filters =
        [
            new("Bem", query.AssetId is null ? null : overview.Orders.Select(o => $"{o.AssetTag} · {o.AssetName}").FirstOrDefault()),
            new("Centro", query.WorkCenterId is null ? null : overview.Orders.Select(o => o.WorkCenterName).FirstOrDefault(n => n is not null)),
            new("Tipo", query.Type is { } type ? MaintenanceOrderPdf.TypeLabel(type) : null),
            new("Situação", query.Status is { } status ? MaintenanceOrderPdf.StatusLabel(status) : "Abertas e em andamento"),
        ];
        return new ExportData<MaintenanceOrderListItem>(OrdersSpec, filters, new ExportRows<MaintenanceOrderListItem>(overview.Orders.Count, overview.Orders))
        {
            Decorate = t => overview.DuePlans.Count == 0 ? t : t with { Sheets = [ExportSheet.From("Preventivas", DuePlansSpec, overview.DuePlans)] },
        };
    }

    /// <summary>Custo de manutenção (CA 17): por bem (aba principal), por mês e por centro, com o total de cada aba igual ao da tela.</summary>
    public async Task<Result<ExportData<MaintenanceCostExportRow>>> CostAsync(MaintenanceCostQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await costs.ReportAsync(query, ct);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        var report = result.Value;
        var total = new MaintenanceCostExportRow("Total do período", null, report.Total.Preventive, report.Total.Corrective, report.Total.Total, report.Total.Orders, true);
        List<MaintenanceCostExportRow> byAsset = [.. report.Assets.Select(a => Row($"{a.Tag} · {a.Name}", a.WorkCenterName ?? "Sem centro de trabalho", a.Cost)), total];
        List<MaintenanceCostExportRow> byMonth = [.. report.Months.Select(m => Row(MonthLabel(m.Month), null, m.Cost)), total];
        List<MaintenanceCostExportRow> byCenter = [.. report.Centers.Select(c => Row(c.WorkCenterName ?? "Sem centro de trabalho", null, c.Cost)), total];
        ExportFilter[] filters =
        [
            new("De", MonthLabel(report.From)),
            new("Até", MonthLabel(report.To)),
            new("Centro", query.WorkCenterId is null ? null : report.Centers.Select(c => c.WorkCenterName).FirstOrDefault(n => n is not null)),
            new("Bem", query.AssetId is null ? null : report.Assets.Select(a => $"{a.Tag} · {a.Name}").FirstOrDefault()),
        ];
        return new ExportData<MaintenanceCostExportRow>(CostSpec("Bem"), filters, new ExportRows<MaintenanceCostExportRow>(byAsset.Count, byAsset))
        {
            Decorate = t => t with
            {
                SheetName = "Por bem",
                Sheets = [ExportSheet.From("Por mês", CostSpec("Mês"), byMonth), ExportSheet.From("Por centro", CostSpec("Centro de trabalho"), byCenter)],
                Notes = ["O mês é o da conclusão da OS. O custo soma a mão de obra interna, o prestador, as peças e os outros lançados em cada OS concluída."],
            },
        };
    }

    private static MaintenanceCostExportRow Row(string label, string? center, MaintenanceCostCell cost) =>
        new(label, center, cost.Preventive, cost.Corrective, cost.Total, cost.Orders, false);
}
