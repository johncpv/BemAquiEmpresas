using System.Globalization;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Resultado gerencial por projeto (DSH-01b CA 6 a 9; ADR 0028 §1, saído do DRE da FIN-08): receita do período pelo
/// <see cref="IRevenueSummary"/> (a regra do DRE, por projeto) e custos diretos realizados com data no período pelo
/// <see cref="IProjectMargins.RealizedByPeriodAsync"/>. Não fecha com o DRE mensal: o projeto soma pelo consumo, o DRE pela data do documento.
/// As rotas exigem <c>projects.view_cost</c> e <c>finance.view</c>; a lista e a visão passam pelo cache de 60 s do painel.
/// </summary>
public sealed class ProjectResultsService(
    DashboardAccess access, IProjects projects, IProjectMargins margins, IRevenueSummary revenue, IParties parties, TimeProvider clock, ITenantContext tenant,
    ITenantScopeFactory scopes)
{
    public async Task<Result<ProjectResultsPage>> ListAsync(
        string? situation, Guid? partyId, string? from, string? to, string? sort, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var key = situation ?? "todos";
        if (!ProjectResultRules.Situations.TryGetValue(key, out var statuses))
        {
            return Invalid("situation", "Escolha em andamento, entregues, encerrados, entregues e encerrados, cancelados ou todos.");
        }

        sort ??= "result";
        if (!ProjectResultRules.Sorts.Contains(sort))
        {
            return Invalid("sort", "Ordene pelo resultado, receita, margem ou número.");
        }

        var period = ProjectResultRules.Period(from, to, BusinessTime.Today(clock));
        if (period.Error is { } error)
        {
            return Invalid("from", error);
        }

        var parameters = string.Create(CultureInfo.InvariantCulture, $"s={key}&p={partyId}&f={period.From:yyyy-MM}&t={period.To:yyyy-MM}&o={sort}&pg={page.Page}&ps={page.PageSize}");
        return await access.CachedAsync("project-results", parameters, async ct =>
        {
            var list = await projects.ListAsync(statuses, partyId, ct);
            var ids = list.Select(p => p.Id).ToList();
            // CA 10: a receita num escopo próprio do tenant (outra conexão), em paralelo com os custos.
            var separate = ids.Count >= 500 && tenant.Tenant is not null;
            var revenueTask = separate ? SeparateRevenueAsync(ids, period.From, period.To, ct) : null;
            var costs = await margins.RealizedByPeriodAsync(ids, period.From, period.To, ct);
            var monthly = revenueTask is null ? await revenue.GrossRevenueByProjectsAsync(ids, period.From, period.To, ct) : await revenueTask;
            if (costs.IsFailure)
            {
                throw new InvalidOperationException("O realizado por período recusou o pedido: " + costs.Error!.Message);
            }

            var figures = ProjectResultRules.ByProject(ids, monthly, costs.Value);
            var rows = list.Select(p => (Project: p, Figures: figures[p.Id])).Where(r => !r.Figures.IsEmpty).ToList();
            var ordered = ProjectResultRules.Sort(rows, sort).ToList();
            var pageRows = ordered.Skip(page.Skip).Take(page.PageSize).ToList();
            var names = (await parties.FindManyAsync(pageRows.Select(r => r.Project.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
            var months = Months(period.Months, monthly);
            return new ProjectResultsPage(
                ProjectResultRules.MonthKey(period.From), ProjectResultRules.MonthKey(period.To), months, months.Any(m => m.Provisional),
                pageRows.Select(r => new ProjectResultRow(
                    r.Project.Id, r.Project.Number, r.Project.Name, r.Project.PartyId, names.GetValueOrDefault(r.Project.PartyId, string.Empty), r.Project.Status, r.Figures.Revenue,
                    r.Figures.Costs, r.Figures.DirectCosts, r.Figures.Result, r.Figures.MarginPct)).ToList(),
                page.Page, page.PageSize, ordered.Count, ProjectResultRules.Totals(rows.Select(r => r.Figures).ToList()), clock.GetUtcNow());
        }, cancellationToken);
    }

    /// <summary>Visão mês a mês de um projeto (CA 7). Projeto inexistente: 404.</summary>
    public async Task<Result<ProjectResultDetail>> DetailAsync(Guid projectId, string? from, string? to, CancellationToken cancellationToken = default)
    {
        var period = ProjectResultRules.Period(from, to, BusinessTime.Today(clock));
        if (period.Error is { } error)
        {
            return Invalid("from", error);
        }

        var project = await projects.FindAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        var monthly = await revenue.GrossRevenueByProjectsAsync([projectId], period.From, period.To, cancellationToken);
        var costs = await margins.RealizedByPeriodAsync([projectId], period.From, period.To, cancellationToken);
        if (costs.IsFailure)
        {
            return costs.Error!;
        }

        var party = await parties.FindAsync(project.PartyId, cancellationToken);
        var lines = ProjectResultRules.Lines(period.Months, monthly, costs.Value);
        var revenueTotal = lines.Single(l => l.Key == "revenue").Total;
        var resultTotal = lines.Single(l => l.Key == "result").Total;
        return new ProjectResultDetail(
            project.Id, project.Number, project.Name, party is null ? string.Empty : party.TradeName ?? party.LegalName, project.Status, ProjectResultRules.MonthKey(period.From),
            ProjectResultRules.MonthKey(period.To), Months(period.Months, monthly), lines, ProjectResultRules.MarginPct(resultTotal, revenueTotal), clock.GetUtcNow());
    }

    /// <summary>
    /// Drill-down da célula (CA 8): na Receita, os títulos e sinais reconhecidos; numa natureza, os lançamentos do realizado do DSH-02 com data no
    /// mês (ou no período, sem mês), com as horas agregadas por OS e centro. O total é a soma dos documentos, igual ao da célula.
    /// </summary>
    public async Task<Result<ProjectResultCell>> CellAsync(Guid projectId, string? line, string? month, string? from, string? to, CancellationToken cancellationToken = default)
    {
        var period = ProjectResultRules.Period(from, to, BusinessTime.Today(clock));
        if (period.Error is { } error)
        {
            return Invalid("from", error);
        }

        DateOnly start = period.From, end = period.To;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (ProjectResultRules.ParseMonth(month) is not { } m)
            {
                return Invalid("month", "Informe o mês no formato aaaa-mm.");
            }

            (start, end) = (m, m.AddMonths(1).AddDays(-1));
        }

        if (await projects.FindAsync(projectId, cancellationToken) is null)
        {
            return NotFound();
        }

        if (string.Equals(line, "revenue", StringComparison.Ordinal))
        {
            var docs = await revenue.GrossRevenueDocumentsAsync(projectId, start, end, cancellationToken);
            var entries = docs.Select(d => new ProjectResultEntry(d.Kind, d.Number, d.Date, d.Amount, null, d.Note, d.Link)).ToList();
            return new ProjectResultCell("revenue", "Receita", MonthOrNull(month), entries.Sum(e => e.Amount), entries);
        }

        if (!Enum.TryParse<CostType>(line, ignoreCase: false, out var type) || !Enum.IsDefined(type) || int.TryParse(line, out _))
        {
            return Invalid("line", "Escolha a receita ou uma natureza de custo.");
        }

        var realized = await margins.RealizedEntriesAsync(projectId, type, start, end, cancellationToken);
        if (realized.IsFailure)
        {
            return realized.Error!;
        }

        var costEntries = realized.Value.Select(e => new ProjectResultEntry(e.Kind, e.Document, e.Date, e.Amount, e.Hours, null, e.Link)).ToList();
        return new ProjectResultCell(type.ToString(), ProjectResultRules.CostLabel(type), MonthOrNull(month), costEntries.Sum(e => e.Amount), costEntries);
    }

    /// <summary>A receita num escopo próprio do mesmo tenant (outra conexão, resolvendo o IRevenueSummary nele), para rodar junto com os custos.</summary>
    private async Task<IReadOnlyList<ProjectMonthlyRevenue>> SeparateRevenueAsync(List<Guid> ids, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var scope = await scopes.CreateScopeAsync(tenant.Tenant!, ct);
        return await scope.ServiceProvider.GetRequiredService<IRevenueSummary>().GrossRevenueByProjectsAsync(ids, from, to, ct);
    }

    private static string? MonthOrNull(string? month) => string.IsNullOrWhiteSpace(month) ? null : month;

    private static List<ProjectResultMonth> Months(IReadOnlyList<DateOnly> months, IReadOnlyList<ProjectMonthlyRevenue> monthly)
    {
        // A marca vem do IRevenueSummary (a mesma para todos os projetos no mês); sem projeto na lista, o mês é provisório até as notas reais.
        var flags = monthly.GroupBy(m => new DateOnly(m.Year, m.Month, 1)).ToDictionary(g => g.Key, g => g.Any(m => m.IsProvisional));
        return months.Select(m => new ProjectResultMonth(ProjectResultRules.MonthKey(m), flags.GetValueOrDefault(m, true))).ToList();
    }

    private static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });

    private static Error NotFound() => Error.NotFound("project_not_found", "Projeto não encontrado.");
}
