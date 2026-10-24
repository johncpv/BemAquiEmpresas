using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Solicitações de compra na tela (CA 3, 4, 12 a 14): enviar a manual, ver, editar a linha aberta e cancelar. Sem aprovação e sem efeito
/// de estoque nem financeiro (ponto 32). Item, projeto e OS são validados pelos Contracts dos donos; a linha é sempre lida pela SC da URL
/// (IDOR, CA 16).
/// </summary>
public sealed partial class PurchaseRequestService(
    IPurchasingDb db, IItems items, IItemUnits itemUnits, IUnitConversion conversion, IProjects projects, IProductionOrders orders, IStockReservations reservations,
    INumberSequence numbers, PurchasingParametersService parameters, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, TimeProvider clock)
{
    public const string ServiceNotAllowedCode = "service_not_allowed";
    public const string ServiceNotAllowed = "Serviço não entra na solicitação de compra. Lance um título a pagar manual.";
    public const string InProgressCode = "purchase_request_in_progress";
    public const string LineNotOpenCode = "purchase_request_line_not_open";

    /// <summary>"Enviar solicitação" (CA 3, 4): nasce <c>Open</c>, origem <c>Manual</c>, com o número na mesma transação.</summary>
    public async Task<Result<PurchaseRequestDetail>> CreateAsync(PurchaseRequestInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var lines = input.Lines ?? [];
        if (lines.Count == 0)
        {
            return Invalid("lines", "Inclua pelo menos um item.");
        }

        if (lines.Count > PurchaseRequestRules.MaxLines)
        {
            return Invalid("lines", $"Uma solicitação tem até {PurchaseRequestRules.MaxLines} itens.");
        }

        var notes = Clean(input.Notes);
        if (notes is { Length: > PurchaseRequestRules.MaxNotes })
        {
            return Invalid("notes", $"Escreva a observação com até {PurchaseRequestRules.MaxNotes} caracteres.");
        }

        var destination = await DestinationAsync(input.ProjectId, input.ProductionOrderId, cancellationToken);
        if (destination.IsFailure)
        {
            return destination.Error!;
        }

        var today = BusinessTime.Today(clock);
        var found = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var prepared = new List<(PurchaseRequestLineInput Input, ItemSummary Item, decimal Quantity)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"lines[{i}].";
            if (!found.TryGetValue(line.ItemId, out var item) || !item.IsActive || item.IsDeleted)
            {
                return Invalid(prefix + "itemId", "Escolha um item ativo.");
            }

            if (item.Type == ItemType.Service)
            {
                return Error.Validation(ServiceNotAllowedCode, ServiceNotAllowed, new Dictionary<string, string[]> { [prefix + "itemId"] = [ServiceNotAllowed] });
            }

            if (prepared.Any(p => p.Item.Id == item.Id))
            {
                return Invalid(prefix + "itemId", "Este item já está em outra linha. Some as quantidades numa linha só.");
            }

            if (line.Quantity is <= 0m or > PurchaseRequestRules.MaxQuantity)
            {
                return Invalid(prefix + "quantity", "Informe a quantidade, maior que zero.");
            }

            var quantity = line.Quantity;
            if (line.UnitId is { } unit && unit != item.UnitId)
            {
                var converted = await conversion.ConvertAsync(item.Id, line.Quantity, unit, item.UnitId, cancellationToken);
                if (converted.IsFailure)
                {
                    return Invalid(prefix + "unitId", converted.Error!.Message);
                }

                quantity = converted.Value;
            }

            if (line.NeededBy < today)
            {
                return Invalid(prefix + "neededBy", "A necessidade não pode ser antes de hoje.");
            }

            prepared.Add((line, item, quantity));
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var request = new PurchaseRequest
        {
            Number = await numbers.NextAsync(ShortageRecomposer.DocType, cancellationToken), Origin = PurchaseRequestOrigin.Manual, ProjectId = destination.Value.ProjectId,
            ProductionOrderId = destination.Value.OrderId, Notes = notes, RequestedBy = currentUser.UserId, CreatedAt = now, UpdatedAt = now,
        };
        db.PurchaseRequests.Add(request);
        var sequence = 0;
        foreach (var (line, item, quantity) in prepared)
        {
            var entered = line.UnitId is { } unit && unit != item.UnitId;
            db.PurchaseRequestLines.Add(new PurchaseRequestLine
            {
                RequestId = request.Id, Origin = PurchaseRequestOrigin.Manual, Sequence = ++sequence, ItemId = item.Id, Quantity = quantity, EnteredQuantity = entered ? line.Quantity : null,
                EnteredUnitId = entered ? line.UnitId : null, NeededBy = line.NeededBy, ProductionOrderId = destination.Value.OrderId, ProjectId = destination.Value.ProjectId, CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(request.Id, cancellationToken);
    }

    /// <summary>
    /// Editar a linha aberta (CA 13): quantidade (unidade de estoque) e necessidade, com auditoria. Linha automática ganha "Editada à mão"
    /// e sai da recomposição e do job; a linha da falta editada a menor devolve o aviso de que a OS continua com falta.
    /// </summary>
    public async Task<Result<PurchaseRequestLineUpdated>> UpdateLineAsync(Guid id, Guid lineId, PurchaseRequestLineUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var line = await db.PurchaseRequestLines.FirstOrDefaultAsync(l => l.Id == lineId && l.RequestId == id, cancellationToken);
        if (line is null)
        {
            return LineNotFound();
        }

        if (line.Status != PurchaseRequestStatus.Open)
        {
            return LineNotOpen(line.Status);
        }

        if (line.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "A linha da solicitação mudou desde que você abriu. Recarregue.");
        }

        if (input.Quantity is <= 0m or > PurchaseRequestRules.MaxQuantity)
        {
            return Invalid("quantity", "Informe a quantidade, maior que zero.");
        }

        if (input.NeededBy < BusinessTime.Today(clock))
        {
            return Invalid("neededBy", "A necessidade não pode ser antes de hoje.");
        }

        var quantity = decimal.Round(input.Quantity, 4, MidpointRounding.AwayFromZero);
        if (quantity == line.Quantity && input.NeededBy == line.NeededBy)
        {
            return new PurchaseRequestLineUpdated((await DetailAsync(id, cancellationToken)).Value, null);
        }

        db.Entry(line).Property(l => l.Version).OriginalValue = input.Version;
        line.Quantity = quantity;
        line.NeededBy = input.NeededBy;
        line.EnteredQuantity = null;
        line.EnteredUnitId = null;
        line.ManuallyEdited = line.Origin != PurchaseRequestOrigin.Manual || line.ManuallyEdited;
        line.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        var warning = line.Origin == PurchaseRequestOrigin.BomShortage && line.ProductionOrderId is { } order ? await StillShortAsync(order, line.ItemId, cancellationToken) : null;
        return new PurchaseRequestLineUpdated((await DetailAsync(id, cancellationToken)).Value, warning);
    }

    /// <summary>"Cancelar solicitação" (CA 14): só com todas as linhas em aberto ou canceladas; 409 com o documento se alguma está em cotação ou pedido.</summary>
    public async Task<Result<PurchaseRequestDetail>> CancelAsync(Guid id, PurchaseRequestCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var request = await db.PurchaseRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        if (request.Status == PurchaseRequestStatus.Cancelled)
        {
            return Error.Conflict("purchase_request_cancelled", $"A {request.Number} já está cancelada.");
        }

        var lines = await db.PurchaseRequestLines.Where(l => l.RequestId == id).ToListAsync(cancellationToken);
        if (lines.Any(l => l.Status is PurchaseRequestStatus.Quoting or PurchaseRequestStatus.Ordered))
        {
            return Error.Conflict(InProgressCode, $"A {request.Number} tem item em cotação ou pedido e não pode ser cancelada. Cancele só as linhas em aberto.");
        }

        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > PurchaseRequestRules.MaxReason)
        {
            return Invalid("reason", $"Informe o motivo, com até {PurchaseRequestRules.MaxReason} caracteres.");
        }

        if (request.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "A solicitação mudou desde que você abriu. Recarregue.");
        }

        var now = clock.GetUtcNow();
        db.Entry(request).Property(r => r.Version).OriginalValue = input.Version;
        foreach (var line in lines.Where(l => l.Status == PurchaseRequestStatus.Open))
        {
            CancelByUser(line, reason, now);
        }

        request.Status = PurchaseRequestStatus.Cancelled;
        request.CancelReason = reason;
        request.CancelledAt = now;
        request.CancelledBy = currentUser.UserId;
        request.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Cancelar uma linha em aberto (CA 14), com motivo; a situação da SC é recalculada.</summary>
    public async Task<Result<PurchaseRequestDetail>> CancelLineAsync(Guid id, Guid lineId, PurchaseRequestCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var line = await db.PurchaseRequestLines.FirstOrDefaultAsync(l => l.Id == lineId && l.RequestId == id, cancellationToken);
        if (line is null)
        {
            return LineNotFound();
        }

        if (line.Status != PurchaseRequestStatus.Open)
        {
            return LineNotOpen(line.Status);
        }

        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > PurchaseRequestRules.MaxReason)
        {
            return Invalid("reason", $"Informe o motivo, com até {PurchaseRequestRules.MaxReason} caracteres.");
        }

        if (line.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "A linha da solicitação mudou desde que você abriu. Recarregue.");
        }

        var now = clock.GetUtcNow();
        db.Entry(line).Property(l => l.Version).OriginalValue = input.Version;
        CancelByUser(line, reason, now);
        await db.SaveChangesAsync(cancellationToken);
        await PurchaseRequestUpdates.RefreshStatusAsync(db, [id], now, cancellationToken, currentUser.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Linha automática cancelada pelo comprador não volta pela recomposição (a mão manda, CA 13).</summary>
    private static void CancelByUser(PurchaseRequestLine line, string reason, DateTimeOffset now)
    {
        PurchaseRequestUpdates.Cancel(line, reason, now);
        line.ManuallyEdited = line.Origin != PurchaseRequestOrigin.Manual || line.ManuallyEdited;
    }

    /// <summary>"A OS {número} continua com falta de {x}." quando o que está pedido não cobre a falta atual da fonte.</summary>
    private async Task<string?> StillShortAsync(Guid orderId, Guid itemId, CancellationToken ct)
    {
        var shortage = (await reservations.OpenShortagesAsync(ct)).Where(s => s.ProductionOrderId == orderId && s.ItemId == itemId).Sum(s => s.ShortQty);
        var covered = await db.PurchaseRequestLines.AsNoTracking()
            .Where(l => l.ProductionOrderId == orderId && l.ItemId == itemId && l.Status != PurchaseRequestStatus.Cancelled).SumAsync(l => l.Quantity, ct);
        var missing = shortage - covered;
        if (missing <= 0m)
        {
            return null;
        }

        var number = (await orders.FindAsync(orderId, ct))?.Number ?? "OS";
        return $"A OS {number} continua com falta de {PurchaseFormat.Quantity(missing)}.";
    }

    /// <summary>Destino (R-CMP-6): projeto em andamento ou estoque geral; com a OS, o projeto é o dela.</summary>
    private async Task<Result<(Guid? ProjectId, Guid? OrderId)>> DestinationAsync(Guid? projectId, Guid? orderId, CancellationToken ct)
    {
        if (orderId is { } oid)
        {
            var order = await orders.FindAsync(oid, ct);
            if (order is null)
            {
                return Invalid("productionOrderId", "OS de fabricação não encontrada.");
            }

            if (order.Status is ProductionOrderState.Completed or ProductionOrderState.Cancelled)
            {
                return Invalid("productionOrderId", "A OS está concluída ou cancelada.");
            }

            if (projectId is { } pid && pid != order.ProjectId)
            {
                return Invalid("projectId", "O destino da OS é o projeto dela.");
            }

            projectId = order.ProjectId;
        }

        if (projectId is not { } id)
        {
            return ((Guid?)null, orderId);
        }

        var project = await projects.FindAsync(id, ct);
        if (project is null)
        {
            return Invalid("projectId", "Projeto não encontrado.");
        }

        return project.Status is "Closed" or "Cancelled" ? Invalid("projectId", "O projeto precisa estar em andamento (não encerrado nem cancelado).") : ((Guid?)project.Id, orderId);
    }

    private static Error LineNotOpen(PurchaseRequestStatus status) => Error.Conflict(LineNotOpenCode, status switch
    {
        PurchaseRequestStatus.Quoting => "A linha já está em cotação e não muda aqui.",
        PurchaseRequestStatus.Ordered => "A linha já virou pedido e não muda aqui.",
        _ => "A linha está cancelada.",
    });

    private static Error NotFound() => Error.NotFound("purchase_request_not_found", "Solicitação de compra não encontrada.");

    private static Error LineNotFound() => Error.NotFound("purchase_request_line_not_found", "Linha não encontrada nesta solicitação.");

    internal static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
