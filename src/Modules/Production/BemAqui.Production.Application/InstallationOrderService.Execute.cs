using BemAqui.Finance.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class InstallationOrderService
{
    /// <summary>Valor e datas (CA 12): o título previsto acompanha; depois do aceite, o valor muda só no título.</summary>
    public async Task<Result<InstallationDetail>> UpdateAsync(Guid id, InstallationUpdateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (order.Status is not (ProductionOrderStatus.Open or ProductionOrderStatus.InProgress))
        {
            return Error.Conflict("production_order_closed", "OS concluída ou cancelada não muda. Ajuste no título a pagar.");
        }

        if (input.ContractValue <= 0)
        {
            return Invalid("contractValue", "Informe um valor combinado maior que zero.");
        }

        if (input.PlannedEnd < input.PlannedStart)
        {
            return Invalid("plannedEnd", "O fim previsto não pode ser antes do início.");
        }

        var before = order.ContractValue;
        if (before != input.ContractValue || order.PlannedEnd != input.PlannedEnd)
        {
            var installer = await parties.FindAsync(order.InstallerId, cancellationToken);
            var due = installer is null ? input.PlannedEnd : await InstallerDueAsync(installer, input.ContractValue, input.PlannedEnd, cancellationToken);
            var updated = await payables.UpdatePlannedAsync(DocumentOrigin.Installer, order.Id, input.ContractValue, due, cancellationToken);
            if (updated.IsFailure)
            {
                return updated.Error!.Code == "payable_not_planned" ? Error.Conflict("payable_not_planned", "Ajuste no título a pagar.") : updated.Error!;
            }
        }

        order.ContractValue = input.ContractValue;
        order.PlannedStart = input.PlannedStart;
        order.PlannedEnd = input.PlannedEnd;
        if (before != input.ContractValue)
        {
            var who = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, cancellationToken) ?? "um usuário" : "o sistema";
            foreach (var user in await users.UsersWithPermissionAsync("finance.edit", cancellationToken))
            {
                notifications.Notify(user, "production.installation_value", $"Valor da instalação {order.Number} mudou de {Money(before)} para {Money(input.ContractValue)} por {who}.", "/app/producao/instalacoes/" + order.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Iniciar instalação (CA 6).</summary>
    public async Task<Result<InstallationDetail>> StartAsync(Guid id, InstallationDateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (order.Status != ProductionOrderStatus.Open)
        {
            return Error.Conflict("production_order_not_open", "Só a OS aberta é iniciada.");
        }

        if (input.Date > Today)
        {
            return Invalid("date", "A data não pode ser no futuro.");
        }

        order.Status = ProductionOrderStatus.InProgress;
        order.StartedOn = input.Date;
        events.Publish(new InstallationStarted(order.Id, order.ProjectId, input.Date));
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Concluir etapa (CA 7): em sequência; a última, "Entrega técnica", só pelo aceite.</summary>
    public async Task<Result<InstallationDetail>> CompleteStepAsync(Guid id, Guid stepId, uint version, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(id, version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (order.Status != ProductionOrderStatus.InProgress)
        {
            return Error.Conflict("production_order_not_started", "Inicie a instalação antes de concluir etapas.");
        }

        var steps = await db.ProductionOrderSteps.Where(s => s.OrderId == id).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var index = steps.FindIndex(s => s.Id == stepId);
        if (index < 0)
        {
            return Error.NotFound("step_not_found", "Etapa não encontrada.");
        }

        if (index == steps.Count - 1)
        {
            return Error.Conflict("step_by_acceptance", "A entrega técnica é concluída pelo aceite técnico.");
        }

        if (steps.Take(index).Any(s => s.CompletedAt is null))
        {
            return Error.Conflict("step_out_of_order", "Conclua as etapas anteriores antes.");
        }

        steps[index].CompletedAt ??= clock.GetUtcNow();
        steps[index].CompletedBy ??= currentUser.UserId;
        // Toca a OS: a versão (xmin) muda e a outra aba recebe 409 na próxima gravação.
        db.Entry(order).Property(o => o.PlannedStart).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Marcar o checklist (CA 8): feito, não se aplica ou desmarcar; com usuário e data.</summary>
    public async Task<Result<InstallationDetail>> MarkChecklistAsync(Guid id, Guid itemId, InstallationChecklistInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (order.Status is not (ProductionOrderStatus.Open or ProductionOrderStatus.InProgress))
        {
            return Error.Conflict("production_order_closed", "OS concluída ou cancelada não muda.");
        }

        var item = await db.ProductionOrderChecklist.FirstOrDefaultAsync(c => c.OrderId == id && c.Id == itemId, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("checklist_item_not_found", "Item do checklist não encontrado.");
        }

        item.Result = input.Result;
        item.MarkedAt = input.Result is null ? null : clock.GetUtcNow();
        item.MarkedBy = input.Result is null ? null : currentUser.UserId;
        // Toca a OS: a versão (xmin) muda e a outra aba recebe 409 na próxima gravação.
        db.Entry(order).Property(o => o.PlannedStart).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<Result<ProductionOrder>> LoadAsync(Guid id, uint version, CancellationToken ct)
    {
        var order = await db.ProductionOrders.FirstOrDefaultAsync(o => o.Id == id && o.Type == ProductionOrderType.Installation, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Version != version)
        {
            return Stale();
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = version;
        return order;
    }

    private static string Money(decimal value) => value.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}
