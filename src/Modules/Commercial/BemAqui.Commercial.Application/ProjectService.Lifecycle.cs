using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed record ProjectEventInput(DateOnly Date, uint Version);

public sealed partial class ProjectService
{
    /// <summary>Registrar fabricação concluída (COM-04b CA 9): projeto planejado ou em produção, data até hoje.</summary>
    public async Task<Result<ProjectDetail>> RegisterFabricationAsync(Guid id, ProjectEventInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadForEventAsync(id, input, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var project = loaded.Value;
        if (project.Status is not (ProjectStatus.Planned or ProjectStatus.InProgress))
        {
            return Error.Conflict("project_not_in_production", "A fabricação só é registrada em projeto planejado ou em produção.");
        }

        project.Status = ProjectStatus.Fabricated;
        project.FabricatedOn = input.Date;
        events.Publish(new ProjectFabricated(project.Id, project.ContractId, input.Date));
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Registrar entrega (COM-04b CA 10, D15): venda sem instalação. Exige a fabricação registrada quando o contrato tem
    /// marco de fabricação; publica a entrega e o aceite técnico com a mesma data.
    /// </summary>
    public async Task<Result<ProjectDetail>> RegisterDeliveryAsync(Guid id, ProjectEventInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadForEventAsync(id, input, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var project = loaded.Value;
        if (project.Status is not (ProjectStatus.Planned or ProjectStatus.InProgress or ProjectStatus.Fabricated))
        {
            return Error.Conflict("project_not_deliverable", "Só projeto em produção ou fabricado tem a entrega registrada.");
        }

        // D15: com OS de instalação aberta, a entrega vem do aceite técnico (COM-04b CA 10, COM-06).
        if ((await installations.ByProjectAsync(project.Id, cancellationToken)).Any(o => o.Status is Production.Contracts.InstallationState.Open or Production.Contracts.InstallationState.InProgress))
        {
            return Error.Conflict("installation_open", "Há OS de instalação aberta: a entrega é registrada pelo aceite técnico.");
        }

        var needsFabrication = project.Status != ProjectStatus.Fabricated
            && await db.ContractMilestones.AnyAsync(m => m.ContractId == project.ContractId && m.TriggerEvent == TriggerEvent.FabricationDone, cancellationToken);
        if (needsFabrication)
        {
            return Error.Conflict("fabrication_pending", "Registre a fabricação concluída antes da entrega.");
        }

        if (project.FabricatedOn is { } fabricated && input.Date < fabricated)
        {
            return QuoteService.Invalid("date", "A entrega não pode ser antes da fabricação.");
        }

        project.Status = ProjectStatus.Delivered;
        project.DeliveredOn = input.Date;
        project.DeliveredManually = true;
        events.Publish(new ProjectDelivered(project.Id, project.ContractId, input.Date));
        events.Publish(new TechnicalAcceptance(project.Id, project.ContractId, input.Date));
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task<Result<Project>> LoadForEventAsync(Guid id, ProjectEventInput input, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
        {
            return NotFound();
        }

        if (project.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O projeto mudou desde que você abriu. Recarregue.");
        }

        if (input.Date > BusinessTime.Today(clock))
        {
            return QuoteService.Invalid("date", "A data não pode ser no futuro.");
        }

        if (await db.Contracts.Where(c => c.Id == project.ContractId).Select(c => c.Status).FirstAsync(ct) != ContractStatus.Active)
        {
            return Error.Conflict("contract_not_active", "O contrato não está ativo.");
        }

        db.Entry(project).Property(p => p.Version).OriginalValue = input.Version;
        return project;
    }
}
