using BemAqui.Finance.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class InstallationOrderService
{
    /// <summary>
    /// Aceite técnico (CA 9, 11, 17): etapas anteriores e checklist completos; conclui a entrega técnica e a OS, confirma o
    /// título do instalador com o vencimento pela condição dele e publica o aceite. Dois aceites juntos: só um grava (xmin).
    /// </summary>
    public async Task<Result<InstallationAcceptResult>> AcceptAsync(Guid id, InstallationAcceptInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (order.Status != ProductionOrderStatus.InProgress)
        {
            return Error.Conflict("production_order_not_started", order.Status == ProductionOrderStatus.Completed ? "O aceite técnico já foi registrado." : "Inicie a instalação antes do aceite.");
        }

        var name = input.AcceptedByName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120)
        {
            return Invalid("acceptedByName", "Informe quem aceitou pelo cliente.");
        }

        if (input.Date > Today || (order.StartedOn is { } started && input.Date < started))
        {
            return Invalid("date", "A data do aceite vai do início da instalação até hoje.");
        }

        var steps = await db.ProductionOrderSteps.Where(s => s.OrderId == id).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var checklist = await db.ProductionOrderChecklist.Where(c => c.OrderId == id).ToListAsync(cancellationToken);
        var missing = steps.Take(steps.Count - 1).Where(s => s.CompletedAt is null).Select(s => $"Etapa {s.Name}")
            .Concat(checklist.Where(c => c.Result is null).Select(c => $"Checklist: {c.Text}")).ToArray();
        if (missing.Length > 0)
        {
            return Error.Validation("acceptance_incomplete", "Conclua as etapas e o checklist antes do aceite.", new Dictionary<string, string[]> { ["missing"] = missing });
        }

        var last = steps[^1];
        last.CompletedAt = clock.GetUtcNow();
        last.CompletedBy = currentUser.UserId;
        order.Status = ProductionOrderStatus.Completed;
        order.AcceptedOn = input.Date;
        order.AcceptedByName = name;
        var installer = await parties.FindAsync(order.InstallerId, cancellationToken);
        var due = installer is null ? input.Date : await InstallerDueAsync(installer, order.ContractValue, input.Date, cancellationToken);
        var confirmed = await payables.ConfirmAsync(DocumentOrigin.Installer, order.Id, order.ContractValue, due, cancellationToken);
        if (confirmed.IsFailure)
        {
            return confirmed.Error!;
        }

        events.Publish(new InstallationAccepted(order.Id, order.ProjectId, input.Date));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency_conflict", "O aceite técnico já foi registrado por outra pessoa. Recarregue.");
        }

        return new InstallationAcceptResult(confirmed.Value.Id, confirmed.Value.Number, confirmed.Value.DueDate);
    }

    /// <summary>
    /// Cancelar OS (CA 13): a aberta cancela também o título previsto; a iniciada exige <c>production.approve</c> e o título
    /// fica previsto para o financeiro decidir.
    /// </summary>
    public async Task<Result<InstallationDetail>> CancelAsync(Guid id, InstallationCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, input.Version, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (order.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
        {
            return Error.Conflict("production_order_closed", "OS concluída ou cancelada não é cancelada.");
        }

        if (order.Status == ProductionOrderStatus.InProgress && !await permissions.HasPermissionAsync("production", "approve", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "OS iniciada só é cancelada por quem aprova na produção.");
        }

        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            return Invalid("reason", "Informe o motivo do cancelamento, com até 500 caracteres.");
        }

        await CancelOrderAsync(order, reason, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Cancela a OS; o título previsto cai junto só com a OS não iniciada, senão o financeiro é avisado.</summary>
    internal async Task CancelOrderAsync(ProductionOrder order, string reason, CancellationToken ct)
    {
        var started = order.Status == ProductionOrderStatus.InProgress;
        order.Status = ProductionOrderStatus.Cancelled;
        order.CancelReason = reason;
        order.CancelledAt = clock.GetUtcNow();
        order.CancelledBy = currentUser.UserId;
        if (!started)
        {
            await payables.CancelFromOriginAsync(DocumentOrigin.Installer, order.Id, $"OS {order.Number} cancelada", ct);
            return;
        }

        foreach (var user in await users.UsersWithPermissionAsync("finance.edit", ct))
        {
            notifications.Notify(user, "production.installation_cancelled", $"OS {order.Number} cancelada depois de iniciada: decida o título do instalador.", "/app/producao/instalacoes/" + order.Id);
        }
    }
}
