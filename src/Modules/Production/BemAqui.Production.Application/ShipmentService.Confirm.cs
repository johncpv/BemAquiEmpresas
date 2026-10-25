using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class ShipmentService
{
    /// <summary>
    /// "Confirmar expedição" (CA 4 e 6, D4): só o rascunho. Sem produto do estoque geral, expede agora (<see cref="ShipAsync"/>). Com linha
    /// "Estoque geral", o romaneio fica "Aguardando confirmação do estoque" e quem tem <c>inventory.approve</c> (menos quem pediu) é avisado.
    /// </summary>
    public async Task<Result<ShipmentDetail>> ConfirmAsync(Guid id, ShipmentDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var shipment = loaded.Value;
        if (shipment.Status != ShipmentState.Draft)
        {
            return Error.Conflict("shipment_not_draft", ShipmentRules.NotDraft);
        }

        var lines = await db.ShipmentLines.Where(l => l.ShipmentId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        if (!ShipmentRules.RequiresStockApproval(lines.Select(l => l.FromGeneralStock)))
        {
            var shipped = await ShipAsync(shipment, lines, cancellationToken);
            return shipped.IsFailure ? shipped.Error! : await GetAsync(id, cancellationToken);
        }

        shipment.Status = ShipmentState.AwaitingStockApproval;
        shipment.StockApprovalRequestedBy = currentUser.UserId;
        shipment.StockApprovalRequestedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        foreach (var user in (await users.UsersWithPermissionAsync("inventory.approve", cancellationToken)).Where(u => !Involved(shipment).Contains(u)))
        {
            await notifications.NotifyOnceAsync(user, AwaitingType, ShipmentRules.AwaitingTitle(shipment.Number), Link + shipment.Id, $"shipment-stock:{shipment.Id}:{shipment.StockApprovalRequestedAt:O}", cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// "Confirmar estoque geral" (CA 6, ponto 86): outra pessoa com <c>inventory.approve</c> (endpoint) decide sobre o que foi montado, sem
    /// editar, e a expedição acontece na mesma chamada. Quem montou ou pediu recebe 403, salvo o Proprietário único (<see cref="SoleOwnerRule"/>):
    /// passa com a marca "Autoaprovado" e o Proprietário é notificado.
    /// </summary>
    public async Task<Result<ShipmentDetail>> ConfirmStockAsync(Guid id, ShipmentDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var shipment = loaded.Value;
        if (shipment.Status != ShipmentState.AwaitingStockApproval)
        {
            return Error.Conflict("shipment_not_awaiting", "Só romaneio aguardando a confirmação do estoque geral.");
        }

        var self = false;
        if (currentUser.UserId is { } me && Involved(shipment).Contains(me))
        {
            if (!await SoleOwnerRule.NoOtherEligibleAsync(users, "inventory.approve", me, Involved(shipment), cancellationToken))
            {
                return Error.Forbidden("own_shipment", ShipmentRules.OwnBlocked);
            }

            self = true;
        }

        shipment.StockApprovedBy = currentUser.UserId;
        shipment.SelfApproved = self;
        var lines = await db.ShipmentLines.Where(l => l.ShipmentId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var shipped = await ShipAsync(shipment, lines, cancellationToken, async () =>
        {
            if (self)
            {
                var name = currentUser.UserId is { } actor ? await users.DisplayNameAsync(actor, cancellationToken) ?? "Um usuário" : "Um usuário";
                foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
                {
                    notifications.Notify(owner, SelfApprovedType, $"{name} confirmou o estoque geral do próprio romaneio {shipment.Number} ({SoleOwnerRule.SelfApprovedLabel}).", Link + shipment.Id);
                }
            }
            else if (shipment.CreatedBy is { } author)
            {
                notifications.Notify(author, DecisionType, $"Estoque geral confirmado: romaneio {shipment.Number} expedido.", Link + shipment.Id);
            }
        });
        return shipped.IsFailure ? shipped.Error! : await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// "Cancelar romaneio" (CA 10): rascunho ou aguardando o estoque, com motivo; as séries ficam livres para outro romaneio. Nada no estoque.
    /// </summary>
    public async Task<Result<ShipmentDetail>> CancelAsync(Guid id, ShipmentReasonInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (ShipmentRules.Reason(input.Reason) is not { } reason)
        {
            return Invalid("reason", $"Informe o motivo, com até {ShipmentRules.MaxReason} caracteres.");
        }

        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var shipment = loaded.Value;
        if (!ShipmentRules.CanCancel(shipment.Status))
        {
            return Error.Conflict("shipment_not_cancellable", "Só romaneio em rascunho ou aguardando o estoque é cancelado. O expedido se estorna.");
        }

        var now = clock.GetUtcNow();
        shipment.Status = ShipmentState.Cancelled;
        shipment.CancelledAt = now;
        shipment.CancelledBy = currentUser.UserId;
        shipment.CancelReason = reason;
        foreach (var serial in await db.ShipmentLineSerials.Where(s => s.ShipmentId == id && s.ReleasedAt == null).ToListAsync(cancellationToken))
        {
            serial.ReleasedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// "Estornar expedição" (CA 11, ADR 0023 §4): só o expedido, com motivo e <c>production.approve</c> (endpoint). Na mesma transação, cada
    /// saída ganha a entrada inversa (<c>reverses_movement_id</c>), as séries voltam a <c>InStock</c> e ficam livres, o romaneio vai a
    /// <c>Reversed</c> e o <see cref="ShipmentReversed"/> vai para o outbox. Na Fase 5, com NF-e autorizada, o estorno exigirá cancelar a nota.
    /// </summary>
    public async Task<Result<ShipmentDetail>> ReverseAsync(Guid id, ShipmentReasonInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (ShipmentRules.Reason(input.Reason) is not { } reason)
        {
            return Invalid("reason", $"Informe o motivo do estorno, com até {ShipmentRules.MaxReason} caracteres.");
        }

        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var shipment = loaded.Value;
        if (!ShipmentRules.CanReverse(shipment.Status))
        {
            return Error.Conflict("shipment_not_shipped", "Só romaneio expedido tem a expedição estornada.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.GetUtcNow();
        shipment.Status = ShipmentState.Reversed;
        shipment.ReversedAt = now;
        shipment.ReversedBy = currentUser.UserId;
        shipment.ReversalReason = reason;
        // Grava o romaneio antes do estoque: dois estornos juntos param no xmin e só um lança as entradas inversas.
        await db.SaveChangesAsync(cancellationToken);
        var lines = await db.ShipmentLines.AsNoTracking().Where(l => l.ShipmentId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        foreach (var line in lines.Where(l => l.MovementId is not null))
        {
            var reversed = await stock.ReverseAsync(new StockReversal(line.MovementId!.Value, StockSources.Shipment, shipment.Id, "Estorno da expedição: " + reason), cancellationToken);
            if (reversed.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return reversed.Error!;
            }
        }

        var lineSerials = await db.ShipmentLineSerials.Where(s => s.ShipmentId == id && s.ReleasedAt == null).ToListAsync(cancellationToken);
        var back = await serials.UnshipAsync(shipment.Id, lineSerials.Select(s => s.SerialId).ToList(), cancellationToken);
        if (back.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return back.Error!;
        }

        foreach (var serial in lineSerials)
        {
            serial.ReleasedAt = now;
        }

        events.Publish(new ShipmentReversed(shipment.Id, shipment.ProjectId, BusinessTime.Today(clock)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// A expedição (CA 4, 5 e 16; ADR 0023 §1): na mesma transação, o romaneio vai a <c>Shipped</c> (gravado antes, para o xmin segurar a
    /// confirmação dupla), as saídas <c>Shipment</c> por linha pelo custo médio do momento (chave <c>(Shipment, romaneioId, linha, Shipment)</c>),
    /// as séries a <c>Shipped</c>, o PDF guardado como arquivo do sistema e o <see cref="ShipmentDispatched"/> no outbox. Falta de saldo (409
    /// <c>insufficient_stock</c>) ou série fora do estoque desfaz tudo e o romaneio continua como estava.
    /// </summary>
    private async Task<Result> ShipAsync(Shipment shipment, List<ShipmentLine> lines, CancellationToken ct, Func<Task>? beforeCommit = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        shipment.Status = ShipmentState.Shipped;
        shipment.ShippedAt = clock.GetUtcNow();
        shipment.ShippedBy = currentUser.UserId;
        await db.SaveChangesAsync(ct);
        var lineSerials = await db.ShipmentLineSerials.AsNoTracking().Where(s => s.ShipmentId == shipment.Id && s.ReleasedAt == null).ToListAsync(ct);
        var postings = lines.Select(l =>
        {
            var own = lineSerials.Where(s => s.LineId == l.Id).ToList();
            // O produto do estoque geral sai sem projeto: o que é do projeto continua igual (CA 2 e 6).
            return new StockPosting(
                StockMovementType.Shipment, l.ItemId, l.LocationId, l.LotId, -l.Quantity, null, StockSources.Shipment, shipment.Id, l.Sequence,
                ProjectId: l.FromGeneralStock ? null : shipment.ProjectId, Reason: "Romaneio " + shipment.Number, SerialId: own.Count == 1 ? own[0].SerialId : null,
                SourceNumber: shipment.Number);
        }).ToList();
        var posted = await stock.PostManyAsync(postings, ct);
        if (posted.IsFailure)
        {
            await transaction.RollbackAsync(ct);
            return posted.Error!;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].MovementId = posted.Value[i].MovementId;
        }

        var shippedSerials = await serials.ShipAsync(shipment.Id, lineSerials.Select(s => s.SerialId).ToList(), ct);
        if (shippedSerials.IsFailure)
        {
            await transaction.RollbackAsync(ct);
            return shippedSerials.Error!;
        }

        await db.SaveChangesAsync(ct);
        var detail = (await GetAsync(shipment.Id, ct)).Value;
        var pdf = await renderer.RenderAsync(new ShipmentPdfDocument(await PdfModelAsync(detail, ct)), ct);
        var stored = await attachments.StoreGeneratedAsync(ShipmentRules.AttachmentOwner, shipment.Id, shipment.Number + ".pdf", "application/pdf", pdf, ct);
        shipment.PdfAttachmentId = stored.Id;
        events.Publish(new ShipmentDispatched(shipment.Id, shipment.ProjectId, lines.Select(l => new ShipmentDispatchedLine(
            l.ItemId, lineSerials.Where(s => s.LineId == l.Id).Select(s => s.SerialId).ToList(), l.Quantity, l.LocationId)).ToList()));
        if (beforeCommit is not null)
        {
            await beforeCommit();
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }

    private async Task<Result<Shipment>> LoadAsync(Guid id, uint version, CancellationToken ct)
    {
        var shipment = await db.Shipments.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shipment is null)
        {
            return NotFound();
        }

        if (shipment.Version != version)
        {
            return Error.Conflict("concurrency_conflict", "O romaneio mudou desde que você abriu. Recarregue.");
        }

        db.Entry(shipment).Property(s => s.Version).OriginalValue = version;
        return shipment;
    }
}
