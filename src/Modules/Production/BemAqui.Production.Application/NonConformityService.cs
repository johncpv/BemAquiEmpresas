using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Não conformidades (PRD-05 CA 8 a 11): lista com o filtro "Aguardando você", detalhe, salvar, "Iniciar ação", "Encerrar NC" e o custo
/// estimado (só informativo, ponto 78). Ver com <c>production.view</c>; mudar com <c>production.edit</c>; custo com
/// <c>projects.view_cost</c>. NC encerrada não muda (modo leitura). Conferida pela versão (xmin).
/// </summary>
public sealed class NonConformityService(
    IProductionDb db, IUserDirectory users, IPermissionChecker permissions, ICurrentUser currentUser, IProjects projects, IParties parties, TimeProvider clock)
{
    public const string AttachmentOwner = "NonConformity";

    public static Error NotFound() => Error.NotFound("non_conformity_not_found", "Não conformidade não encontrada.");

    /// <summary>Lista (CA 11): "aguardando" (padrão: eu sou o responsável e não está encerrada) ou "todas", com situação, OS e projeto.</summary>
    public async Task<NonConformityPage> ListAsync(NonConformityListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var me = currentUser.UserId ?? Guid.Empty;
        var rows = from n in db.NonConformities.AsNoTracking()
                   join o in db.ProductionOrders.AsNoTracking() on n.OrderId equals o.Id
                   join s in db.ProductionOrderSteps.AsNoTracking() on n.OrderStepId equals s.Id
                   select new { Nc = n, o.Number, o.ProjectId, s.Code, s.Name };
        var mineOpen = await rows.CountAsync(r => r.Nc.ResponsibleUserId == me && r.Nc.Status != NonConformityStatus.Closed, cancellationToken);
        if (!string.Equals(query.Filter, "todas", StringComparison.Ordinal))
        {
            rows = rows.Where(r => r.Nc.ResponsibleUserId == me && r.Nc.Status != NonConformityStatus.Closed);
        }

        rows = query.Situation switch
        {
            "aberta" => rows.Where(r => r.Nc.Status == NonConformityStatus.Open),
            "acao" => rows.Where(r => r.Nc.Status == NonConformityStatus.InAction),
            "encerrada" => rows.Where(r => r.Nc.Status == NonConformityStatus.Closed),
            "pendente" => rows.Where(r => r.Nc.Status != NonConformityStatus.Closed),
            _ => rows,
        };
        if (query.OrderId is { } orderId)
        {
            rows = rows.Where(r => r.Nc.OrderId == orderId);
        }

        if (query.ProjectId is { } projectId)
        {
            rows = rows.Where(r => r.ProjectId == projectId);
        }

        var total = await rows.CountAsync(cancellationToken);
        var list = await rows.OrderBy(r => r.Nc.Status == NonConformityStatus.Closed).ThenBy(r => r.Nc.OpenedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        var projectNumbers = (await projects.FindManyAsync(list.Select(r => r.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.Number);
        var names = await NamesAsync(list.Select(r => (Guid?)r.Nc.ResponsibleUserId), cancellationToken);
        var today = BusinessTime.Today(clock);
        return new NonConformityPage(list.Select(r => new NonConformitySummary(
            r.Nc.Id, r.Nc.Number, r.Nc.OrderId, r.Number, r.ProjectId, projectNumbers.GetValueOrDefault(r.ProjectId, string.Empty), r.Code ?? string.Empty, r.Name,
            r.Nc.ResponsibleUserId, names.GetValueOrDefault(r.Nc.ResponsibleUserId) ?? "Usuário", r.Nc.Status, Age(r.Nc, today), r.Nc.OpenedAt, r.Nc.Description, r.Nc.Attempt)).ToList(),
            page.Page, page.PageSize, total, mineOpen);
    }

    /// <summary>Detalhe (CA 9 e 10); pela OS, só a NC daquela OS (IDOR, CA 12).</summary>
    public async Task<Result<NonConformityDetail>> GetAsync(Guid id, Guid? orderId = null, CancellationToken cancellationToken = default)
    {
        var nc = await db.NonConformities.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id && (orderId == null || n.OrderId == orderId), cancellationToken);
        return nc is null ? NotFound() : await DetailAsync(nc, cancellationToken);
    }

    /// <summary>"Salvar" (CA 9): descrição, causa, ação e responsável (usuário ativo com <c>production.view</c>); a encerrada não muda.</summary>
    public async Task<Result<NonConformityDetail>> UpdateAsync(Guid id, NonConformityUpdateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var description = QualityRules.Text(input.Description, "description", QualityRules.MaxDescription, "a descrição da não conformidade", required: true);
        var cause = QualityRules.Text(input.Cause, "cause", QualityRules.MaxCause, "a causa", required: false);
        var action = QualityRules.Text(input.Action, "action", QualityRules.MaxAction, "a ação corretiva", required: false);
        if (new[] { description, cause, action }.FirstOrDefault(t => t.IsFailure) is { } invalid)
        {
            return invalid.Error!;
        }

        var loaded = await LoadOpenAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        if (!(await users.UsersWithPermissionAsync("production.view", cancellationToken)).Contains(input.ResponsibleUserId))
        {
            return LaborRules.Invalid("responsibleUserId", "Escolha um usuário ativo com acesso à Produção.");
        }

        var nc = loaded.Value;
        nc.Description = description.Value!;
        nc.Cause = cause.Value;
        nc.Action = action.Value;
        nc.ResponsibleUserId = input.ResponsibleUserId;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(nc, cancellationToken);
    }

    /// <summary>"Iniciar ação" (CA 9): da aberta, com causa e ação preenchidas (422 com os campos), vai a <c>InAction</c>.</summary>
    public async Task<Result<NonConformityDetail>> StartActionAsync(Guid id, NonConformityActionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadOpenAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var nc = loaded.Value;
        if (nc.Status != NonConformityStatus.Open)
        {
            return Error.Conflict("non_conformity_not_open", "A ação desta não conformidade já foi iniciada.");
        }

        var cause = QualityRules.Text(input.Cause, "cause", QualityRules.MaxCause, "a causa", required: false);
        var action = QualityRules.Text(input.Action, "action", QualityRules.MaxAction, "a ação corretiva", required: false);
        if (new[] { cause, action }.FirstOrDefault(t => t.IsFailure) is { } invalid)
        {
            return invalid.Error!;
        }

        if (MissingCauseAndAction(cause.Value, action.Value) is { } missing)
        {
            return missing;
        }

        nc.Cause = cause.Value;
        nc.Action = action.Value;
        nc.Status = NonConformityStatus.InAction;
        nc.ActionStartedAt = clock.GetUtcNow();
        nc.ActionStartedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(nc, cancellationToken);
    }

    /// <summary>"Encerrar NC" (CA 9): resultado obrigatório (até 500) e causa e ação preenchidas (422 com os campos); vai a <c>Closed</c>.</summary>
    public async Task<Result<NonConformityDetail>> CloseAsync(Guid id, NonConformityCloseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadOpenAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var nc = loaded.Value;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(nc.Cause))
        {
            errors["cause"] = ["Informe a causa antes de encerrar."];
        }

        if (string.IsNullOrWhiteSpace(nc.Action))
        {
            errors["action"] = ["Informe a ação corretiva antes de encerrar."];
        }

        var result = QualityRules.Text(input.Result, "result", QualityRules.MaxResult, "o resultado", required: true);
        if (result.IsFailure)
        {
            errors["result"] = [result.Error!.Message];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation", errors.First().Value[0], errors);
        }

        nc.Result = result.Value;
        nc.Status = NonConformityStatus.Closed;
        nc.ClosedAt = clock.GetUtcNow();
        nc.ClosedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(nc, cancellationToken);
    }

    /// <summary>Custo estimado (CA 10): só com <c>projects.view_cost</c>; informativo, não entra no realizado do projeto. Nulo apaga.</summary>
    public async Task<Result<NonConformityDetail>> SetCostAsync(Guid id, NonConformityCostInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Só quem vê o custo dos projetos informa o custo da não conformidade.");
        }

        if (input.Cost is < 0 or > QualityRules.MaxCost)
        {
            return LaborRules.Invalid("cost", "Informe um custo de zero para cima.");
        }

        var loaded = await LoadOpenAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var nc = loaded.Value;
        nc.Cost = input.Cost is { } cost ? Math.Round(cost, 2, MidpointRounding.AwayFromZero) : null;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(nc, cancellationToken);
    }

    /// <summary>A NC existe (dono de anexo, ADR 0019 §7 adendo).</summary>
    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => await db.NonConformities.AnyAsync(n => n.Id == id, cancellationToken);

    /// <summary>NC encerrada não recebe anexo (modo leitura).</summary>
    public async Task<Error?> CanAttachAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.NonConformities.AsNoTracking().Where(n => n.Id == id).Select(n => (NonConformityStatus?)n.Status).FirstOrDefaultAsync(cancellationToken) switch
        {
            null => NotFound(),
            NonConformityStatus.Closed => Error.Conflict("non_conformity_closed", "Não conformidade encerrada não recebe anexos."),
            _ => null,
        };

    private static Error? MissingCauseAndAction(string? cause, string? action)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (cause is null)
        {
            errors["cause"] = ["Informe a causa."];
        }

        if (action is null)
        {
            errors["action"] = ["Informe a ação corretiva."];
        }

        return errors.Count == 0 ? null : Error.Validation("validation", errors.First().Value[0], errors);
    }

    private async Task<Result<NonConformity>> LoadOpenAsync(Guid id, uint version, CancellationToken ct)
    {
        var nc = await db.NonConformities.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (nc is null)
        {
            return NotFound();
        }

        if (nc.Version != version)
        {
            return InstallationOrderService.Stale();
        }

        db.Entry(nc).Property(n => n.Version).OriginalValue = version;
        return nc.Status == NonConformityStatus.Closed ? Error.Conflict("non_conformity_closed", "Não conformidade encerrada não muda.") : nc;
    }

    private async Task<NonConformityDetail> DetailAsync(NonConformity nc, CancellationToken ct)
    {
        var order = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == nc.OrderId, ct);
        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.Id == nc.OrderStepId || s.Id == nc.ReturnToStepId).ToDictionaryAsync(s => s.Id, ct);
        var project = (await projects.FindManyAsync([order.ProjectId], ct)) is [var p, ..] ? p : null;
        var party = project is not null && await parties.FindManyAsync([project.PartyId], ct) is [var found, ..] ? found.TradeName ?? found.LegalName : string.Empty;
        var names = await NamesAsync([nc.ResponsibleUserId, nc.OpenedBy, nc.ActionStartedBy, nc.ClosedBy], ct);
        var responsibles = new List<QualityUserOption>();
        foreach (var id in await users.UsersWithPermissionAsync("production.view", ct))
        {
            responsibles.Add(new QualityUserOption(id, await users.DisplayNameAsync(id, ct) ?? "Usuário"));
        }

        var costVisible = await permissions.HasPermissionAsync("projects", "view_cost", ct);
        var step = steps.GetValueOrDefault(nc.OrderStepId);
        var back = steps.GetValueOrDefault(nc.ReturnToStepId);
        return new NonConformityDetail(
            nc.Id, nc.Number, nc.Status, order.Id, order.Number, order.ProjectId, project?.Number ?? string.Empty, project?.Name ?? string.Empty, party, nc.OrderStepId,
            step?.Code ?? string.Empty, step?.Name ?? string.Empty, nc.ReturnToStepId, back?.Name ?? string.Empty, nc.Attempt, nc.Description, nc.Cause, nc.Action, nc.ResponsibleUserId,
            names.GetValueOrDefault(nc.ResponsibleUserId) ?? "Usuário", costVisible ? nc.Cost : null, costVisible, nc.Result, Name(names, nc.OpenedBy), nc.OpenedAt,
            Name(names, nc.ActionStartedBy), nc.ActionStartedAt, Name(names, nc.ClosedBy), nc.ClosedAt, Age(nc, BusinessTime.Today(clock)),
            responsibles.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList(), nc.Version);
    }

    private static int Age(NonConformity nc, DateOnly today) =>
        QualityRules.AgeDays(Day(nc.OpenedAt), nc.ClosedAt is { } closed ? Day(closed) : null, today);

    private static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime);

    private async Task<Dictionary<Guid, string?>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, ct);
        }

        return names;
    }

    private static string? Name(Dictionary<Guid, string?> names, Guid? id) => id is { } value ? names.GetValueOrDefault(value) : null;
}