using BemAqui.BuildingBlocks.Time;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class ManualPostingService
{
    public const string HasMovementCode = "item_has_movement";

    /// <summary>Situação da implantação do estoque (CA 5): itens e locais com saldo inicial e o valor total.</summary>
    public async Task<StockOpeningStatus> OpeningStatusAsync(CancellationToken cancellationToken = default)
    {
        var parameters = await db.InventoryParameters.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new InventoryParameters();
        var openings = db.StockMovements.AsNoTracking().Where(m => m.Type == StockMovementType.OpeningBalance);
        var live = openings.Where(m => m.ReversesMovementId == null && !openings.Any(r => r.ReversesMovementId == m.Id));
        var items = await live.Select(m => m.ItemId).Distinct().CountAsync(cancellationToken);
        var locations = await live.Select(m => m.LocationId).Distinct().CountAsync(cancellationToken);
        var lines = await live.CountAsync(cancellationToken);
        var total = await live.SumAsync(m => (decimal?)m.TotalCost, cancellationToken) ?? 0m;
        var by = parameters.OpeningCompletedBy is { } user ? await users.DisplayNameAsync(user, cancellationToken) : null;
        return new StockOpeningStatus(parameters.OpeningCompletedAt is not null, parameters.OpeningCompletedAt, by, items, locations, lines, total,
            parameters.ManualApprovalAmount, parameters.ManualCostDeviationPct, parameters.Version);
    }

    /// <summary>"Lançar saldo inicial" (CA 1 a 3): só na janela de implantação e sem nenhum movimento no item e local.</summary>
    public async Task<Result<ManualPostingDetail>> CreateOpeningAsync(OpeningBalanceInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return await OpeningAsync(input.CutoffDate, [new OpeningBalanceLineInput(input.ItemId, input.LocationId, input.Quantity, input.UnitId, input.UnitCost, input.LotNumber)], batch: false, cancellationToken);
    }

    /// <summary>"Saldo inicial em lote" (CA 4): uma transação, um número; linha inválida devolve 422 com o caminho e nada grava.</summary>
    public async Task<Result<ManualPostingDetail>> CreateOpeningBatchAsync(OpeningBatchInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Lines is not { Count: > 0 })
        {
            return Invalid("lines", "Informe ao menos uma linha.");
        }

        return input.Lines.Count > 500 ? Invalid("lines", "Lance até 500 linhas por vez.") : await OpeningAsync(input.CutoffDate, input.Lines, batch: true, cancellationToken);
    }

    /// <summary>"Concluir implantação do estoque" (CA 5): com <c>inventory.approve</c>; não se desfaz (segunda chamada: 409).</summary>
    public async Task<Result<StockOpeningStatus>> CompleteOpeningAsync(CompleteStockOpeningInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("inventory", "approve", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem conclui a implantação do estoque é quem aprova no Estoque.");
        }

        var parameters = await ParametersAsync(cancellationToken);
        if (parameters.OpeningCompletedAt is { } done)
        {
            return Completed(done);
        }

        if (db.Entry(parameters).State != EntityState.Added)
        {
            db.Entry(parameters).Property(p => p.Version).OriginalValue = input.Version;
        }

        parameters.OpeningCompletedAt = clock.GetUtcNow();
        parameters.OpeningCompletedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await OpeningStatusAsync(cancellationToken);
    }

    private async Task<Result<ManualPostingDetail>> OpeningAsync(DateOnly cutoff, IReadOnlyList<OpeningBalanceLineInput> lines, bool batch, CancellationToken ct)
    {
        var parameters = await ParametersAsync(ct);
        if (parameters.OpeningCompletedAt is { } done)
        {
            return Completed(done);
        }

        if (cutoff > BusinessTime.Today(clock))
        {
            return Invalid("cutoffDate", "A data de corte não pode ser futura.");
        }

        var built = new List<ManualPostingLine>();
        var seen = new HashSet<(Guid, Guid)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var prefix = batch ? $"lines[{i}]." : string.Empty;
            var line = lines[i];
            var item = await ActiveItemAsync(line.ItemId, prefix + "itemId", ct);
            if (item.IsFailure)
            {
                return item.Error!;
            }

            if (await ActiveLocationAsync(line.LocationId, prefix + "locationId", ct) is { IsFailure: true } location)
            {
                return location.Error!;
            }

            var quantity = await StockQuantityAsync(item.Value, line.Quantity, line.UnitId, prefix + "quantity", ct);
            if (quantity.IsFailure)
            {
                return quantity.Error!;
            }

            if (line.UnitCost <= 0)
            {
                return Invalid(prefix + "unitCost", "Informe o custo unitário, maior que zero.");
            }

            var lot = await LotForRequestAsync(item.Value, entry: true, null, line.LotNumber, ct);
            if (lot.IsFailure)
            {
                return Prefixed(lot.Error!, prefix);
            }

            if (!seen.Add((line.ItemId, line.LocationId)) || await HasMovementAsync(line.ItemId, line.LocationId, null, ct))
            {
                return batch
                    ? Invalid(prefix + "itemId", "Este item já tem movimentos neste local (ou aparece duas vezes no lote). Use um ajuste de estoque.")
                    : HasMovement();
            }

            built.Add(new ManualPostingLine
            {
                Sequence = i + 1, ItemId = item.Value.Id, LocationId = line.LocationId, LotId = lot.Value.LotId, LotNumber = lot.Value.LotNumber, Quantity = quantity.Value,
                EnteredQuantity = line.Quantity, EnteredUnitId = line.UnitId ?? item.Value.UnitId, UnitCost = AverageCostCalculator.RoundCost(line.UnitCost),
            });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var posting = new ManualPosting
        {
            Number = await numbers.NextAsync(StockLedger.DocType, ct),
            Kind = ManualPostingKind.OpeningBalance,
            Status = ManualPostingStatus.Posted,
            EstimatedValue = built.Sum(l => AverageCostCalculator.LineTotal(l.Quantity, l.UnitCost!.Value)),
            Reason = "Saldo inicial",
            CutoffDate = cutoff,
            RequestedBy = currentUser.UserId,
            RequestedAt = clock.GetUtcNow(),
        };
        db.ManualPostings.Add(posting);
        var lineRows = built.Select(l => new ManualPostingLine
        {
            PostingId = posting.Id, Sequence = l.Sequence, ItemId = l.ItemId, LocationId = l.LocationId, LotId = l.LotId, LotNumber = l.LotNumber, Quantity = l.Quantity,
            EnteredQuantity = l.EnteredQuantity, EnteredUnitId = l.EnteredUnitId, UnitCost = l.UnitCost,
        }).ToList();
        db.ManualPostingLines.AddRange(lineRows);
        var posted = await PostLinesAsync(posting, lineRows, ct);
        if (posted.IsFailure)
        {
            await transaction.RollbackAsync(ct);
            return posted.Error!;
        }

        // Com o item travado pelo motor, confere de novo: outro saldo inicial em paralelo no mesmo item e local perde (CA 2).
        foreach (var line in lineRows)
        {
            if (await HasMovementAsync(line.ItemId, line.LocationId, posting.Id, ct))
            {
                await transaction.RollbackAsync(ct);
                return batch ? Invalid($"lines[{line.Sequence - 1}].itemId", "Este item já tem movimentos neste local. Use um ajuste de estoque.") : HasMovement();
            }
        }

        await transaction.CommitAsync(ct);
        return await DetailAsync(posting.Id, ct);
    }

    private Task<bool> HasMovementAsync(Guid itemId, Guid locationId, Guid? exceptSource, CancellationToken ct) =>
        db.StockMovements.AnyAsync(m => m.ItemId == itemId && m.LocationId == locationId && (exceptSource == null || m.SourceId != exceptSource), ct);

    /// <summary>CA 2: 409 com o caminho para corrigir ("Lançar entrada" até a EST-01b trazer o ajuste).</summary>
    private static Error HasMovement() =>
        Error.Conflict(HasMovementCode, "Este item já tem movimentos neste local. Use um ajuste de estoque.") with
        {
            Data = new Dictionary<string, object?> { ["recovery"] = "Lançar entrada", ["recoveryAction"] = "receipt" },
        };

    /// <summary>CA 3: a janela do saldo inicial fechou.</summary>
    private static Error Completed(DateTimeOffset at) =>
        Error.Conflict("stock_opening_completed", $"A implantação do estoque foi concluída em {BusinessTime.ToBusiness(at):dd/MM/yyyy}. Use um ajuste de estoque.");

    private static Error Prefixed(Error error, string prefix) =>
        prefix.Length == 0 || error.Fields is null ? error : error with { Fields = error.Fields.ToDictionary(f => prefix + f.Key, f => f.Value) };
}
