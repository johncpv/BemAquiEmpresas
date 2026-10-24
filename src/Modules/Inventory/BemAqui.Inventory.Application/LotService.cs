using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Inventory.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Lotes do item (EST-04 CA 5 e 6): a aba "Lotes" com fornecedor, entrada, validade, saldo e locais, e a correção do lote digitado errado
/// (número e validade sem saída; só a validade com saída), com o motivo no próprio lote e a auditoria do diff. A correção trava a linha do
/// item, como o motor (ADR 0023 §3), para nenhuma saída entrar entre a conferência e a gravação.
/// </summary>
public sealed class LotService(IInventoryDb db, IParties parties, ICurrentUser currentUser, TimeProvider clock)
{
    public const int ReasonMin = 3;
    public const int ReasonMax = 500;
    public const int NumberMax = 40;
    public const string HasOutflowMessage = "Lote com saída só tem a validade corrigida.";

    /// <summary>Aba "Lotes" (CA 5): os lotes do item, os mais novos primeiro; 404 se o item não existe.</summary>
    public async Task<Result<IReadOnlyList<ItemLotRow>>> ListAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        if (!await db.Items.IgnoreQueryFilters().AsNoTracking().AnyAsync(i => i.Id == itemId, cancellationToken))
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        var lots = await db.Lots.AsNoTracking().Where(l => l.ItemId == itemId).OrderByDescending(l => l.CreatedAt).ThenBy(l => l.LotNumber).ToListAsync(cancellationToken);
        return await RowsAsync(lots, cancellationToken);
    }

    /// <summary>Um lote do item (CA 8, IDOR): lote de outro item responde 404.</summary>
    public async Task<Result<ItemLotRow>> GetAsync(Guid itemId, Guid lotId, CancellationToken cancellationToken = default)
    {
        var lot = await db.Lots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotId && l.ItemId == itemId, cancellationToken);
        return lot is null ? NotFound() : (await RowsAsync([lot], cancellationToken))[0];
    }

    /// <summary>
    /// Correção (CA 6, <c>inventory.edit</c>): motivo obrigatório; número novo só sem saída (422 "Lote com saída só tem a validade
    /// corrigida."); número repetido no item, 422; nada mudou, devolve o lote sem gravar (idempotente). Corrida pelo mesmo número: 409.
    /// </summary>
    public async Task<Result<ItemLotRow>> CorrectAsync(Guid itemId, Guid lotId, LotCorrectionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var number = input.LotNumber?.Trim() ?? string.Empty;
        var reason = input.Reason?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (number.Length == 0)
        {
            errors["lotNumber"] = ["Informe o número do lote."];
        }
        else if (number.Length > NumberMax)
        {
            errors["lotNumber"] = [$"O número do lote tem até {NumberMax} caracteres."];
        }

        if (reason.Length < ReasonMin || reason.Length > ReasonMax)
        {
            errors["reason"] = [$"Informe o motivo da correção, de {ReasonMin} a {ReasonMax} caracteres."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation(Error.Codes.Validation, errors.Values.First()[0], errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var locked = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM inventory.items WHERE id = {itemId} FOR UPDATE").ToListAsync(cancellationToken);
        var lot = locked.Count == 0 ? null : await db.Lots.FirstOrDefaultAsync(l => l.Id == lotId && l.ItemId == itemId, cancellationToken);
        if (lot is null)
        {
            return NotFound();
        }

        var renamed = !string.Equals(lot.LotNumber, number, StringComparison.Ordinal);
        if (!renamed && lot.Expiry == input.Expiry)
        {
            return (await RowsAsync([lot], cancellationToken))[0];
        }

        if (renamed)
        {
            if (await HasOutflowAsync(lotId, cancellationToken))
            {
                return Field("lotNumber", HasOutflowMessage, "lot_has_outflow");
            }

            if (await db.Lots.AnyAsync(l => l.ItemId == itemId && l.Id != lotId && l.LotNumber == number, cancellationToken))
            {
                return Field("lotNumber", $"Já existe o lote {number} neste item.");
            }

            lot.LotNumber = number;
        }

        lot.Expiry = input.Expiry;
        lot.CorrectionReason = reason;
        lot.CorrectedAt = clock.GetUtcNow();
        lot.CorrectedBy = currentUser.UserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, "ux_lots_item_number"))
        {
            return Error.Conflict("lot_conflict", "Outro usuário gravou o mesmo número de lote neste item. Recarregue e tente de novo.");
        }

        return (await RowsAsync([lot], cancellationToken))[0];
    }

    /// <summary>Saída do lote: qualquer movimento negativo com o lote (baixa, expedição, ajuste, transferência, saída avulsa).</summary>
    private Task<bool> HasOutflowAsync(Guid lotId, CancellationToken ct) => db.StockMovements.AsNoTracking().AnyAsync(m => m.LotId == lotId && m.Quantity < 0, ct);

    private async Task<List<ItemLotRow>> RowsAsync(IReadOnlyList<Lot> lots, CancellationToken ct)
    {
        var ids = lots.Select(l => l.Id).ToList();
        var balances = await (from b in db.StockBalances.AsNoTracking()
                              where b.LotId != null && ids.Contains(b.LotId.Value) && b.QuantityOnHand != 0
                              join l in db.Locations.IgnoreQueryFilters().AsNoTracking() on b.LocationId equals l.Id
                              orderby l.Name
                              select new { LotId = b.LotId!.Value, Balance = new TraceLotBalance(b.LocationId, l.Name, b.QuantityOnHand) }).ToListAsync(ct);
        var outflows = await db.StockMovements.AsNoTracking().Where(m => m.LotId != null && ids.Contains(m.LotId.Value) && m.Quantity < 0).Select(m => m.LotId!.Value).Distinct().ToListAsync(ct);
        var suppliers = (await parties.FindManyAsync(lots.Where(l => l.SupplierPartyId is not null).Select(l => l.SupplierPartyId!.Value).Distinct().ToList(), ct))
            .ToDictionary(p => p.Id, p => p.LegalName);
        var today = BusinessTime.Today(clock);
        return lots.Select(l =>
        {
            var mine = balances.Where(b => b.LotId == l.Id).Select(b => b.Balance).ToList();
            var (state, days) = TraceabilityRules.Expiry(l.Expiry, today);
            return new ItemLotRow(
                l.Id, l.ItemId, l.LotNumber, l.SupplierPartyId, l.SupplierPartyId is { } s ? suppliers.GetValueOrDefault(s) : null, l.ReceivedAt, l.Expiry, state, days, mine.Sum(b => b.OnHand), mine,
                outflows.Contains(l.Id), l.CorrectionReason, l.CorrectedAt);
        }).ToList();
    }

    private static Error NotFound() => Error.NotFound("lot_not_found", "Lote não encontrado neste item.");

    private static Error Field(string field, string message, string? code = null) =>
        Error.Validation(code ?? Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
