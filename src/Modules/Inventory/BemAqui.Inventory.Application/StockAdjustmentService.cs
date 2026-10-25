using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Ajuste de estoque (EST-01b, ADR 0023 §1 e §5): sobra ou falta sem documento de origem, num modo só (quantidade contada ou
/// diferença), com motivo. Quem tem <c>inventory.adjust</c> lança; nasce <c>Pending</c> sem mexer no saldo; outra pessoa com
/// <c>inventory.approve</c> decide sobre o que foi enviado, sem editar, e a aprovação grava os movimentos <c>Adjustment</c> pelo
/// <see cref="IStockMovements"/> na mesma transação, recalculando sobre o saldo de agora. O saldo nunca fica abaixo do reservado (CA 4).
/// Todo ajuste passa por aprovação, sem faixa de valor (CA 14). O inventário (EST-05) gera o ajuste no modo Delta pelo
/// <see cref="CreateFromCountAsync"/>.
/// </summary>
public sealed partial class StockAdjustmentService(
    IInventoryDb db, IStockMovements ledger, ReservationAllocator allocator, INumberSequence numbers, IUnitConversion conversion, IUserDirectory users,
    IPermissionChecker permissions, INotificationService notifications, ICurrentUser currentUser, TimeProvider clock, ILocationCountLock locks)
{
    public const string Link = "/app/estoque/ajustes/";
    public const string ApprovalType = "inventory.adjustment_pending";
    public const string DecisionType = "inventory.adjustment_decided";
    public const string SelfApprovedType = "inventory.self_approved";
    public const string ReservationsRecovery = "Ver reservas do item";

    /// <summary>"Enviar para aprovação" (CA 2 a 4 e 18): o ajuste nasce <c>Pending</c>, com o número AJ, e o saldo não muda.</summary>
    public async Task<Result<StockAdjustmentDetail>> CreateAsync(StockAdjustmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var location = await ActiveLocationAsync(input.LocationId, cancellationToken);
        if (location.IsFailure)
        {
            return location.Error!;
        }

        var detail = Clean(input.ReasonDetail);
        if (!Enum.IsDefined(input.Reason))
        {
            return Invalid("reason", "Escolha o motivo do ajuste.");
        }

        if ((input.Reason == StockAdjustmentReason.Other && detail is null) || detail is { Length: > StockAdjustmentRules.MaxReasonDetail })
        {
            return Invalid("reasonDetail", $"Descreva o motivo, com até {StockAdjustmentRules.MaxReasonDetail} caracteres.");
        }

        if (!Enum.IsDefined(input.Mode))
        {
            return Invalid("mode", "Escolha o modo do ajuste.");
        }

        var lines = await ValidateLinesAsync(input.Mode, input.Lines, cancellationToken);
        if (lines.IsFailure)
        {
            return lines.Error!;
        }

        // CA 18: com inventário em contagem no local, o ajuste manual não nasce.
        if (await locks.CheckAsync([location.Value.Id], cancellationToken) is { } blocked)
        {
            return blocked;
        }

        return await SubmitAsync(location.Value, input.Mode, input.Reason, detail, StockAdjustmentOrigin.Manual, null, null, lines.Value, cancellationToken);
    }

    /// <summary>
    /// Ajuste do inventário (CA 13, EST-05 CA 10): nasce no modo Delta, motivo "Contagem", com a referência da contagem, e segue o mesmo
    /// fluxo de aprovação. Usa a transação aberta do chamador (o fechamento do inventário) ou abre a própria. Não passa pelo bloqueio do
    /// local (CA 18): é o próprio inventário que gera.
    /// </summary>
    public async Task<Result<StockAdjustmentDetail>> CreateFromCountAsync(CountAdjustmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var location = await ActiveLocationAsync(input.LocationId, cancellationToken);
        if (location.IsFailure)
        {
            return location.Error!;
        }

        var lines = await ValidateLinesAsync(
            StockAdjustmentMode.Delta, input.Lines.Select(l => new StockAdjustmentLineInput(l.ItemId, l.LotId, l.DeltaQuantity, null)).ToList(), cancellationToken);
        if (lines.IsFailure)
        {
            return lines.Error!;
        }

        return await SubmitAsync(location.Value, StockAdjustmentMode.Delta, StockAdjustmentReason.Count, $"Inventário {input.CountNumber}", StockAdjustmentOrigin.InventoryCount,
            input.CountId, input.CountNumber, lines.Value, cancellationToken);
    }

    /// <summary>Linha validada, na unidade de estoque.</summary>
    private sealed record ValidLine(Item Item, string UnitCode, Guid? LotId, decimal Quantity, decimal Entered, Guid? EnteredUnitId);

    private async Task<Result<StockAdjustmentDetail>> SubmitAsync(
        Location location, StockAdjustmentMode mode, StockAdjustmentReason reason, string? detail, StockAdjustmentOrigin origin, Guid? originId, string? originNumber,
        IReadOnlyList<ValidLine> lines, CancellationToken ct)
    {
        var balances = await BalancesAsync(location.Id, lines.Select(l => (l.Item.Id, l.LotId)).ToList(), ct);
        var (results, refusal) = StockAdjustmentRules.Evaluate(mode, Inputs(mode, lines), balances, lines.Select(l => l.UnitCode).ToList());
        for (var i = 0; i < results.Count; i++)
        {
            if (results[i].Delta == 0)
            {
                return Invalid($"lines[{i}].quantity", mode == StockAdjustmentMode.Counted
                    ? "Sem diferença: a quantidade contada é igual ao saldo do sistema. Tire a linha do ajuste."
                    : "Informe uma diferença diferente de zero.");
            }
        }

        if (refusal is not null)
        {
            return Invalid($"lines[{refusal.Index}].quantity", refusal.Message) with { Data = ReservationsData(lines[refusal.Index].Item.Id, location.Id, refusal) };
        }

        var (surplus, shortage) = StockAdjustmentRules.Totals(results.Select((r, i) => (r.Delta, lines[i].Item.AverageCost)));
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var adjustment = new StockAdjustment
            {
                Number = await numbers.NextAsync(StockAdjustmentRules.DocType, ct), LocationId = location.Id, Mode = mode, Origin = origin, OriginId = originId, OriginNumber = originNumber,
                Reason = reason, ReasonDetail = detail, Status = StockAdjustmentStatus.Pending, RequestedBy = currentUser.UserId, RequestedAt = clock.GetUtcNow(),
                SurplusValue = surplus, ShortageValue = shortage,
            };
            db.StockAdjustments.Add(adjustment);
            db.StockAdjustmentLines.AddRange(lines.Select((l, i) => new StockAdjustmentLine
            {
                AdjustmentId = adjustment.Id, Sequence = i + 1, ItemId = l.Item.Id, LotId = l.LotId, SystemQtyAtRequest = results[i].SystemQty,
                CountedQty = mode == StockAdjustmentMode.Counted ? l.Quantity : null, DeltaQty = results[i].Delta, UnitCostEstimate = l.Item.AverageCost,
                EnteredQuantity = l.Entered, EnteredUnitId = l.EnteredUnitId,
            }));
            await db.SaveChangesAsync(ct);
            await NotifyApproversAsync(adjustment, location.Name, lines.Select(l => l.Item.Id).Distinct().Count(), ct);
            if (own is not null)
            {
                await own.CommitAsync(ct);
            }

            return await DetailAsync(adjustment.Id, ct);
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync();
            }
        }
    }

    private static List<AdjustmentLineInput> Inputs(StockAdjustmentMode mode, IReadOnlyList<ValidLine> lines) =>
        lines.Select(l => new AdjustmentLineInput(l.Item.Id, l.LotId, mode == StockAdjustmentMode.Counted ? l.Quantity : null, mode == StockAdjustmentMode.Delta ? l.Quantity : 0m)).ToList();

    /// <summary>
    /// Validação das linhas (CA 2 e 16): item com estoque e ativo, lote do próprio item (outro item: 422), quantidade conforme o modo do
    /// ajuste (contada ≥ 0, diferença ≠ 0), a conversão de unidade e item e lote sem repetição. Campo a mais na linha: 422 (o modo é do
    /// ajuste inteiro).
    /// </summary>
    private async Task<Result<List<ValidLine>>> ValidateLinesAsync(StockAdjustmentMode mode, IReadOnlyList<StockAdjustmentLineInput>? input, CancellationToken ct)
    {
        if (input is not { Count: > 0 })
        {
            return Invalid("lines", "Inclua ao menos uma linha no ajuste.");
        }

        if (input.Count > StockAdjustmentRules.MaxLines)
        {
            return Invalid("lines", $"Use até {StockAdjustmentRules.MaxLines} linhas por ajuste.");
        }

        var itemIds = input.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var unitIds = items.Values.Select(i => i.UnitId).Distinct().ToList();
        var units = await db.Units.IgnoreQueryFilters().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, ct);
        var lotIds = input.Where(l => l.LotId is not null).Select(l => l.LotId!.Value).Distinct().ToList();
        var lots = await db.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.ItemId, ct);
        var valid = new List<ValidLine>();
        var seen = new HashSet<(Guid, Guid?)>();
        for (var i = 0; i < input.Count; i++)
        {
            var line = input[i];
            var field = $"lines[{i}]";
            if (line.Extra is { Count: > 0 } extra)
            {
                const string message = "O modo é do ajuste inteiro, não da linha: envie só o item, o lote, a quantidade e a unidade.";
                return Error.Validation(Error.Codes.Validation, message, extra.Keys.ToDictionary(k => $"{field}.{k}", _ => new[] { message }));
            }

            if (!items.TryGetValue(line.ItemId, out var item))
            {
                return Invalid(field + ".itemId", "Item não encontrado.");
            }

            if (!item.IsActive || item.Type == ItemType.Service)
            {
                return Invalid(field + ".itemId", item.Type == ItemType.Service ? "Serviço não tem estoque." : "Item inativo não movimenta estoque.");
            }

            if (item.TracksLot && line.LotId is null)
            {
                return Invalid(field + ".lotId", "Este item controla lote: escolha o lote.");
            }

            if (!item.TracksLot && line.LotId is not null)
            {
                return Invalid(field + ".lotId", "Este item não controla lote.");
            }

            if (line.LotId is { } lotId && (!lots.TryGetValue(lotId, out var owner) || owner != item.Id))
            {
                return Invalid(field + ".lotId", "Lote não encontrado neste item.");
            }

            if (!seen.Add((item.Id, line.LotId)))
            {
                return Invalid(field + ".itemId", item.TracksLot ? "Item e lote repetidos neste ajuste." : "Item repetido neste ajuste.");
            }

            if (mode == StockAdjustmentMode.Counted && line.Quantity < 0)
            {
                return Invalid(field + ".quantity", "A quantidade contada não pode ser negativa.");
            }

            if (mode == StockAdjustmentMode.Delta && line.Quantity == 0)
            {
                return Invalid(field + ".quantity", "Informe uma diferença diferente de zero.");
            }

            var quantity = await StockQuantityAsync(item, line.Quantity, line.UnitId, field, ct);
            if (quantity.IsFailure)
            {
                return quantity.Error!;
            }

            valid.Add(new ValidLine(item, units.GetValueOrDefault(item.UnitId) ?? string.Empty, line.LotId, quantity.Value, line.Quantity, line.UnitId ?? item.UnitId));
        }

        return valid;
    }

    /// <summary>Quantidade na unidade de estoque (R-EST-3), com o sinal da diferença: outra unidade passa pelo <see cref="IUnitConversion"/>.</summary>
    private async Task<Result<decimal>> StockQuantityAsync(Item item, decimal quantity, Guid? unitId, string field, CancellationToken ct)
    {
        if (unitId is null || unitId == item.UnitId || quantity == 0)
        {
            return StockAdjustmentRules.Round(quantity);
        }

        var converted = await conversion.ConvertAsync(item.Id, Math.Abs(quantity), unitId.Value, item.UnitId, ct);
        if (converted.IsFailure)
        {
            return Invalid(field + ".unitId", converted.Error!.Message);
        }

        return converted.Value == 0 ? Invalid(field + ".quantity", "A quantidade convertida fica zero. Aumente a quantidade.") : Math.Sign(quantity) * converted.Value;
    }

    /// <summary>Físico e reservado da posição (item, local) e o físico do lote de cada linha, lidos agora.</summary>
    private async Task<List<AdjustmentBalance>> BalancesAsync(Guid locationId, IReadOnlyList<(Guid ItemId, Guid? LotId)> lines, CancellationToken ct)
    {
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var positions = await db.StockPositions.AsNoTracking().Where(p => p.LocationId == locationId && itemIds.Contains(p.ItemId))
            .Select(p => new { p.ItemId, p.QuantityOnHand, p.QuantityReserved }).ToListAsync(ct);
        var balances = await db.StockBalances.AsNoTracking().Where(b => b.LocationId == locationId && itemIds.Contains(b.ItemId))
            .Select(b => new { b.ItemId, b.LotId, b.QuantityOnHand }).ToListAsync(ct);
        return lines.Select(l =>
        {
            var position = positions.FirstOrDefault(p => p.ItemId == l.ItemId);
            var lot = balances.FirstOrDefault(b => b.ItemId == l.ItemId && b.LotId == l.LotId)?.QuantityOnHand ?? 0m;
            return new AdjustmentBalance(position?.QuantityOnHand ?? 0m, position?.QuantityReserved ?? 0m, lot);
        }).ToList();
    }

    /// <summary>Ação "Ver reservas do item" (CA 4): as reservas daquele item e local (aba Reservas do item, EST-02).</summary>
    private static Dictionary<string, object?> ReservationsData(Guid itemId, Guid locationId, AdjustmentRefusal refusal) => new()
    {
        ["recovery"] = ReservationsRecovery, ["itemId"] = itemId, ["locationId"] = locationId, ["line"] = refusal.Index, ["reserved"] = refusal.Reserved, ["after"] = refusal.After,
        ["link"] = $"/app/estoque/itens/{itemId}?aba=reservas",
    };

    /// <summary>Aviso a quem pode aprovar (com <c>inventory.approve</c> efetivo), menos o autor (CA 3, R-PRP-5), uma vez por ajuste.</summary>
    private async Task NotifyApproversAsync(StockAdjustment adjustment, string locationName, int items, CancellationToken ct)
    {
        var count = items == 1 ? "1 item" : $"{items} itens";
        var title = $"Ajuste {adjustment.Number} de {locationName} aguardando aprovação: {count}, {ManualPostingRules.Money(adjustment.SurplusValue + adjustment.ShortageValue)} de diferença.";
        foreach (var user in (await users.UsersWithPermissionAsync(StockAdjustmentRules.ApproverPermission, ct)).Where(u => u != adjustment.RequestedBy))
        {
            await notifications.NotifyOnceAsync(user, ApprovalType, title, Link + adjustment.Id, $"inventory-adjustment:{adjustment.Id}", ct);
        }
    }

    private async Task<Result<Location>> ActiveLocationAsync(Guid id, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        if (location is null)
        {
            return Invalid("locationId", "Local não encontrado.");
        }

        return location.IsActive ? location : Invalid("locationId", "Local inativo não recebe ajuste.");
    }

    private async Task<bool> IsDecidingAloneAsync(Guid me, CancellationToken ct) =>
        await SoleOwnerRule.NoOtherEligibleAsync(users, StockAdjustmentRules.ApproverPermission, me, [me], ct);

    private async Task NotifySelfApprovedAsync(string what, string link, CancellationToken ct)
    {
        var name = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, ct) ?? "Um usuário" : "Um usuário";
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, SelfApprovedType, $"{name} {what} ({SoleOwnerRule.SelfApprovedLabel}).", link);
        }
    }

    internal static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    internal static Error NotFound() => Error.NotFound("stock_adjustment_not_found", "Ajuste não encontrado.");

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
