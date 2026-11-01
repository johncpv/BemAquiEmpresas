using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Movimentação e baixa do bem (PAT-02). Cada comando grava o movimento (imutável, com antes e depois) e o bem na mesma transação (um
/// <c>SaveChanges</c>); o lote grava um movimento por bem ou nada. A baixa não tem efeito financeiro: na venda, só devolve o título a receber
/// pré-preenchido para quem tem <c>finance.create</c>, que grava no Financeiro com a confirmação de lá (CA 7, R-PRP-7). Quem registrou,
/// quando e o IP ficam na auditoria (<c>IAuditable</c>).
/// </summary>
public sealed class AssetMovementService(
    IAssetsDb db,
    AssetService assets,
    IPermissionChecker permissions,
    IEmployees employees,
    IWorkCenters workCenters,
    IParties parties,
    IUserDirectory users,
    ICurrentUser currentUser,
    IEventPublisher events,
    TimeProvider clock)
{
    public const string NothingChangedCode = "asset_nothing_changed";
    public const string NotDisposedCode = "asset_not_disposed";
    public const string MovementNotFoundCode = "asset_movement_not_found";
    public const string TransferRefusedCode = "asset_transfer_refused";
    public const string NothingChangedMessage = "Informe pelo menos um local, responsável ou centro diferente do atual.";

    /// <summary>Aba Movimentações (CA 3): do mais recente ao mais antigo.</summary>
    public async Task<Result<IReadOnlyList<AssetMovementView>>> ListAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        if (!await db.Assets.AsNoTracking().AnyAsync(a => a.Id == assetId, cancellationToken))
        {
            return AssetService.NotFound();
        }

        var rows = await db.AssetMovements.AsNoTracking().Where(m => m.AssetId == assetId)
            .OrderByDescending(m => m.MovedOn).ThenByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<AssetMovementView>>(await ViewsAsync(rows, cancellationToken));
    }

    /// <summary>Um movimento pela rota do bem (CA 9): o movimento de outro bem é 404 (IDOR).</summary>
    public async Task<Result<AssetMovementView>> GetAsync(Guid assetId, Guid movementId, CancellationToken cancellationToken = default)
    {
        var movement = await db.AssetMovements.AsNoTracking().FirstOrDefaultAsync(m => m.Id == movementId && m.AssetId == assetId, cancellationToken);
        return movement is null ? MovementNotFound() : (await ViewsAsync([movement], cancellationToken))[0];
    }

    /// <summary>"Registrar movimentação" (CA 1 e 2): o destino é o estado completo; nada mudou é 422.</summary>
    public async Task<Result<AssetMovementResult>> MoveAsync(Guid assetId, AssetMovementInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var asset = await LoadAsync(assetId, input.Version, cancellationToken);
        if (asset is null)
        {
            return AssetService.NotFound();
        }

        if (!AssetMovementRules.IsMovable(asset.Status))
        {
            return Error.Conflict(AssetService.DisposedCode, "Bem baixado não é movimentado. Reative o bem primeiro.");
        }

        if (RequiredReason(input.Reason, out var reason) is { } badReason)
        {
            return badReason;
        }

        var (movedOn, badDate) = await DateAsync(asset.Id, input.MovedOn, "movedOn", cancellationToken);
        if (badDate is not null)
        {
            return badDate;
        }

        var current = AssetPlacement.Of(asset);
        var target = new AssetPlacement(input.SiteId, input.ResponsibleEmployeeId, input.WorkCenterId);
        if (!AssetMovementRules.Changes(current, target))
        {
            return Error.Validation(NothingChangedCode, NothingChangedMessage);
        }

        if (await LinksAsync(current, target, cancellationToken) is { } badLink)
        {
            return badLink;
        }

        var movement = AssetMovement.Transfer(asset, target, movedOn, reason, currentUser.UserId, clock.GetUtcNow());
        db.AssetMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        return await ResultAsync(asset, movement, null, false, cancellationToken);
    }

    /// <summary>Prévia de "Transferir selecionados" (CA 4): cada bem, de → para, e o que impede.</summary>
    public async Task<Result<AssetTransferPreview>> PreviewTransferAsync(AssetTransferInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var plan = await PlanAsync(input, false, cancellationToken);
        return plan.Error is { } error ? error : plan.Preview!;
    }

    /// <summary>"Transferir selecionados" (CA 4): um movimento por bem na mesma transação; um bem que impede faz o lote falhar inteiro, com a lista.</summary>
    public async Task<Result<AssetTransferResult>> TransferAsync(AssetTransferInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var plan = await PlanAsync(input, true, cancellationToken);
        if (plan.Error is { } error)
        {
            return error;
        }

        if (plan.Preview!.Refused.Count > 0)
        {
            return Error.Conflict(TransferRefusedCode, "Nenhum bem foi transferido. Tire da seleção os bens que impedem.") with
            {
                Data = new Dictionary<string, object?> { ["refused"] = plan.Preview.Refused },
            };
        }

        var (by, at) = (currentUser.UserId, clock.GetUtcNow());
        foreach (var (asset, target) in plan.Moves)
        {
            db.AssetMovements.Add(AssetMovement.Transfer(asset, target, plan.MovedOn, plan.Reason, by, at));
        }

        await db.SaveChangesAsync(cancellationToken);
        var count = plan.Moves.Count;
        return new AssetTransferResult(count, count == 1 ? "1 bem transferido" : $"{count} bens transferidos");
    }

    /// <summary>
    /// "Baixar bem" (CA 6): o bem passa a <c>Disposed</c>, sai das listas padrão e dos seletores e o movimento de baixa fica no histórico. Na
    /// venda, o valor (<c>numeric(18,2)</c>) e o comprador opcional; o título a receber só nasce no Financeiro (CA 7). A recusa por OS de
    /// manutenção aberta entra com a PAT-03a.
    /// </summary>
    public async Task<Result<AssetMovementResult>> DisposeAsync(Guid assetId, AssetDisposalInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var asset = await LoadAsync(assetId, input.Version, cancellationToken);
        if (asset is null)
        {
            return AssetService.NotFound();
        }

        if (!AssetMovementRules.IsMovable(asset.Status))
        {
            return Error.Conflict(AssetService.DisposedCode, "O bem já está baixado.");
        }

        if (input.DisposalReason is not { } kind || !Enum.IsDefined(kind))
        {
            return AssetService.Invalid("disposalReason", "Escolha o motivo da baixa.");
        }

        var reason = AssetRules.Clean(input.Reason);
        if (reason is { Length: > AssetMovementRules.ReasonMax })
        {
            return AssetService.Invalid("reason", "A observação aceita até 200 caracteres.");
        }

        if (kind == AssetDisposalReason.Other && reason is null)
        {
            return AssetService.Invalid("reason", "Descreva o motivo da baixa.");
        }

        var (disposedOn, badDate) = await DateAsync(asset.Id, input.DisposedOn, "disposedOn", cancellationToken);
        if (badDate is not null)
        {
            return badDate;
        }

        var sale = kind == AssetDisposalReason.Sale;
        PartySummary? buyer = null;
        if (sale)
        {
            if (input.Value is not { } value)
            {
                return AssetService.Invalid("value", "Informe o valor da venda.");
            }

            if (!AssetMovementRules.IsValidValue(value))
            {
                return AssetService.Invalid("value", "Informe o valor da venda de zero para cima, com até 2 casas.");
            }

            if (input.BuyerPartyId is { } buyerId)
            {
                buyer = await parties.FindAsync(buyerId, cancellationToken);
                if (buyer is not { IsActive: true, IsDeleted: false } || !buyer.Roles.Contains(PartyRoleKind.Customer))
                {
                    return AssetService.Invalid("buyerPartyId", "Escolha um cliente ativo da lista.");
                }
            }
        }

        var movement = AssetMovement.Disposal(asset, kind, disposedOn, reason, input.Value, buyer?.Id, currentUser.UserId, clock.GetUtcNow());
        db.AssetMovements.Add(movement);
        events.Publish(new AssetDisposed(asset.Id, disposedOn));
        await db.SaveChangesAsync(cancellationToken);

        // CA 7: o próximo passo "Lançar título a receber" só para quem lança no Financeiro; nada é gravado lá daqui.
        var receivable = sale && await permissions.HasPermissionAsync("finance", StandardActions.Create, cancellationToken)
            ? new AssetReceivableDraft(buyer?.Id, buyer is null ? null : buyer.TradeName ?? buyer.LegalName, input.Value!.Value, disposedOn, AssetMovementRules.SaleDescription(asset.Tag, asset.Name))
            : null;
        return await ResultAsync(asset, movement, receivable, false, cancellationToken);
    }

    /// <summary>"Reativar bem" (CA 8): o baixado por engano volta a <c>Active</c> com um movimento de reativação; nada é apagado.</summary>
    public async Task<Result<AssetMovementResult>> ReactivateAsync(Guid assetId, AssetReactivationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var asset = await LoadAsync(assetId, input.Version, cancellationToken);
        if (asset is null)
        {
            return AssetService.NotFound();
        }

        if (asset.Status != AssetStatus.Disposed)
        {
            return Error.Conflict(NotDisposedCode, "Só o bem baixado é reativado.");
        }

        if (RequiredReason(input.Reason, out var reason) is { } badReason)
        {
            return badReason;
        }

        var (reactivatedOn, badDate) = await DateAsync(asset.Id, input.ReactivatedOn, "reactivatedOn", cancellationToken);
        if (badDate is not null)
        {
            return badDate;
        }

        var disposal = await db.AssetMovements.AsNoTracking().Where(m => m.AssetId == asset.Id && m.Kind == AssetMovementKind.Disposal)
            .OrderByDescending(m => m.CreatedAt).Select(m => m.DisposalReason).FirstOrDefaultAsync(cancellationToken);
        var movement = AssetMovement.Reactivation(asset, reactivatedOn, reason, currentUser.UserId, clock.GetUtcNow());
        db.AssetMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);
        return await ResultAsync(asset, movement, null, disposal == AssetDisposalReason.Sale, cancellationToken);
    }

    /// <summary>Comprador da venda (CA 6): clientes ativos do <c>IParties</c>.</summary>
    public async Task<IReadOnlyList<AssetBuyerChoice>> BuyerChoicesAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, PartyRoleKind.Customer, 20, cancellationToken)).Select(p => new AssetBuyerChoice(p.Id, p.TradeName ?? p.LegalName, p.Kind == PartyKind.PJ ? p.Document : null)).ToList();

    private sealed record TransferPlan(Error? Error, AssetTransferPreview? Preview, IReadOnlyList<(Asset Asset, AssetPlacement Target)> Moves, DateOnly MovedOn, string Reason);

    private async Task<TransferPlan> PlanAsync(AssetTransferInput input, bool track, CancellationToken ct)
    {
        TransferPlan Fail(Error error) => new(error, null, [], default, string.Empty);
        var ids = (input.AssetIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            return Fail(AssetService.Invalid("assetIds", "Selecione ao menos um bem."));
        }

        if (ids.Count > AssetMovementRules.BatchMax)
        {
            return Fail(AssetService.Invalid("assetIds", $"Transfira até {AssetMovementRules.BatchMax} bens por vez."));
        }

        if (input.SiteId is null && input.ResponsibleEmployeeId is null && input.WorkCenterId is null)
        {
            return Fail(AssetService.Invalid("siteId", "Escolha o local, o responsável ou o centro de destino."));
        }

        if (RequiredReason(input.Reason, out var reason) is { } badReason)
        {
            return Fail(badReason);
        }

        var today = BusinessTime.Today(clock);
        var movedOn = input.MovedOn ?? today;
        if (!AssetMovementRules.IsValidDate(movedOn, today, null))
        {
            return Fail(AssetService.Invalid("movedOn", "A data não pode ser futura."));
        }

        if (await LinksAsync(new AssetPlacement(null, null, null), new AssetPlacement(input.SiteId, input.ResponsibleEmployeeId, input.WorkCenterId), ct) is { } badLink)
        {
            return Fail(badLink);
        }

        var query = track ? db.Assets : db.Assets.AsNoTracking();
        var found = await query.Where(a => ids.Contains(a.Id)).OrderBy(a => a.Tag).ToListAsync(ct);
        var last = await db.AssetMovements.AsNoTracking().Where(m => ids.Contains(m.AssetId)).GroupBy(m => m.AssetId)
            .Select(g => new { g.Key, Last = g.Max(m => m.MovedOn) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var refused = new List<AssetTransferRefusal>();
        foreach (var missing in ids.Where(id => found.All(a => a.Id != id)))
        {
            refused.Add(new AssetTransferRefusal(missing, "—", "—", "Bem não encontrado."));
        }

        var moves = new List<(Asset, AssetPlacement)>();
        foreach (var asset in found)
        {
            var current = AssetPlacement.Of(asset);
            var target = AssetMovementRules.Merge(current, input.SiteId, input.ResponsibleEmployeeId, input.WorkCenterId);
            var why = !AssetMovementRules.IsMovable(asset.Status) ? "Baixado: reative o bem antes de transferir."
                : !AssetMovementRules.Changes(current, target) ? "Já está no destino."
                : !AssetMovementRules.IsValidDate(movedOn, today, last.TryGetValue(asset.Id, out var l) ? l : null) ? "A data é anterior à última movimentação do bem."
                : null;
            if (why is null)
            {
                moves.Add((asset, target));
            }
            else
            {
                refused.Add(new AssetTransferRefusal(asset.Id, asset.Tag, asset.Name, why));
            }
        }

        var places = await PlacesAsync(moves.SelectMany(m => new[] { AssetPlacement.Of(m.Item1), m.Item2 }), ct);
        var items = moves.Select(m => new AssetTransferLine(m.Item1.Id, m.Item1.Tag, m.Item1.Name, places.View(AssetPlacement.Of(m.Item1)), places.View(m.Item2))).ToList();
        return new TransferPlan(null, new AssetTransferPreview(items, refused), moves, movedOn, reason);
    }

    private async Task<Asset?> LoadAsync(Guid id, uint? version, CancellationToken ct)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is not null && version is { } v)
        {
            db.Entry(asset).Property(a => a.Version).OriginalValue = v;
        }

        return asset;
    }

    private static Error? RequiredReason(string? input, out string reason)
    {
        reason = AssetRules.Clean(input) ?? string.Empty;
        return reason.Length is 0 or > AssetMovementRules.ReasonMax ? AssetService.Invalid("reason", "Informe o motivo, com até 200 caracteres.") : null;
    }

    /// <summary>Data do movimento: padrão hoje, até hoje e não antes do último movimento do bem.</summary>
    private async Task<(DateOnly Date, Error? Error)> DateAsync(Guid assetId, DateOnly? input, string field, CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        var date = input ?? today;
        var last = await db.AssetMovements.AsNoTracking().Where(m => m.AssetId == assetId).OrderByDescending(m => m.MovedOn).Select(m => (DateOnly?)m.MovedOn).FirstOrDefaultAsync(ct);
        if (AssetMovementRules.IsValidDate(date, today, last))
        {
            return (date, null);
        }

        return (date, AssetService.Invalid(field, last is { } l && date < l && date <= today
            ? $"A data não pode ser anterior à última movimentação ({l:dd/MM/yyyy})."
            : "A data não pode ser futura."));
    }

    /// <summary>O destino novo precisa estar ativo (local, funcionário e centro); o que já estava no bem continua aceito.</summary>
    private async Task<Error?> LinksAsync(AssetPlacement current, AssetPlacement target, CancellationToken ct)
    {
        if (target.SiteId is { } site && site != current.SiteId && !await db.AssetSites.AnyAsync(s => s.Id == site && s.IsActive, ct))
        {
            return AssetService.Invalid("siteId", "Escolha um local ativo da lista.");
        }

        if (target.ResponsibleEmployeeId is { } employee && employee != current.ResponsibleEmployeeId && !(await employees.ActiveAsync(ct)).Any(e => e.Id == employee))
        {
            return AssetService.Invalid("responsibleEmployeeId", "Escolha um funcionário ativo da lista.");
        }

        if (target.WorkCenterId is { } center && center != current.WorkCenterId && !(await workCenters.ActiveAsync(ct)).Any(c => c.Id == center))
        {
            return AssetService.Invalid("workCenterId", "Escolha um centro de trabalho ativo da lista.");
        }

        return null;
    }

    private async Task<AssetMovementResult> ResultAsync(Asset asset, AssetMovement movement, AssetReceivableDraft? receivable, bool saleReverted, CancellationToken ct)
    {
        var detail = await assets.GetAsync(asset.Id, ct);
        return new AssetMovementResult(detail.Value, (await ViewsAsync([movement], ct))[0], receivable, saleReverted);
    }

    private sealed record Places(IReadOnlyDictionary<Guid, string> Sites, IReadOnlyDictionary<Guid, string> Employees, IReadOnlyDictionary<Guid, string> Centers)
    {
        public AssetPlaceView View(AssetPlacement p) => new(
            p.SiteId, p.SiteId is { } s ? Sites.GetValueOrDefault(s) : null,
            p.ResponsibleEmployeeId, p.ResponsibleEmployeeId is { } e ? Employees.GetValueOrDefault(e) : null,
            p.WorkCenterId, p.WorkCenterId is { } c ? Centers.GetValueOrDefault(c) : null);
    }

    /// <summary>Nomes de local, funcionário e centro numa consulta por fonte (inclusive excluídos e desligados: o histórico continua legível).</summary>
    private async Task<Places> PlacesAsync(IEnumerable<AssetPlacement> placements, CancellationToken ct)
    {
        var all = placements.ToList();
        var siteIds = all.Select(p => p.SiteId).OfType<Guid>().Distinct().ToList();
        var sites = siteIds.Count == 0 ? new Dictionary<Guid, string>()
            : await db.AssetSites.IgnoreQueryFilters().AsNoTracking().Where(s => siteIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var employeeIds = all.Select(p => p.ResponsibleEmployeeId).OfType<Guid>().Distinct().ToList();
        var people = employeeIds.Count == 0 ? new Dictionary<Guid, string>() : (await employees.FindAsync(employeeIds, ct)).ToDictionary(e => e.Id, e => e.Name);
        var centerIds = all.Select(p => p.WorkCenterId).OfType<Guid>().Distinct().ToList();
        var centers = centerIds.Count == 0 ? new Dictionary<Guid, string>() : (await workCenters.FindAsync(centerIds, ct)).ToDictionary(c => c.Id, c => c.Name);
        return new Places(sites, people, centers);
    }

    private async Task<List<AssetMovementView>> ViewsAsync(IReadOnlyList<AssetMovement> rows, CancellationToken ct)
    {
        static AssetPlacement From(AssetMovement m) => new(m.FromSiteId, m.FromEmployeeId, m.FromWorkCenterId);
        static AssetPlacement To(AssetMovement m) => new(m.ToSiteId, m.ToEmployeeId, m.ToWorkCenterId);
        var places = await PlacesAsync(rows.SelectMany(m => new[] { From(m), To(m) }), ct);
        var buyerIds = rows.Select(m => m.BuyerPartyId).OfType<Guid>().Distinct().ToList();
        var buyers = buyerIds.Count == 0 ? new Dictionary<Guid, string>() : (await parties.FindManyAsync(buyerIds, ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var names = new Dictionary<Guid, string?>();
        foreach (var user in rows.Select(m => m.CreatedBy).OfType<Guid>().Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, ct);
        }

        var canEdit = await permissions.HasPermissionAsync(AssetService.Module, StandardActions.Edit, ct);
        return rows.Select(m => new AssetMovementView(
            m.Id, m.AssetId, m.Kind, m.MovedOn, places.View(From(m)), places.View(To(m)), m.Reason, m.DisposalReason,
            m.BuyerPartyId, m.BuyerPartyId is { } b ? buyers.GetValueOrDefault(b) : null,
            m.CreatedBy, m.CreatedBy is { } u ? names.GetValueOrDefault(u) : null, m.CreatedAt,
            canEdit ? m.DisposalValue : null)).ToList();
    }

    private static Error MovementNotFound() => Error.NotFound(MovementNotFoundCode, "Movimentação não encontrada.");
}
