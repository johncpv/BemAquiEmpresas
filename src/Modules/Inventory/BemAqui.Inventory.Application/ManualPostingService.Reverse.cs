using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class ManualPostingService
{
    public const string ReverseDeniedMessage = "Este estorno precisa de outra pessoa com permissão de aprovar no Estoque.";

    /// <summary>
    /// "Estornar movimento" (CA 20 a 23): só o movimento manual, pelo movimento inverso (o original nunca muda). O autor estorna com
    /// <c>inventory.edit</c> até o limite de valor e em 24 h; fora disso, outra pessoa com <c>inventory.approve</c> (exceção do
    /// Proprietário único). A transferência estorna as duas pernas juntas, com um número novo.
    /// </summary>
    public async Task<Result<MovementDetail>> ReverseAsync(Guid movementId, ReverseMovementInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > ManualPostingRules.MaxReason)
        {
            return Invalid("reason", $"Informe o motivo do estorno, com até {ManualPostingRules.MaxReason} caracteres.");
        }

        var movement = await db.StockMovements.AsNoTracking().FirstOrDefaultAsync(m => m.Id == movementId, cancellationToken);
        if (movement is null)
        {
            return Error.NotFound("movement_not_found", "Movimento não encontrado.");
        }

        var line = await db.ManualPostingLines.FirstOrDefaultAsync(l => l.MovementId == movementId || l.InMovementId == movementId, cancellationToken);
        var decision = await policy.EvaluateAsync(movement, line, cancellationToken);
        switch (decision.Rule)
        {
            case ReverseRule.IsReversal:
                return Error.Conflict("movement_is_reversal", decision.Message!);
            case ReverseRule.AlreadyReversed:
                return Error.Conflict("movement_already_reversed", decision.Message!);
            case ReverseRule.NotManual:
                return Error.Conflict("reverse_from_source", decision.Message!) with
                {
                    Data = new Dictionary<string, object?> { ["sourceType"] = movement.SourceType, ["sourceId"] = movement.SourceId, ["sourceNumber"] = movement.SourceNumber },
                };
            case ReverseRule.Denied:
                return Error.Forbidden("reverse_needs_approver", decision.Message!);
            default:
                break;
        }

        // EST-01b CA 18: com inventário em contagem no local (origem ou destino da transferência), o estorno manual fica bloqueado.
        if (await locks.CheckAsync(new[] { line!.LocationId, line.ToLocationId ?? line.LocationId }.Distinct().ToList(), cancellationToken) is { } blocked)
        {
            return blocked;
        }

        var legs = new[] { line.InMovementId, line.MovementId }.Where(m => m is not null).Select(m => m!.Value).ToList();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        string? number = null;
        foreach (var leg in legs)
        {
            // Na transferência, primeiro tira do destino: se lá não houver saldo, nada é gravado.
            var reversed = await ledger.ReverseAsync(new StockReversal(leg, StockSources.ManualPosting, movement.SourceId, reason, number), cancellationToken);
            if (reversed.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return reversed.Error!;
            }

            number ??= reversed.Value.Number;
        }

        line.ReversedAt = clock.GetUtcNow();
        line.ReversedBy = currentUser.UserId;
        line.ReversalReason = reason;
        line.ReversalSelfApproved = decision.Rule == ReverseRule.SoleOwner;
        if (decision.Rule == ReverseRule.SoleOwner)
        {
            await NotifySelfApprovedAsync($"estornou o próprio movimento {movement.Number}", MovementLink + movement.Id, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await queries.MovementAsync(movementId, cancellationToken);
    }
}

/// <summary>Quem pode estornar um movimento agora (CA 20, 22, 23), para a tela (detalhe) e para o comando.</summary>
public sealed class ReversePolicy(IInventoryDb db, IPermissionChecker permissions, IUserDirectory users, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<(ReverseRule Rule, string? Message)> EvaluateAsync(StockMovement movement, ManualPostingLine? line, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(movement);
        if (movement.ReversesMovementId is not null)
        {
            return (ReverseRule.IsReversal, "Este movimento já é um estorno e não se estorna.");
        }

        if (!movement.IsManual || line is null)
        {
            var number = movement.SourceNumber is { } n ? " " + n : string.Empty;
            return (ReverseRule.NotManual, $"Estorne pelo documento de origem ({SourceLabel(movement.SourceType)}{number}).");
        }

        if (line.ReversedAt is not null || await db.StockMovements.AnyAsync(m => m.ReversesMovementId == movement.Id, ct))
        {
            return (ReverseRule.AlreadyReversed, "Este movimento já foi estornado.");
        }

        var author = await db.ManualPostings.Where(p => p.Id == line.PostingId).Select(p => p.RequestedBy).FirstOrDefaultAsync(ct);
        var me = currentUser.UserId;
        var parameters = await db.InventoryParameters.AsNoTracking().FirstOrDefaultAsync(ct) ?? new InventoryParameters();
        var canEdit = await permissions.HasPermissionAsync("inventory", "edit", ct);
        var canApprove = await permissions.HasPermissionAsync("inventory", "approve", ct);
        var mine = me is not null && me == author;
        if (mine && canEdit && ManualPostingRules.AuthorMayReverse(movement.TotalCost, movement.PostedAt, clock.GetUtcNow(), parameters.ManualApprovalAmount))
        {
            return (ReverseRule.Author, null);
        }

        if (canApprove && !mine)
        {
            return (ReverseRule.Approver, null);
        }

        return canApprove && me is { } actor && await SoleOwnerRule.NoOtherEligibleAsync(users, "inventory.approve", actor, [actor], ct)
            ? (ReverseRule.SoleOwner, null)
            : (ReverseRule.Denied, ManualPostingService.ReverseDeniedMessage);
    }

    /// <summary>Nome do documento de origem para a mensagem e o link (CA 23).</summary>
    public static string SourceLabel(string sourceType) => sourceType switch
    {
        StockSources.ManualPosting => "Lançamento",
        StockSources.Adjustment => "Ajuste",
        StockSources.Requisition => "Requisição",
        StockSources.GoodsReceipt => "Recebimento",
        StockSources.ProductionOrder => "OS",
        StockSources.Shipment => "Romaneio",
        StockSources.InventoryCount => "Inventário",
        _ => sourceType,
    };

    /// <summary>Link do documento de origem; os documentos das próximas stories ganham rota quando nascerem.</summary>
    public static string? SourceLink(string sourceType, Guid sourceId) => sourceType switch
    {
        StockSources.ManualPosting => ManualPostingService.Link + sourceId,
        StockSources.Adjustment => StockAdjustmentService.Link + sourceId,
        _ => null,
    };
}
