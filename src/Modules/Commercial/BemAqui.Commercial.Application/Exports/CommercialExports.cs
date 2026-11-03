using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Commercial.Application.Documents;
using BemAqui.Commercial.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application.Exports;

public sealed record QuoteExportRow(string Number, int Revision, string Customer, string Status, DateOnly? ValidUntil, decimal Price, decimal? Cost, decimal? MarginPct);

public sealed record ContractExportRow(string Number, string Customer, string? Project, DateOnly SignedAt, string Status, decimal Value);

public sealed record ProjectExportRow(
    string Number, string Name, string Customer, string Stage, DateOnly? PromisedDate, int? DelayDays, decimal ContractValue, string? Manager, decimal? MarginPct, decimal? ReceivedPct);

/// <summary>
/// Exportação das listas do comercial (EXP-04 CA 15 e 16): orçamentos, contratos e projetos em xlsx, com as colunas da tela. Custo e margem do
/// orçamento (<c>quotes.view_cost</c>), margem orçada (<c>projects.view_cost</c>) e % recebido (<c>finance.view</c>) <b>saem do arquivo</b> sem a
/// ação, como somem da tela (<see cref="ExportSensitivity.Omit"/>).
/// </summary>
public static class CommercialExports
{
    public static readonly ExportSensitivity QuoteCost = ExportSensitivity.Omitted("quotes.view_cost");
    public static readonly ExportSensitivity ProjectCost = ExportSensitivity.Omitted("projects.view_cost");
    public static readonly ExportSensitivity Finance = ExportSensitivity.Omitted("finance.view");

    public static readonly ExportSpec<QuoteExportRow> Quotes = new ExportSpec<QuoteExportRow>("Quote", "orcamentos", "Orçamentos")
        .Text("Número", r => r.Number)
        .Quantity("Revisão", r => r.Revision)
        .Text("Cliente", r => r.Customer)
        .Text("Situação", r => r.Status)
        .Date("Validade", r => r.ValidUntil)
        .Money("Preço", r => r.Price)
        .Money("Custo", r => r.Cost, QuoteCost)
        .Percent("Margem", r => r.MarginPct, QuoteCost);

    public static readonly ExportSpec<ContractExportRow> Contracts = new ExportSpec<ContractExportRow>("Contract", "contratos", "Contratos")
        .Text("Número", r => r.Number)
        .Text("Cliente", r => r.Customer)
        .Text("Projeto", r => r.Project)
        .Date("Assinatura", r => r.SignedAt)
        .Text("Situação", r => r.Status)
        .Money("Valor", r => r.Value);

    public static readonly ExportSpec<ProjectExportRow> Projects = new ExportSpec<ProjectExportRow>("Project", "projetos", "Projetos")
        .Text("Número", r => r.Number)
        .Text("Nome", r => r.Name)
        .Text("Cliente", r => r.Customer)
        .Text("Etapa", r => r.Stage)
        .Date("Entrega prometida", r => r.PromisedDate)
        .Quantity("Atraso (dias)", r => r.DelayDays)
        .Money("Contratado", r => r.ContractValue)
        .Text("Responsável", r => r.Manager)
        .Percent("Margem orçada", r => r.MarginPct, ProjectCost)
        .Percent("% recebido", r => r.ReceivedPct, Finance);

    public static readonly IReadOnlyDictionary<string, string> QuoteSituations = new Dictionary<string, string>
    {
        ["rascunho"] = "Rascunhos", ["enviado"] = "Enviados", ["perdido"] = "Perdidos", ["expirado"] = "Expirados", ["aprovado"] = "Aprovados",
    };

    public static readonly IReadOnlyDictionary<string, string> ContractSituations = new Dictionary<string, string>
    {
        ["ativo"] = "Ativos", ["concluido"] = "Concluídos", ["cancelado"] = "Cancelados",
    };

    public static readonly IReadOnlyDictionary<string, string> ProjectSituations = new Dictionary<string, string>
    {
        ["andamento"] = "Em andamento", ["entregue"] = "Entregues", ["encerrado"] = "Encerrados", ["cancelado"] = "Cancelados",
    };

    public static string QuoteStatusLabel(QuoteStatus status) => status switch
    {
        QuoteStatus.Draft => "Rascunho",
        QuoteStatus.Sent => "Enviado",
        QuoteStatus.Rejected => "Perdido",
        QuoteStatus.Expired => "Expirado",
        QuoteStatus.Approved => "Aprovado",
        _ => "Substituído",
    };

    /// <summary>Etapa como o indicador da tela: "{letra} · {nome}" (P E F I T C); cancelado sem letra.</summary>
    public static string Stage(ProjectStatus status) => status switch
    {
        ProjectStatus.Planned => "P · Planejado",
        ProjectStatus.InProgress => "E · Em produção",
        ProjectStatus.Fabricated => "F · Fabricado",
        ProjectStatus.Installing => "I · Instalando",
        ProjectStatus.Delivered => "T · Entregue",
        ProjectStatus.Closed => "C · Encerrado",
        _ => "Cancelado",
    };
}

