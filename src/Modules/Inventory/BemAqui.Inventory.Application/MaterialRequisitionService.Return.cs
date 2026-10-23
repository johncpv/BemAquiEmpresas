using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class MaterialRequisitionService
{
    public const string ReverseDeniedMessage = "Este estorno precisa de outra pessoa com permissão de aprovar no Estoque.";

    /// <summary>Linha baixada com o que já voltou e o custo da baixa original (CA 12).</summary>
    private sealed record IssuedLine(MaterialRequisitionLine Line, MaterialRequisition Requisition, decimal Returnable, decimal UnitCost);

    /// <summary>
    /// "Devolver ao estoque" (CA 12 e 13): nasce a devolução <c>Completed</c> com um <c>Return</c> síncrono por linha, pelo custo da baixa
    /// original (ponto 58), no local escolhido; o baixado da OS desce, o motor completa a falta com o que voltou e a reconciliação leva a
    /// reserva ao alvo da BOM (senão o saldo fica livre). O <see cref="StockReturned"/> tira o valor do realizado do projeto.
    /// </summary>
    public async Task<Result<RequisitionDetail>> ReturnAsync(Guid productionOrderId, ReturnInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await orders.FindAsync(productionOrderId, cancellationToken) is not { } order)
        {
            return OrderNotFound();
        }

        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == input.LocationId, cancellationToken);
        if (location is null || !location.IsActive)
        {
            return Invalid("locationId", location is null ? "Local não encontrado." : "Local inativo não recebe material.");
        }

        var picked = await PickIssuedAsync(productionOrderId, input.Lines, cancellationToken);
        if (picked.IsFailure)
        {
            return picked.Error!;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await reservations.LockOrderAsync(productionOrderId, cancellationToken);
        var requisition = NewDocument(MaterialRequisitionKind.Return, order, await numbers.NextAsync(RequisitionRules.DocType, cancellationToken));
        var lines = picked.Value.Select((p, i) => new MaterialRequisitionLine
        {
            RequisitionId = requisition.Id, Sequence = i + 1, ItemId = p.Issued.Line.ItemId, LotId = p.Issued.Line.LotId, LocationId = location.Id, Quantity = p.Quantity,
            ReturnsLineId = p.Issued.Line.Id,
        }).ToList();
        db.MaterialRequisitionLines.AddRange(lines);
        var reservationOrder = new ReservationOrder(productionOrderId, requisition.ProjectId);
        var origin = ReservationOrigins.Return(requisition.Number);
        foreach (var group in lines.GroupBy(l => l.ItemId).OrderBy(g => g.Key))
        {
            await allocator.LockItemAsync(group.Key, cancellationToken);
            await allocator.UnissueAsync(reservationOrder, group.Key, location.Id, group.Sum(l => l.Quantity), origin, cancellationToken);
        }

        var postings = lines.Select((l, i) => new StockPosting(
            StockMovementType.Return, l.ItemId, l.LocationId, l.LotId, l.Quantity, picked.Value[i].Issued.UnitCost, StockSources.Requisition, l.Id, 1, null, requisition.ProjectId,
            productionOrderId, Reason: "Devolução da " + picked.Value[i].Issued.Requisition.Number, SourceNumber: requisition.Number)).ToList();
        var posted = await ledger.PostManyAsync(postings, cancellationToken);
        if (posted.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return posted.Error!;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].MovementId = posted.Value[i].MovementId;
        }

        await db.SaveChangesAsync(cancellationToken);
        await reservations.ReconcileOrderAsync(productionOrderId, origin, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(requisition.Id, cancellationToken);
    }

    /// <summary>
    /// "Manter no projeto como perda" (CA 13): só na OS cancelada; o custo continua no projeto (sem movimento) e a OS registra a perda com
    /// o motivo. A quantidade dada como perda não volta mais ao estoque por esta OS.
    /// </summary>
    public async Task<Result<RequisitionDetail>> LossAsync(Guid productionOrderId, LossInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await orders.FindAsync(productionOrderId, cancellationToken) is not { } order)
        {
            return OrderNotFound();
        }

        if (order.Status != ProductionOrderState.Cancelled)
        {
            return Error.Conflict("order_not_cancelled", "Só a OS cancelada mantém o material no projeto como perda. Devolva ao estoque o que sobrou.");
        }

        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > RequisitionRules.MaxReason)
        {
            return Invalid("reason", $"Escreva o motivo da perda, com até {RequisitionRules.MaxReason} caracteres.");
        }

        var picked = await PickIssuedAsync(productionOrderId, input.Lines, cancellationToken);
        if (picked.IsFailure)
        {
            return picked.Error!;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await reservations.LockOrderAsync(productionOrderId, cancellationToken);
        var requisition = NewDocument(MaterialRequisitionKind.Loss, order, await numbers.NextAsync(RequisitionRules.DocType, cancellationToken));
        requisition.RejectReason = reason;
        db.MaterialRequisitionLines.AddRange(picked.Value.Select((p, i) => new MaterialRequisitionLine
        {
            RequisitionId = requisition.Id, Sequence = i + 1, ItemId = p.Issued.Line.ItemId, LotId = p.Issued.Line.LotId, LocationId = p.Issued.Line.LocationId, Quantity = p.Quantity,
            ReturnsLineId = p.Issued.Line.Id,
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(requisition.Id, cancellationToken);
    }

    /// <summary>
    /// "Estornar requisição" (CA 14): os movimentos inversos pelo motor, pelo custo da baixa (ou da devolução); a reserva volta pela
    /// reconciliação e o evento do estorno (valor novo 0) tira o custo do projeto. O autor estorna com <c>inventory.edit</c> até o limite
    /// e em até 24 h; fora disso, outra pessoa com <c>inventory.approve</c> (exceção do Proprietário único). A devolução estornada tira
    /// de novo o material do estoque: se ele já foi usado em outro lugar, 409 <c>insufficient_stock</c>.
    /// </summary>
    public async Task<Result<RequisitionDetail>> ReverseAsync(Guid id, RequisitionReverseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > RequisitionRules.MaxReason)
        {
            return Invalid("reason", $"Informe o motivo do estorno, com até {RequisitionRules.MaxReason} caracteres.");
        }

        var found = await InStatusAsync(id, MaterialRequisitionStatus.Completed, input.Version, "Só a requisição baixada é estornada.", cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var requisition = found.Value;
        var lines = await db.MaterialRequisitionLines.Where(l => l.RequisitionId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var rule = await ReverseRuleAsync(requisition, lines, cancellationToken);
        if (rule.Error is { } blocked)
        {
            return blocked;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await reservations.LockOrderAsync(requisition.ProductionOrderId, cancellationToken);
        db.Entry(requisition).Property(r => r.Version).OriginalValue = input.Version;
        requisition.Status = MaterialRequisitionStatus.Reversed;
        requisition.ReversedBy = currentUser.UserId;
        requisition.ReversedAt = clock.GetUtcNow();
        requisition.ReversalReason = reason;
        requisition.ReversalSelfApproved = rule.SoleOwner;
        // Grava o estorno antes do motor: dois estornos simultâneos param no xmin (CA 16).
        await db.SaveChangesAsync(cancellationToken);
        var order = new ReservationOrder(requisition.ProductionOrderId, requisition.ProjectId);
        var origin = ReservationOrigins.Reversal(requisition.Number);
        foreach (var group in lines.GroupBy(l => l.ItemId).OrderBy(g => g.Key))
        {
            await allocator.LockItemAsync(group.Key, cancellationToken);
            if (requisition.Kind == MaterialRequisitionKind.Issue)
            {
                foreach (var byLocation in group.GroupBy(l => l.LocationId))
                {
                    await allocator.UnissueAsync(order, group.Key, byLocation.Key, byLocation.Sum(l => l.Quantity), origin, cancellationToken);
                }

                continue;
            }

            // Estorno da devolução: o material volta a ser da OS; sai primeiro do que a devolução reservou de novo para ela.
            foreach (var byLocation in group.GroupBy(l => l.LocationId))
            {
                var quantity = byLocation.Sum(l => l.Quantity);
                var row = await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.ProductionOrderId == requisition.ProductionOrderId && r.ItemId == group.Key && r.LocationId == byLocation.Key, cancellationToken);
                var fromReserved = Math.Min(row?.Quantity ?? 0m, quantity);
                var positions = await allocator.IssueAsync(order, group.Key, [new ReservationIssue(byLocation.Key, fromReserved, quantity)], origin, cancellationToken);
                var available = positions.FirstOrDefault(p => p.LocationId == byLocation.Key)?.Available ?? 0m;
                if (quantity - fromReserved > available)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return await InsufficientAsync(group.Key, byLocation.Key, Math.Max(0m, available), cancellationToken);
                }
            }
        }

        foreach (var line in lines.Where(l => l.MovementId is not null))
        {
            var reversed = await ledger.ReverseAsync(new StockReversal(line.MovementId!.Value, StockSources.Requisition, line.Id, reason), cancellationToken);
            if (reversed.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return reversed.Error!;
            }
        }

        if (rule.SoleOwner)
        {
            await NotifySelfApprovedAsync($"estornou a própria requisição {requisition.Number}", Link + requisition.Id, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await reservations.ReconcileOrderAsync(requisition.ProductionOrderId, origin, cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Quem pode estornar agora (CA 14), para o comando e para o detail: a regra da EST-01a CA 20.</summary>
    private async Task<(Error? Error, bool SoleOwner)> ReverseRuleAsync(MaterialRequisition requisition, IReadOnlyList<MaterialRequisitionLine> lines, CancellationToken ct)
    {
        if (requisition.Kind == MaterialRequisitionKind.Loss)
        {
            return (Error.Conflict("loss_not_reversible", "A perda registrada não se estorna."), false);
        }

        if (requisition.Status != MaterialRequisitionStatus.Completed)
        {
            return (Error.Conflict("requisition_decided", "Só a requisição baixada é estornada."), false);
        }

        if (requisition.Kind == MaterialRequisitionKind.Issue)
        {
            var ids = lines.Select(l => l.Id).ToList();
            var followed = await (from l in db.MaterialRequisitionLines
                                  join r in db.MaterialRequisitions on l.RequisitionId equals r.Id
                                  where l.ReturnsLineId != null && ids.Contains(l.ReturnsLineId.Value) && r.Status == MaterialRequisitionStatus.Completed
                                  select r.Number).FirstOrDefaultAsync(ct);
            if (followed is not null)
            {
                return (Error.Conflict("requisition_has_returns", $"Esta requisição tem devolução ou perda ({followed}): estorne a devolução antes."), false);
            }
        }

        var movementIds = lines.Where(l => l.MovementId is not null).Select(l => l.MovementId!.Value).ToList();
        var value = await db.StockMovements.AsNoTracking().Where(m => movementIds.Contains(m.Id)).SumAsync(m => Math.Abs(m.TotalCost), ct);
        var parameters = await db.InventoryParameters.AsNoTracking().FirstOrDefaultAsync(ct) ?? new InventoryParameters();
        var me = currentUser.UserId;
        var author = requisition.CompletedBy ?? requisition.RequestedBy;
        var mine = me is not null && me == author;
        var canEdit = await permissions.HasPermissionAsync("inventory", "edit", ct);
        var canApprove = await permissions.HasPermissionAsync("inventory", "approve", ct);
        if (mine && canEdit && RequisitionRules.AuthorMayReverse(value, requisition.CompletedAt ?? requisition.RequestedAt, clock.GetUtcNow(), parameters.ManualApprovalAmount))
        {
            return (null, false);
        }

        if (canApprove && !mine)
        {
            return (null, false);
        }

        return canApprove && me is { } actor && await SoleOwnerRule.NoOtherEligibleAsync(users, "inventory.approve", actor, [actor], ct)
            ? (null, true)
            : (Error.Forbidden("reverse_needs_approver", ReverseDeniedMessage), false);
    }

    private MaterialRequisition NewDocument(MaterialRequisitionKind kind, ProductionOrderInfo order, string number)
    {
        var now = clock.GetUtcNow();
        var requisition = new MaterialRequisition
        {
            Number = number, Kind = kind, Status = MaterialRequisitionStatus.Completed, ProductionOrderId = order.Id, ProjectId = order.ProjectId, RequestedBy = currentUser.UserId,
            RequestedAt = now, CompletedBy = currentUser.UserId, CompletedAt = now,
        };
        db.MaterialRequisitions.Add(requisition);
        return requisition;
    }

    /// <summary>
    /// As linhas baixadas escolhidas na devolução ou na perda (CA 12, 13 e 15): linha de outra OS responde como inexistente (IDOR); a
    /// quantidade vai até o baixado e ainda não devolvido nem dado como perda.
    /// </summary>
    private async Task<Result<List<(IssuedLine Issued, decimal Quantity)>>> PickIssuedAsync(Guid orderId, IReadOnlyList<ReturnLineInput>? input, CancellationToken ct)
    {
        if (input is not { Count: > 0 })
        {
            return Invalid("lines", "Escolha ao menos uma linha com quantidade.");
        }

        if (input.Count > RequisitionRules.MaxLines)
        {
            return Invalid("lines", $"Use até {RequisitionRules.MaxLines} linhas por devolução.");
        }

        var issued = (await IssuedLinesAsync(orderId, ct)).ToDictionary(l => l.Line.Id);
        var picked = new List<(IssuedLine, decimal)>();
        for (var i = 0; i < input.Count; i++)
        {
            if (!issued.TryGetValue(input[i].IssueLineId, out var line))
            {
                return Error.NotFound("requisition_line_not_found", "Linha baixada não encontrada nesta OS.");
            }

            var quantity = RequisitionRules.Round(input[i].Quantity);
            var used = picked.Where(p => p.Item1.Line.Id == line.Line.Id).Sum(p => p.Item2);
            if (quantity <= 0 || quantity + used > line.Returnable)
            {
                return Invalid($"lines[{i}].quantity", $"Informe de 0 a {line.Returnable.ToString("N4", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}: o baixado e ainda não devolvido.");
            }

            picked.Add((line, quantity));
        }

        return picked;
    }

    /// <summary>Linhas das requisições baixadas da OS (não estornadas), com o que ainda pode voltar e o custo unitário da baixa.</summary>
    private async Task<List<IssuedLine>> IssuedLinesAsync(Guid orderId, CancellationToken ct)
    {
        var rows = await (from l in db.MaterialRequisitionLines.AsNoTracking()
                          join r in db.MaterialRequisitions.AsNoTracking() on l.RequisitionId equals r.Id
                          where r.ProductionOrderId == orderId && r.Kind == MaterialRequisitionKind.Issue && r.Status == MaterialRequisitionStatus.Completed && l.MovementId != null
                          orderby r.RequestedAt, l.Sequence
                          select new { Line = l, Requisition = r }).ToListAsync(ct);
        var ids = rows.Select(r => r.Line.Id).ToList();
        var back = await (from l in db.MaterialRequisitionLines.AsNoTracking()
                          join r in db.MaterialRequisitions.AsNoTracking() on l.RequisitionId equals r.Id
                          where l.ReturnsLineId != null && ids.Contains(l.ReturnsLineId.Value) && r.Status == MaterialRequisitionStatus.Completed
                          select new { Of = l.ReturnsLineId!.Value, l.Quantity }).ToListAsync(ct);
        var returned = back.GroupBy(b => b.Of).ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));
        var movementIds = rows.Select(r => r.Line.MovementId!.Value).ToList();
        var costs = await db.StockMovements.AsNoTracking().Where(m => movementIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.UnitCost, ct);
        return rows.Select(r => new IssuedLine(r.Line, r.Requisition, RequisitionRules.Returnable(r.Line.Quantity, returned.GetValueOrDefault(r.Line.Id), 0m), costs.GetValueOrDefault(r.Line.MovementId!.Value)))
            .ToList();
    }
}
