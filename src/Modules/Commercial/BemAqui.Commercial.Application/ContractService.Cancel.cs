using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed record ContractCancelInput(string? Reason, uint Version);

public sealed partial class ContractService
{
    /// <summary>
    /// Cancelar contrato (COM-04b CA 12 e 13): só o ativo, com motivo; o projeto ainda não entregue é cancelado junto e o
    /// Finance cancela as parcelas sem baixa pelo <see cref="ContractCancelled"/>.
    /// </summary>
    public async Task<Result<ContractDetail>> CancelAsync(Guid id, ContractCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contract is null)
        {
            return NotFound();
        }

        if (contract.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O contrato mudou desde que você abriu. Recarregue.");
        }

        if (contract.Status != ContractStatus.Active)
        {
            return Error.Conflict("contract_not_active", "Só o contrato ativo é cancelado.");
        }

        var project = await db.Projects.FirstOrDefaultAsync(p => p.ContractId == id, cancellationToken);
        if (project?.Status is ProjectStatus.Delivered or ProjectStatus.Closed)
        {
            return Error.Conflict("project_delivered", "Projeto entregue: o contrato não é cancelado. Use um aditivo.");
        }

        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            return QuoteService.Invalid("reason", "Informe o motivo do cancelamento, com até 500 caracteres.");
        }

        db.Entry(contract).Property(c => c.Version).OriginalValue = input.Version;
        contract.Status = ContractStatus.Cancelled;
        contract.CancelReason = reason;
        contract.CancelledAt = clock.GetUtcNow();
        contract.CancelledBy = currentUser.UserId;
        if (project is not null)
        {
            project.Status = ProjectStatus.Cancelled;
        }

        events.Publish(new ContractCancelled(contract.Id, project?.Id ?? Guid.Empty));
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }
}