/// <summary>
/// Consultas das listas para exportar (EXP-04 CA 15): o mesmo serviço da tela, com os mesmos filtros, sem paginação (até o teto + 1, para o
/// <see cref="ListExportService"/> recusar acima dele). Custo, margem e recebido só são lidos quando o usuário tem a ação.
/// </summary>
public sealed class CommercialExportService(QuoteService quotes, ContractService contracts, ProjectService projects, IParties parties, IPermissionChecker permissions)
{
    public async Task<Result<ExportData<QuoteExportRow>>> QuotesAsync(
        string? q, string? situation, Guid? partyId, DateOnly? from, DateOnly? to, Guid? authorId, int max, CancellationToken ct)
    {
        var page = await quotes.ListAsync(q, situation, All(max), partyId, from, to, authorId, ct);
        var costs = page.Total <= max && page.Items.Count > 0 && await permissions.HasPermissionAsync("quotes", "view_cost", ct)
            && await quotes.CostSummariesAsync(page.Items.Select(i => i.Id).ToList(), ct) is { IsSuccess: true } found
            ? found.Value.ToDictionary(c => c.Id)
            : new Dictionary<Guid, QuoteCostSummary>();
        var rows = page.Items.Select(i => new QuoteExportRow(i.Number, i.Revision, i.PartyName, CommercialExports.QuoteStatusLabel(i.Status), i.ValidUntil, i.TotalPrice,
            costs.GetValueOrDefault(i.Id)?.TotalCost, costs.GetValueOrDefault(i.Id)?.MarginPct)).ToList();
        ExportFilter[] filters =
        [
            new("Busca", Clean(q)),
            new("Situação", situation is { } s ? CommercialExports.QuoteSituations.GetValueOrDefault(s, s) : null),
            new("Cliente", await PartyAsync(partyId, ct)),
            new("Criados de", Day(from)),
            new("Criados até", Day(to)),
            new("Autor", authorId is null ? null : "Um autor (do funil)"),
        ];
        return new ExportData<QuoteExportRow>(CommercialExports.Quotes, filters, new ExportRows<QuoteExportRow>(page.Total, rows));
    }

    public async Task<Result<ExportData<ContractExportRow>>> ContractsAsync(string? q, string? situation, int max, CancellationToken ct)
    {
        var page = await contracts.ListAsync(q, situation, All(max), ct);
        var rows = page.Items.Select(c => new ContractExportRow(c.Number, c.PartyName, c.ProjectNumber, c.SignedAt, ContractPdf.StatusLabel(c.Status), c.TotalValue)).ToList();
        ExportFilter[] filters = [new("Busca", Clean(q)), new("Situação", situation is { } s ? CommercialExports.ContractSituations.GetValueOrDefault(s, s) : null)];
        return new ExportData<ContractExportRow>(CommercialExports.Contracts, filters, new ExportRows<ContractExportRow>(page.Total, rows));
    }

    public async Task<Result<ExportData<ProjectExportRow>>> ProjectsAsync(string? q, string? situation, Guid? partyId, Guid? managerId, int max, CancellationToken ct)
    {
        var page = await projects.ListAsync(q, situation, partyId, managerId, All(max), ct);
        var ids = page.Items.Select(p => p.Id).ToList();
        var fits = page.Total <= max && ids.Count > 0;
        var margins = fits && await projects.MarginsAsync(ids, ct) is { IsSuccess: true } m ? m.Value.ToDictionary(x => x.Id, x => x.MarginPct) : new Dictionary<Guid, decimal?>();
        var received = fits && await projects.ReceivedAsync(ids, ct) is { IsSuccess: true } r ? r.Value.ToDictionary(x => x.Id, x => (decimal?)x.ReceivedPct) : new Dictionary<Guid, decimal?>();
        var rows = page.Items.Select(p => new ProjectExportRow(p.Number, p.Name, p.PartyName, CommercialExports.Stage(p.Status), p.PromisedDate, p.DelayDays, p.ContractValue,
            p.ManagerName, margins.GetValueOrDefault(p.Id), received.GetValueOrDefault(p.Id))).ToList();
        ExportFilter[] filters =
        [
            new("Busca", Clean(q)),
            new("Situação", situation is { } s ? CommercialExports.ProjectSituations.GetValueOrDefault(s, s) : null),
            new("Cliente", await PartyAsync(partyId, ct)),
            new("Responsável", managerId is null ? null : (page.Items.Count > 0 ? page.Items[0].ManagerName : null) ?? "Um responsável"),
        ];
        return new ExportData<ProjectExportRow>(CommercialExports.Projects, filters, new ExportRows<ProjectExportRow>(page.Total, rows));
    }

    /// <summary>Uma página com tudo até o teto (+1 para o serviço ver que passou).</summary>
    private static PageRequest All(int max) => new(1, max + 1);

    private async Task<string?> PartyAsync(Guid? id, CancellationToken ct) =>
        id is { } partyId && await parties.FindAsync(partyId, ct) is { } party ? party.TradeName ?? party.LegalName : null;

    private static string? Clean(string? q) => string.IsNullOrWhiteSpace(q) ? null : q.Trim();

    private static string? Day(DateOnly? day) => day is { } d ? d.ToString("dd/MM/yyyy", BemAqui.BuildingBlocks.Pdf.PdfTheme.PtBr) : null;
}
