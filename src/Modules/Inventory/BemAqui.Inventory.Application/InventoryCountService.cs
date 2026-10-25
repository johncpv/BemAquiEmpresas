using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Inventário por local (EST-05, R-EST-9): abrir com a foto do saldo (<c>inventory.adjust</c>), contar às cegas
/// (<c>inventory.create</c>), recontar as divergências por outra pessoa, fechar gerando um ajuste da EST-01b em modo Delta e cancelar
/// ou reabrir. O inventário nunca mexe no saldo: o efeito vem só da aprovação do ajuste, pelo motor (ADR 0023 §1). Enquanto
/// <c>Counting</c>, o <see cref="InventoryCountLock"/> bloqueia ajuste e estorno manual no local (CA 6).
/// </summary>
public sealed partial class InventoryCountService(
    IInventoryDb db, StockAdjustmentService adjustments, INumberSequence numbers, IUnitConversion conversion, IUserDirectory users, IPermissionChecker permissions,
    ICurrentUser currentUser, TimeProvider clock)
{
    public const string Link = LocationCountLocks.Link;
    public const string CountingIndex = "ux_inventory_counts_location_counting";
    public const string LineIndex = "ux_inventory_count_lines_count_item_lot";
    public const string SequenceIndex = "ux_inventory_count_lines_count_sequence";
    public const string Reload = "Recarregar";

    /// <summary>"Abrir inventário" (CA 1 e 2): uma linha por item e lote do escopo, com o saldo do sistema na foto; um só por local.</summary>
    public async Task<Result<InventoryCountDetail>> OpenAsync(OpenInventoryCountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == input.LocationId, cancellationToken);
        if (location is null)
        {
            return Invalid("locationId", "Local não encontrado.");
        }

        if (!location.IsActive)
        {
            return Invalid("locationId", "Local inativo não tem inventário.");
        }

        var scope = input.Scope ?? new InventoryCountScopeInput(false, null, null);
        if (scope.ItemType == ItemType.Service)
        {
            return Invalid("scope.itemType", "Serviço não tem estoque.");
        }

        if (scope.CategoryId is { } categoryId && !await db.ItemCategories.AsNoTracking().AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            return Invalid("scope.categoryId", "Categoria não encontrada.");
        }

        if (await OpenInAsync(location.Id, cancellationToken) is { } open)
        {
            return AlreadyOpen(location.Name, open);
        }

        // A foto é uma consulta só (consistente no PostgreSQL), com o horário tomado antes dela.
        var snapshotAt = clock.GetUtcNow();
        var balances = from b in db.StockBalances.AsNoTracking()
                       join i in db.Items.AsNoTracking() on b.ItemId equals i.Id
                       where b.LocationId == location.Id && i.IsActive && i.Type != ItemType.Service
                       select new { b.ItemId, b.LotId, b.QuantityOnHand, i.Code, i.CategoryId, i.Type, LotNumber = db.Lots.Where(l => l.Id == b.LotId).Select(l => l.LotNumber).FirstOrDefault() };
        if (scope.CategoryId is { } category)
        {
            balances = balances.Where(b => b.CategoryId == category);
        }

        if (scope.ItemType is { } type)
        {
            balances = balances.Where(b => b.Type == type);
        }

        if (!scope.IncludeZeroWithMovement)
        {
            balances = balances.Where(b => b.QuantityOnHand != 0);
        }

        var rows = await balances.OrderBy(b => b.Code).ThenBy(b => b.LotNumber).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return Invalid("scope", "Nenhum item para contar neste local com este escopo. Inclua os itens zerados com movimento ou mude o filtro.");
        }

        if (rows.Count > InventoryCountRules.MaxLines)
        {
            return Invalid("scope", $"O escopo tem {rows.Count} linhas; use até {InventoryCountRules.MaxLines} por inventário (filtre por categoria ou tipo).");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var count = new InventoryCount
        {
            Number = await numbers.NextAsync(InventoryCountRules.DocType, cancellationToken), LocationId = location.Id, Status = InventoryCountStatus.Counting, SnapshotAt = snapshotAt,
            OpenedBy = currentUser.UserId, Scope = JsonSerializer.Serialize(new InventoryCountScope(scope.IncludeZeroWithMovement, scope.CategoryId, scope.ItemType)),
        };
        db.InventoryCounts.Add(count);
        var lines = rows.Select((r, i) => new InventoryCountLine { CountId = count.Id, Sequence = i + 1, ItemId = r.ItemId, LotId = r.LotId, SystemQty = r.QuantityOnHand }).ToList();
        db.InventoryCountLines.AddRange(lines);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CountingIndex))
        {
            // CA 2: dois "Abrir inventário" no mesmo local ao mesmo tempo; só um nasce.
            await transaction.RollbackAsync(cancellationToken);
            Detach(count);
            lines.ForEach(Detach);
            return await OpenInAsync(location.Id, cancellationToken) is { } other ? AlreadyOpen(location.Name, other) : Error.Conflict("inventory_count_open", $"Já há um inventário aberto em {location.Name}.");
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(count.Id, cancellationToken);
    }

    /// <summary>
    /// "Salvar contagem" de uma linha (CA 3, 8 e 16): a quantidade na unidade de estoque (ou convertida), quem contou, quando e o saldo do
    /// sistema naquele momento (para os movimentos durante a contagem, CA 5). A recontagem por quem contou a original é 422; linha
    /// contada por outra pessoa (ou ao mesmo tempo, <c>xmin</c>) é 409 com "Recarregar".
    /// </summary>
    public async Task<Result<InventoryCountingLine>> CountLineAsync(Guid id, Guid lineId, CountLineInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var count = await db.InventoryCounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (count is null)
        {
            return NotFound();
        }

        var line = await db.InventoryCountLines.FirstOrDefaultAsync(l => l.Id == lineId && l.CountId == id, cancellationToken);
        if (line is null)
        {
            return LineNotFound();
        }

        if (NotCounting(count) is { } closed)
        {
            return closed;
        }

        if (line.Superseded)
        {
            return Error.Conflict("inventory_line_superseded", "Esta linha foi para a recontagem. Recarregue a lista.") with { Data = new Dictionary<string, object?> { ["recovery"] = Reload } };
        }

        if (currentUser.UserId is not { } me)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Entre de novo para contar.");
        }

        if (line.RecountOfLineId is { } originalId)
        {
            var originalCounter = await db.InventoryCountLines.AsNoTracking().Where(l => l.Id == originalId).Select(l => l.CountedBy).FirstOrDefaultAsync(cancellationToken);
            if (!InventoryCountRules.MayRecount(originalCounter, me))
            {
                return Error.Validation("recount_by_other", InventoryCountRules.RecountByOtherMessage, new Dictionary<string, string[]> { ["quantity"] = [InventoryCountRules.RecountByOtherMessage] });
            }
        }

        if (line.CountedBy is { } by && by != me)
        {
            return await CountedByOtherAsync(by, cancellationToken);
        }

        if (line.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "A linha mudou desde que você abriu. Recarregue.") with { Data = new Dictionary<string, object?> { ["recovery"] = Reload } };
        }

        var quantity = await QuantityAsync(line.ItemId, input.Quantity, input.UnitId, cancellationToken);
        if (quantity.IsFailure)
        {
            return quantity.Error!;
        }

        db.Entry(line).Property(l => l.Version).OriginalValue = input.Version;
        line.CountedQty = quantity.Value;
        line.EnteredQuantity = input.Quantity;
        line.EnteredUnitId = input.UnitId;
        line.CountedBy = me;
        line.CountedAt = clock.GetUtcNow();
        line.SystemQtyAtCount = await LotBalanceAsync(count.LocationId, line.ItemId, line.LotId, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // CA 16: dois contadores na mesma linha; o primeiro grava e o segundo vê quem contou.
            var winner = await db.InventoryCountLines.AsNoTracking().Where(l => l.Id == lineId).Select(l => l.CountedBy).FirstOrDefaultAsync(cancellationToken);
            return winner is { } w && w != me ? await CountedByOtherAsync(w, cancellationToken) : Error.Conflict("concurrency_conflict", "A linha mudou desde que você abriu. Recarregue.");
        }

        return await CountingLineAsync(id, lineId, cancellationToken);
    }

    /// <summary>"Encontrei outro item" (CA 3): inclui item e lote que não estavam na lista, já contado; a foto dele é o saldo − os movimentos desde a abertura.</summary>
    public async Task<Result<InventoryCountingLine>> AddFoundAsync(Guid id, FoundLineInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var count = await db.InventoryCounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (count is null)
        {
            return NotFound();
        }

        if (NotCounting(count) is { } closed)
        {
            return closed;
        }

        if (currentUser.UserId is not { } me)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Entre de novo para contar.");
        }

        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == input.ItemId, cancellationToken);
        if (item is null)
        {
            return Invalid("itemId", "Item não encontrado.");
        }

        if (!item.IsActive || item.Type == ItemType.Service)
        {
            return Invalid("itemId", item.Type == ItemType.Service ? "Serviço não tem estoque." : "Item inativo não entra no inventário.");
        }

        if (item.TracksLot && input.LotId is null)
        {
            return Invalid("lotId", "Este item controla lote: escolha o lote.");
        }

        if (!item.TracksLot && input.LotId is not null)
        {
            return Invalid("lotId", "Este item não controla lote.");
        }

        if (input.LotId is { } lotId && !await db.Lots.AsNoTracking().AnyAsync(l => l.Id == lotId && l.ItemId == item.Id, cancellationToken))
        {
            return Invalid("lotId", "Lote não encontrado neste item.");
        }

        var existing = await db.InventoryCountLines.AsNoTracking().Where(l => l.CountId == id && !l.Superseded && l.ItemId == item.Id && l.LotId == input.LotId).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is { } existingId)
        {
            return Exists(existingId);
        }

        var quantity = await QuantityAsync(item.Id, input.Quantity, input.UnitId, cancellationToken);
        if (quantity.IsFailure)
        {
            return quantity.Error!;
        }

        var now = await LotBalanceAsync(count.LocationId, item.Id, input.LotId, cancellationToken);
        var since = await db.StockMovements.AsNoTracking()
            .Where(m => m.LocationId == count.LocationId && m.ItemId == item.Id && m.LotId == input.LotId && m.PostedAt > count.SnapshotAt).SumAsync(m => (decimal?)m.Quantity, cancellationToken) ?? 0m;
        var sequence = (await db.InventoryCountLines.AsNoTracking().Where(l => l.CountId == id).MaxAsync(l => (int?)l.Sequence, cancellationToken) ?? 0) + 1;
        var line = new InventoryCountLine
        {
            CountId = id, Sequence = sequence, ItemId = item.Id, LotId = input.LotId, SystemQty = InventoryCountRules.Round(now - since), CountedQty = quantity.Value,
            EnteredQuantity = input.Quantity, EnteredUnitId = input.UnitId, CountedBy = me, CountedAt = clock.GetUtcNow(), SystemQtyAtCount = now, AddedDuringCount = true,
        };
        db.InventoryCountLines.Add(line);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, LineIndex) || DbErrors.IsUniqueViolation(ex, SequenceIndex))
        {
            Detach(line);
            var other = await db.InventoryCountLines.AsNoTracking().Where(l => l.CountId == id && !l.Superseded && l.ItemId == item.Id && l.LotId == input.LotId).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(cancellationToken);
            return other is { } otherId ? Exists(otherId) : Error.Conflict("concurrency_conflict", "Outra pessoa incluiu uma linha agora. Tente de novo.");
        }

        return await CountingLineAsync(id, line.Id, cancellationToken);
    }

    /// <summary>
    /// "Pedir recontagem" (CA 7): cada linha contada vira uma linha nova de recontagem (<c>recount_of_line_id</c>); a anterior fica
    /// substituída na diferença e continua no histórico.
    /// </summary>
    public async Task<Result<InventoryDivergences>> RecountAsync(Guid id, RecountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.LineIds is not { Count: > 0 } ids)
        {
            return Invalid("lineIds", "Marque ao menos uma linha para recontar.");
        }

        var count = await db.InventoryCounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (count is null)
        {
            return NotFound();
        }

        if (NotCounting(count) is { } closed)
        {
            return closed;
        }

        if (count.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O inventário mudou desde que você abriu. Recarregue.");
        }

        var distinct = ids.Distinct().ToList();
        var lines = await db.InventoryCountLines.Where(l => l.CountId == id && distinct.Contains(l.Id)).ToListAsync(cancellationToken);
        if (lines.Count != distinct.Count)
        {
            return LineNotFound();
        }

        if (lines.FirstOrDefault(l => l.Superseded || l.CountedQty is null) is { } invalid)
        {
            return Invalid("lineIds", invalid.Superseded ? "Uma das linhas já foi para a recontagem. Recarregue." : "Só linhas contadas vão para a recontagem.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var line in lines)
        {
            line.Superseded = true;
        }

        // Duas gravações: o índice único (inventário, item, lote) das linhas vigentes é conferido a cada comando.
        await db.SaveChangesAsync(cancellationToken);
        var next = (await db.InventoryCountLines.Where(l => l.CountId == id).MaxAsync(l => (int?)l.Sequence, cancellationToken) ?? 0) + 1;
        foreach (var line in lines.OrderBy(l => l.Sequence))
        {
            db.InventoryCountLines.Add(new InventoryCountLine { CountId = id, Sequence = next++, ItemId = line.ItemId, LotId = line.LotId, SystemQty = line.SystemQty, RecountOfLineId = line.Id });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DivergencesAsync(id, null, cancellationToken);
    }

    /// <summary>
    /// "Fechar inventário" (CA 9 e 10): com linhas não contadas, a escolha é obrigatória e fica gravada; cada linha grava os movimentos
    /// durante a contagem, a diferença e o custo médio; o inventário vai a <c>Closed</c> e nasce, na mesma transação, um ajuste
    /// <c>Pending</c> em modo Delta com uma linha por divergência (sem divergência, fecha sem ajuste). O saldo não muda aqui.
    /// </summary>
    public async Task<Result<InventoryCountDetail>> CloseAsync(Guid id, CloseInventoryCountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var count = await db.InventoryCounts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (count is null)
        {
            return NotFound();
        }

        if (NotCounting(count) is { } closed)
        {
            return closed;
        }

        if (input.UncountedPolicy is { } chosen && !Enum.IsDefined(chosen))
        {
            return Invalid("uncountedPolicy", "Escolha o que fazer com os itens não contados.");
        }

        var lines = await db.InventoryCountLines.Where(l => l.CountId == id && !l.Superseded).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var uncounted = lines.Count(l => l.CountedQty is null);
        if (uncounted > 0 && input.UncountedPolicy is null)
        {
            var question = InventoryCountRules.UncountedQuestion(uncounted);
            return Error.Validation("uncounted_lines", question, new Dictionary<string, string[]> { ["uncountedPolicy"] = [question] }) with
            {
                Data = new Dictionary<string, object?> { ["uncounted"] = uncounted, ["question"] = question },
            };
        }

        var policy = input.UncountedPolicy ?? UncountedPolicy.KeepSystem;
        var now = await LotBalancesAsync(count.LocationId, lines.Select(l => (l.ItemId, l.LotId)).ToList(), cancellationToken);
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var costs = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.AverageCost, cancellationToken);
        foreach (var line in lines)
        {
            var result = InventoryCountRules.Resolve(line.SystemQty, line.CountedQty, line.SystemQtyAtCount, now.GetValueOrDefault((line.ItemId, line.LotId)), policy);
            line.MovementDelta = result.MovementDelta;
            line.Difference = result.Difference;
            line.UnitCost = costs.GetValueOrDefault(line.ItemId);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Entry(count).Property(c => c.Version).OriginalValue = input.Version;
        count.Status = InventoryCountStatus.Closed;
        count.ClosedBy = currentUser.UserId;
        count.ClosedAt = clock.GetUtcNow();
        count.UncountedPolicy = uncounted > 0 ? policy : null;
        await db.SaveChangesAsync(cancellationToken);
        var divergent = lines.Where(l => l.Difference is not null and not 0m).ToList();
        if (divergent.Count > 0)
        {
            var created = await adjustments.CreateFromCountAsync(
                new CountAdjustmentInput(count.Id, count.Number, count.LocationId, divergent.Select(l => new CountAdjustmentLine(l.ItemId, l.LotId, l.Difference!.Value)).ToList()), cancellationToken);
            if (created.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return created.Error!;
            }

            count.AdjustmentId = created.Value.Id;
            count.AdjustmentNumber = created.Value.Number;
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>"Cancelar inventário" (CA 13): com motivo; fica <c>Cancelled</c>, sem ajuste, e o local é desbloqueado.</summary>
    public async Task<Result<InventoryCountDetail>> CancelAsync(Guid id, CancelInventoryCountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
        if (reason is null || reason.Length > InventoryCountRules.MaxReason)
        {
            return Invalid("reason", $"Informe o motivo do cancelamento, com até {InventoryCountRules.MaxReason} caracteres.");
        }

        var count = await db.InventoryCounts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (count is null)
        {
            return NotFound();
        }

        if (NotCounting(count) is { } closed)
        {
            return closed;
        }

        db.Entry(count).Property(c => c.Version).OriginalValue = input.Version;
        count.Status = InventoryCountStatus.Cancelled;
        count.CancelReason = reason;
        count.CancelledBy = currentUser.UserId;
        count.CancelledAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>
    /// "Reabrir contagem" (CA 12): o ajuste gerado foi reprovado (ou cancelado por quem fechou); o inventário volta a <c>Counting</c>,
    /// com as contagens de antes, e o local volta a ficar bloqueado. Se outro inventário foi aberto no local, 409.
    /// </summary>
    public async Task<Result<InventoryCountDetail>> ReopenAsync(Guid id, ReopenInventoryCountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var count = await db.InventoryCounts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (count is null)
        {
            return NotFound();
        }

        var status = count.AdjustmentId is { } adjustmentId
            ? await db.StockAdjustments.AsNoTracking().Where(a => a.Id == adjustmentId).Select(a => (StockAdjustmentStatus?)a.Status).FirstOrDefaultAsync(cancellationToken)
            : null;
        if (count.Status != InventoryCountStatus.Closed || status is not (StockAdjustmentStatus.Rejected or StockAdjustmentStatus.Cancelled))
        {
            return Error.Conflict("inventory_count_not_reopenable", "Só o inventário fechado com o ajuste reprovado ou cancelado volta para a contagem.");
        }

        db.Entry(count).Property(c => c.Version).OriginalValue = input.Version;
        count.Status = InventoryCountStatus.Counting;
        count.ReopenCount++;
        count.AdjustmentId = null;
        count.AdjustmentNumber = null;
        count.ClosedBy = null;
        count.ClosedAt = null;
        count.UncountedPolicy = null;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CountingIndex))
        {
            var name = await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => l.Id == count.LocationId).Select(l => l.Name).FirstAsync(cancellationToken);
            await db.Entry(count).ReloadAsync(cancellationToken);
            return await OpenInAsync(count.LocationId, cancellationToken) is { } other ? AlreadyOpen(name, other) : Error.Conflict("inventory_count_open", $"Já há um inventário aberto em {name}.");
        }

        return await DetailAsync(id, cancellationToken);
    }

    private async Task<(Guid Id, string Number)?> OpenInAsync(Guid locationId, CancellationToken ct)
    {
        var open = await db.InventoryCounts.AsNoTracking().Where(c => c.LocationId == locationId && c.Status == InventoryCountStatus.Counting)
            .Select(c => new { c.Id, c.Number }).FirstOrDefaultAsync(ct);
        return open is null ? null : (open.Id, open.Number);
    }

    /// <summary>CA 2: "Já há um inventário aberto em {local}: {número}.", com o link.</summary>
    internal static Error AlreadyOpen(string locationName, (Guid Id, string Number) open) =>
        Error.Conflict("inventory_count_open", $"Já há um inventário aberto em {locationName}: {open.Number}.") with
        {
            Data = new Dictionary<string, object?> { ["countId"] = open.Id, ["countNumber"] = open.Number, ["link"] = Link + open.Id },
        };

    private static Error? NotCounting(InventoryCount count) => count.Status == InventoryCountStatus.Counting
        ? null
        : Error.Conflict("inventory_count_not_counting", $"O inventário {count.Number} está {InventoryCountRules.StatusLabel(count.Status).ToLowerInvariant()}: a contagem não muda.");

    private async Task<Error> CountedByOtherAsync(Guid by, CancellationToken ct)
    {
        var name = await users.DisplayNameAsync(by, ct) ?? "outra pessoa";
        return Error.Conflict("inventory_line_counted", $"Esta linha foi contada por {name} agora há pouco.") with
        {
            Data = new Dictionary<string, object?> { ["recovery"] = Reload, ["countedBy"] = name },
        };
    }

    private static Error Exists(Guid lineId) => Error.Conflict("inventory_line_exists", "Este item já está na lista: conte na linha dele.") with
    {
        Data = new Dictionary<string, object?> { ["lineId"] = lineId },
    };

    /// <summary>Quantidade contada na unidade de estoque (R-EST-3): ≥ 0, convertida pelo <see cref="IUnitConversion"/>.</summary>
    private async Task<Result<decimal>> QuantityAsync(Guid itemId, decimal quantity, Guid? unitId, CancellationToken ct)
    {
        if (quantity < 0)
        {
            return Invalid("quantity", "A quantidade contada não pode ser negativa.");
        }

        var stockUnit = await db.Items.AsNoTracking().Where(i => i.Id == itemId).Select(i => i.UnitId).FirstAsync(ct);
        if (unitId is null || unitId == stockUnit || quantity == 0)
        {
            return InventoryCountRules.Round(quantity);
        }

        var converted = await conversion.ConvertAsync(itemId, quantity, unitId.Value, stockUnit, ct);
        return converted.IsFailure ? Invalid("unitId", converted.Error!.Message) : InventoryCountRules.Round(converted.Value);
    }

    private async Task<decimal> LotBalanceAsync(Guid locationId, Guid itemId, Guid? lotId, CancellationToken ct) =>
        await db.StockBalances.AsNoTracking().Where(b => b.LocationId == locationId && b.ItemId == itemId && b.LotId == lotId).Select(b => (decimal?)b.QuantityOnHand).FirstOrDefaultAsync(ct) ?? 0m;

    /// <summary>Saldo de agora de cada (item, lote) no local.</summary>
    private async Task<Dictionary<(Guid ItemId, Guid? LotId), decimal>> LotBalancesAsync(Guid locationId, IReadOnlyCollection<(Guid ItemId, Guid? LotId)> keys, CancellationToken ct)
    {
        var itemIds = keys.Select(k => k.ItemId).Distinct().ToList();
        var rows = await db.StockBalances.AsNoTracking().Where(b => b.LocationId == locationId && itemIds.Contains(b.ItemId))
            .Select(b => new { b.ItemId, b.LotId, b.QuantityOnHand }).ToListAsync(ct);
        return rows.GroupBy(r => (r.ItemId, r.LotId)).ToDictionary(g => g.Key, g => g.Sum(r => r.QuantityOnHand));
    }

    private void Detach(object entity) => db.Entry(entity).State = EntityState.Detached;

    internal static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    internal static Error NotFound() => Error.NotFound("inventory_count_not_found", "Inventário não encontrado.");

    internal static Error LineNotFound() => Error.NotFound("inventory_count_line_not_found", "Linha não encontrada neste inventário.");
}
