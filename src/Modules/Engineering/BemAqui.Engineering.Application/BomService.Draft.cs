using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Engineering.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Engineering.Application;

public sealed partial class BomService
{
    /// <summary>
    /// "Criar BOM" e "Copiar BOM" (CA 2, 4, 5): nasce a revisão em rascunho, vazia ou com as linhas da BOM de outra OS
    /// (<c>copied_from_bom_id</c>), sem efeito em estoque. Um rascunho por vez; com BOM liberada, o caminho é "Revisar BOM" (CA 12). Duas
    /// criações juntas: a segunda recebe 409 pelo índice único parcial do rascunho.
    /// </summary>
    public async Task<Result<BomDetail>> CreateAsync(BomCreateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await orders.FindAsync(input.ProductionOrderId, cancellationToken);
        if (order is null)
        {
            return OrderNotFound();
        }

        if (BomRules.Closed(order) is { } closed)
        {
            return closed;
        }

        var existing = await db.Boms.AsNoTracking().Where(b => b.ProductionOrderId == order.Id).ToListAsync(cancellationToken);
        if (existing.FirstOrDefault(b => b.IsDraft) is { } draft)
        {
            return DraftExists(draft.Id);
        }

        if (existing.Any(b => b.Status == BomStatus.Released))
        {
            return Error.Conflict("bom_already_released", "A BOM desta OS já foi liberada. Para mudar, revise a BOM.");
        }

        List<BomLine> source = [];
        if (input.CopyFromBomId is { } sourceId)
        {
            var origin = await db.Boms.AsNoTracking().FirstOrDefaultAsync(b => b.Id == sourceId, cancellationToken);
            if (origin is null || origin.IsDraft)
            {
                return BomRules.Invalid("copyFromBomId", "Escolha uma BOM liberada de outra OS para copiar.");
            }

            source = await db.BomLines.AsNoTracking().Where(l => l.BomId == sourceId).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        }

        var bom = new Bom
        {
            ProductionOrderId = order.Id, Revision = existing.Count == 0 ? 0 : existing.Max(b => b.Revision) + 1, CopiedFromBomId = input.CopyFromBomId,
            CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.Boms.Add(bom);
        await CopyLinesAsync(bom, order, source, cancellationToken);
        return await SaveNewAsync(bom, order.Id, cancellationToken);
    }

    /// <summary>
    /// "Revisar BOM" (CA 12): nasce a revisão n + 1 em rascunho com as linhas da liberada; a liberada continua valendo até a nova ser
    /// liberada. Só da liberada vigente, na versão aberta, e sem outro rascunho.
    /// </summary>
    public async Task<Result<BomDetail>> ReviseAsync(Guid id, BomVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var released = await db.Boms.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (released is null)
        {
            return BomRules.NotFound();
        }

        if (released.Status != BomStatus.Released)
        {
            return Error.Conflict("bom_not_released", "Só a BOM liberada vigente é revisada.");
        }

        if (released.Version != input.Version)
        {
            return BomRules.Stale();
        }

        var order = await orders.FindAsync(released.ProductionOrderId, cancellationToken);
        if (order is null)
        {
            return OrderNotFound();
        }

        if (BomRules.Closed(order) is { } closed)
        {
            return closed;
        }

        var existing = await db.Boms.AsNoTracking().Where(b => b.ProductionOrderId == order.Id).ToListAsync(cancellationToken);
        if (existing.FirstOrDefault(b => b.IsDraft) is { } draft)
        {
            return DraftExists(draft.Id);
        }

        var bom = new Bom
        {
            ProductionOrderId = order.Id, Revision = existing.Max(b => b.Revision) + 1, CopiedFromBomId = released.Id, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.Boms.Add(bom);
        await CopyLinesAsync(bom, order, await db.BomLines.AsNoTracking().Where(l => l.BomId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken), cancellationToken);
        return await SaveNewAsync(bom, order.Id, cancellationToken);
    }

    /// <summary>
    /// "Salvar BOM" (CA 2, 3, 6): só o rascunho, na versão aberta, sem efeito em estoque. Linha com id é deste rascunho (outra BOM: 404,
    /// CA 19); as que saíram da lista são removidas; a necessidade é recalculada. A versão (xmin) sobe a cada gravação, mesmo quando só
    /// as linhas mudam.
    /// </summary>
    public async Task<Result<BomDetail>> SaveAsync(Guid id, BomUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadDraftAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (bom, order) = loaded.Value;
        var existing = await db.BomLines.Where(l => l.BomId == id).ToDictionaryAsync(l => l.Id, cancellationToken);
        var (lines, errors) = BomRules.Validate(input.Lines);
        var seen = new HashSet<Guid>();
        foreach (var line in lines)
        {
            if (line.Id is { } lineId && (!existing.ContainsKey(lineId) || !seen.Add(lineId)))
            {
                return Error.NotFound("bom_line_not_found", "Linha não encontrada nesta BOM.");
            }
        }

        var computed = await ComputeAsync(order, lines, existing.Values.Select(l => l.ItemId).ToHashSet(), errors, cancellationToken);
        if (BomRules.ToError(errors) is { } invalid)
        {
            return invalid;
        }

        db.Entry(bom).Property(b => b.Version).OriginalValue = input.Version;
        foreach (var (line, unitId, required) in computed)
        {
            var entity = line.Id is { } lineId ? existing[lineId] : new BomLine { BomId = bom.Id };
            entity.Sequence = line.Sequence;
            entity.ItemId = line.ItemId;
            entity.Quantity = line.Quantity;
            entity.UnitId = unitId;
            entity.ScrapPct = line.ScrapPct;
            entity.Notes = line.Notes;
            entity.RequiredQuantity = required;
            if (line.Id is null)
            {
                db.BomLines.Add(entity);
            }
        }

        db.BomLines.RemoveRange(existing.Values.Where(l => !seen.Contains(l.Id)));
        // Toda gravação passa pelo xmin da BOM, inclusive a que só muda linhas: a liberação com a versão antiga recebe 409 (CA 20).
        db.Entry(bom).State = EntityState.Modified;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, LineItemIndex))
        {
            return Error.Conflict("bom_conflict", "A BOM mudou ao mesmo tempo em outra tela. Recarregue.");
        }

        return await DetailAsync(bom, cancellationToken);
    }

