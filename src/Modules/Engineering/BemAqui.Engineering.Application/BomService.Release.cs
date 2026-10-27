using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Engineering.Contracts;
using BemAqui.Engineering.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Engineering.Application;

public sealed partial class BomService
{
    /// <summary>
    /// Prévia do diálogo de impacto (CA 8, 9, 13, 14; padrão 4): por item, a necessidade (antes → depois na revisão), o disponível, quanto
    /// vai reservar e a falta; na revisão, a reservar a mais e a liberar, e o aviso quando a nova necessidade fica abaixo do já baixado.
    /// Sem o estoque ligado (antes da EST-01a), disponível, reserva e falta vêm nulos.
    /// </summary>
    public async Task<Result<BomReleasePreview>> ReleasePreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bom = await db.Boms.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bom is null)
        {
            return BomRules.NotFound();
        }

        if (!bom.IsDraft)
        {
            return BomRules.NotDraft();
        }

        var previous = await db.Boms.AsNoTracking().FirstOrDefaultAsync(b => b.ProductionOrderId == bom.ProductionOrderId && b.Status == BomStatus.Released, cancellationToken);
        var lines = await db.BomLines.AsNoTracking().Where(l => l.BomId == id).ToListAsync(cancellationToken);
        var before = previous is null ? [] : await db.BomLines.AsNoTracking().Where(l => l.BomId == previous.Id).ToListAsync(cancellationToken);
        var diff = BomRules.Diff(before.Select(l => (l.ItemId, l.RequiredQuantity)), lines.Select(l => (l.ItemId, l.RequiredQuantity)));
        var itemIds = diff.Select(d => d.ItemId).ToList();
        var found = (await items.FindAsync(itemIds, cancellationToken)).ToDictionary(i => i.Id);
        var positions = await stock.PositionsAsync(itemIds, cancellationToken);
        var issued = previous is null ? null : await stock.IssuedForOrderAsync(bom.ProductionOrderId, cancellationToken);
        var order = lines.ToDictionary(l => l.ItemId, l => l.Sequence);
        var preview = diff.OrderBy(d => order.GetValueOrDefault(d.ItemId, int.MaxValue)).Select(d =>
        {
            var item = found.GetValueOrDefault(d.ItemId);
            var available = stock.Connected ? positions.GetValueOrDefault(d.ItemId)?.Available ?? 0 : (decimal?)null;
            // Primeira liberação: reserva a necessidade toda; revisão: só o que aumentou (o resto já está reservado pela anterior).
            var wanted = previous is null ? d.After : d.MoreToReserve;
            decimal? done = issued is null ? null : issued.GetValueOrDefault(d.ItemId);
            return new BomReleasePreviewLine(d.ItemId, item?.Code ?? "—", item?.Name ?? "Item não encontrado", item?.UnitCode ?? string.Empty, item is { IsActive: true, IsDeleted: false },
                d.Change, d.Before, d.After, d.MoreToReserve, d.ToRelease,
                available, stock.ReservationsConnected && available is { } free ? Math.Min(wanted, Math.Max(0, free)) : null, available is { } a ? BomRules.Shortage(wanted, a) : null,
                done, done is { } baixado ? BomRules.IssuedAboveWarning(baixado, d.After, item?.Name ?? "item", item?.UnitCode ?? string.Empty) : null);
        }).ToList();
        return new BomReleasePreview(bom.Id, bom.Revision, previous is not null, previous?.Revision, lines.Count == 0, stock.Connected, stock.ReservationsConnected,
            preview.Count(p => p.Shortage > 0), preview.Count(p => !p.ItemActive && p.Change != BomChange.Removed), preview);
    }

    /// <summary>
    /// "Liberar BOM" e "Liberar revisão" (CA 8, 9, 13, 20): na mesma transação a revisão fica <c>Released</c> (com quem e quando), a anterior
    /// fica <c>Superseded</c> e o <see cref="BomReleased"/> ou o <see cref="BomRevised"/> vai para o outbox, com a necessidade recalculada na
    /// unidade de estoque. A anterior sai de <c>Released</c> num primeiro comando (o índice único parcial é conferido a cada comando). Duas
    /// liberações juntas: uma grava, a outra recebe 409 (<c>xmin</c> e o índice), e o evento sai uma vez.
    /// </summary>
    public async Task<Result<BomDetail>> ReleaseAsync(Guid id, BomVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadDraftAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (bom, order) = loaded.Value;
        var lines = await db.BomLines.Where(l => l.BomId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var computed = await ComputeAsync(order, lines.Select(l => new NormalizedBomLine(l.Id, l.Sequence, l.ItemId, l.Quantity, l.UnitId, l.ScrapPct, l.Notes)).ToList(),
            lines.Select(l => l.ItemId).ToHashSet(), errors, cancellationToken);
        if (BomRules.ToError(errors) is { } invalid)
        {
            return invalid;
        }

        foreach (var (line, _, required) in computed)
        {
            lines.Single(l => l.Id == line.Id).RequiredQuantity = required;
        }

        // A própria revisão fica de fora: se outra liberação dela acabou de gravar, o banco já a vê liberada, mas a instância rastreada
        // ainda é o rascunho — a versão (xmin) é que decide, abaixo.
        var previous = await db.Boms.FirstOrDefaultAsync(b => b.ProductionOrderId == bom.ProductionOrderId && b.Status == BomStatus.Released && b.Id != bom.Id, cancellationToken);
        var before = previous is null ? [] : await db.BomLines.AsNoTracking().Where(l => l.BomId == previous.Id).ToListAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.Entry(bom).Property(b => b.Version).OriginalValue = input.Version;
            if (previous is not null)
            {
                previous.Supersede();
                await db.SaveChangesAsync(cancellationToken);
            }

            bom.Release(clock.GetUtcNow(), currentUser.UserId);
            if (previous is null)
            {
                events.Publish(new BomReleased(bom.Id, order.Id, order.ProjectId, bom.Revision, lines.Select(l => new BomReleasedLine(l.Id, l.ItemId, l.RequiredQuantity)).ToList()));
            }
            else
            {
                events.Publish(new BomRevised(bom.Id, previous.Id, order.Id, order.ProjectId, bom.Revision, RevisedLines(lines, before)));
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || DbErrors.IsUniqueViolation(ex, ReleasedIndex))
        {
            return Error.Conflict("bom_conflict", "Outra revisão da BOM desta OS foi liberada agora. Recarregue.");
        }

        return await DetailAsync(bom, cancellationToken);
    }

    /// <summary>
    /// Linhas do <see cref="BomRevised"/>: cada linha nova com a necessidade anterior do item (0 se é novo) e, para o item que saiu, a linha
    /// da revisão anterior com necessidade 0.
    /// </summary>
    public static IReadOnlyList<BomRevisedLine> RevisedLines(IReadOnlyList<BomLine> lines, IReadOnlyList<BomLine> previous)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(previous);
        var old = previous.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.RequiredQuantity));
        var now = lines.Select(l => l.ItemId).ToHashSet();
        return lines.Select(l => new BomRevisedLine(l.Id, l.ItemId, l.RequiredQuantity, old.GetValueOrDefault(l.ItemId)))
            .Concat(previous.Where(l => !now.Contains(l.ItemId)).Select(l => new BomRevisedLine(l.Id, l.ItemId, 0, l.RequiredQuantity)))
            .ToList();
    }
}