    /// <summary>"Copiar BOM" para dentro do rascunho (CA 5): as linhas da origem substituem as do rascunho, com a necessidade desta OS.</summary>
    public async Task<Result<BomDetail>> CopyAsync(Guid id, BomCopyInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadDraftAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (bom, order) = loaded.Value;
        var origin = await db.Boms.AsNoTracking().FirstOrDefaultAsync(b => b.Id == input.SourceBomId, cancellationToken);
        if (origin is null || origin.IsDraft || origin.Id == bom.Id)
        {
            return BomRules.Invalid("sourceBomId", "Escolha uma BOM liberada de outra OS para copiar.");
        }

        var source = await db.BomLines.AsNoTracking().Where(l => l.BomId == origin.Id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // As linhas antigas saem antes das novas (o mesmo item não se repete no índice único da linha); a versão confere já no primeiro passo.
            db.Entry(bom).Property(b => b.Version).OriginalValue = input.Version;
            bom.CopiedFromBomId = origin.Id;
            db.BomLines.RemoveRange(await db.BomLines.Where(l => l.BomId == id).ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
            await CopyLinesAsync(bom, order, source, cancellationToken);
            db.Entry(bom).State = EntityState.Modified;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await DetailAsync(bom, cancellationToken);
    }

    /// <summary>
    /// "Descartar rascunho" (CA 7): a revisão em rascunho é apagada sem efeito; a liberada anterior, se houver, continua valendo. O
    /// histórico da OS guarda quem descartou (auditoria da exclusão).
    /// </summary>
    public async Task<Result> DiscardAsync(Guid id, BomVersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var bom = await db.Boms.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bom is null)
        {
            return BomRules.NotFound();
        }

        if (!bom.IsDraft)
        {
            return BomRules.NotDraft();
        }

        if (bom.Version != input.Version)
        {
            return BomRules.Stale();
        }

        db.Entry(bom).Property(b => b.Version).OriginalValue = input.Version;
        db.BomLines.RemoveRange(await db.BomLines.Where(l => l.BomId == id).ToListAsync(cancellationToken));
        db.Boms.Remove(bom);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Rascunho na versão aberta, de OS que ainda muda (CA 6, 11).</summary>
    private async Task<Result<(Bom Bom, ProductionOrderInfo Order)>> LoadDraftAsync(Guid id, uint version, CancellationToken ct)
    {
        var bom = await db.Boms.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (bom is null)
        {
            return BomRules.NotFound();
        }

        if (!bom.IsDraft)
        {
            return BomRules.NotDraft();
        }

        if (bom.Version != version)
        {
            return BomRules.Stale();
        }

        var order = await orders.FindAsync(bom.ProductionOrderId, ct);
        if (order is null)
        {
            return OrderNotFound();
        }

        if (BomRules.Closed(order) is { } closed)
        {
            return closed;
        }

        return (bom, order);
    }

    /// <summary>
    /// Regras com os contratos do Estoque (CA 2, 3): item existente, sem Serviço e sem o produto da OS (o item novo na BOM precisa estar
    /// ativo; o que já estava, mesmo inativo, continua, CA 5), a unidade (de estoque por padrão; outra só com conversão) e a necessidade.
    /// </summary>
    private async Task<List<(NormalizedBomLine Line, Guid UnitId, decimal Required)>> ComputeAsync(
        ProductionOrderInfo order, IReadOnlyList<NormalizedBomLine> lines, HashSet<Guid> alreadyInBom, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        var itemIds = lines.Select(l => l.ItemId).Where(i => i != Guid.Empty).Distinct().ToList();
        var found = (await items.FindAsync(itemIds, ct)).ToDictionary(i => i.Id);
        var options = await units.ForItemsAsync(itemIds, ct);
        var codes = await units.CodesAsync(lines.Where(l => l.UnitId != null).Select(l => l.UnitId!.Value).Distinct().ToList(), ct);
        var result = new List<(NormalizedBomLine, Guid, decimal)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var field = $"lines[{i}]";
            if (line.ItemId == Guid.Empty || errors.ContainsKey(field + ".itemId"))
            {
                continue;
            }

            var item = found.GetValueOrDefault(line.ItemId);
            if (BomRules.ItemProblem(item, order.ProductId, alreadyInBom.Contains(line.ItemId)) is { } problem)
            {
                errors.TryAdd(field + ".itemId", [problem]);
                continue;
            }

            var unit = line.UnitId ?? item!.UnitId;
            if (unit != item!.UnitId && !(options.GetValueOrDefault(item.Id) ?? []).Any(o => o.UnitId == unit))
            {
                errors.TryAdd(field + ".unitId", [MissingConversion(codes.GetValueOrDefault(unit, "?"), item.UnitCode)]);
                continue;
            }

            if (errors.Keys.Any(k => k.StartsWith(field + ".", StringComparison.Ordinal)))
            {
                continue;
            }

            var required = await BomRules.RequirementAsync(conversion, item.Id, line.Quantity, order.Quantity, line.ScrapPct, unit, item.UnitId, ct);
            if (required.IsFailure)
            {
                errors.TryAdd(field + ".unitId", [MissingConversion(codes.GetValueOrDefault(unit, "?"), item.UnitCode)]);
            }
            else if (required.Value > BomRules.MaxRequired)
            {
                errors.TryAdd(field + ".quantity", ["A necessidade da OS passa do limite. Confira a quantidade."]);
            }
            else
            {
                result.Add((line, unit, required.Value));
            }
        }

        return result;
    }

    /// <summary>Copia as linhas da origem com a necessidade desta OS; a unidade sem conversão fica com necessidade 0 até o salvar pedir a conversão.</summary>
    private async Task CopyLinesAsync(Bom bom, ProductionOrderInfo order, IReadOnlyList<BomLine> source, CancellationToken ct)
    {
        var found = (await items.FindAsync(source.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var sequence = 0;
        foreach (var line in source)
        {
            if (found.GetValueOrDefault(line.ItemId) is not { IsDeleted: false } item || item.Type == ItemType.Service || item.Id == order.ProductId)
            {
                continue;
            }

            var required = await BomRules.RequirementAsync(conversion, item.Id, line.Quantity, order.Quantity, line.ScrapPct, line.UnitId, item.UnitId, ct);
            db.BomLines.Add(new BomLine
            {
                BomId = bom.Id, Sequence = ++sequence, ItemId = item.Id, Quantity = line.Quantity, UnitId = line.UnitId, ScrapPct = line.ScrapPct, Notes = line.Notes,
                RequiredQuantity = required.IsSuccess ? required.Value : 0,
            });
        }
    }

    private async Task<Result<BomDetail>> SaveNewAsync(Bom bom, Guid orderId, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, DraftIndex) || DbErrors.IsUniqueViolation(ex, RevisionIndex))
        {
            db.Entry(bom).State = EntityState.Detached;
            foreach (var line in db.BomLines.Local.Where(l => l.BomId == bom.Id).ToList())
            {
                db.Entry(line).State = EntityState.Detached;
            }

            var draft = await db.Boms.AsNoTracking().FirstOrDefaultAsync(b => b.ProductionOrderId == orderId && b.Status == BomStatus.Draft, ct);
            return draft is null ? Error.Conflict("bom_conflict", "Outra pessoa mudou a BOM desta OS agora. Recarregue.") : DraftExists(draft.Id);
        }

        return await DetailAsync(bom, ct);
    }

    private static Error DraftExists(Guid draftId) =>
        Error.Conflict("bom_draft_exists", "Esta OS já tem um rascunho de BOM. Abra o rascunho.") with { Data = new Dictionary<string, object?> { ["bomId"] = draftId } };

    internal static string MissingConversion(string from, string to) => $"Cadastre a conversão de {from} para {to} no item.";
}
