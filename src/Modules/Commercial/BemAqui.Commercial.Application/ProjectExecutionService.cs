using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Fiscal.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>Iniciar a execução do projeto de serviço (EXE-01a CA 10).</summary>
public sealed record ProjectStartInput(DateOnly Date, uint Version, ProjectStatus? ExpectedStatus = null, string? BackdateReason = null);

/// <summary>
/// Registrar entrega (COM-04b CA 10; EXE-01a CA 11): o corpo antigo (<c>date</c>, <c>version</c>) segue valendo para o projeto de fabricação; no de serviço,
/// <c>expectedStatus</c> é obrigatório e "também aceitou" grava o aceite junto.
/// </summary>
public sealed record ProjectDeliveryInput(
    DateOnly Date, uint Version, ProjectStatus? ExpectedStatus = null, string? BackdateReason = null, bool AlsoAccept = false, string? AcceptedByName = null,
    string? AcceptedByRole = null);

/// <summary>Registrar o aceite do cliente (EXE-01a CA 13).</summary>
public sealed record ProjectAcceptanceInput(
    DateOnly Date, uint Version, string? AcceptedByName, ProjectStatus? ExpectedStatus = null, string? AcceptedByRole = null, Guid? AttachmentId = null,
    string? BackdateReason = null);

/// <summary>Desfazer o aceite do cliente (EXE-01a CA 15), com motivo.</summary>
public sealed record ProjectAcceptanceRevertInput(uint Version, string? Reason, ProjectStatus? ExpectedStatus = null);

/// <summary>
/// Projeto de serviço (EXE-01a): iniciar, registrar a entrega, registrar e desfazer o aceite do cliente, pela tabela de transições, com versão e
/// situação esperada, data retroativa controlada e o <c>contractId</c> dos eventos sempre lido do projeto. O projeto de fabricação segue o
/// <see cref="ProjectService.RegisterDeliveryAsync"/> como antes (Konebras sem mudança).
/// </summary>
public sealed class ProjectExecutionService(
    ICommercialDb db, ProjectService projects, CommercialParametersService parameters, IEventPublisher events, IPermissionChecker permissions, ICurrentUser currentUser,
    IReceivables receivables, IOutgoingInvoices invoices, AttachmentService attachments, AuditOrigin origin, TimeProvider clock)
{
    public const string AcceptanceAttachmentOwner = "ProjectAcceptance";
    public const int BackdateDays = 30;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<Result<ProjectDetail>> StartAsync(Guid id, ProjectStartInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var project = loaded.Value;
        if (project.Status == ProjectStatus.InProgress && project.StartedOn == input.Date)
        {
            return await projects.GetAsync(id, cancellationToken);
        }

        if (await CheckAsync(project, input.Version, input.ExpectedStatus, ServiceAction.Start, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await DateErrorAsync(input.Date, input.BackdateReason, cancellationToken) is { } dateError)
        {
            return dateError;
        }

        Backdate(input.Date, input.BackdateReason);
        project.Status = ProjectStatus.InProgress;
        project.StartedOn = input.Date;
        events.Publish(new ProjectStarted(project.Id, project.ContractId, input.Date));
        return await SaveAsync(project, cancellationToken);
    }

    /// <summary>
    /// Entrega (EXE-01a CA 11 e 12): vai a <c>Delivered</c> (de <c>Planned</c>, o início fica na data da entrega). Com marco antigo "fabricação concluída"
    /// pendente, publica antes o <c>ProjectFabricated</c> de compatibilidade, uma vez só. "Também aceitou" exige <c>projects.register_acceptance</c>.
    /// </summary>
    public async Task<Result<ProjectDetail>> RegisterDeliveryAsync(Guid id, ProjectDeliveryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var kind = await db.Projects.AsNoTracking().Where(p => p.Id == id).Select(p => (ExecutionKind?)p.ExecutionKind).FirstOrDefaultAsync(cancellationToken);
        if (kind is null)
        {
            return ProjectService.NotFound();
        }

        if (kind == ExecutionKind.Fabrication)
        {
            return await projects.RegisterDeliveryAsync(id, new ProjectEventInput(input.Date, input.Version), cancellationToken);
        }

        var loaded = await LoadAsync(id, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var project = loaded.Value;
        if (project.Status == ProjectStatus.Delivered && project.DeliveredOn == input.Date && (!input.AlsoAccept || project.AcceptedOn == input.Date))
        {
            return await projects.GetAsync(id, cancellationToken);
        }

        if (await CheckAsync(project, input.Version, input.ExpectedStatus, ServiceAction.Deliver, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await DateErrorAsync(input.Date, input.BackdateReason, cancellationToken) is { } dateError)
        {
            return dateError;
        }

        if (project.StartedOn is { } started && input.Date < started)
        {
            return QuoteService.Invalid("date", $"A data não pode ser antes do início ({started.ToString("dd/MM/yyyy", PtBr)}).");
        }

        var name = input.AcceptedByName?.Trim();
        var role = string.IsNullOrWhiteSpace(input.AcceptedByRole) ? null : input.AcceptedByRole.Trim();
        if (input.AlsoAccept)
        {
            if (!await permissions.HasPermissionAsync("projects", "register_acceptance", cancellationToken))
            {
                return Error.Forbidden(Error.Codes.Forbidden, "Registrar o aceite do cliente exige a permissão própria. Fale com o administrador.");
            }

            if (await parameters.AcceptanceSegregationAsync(cancellationToken))
            {
                return SegregationError();
            }

            if (NameError(name, role) is { } nameError)
            {
                return nameError;
            }
        }

        Backdate(input.Date, input.BackdateReason);
        // CA 12: contrato antigo de serviço com marco "fabricação concluída" ainda não liberado: a entrega libera, uma vez só.
        var triggers = await projects.MilestoneTriggersAsync(project.ContractId, cancellationToken);
        if (project.FabricationCompatOn is null && triggers.Contains(TriggerEvent.FabricationDone))
        {
            project.FabricationCompatOn = input.Date;
            events.Publish(new ProjectFabricated(project.Id, project.ContractId, input.Date));
        }

        project.StartedOn ??= input.Date;
        project.Status = ProjectStatus.Delivered;
        project.DeliveredOn = input.Date;
        project.DeliveredManually = true;
        project.DeliveredBy = currentUser.UserId;
        events.Publish(new ProjectDelivered(project.Id, project.ContractId, input.Date));
        if (input.AlsoAccept)
        {
            var version = await NextVersionAsync(project.Id, cancellationToken);
            db.ProjectAcceptances.Add(new ProjectAcceptance
            {
                ProjectId = project.Id, Version = version, Kind = AcceptanceKind.FromDelivery, OccurredOn = input.Date, RecordedAt = clock.GetUtcNow(), RecordedBy = currentUser.UserId,
                AcceptedByName = name, AcceptedByRole = role, BackdateReason = Reason(input.BackdateReason),
            });
            project.AcceptedOn = input.Date;
            events.Publish(new TechnicalAcceptance(project.Id, project.ContractId, input.Date, version));
        }

        return await SaveAsync(project, cancellationToken);
    }

    /// <summary>Aceite do cliente (EXE-01a CA 13, 14 e 17): versão = última + 1; data não antes da entrega; anexo do próprio projeto e limpo.</summary>
    public async Task<Result<ProjectDetail>> RegisterAcceptanceAsync(Guid id, ProjectAcceptanceInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var project = loaded.Value;
        var name = input.AcceptedByName?.Trim();
        var role = string.IsNullOrWhiteSpace(input.AcceptedByRole) ? null : input.AcceptedByRole.Trim();
        var active = await db.ProjectAcceptances.AsNoTracking().FirstOrDefaultAsync(a => a.ProjectId == id && a.RevertedAt == null, cancellationToken);
        if (active is not null)
        {
            return active.OccurredOn == input.Date && string.Equals(active.AcceptedByName, name, StringComparison.Ordinal)
                ? await projects.GetAsync(id, cancellationToken)
                : Error.Conflict("already_accepted", $"O aceite já foi registrado em {active.OccurredOn.ToString("dd/MM/yyyy", PtBr)}.");
        }

        if (await CheckAsync(project, input.Version, input.ExpectedStatus, ServiceAction.Accept, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await DateErrorAsync(input.Date, input.BackdateReason, cancellationToken) is { } dateError)
        {
            return dateError;
        }

        if (project.DeliveredOn is { } delivered && input.Date < delivered)
        {
            return QuoteService.Invalid("date", $"O aceite não pode ser antes da entrega ({delivered.ToString("dd/MM/yyyy", PtBr)}).");
        }

        if (NameError(name, role) is { } nameError)
        {
            return nameError;
        }

        if (input.AttachmentId is { } attachmentId && !await AttachmentOkAsync(attachmentId, project.Id, cancellationToken))
        {
            return QuoteService.Invalid("attachmentId", "Anexe o termo de aceite deste projeto (PDF ou imagem já verificado pelo antivírus).");
        }

        if (await parameters.AcceptanceSegregationAsync(cancellationToken) && project.DeliveredBy is { } deliverer && deliverer == currentUser.UserId)
        {
            return SegregationError();
        }

        Backdate(input.Date, input.BackdateReason);
        var version = await NextVersionAsync(project.Id, cancellationToken);
        db.ProjectAcceptances.Add(new ProjectAcceptance
        {
            ProjectId = project.Id, Version = version, Kind = AcceptanceKind.Manual, OccurredOn = input.Date, RecordedAt = clock.GetUtcNow(), RecordedBy = currentUser.UserId,
            AcceptedByName = name, AcceptedByRole = role, AttachmentId = input.AttachmentId, BackdateReason = Reason(input.BackdateReason),
        });
        project.AcceptedOn = input.Date;
        events.Publish(new TechnicalAcceptance(project.Id, project.ContractId, input.Date, version));
        return await SaveAsync(project, cancellationToken);
    }

    public const string AcceptanceLockedReceivable = "A parcela já foi recebida ou cobrada. Para corrigir, use um aditivo.";
    public const string AcceptanceLockedInvoice = "Já há nota emitida depois do aceite. Para corrigir, use um aditivo.";

    /// <summary>
    /// Desfazer o aceite (EXE-01a CA 15 e 16): pelo contrato público do Finance (nenhuma parcela de aceite com baixa, nem parcial, nem boleto) e do
    /// Fiscal (nenhuma NF autorizada do projeto na data do aceite ou depois). Projeto encerrado ou contrato concluído: 409 <c>project_closed</c>.
    /// </summary>
    public async Task<Result<ProjectDetail>> RevertAcceptanceAsync(Guid id, ProjectAcceptanceRevertInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(id, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var project = loaded.Value;
        var contractStatus = await db.Contracts.Where(c => c.Id == project.ContractId).Select(c => c.Status).FirstAsync(cancellationToken);
        if (project.Status == ProjectStatus.Closed || contractStatus == ContractStatus.Completed)
        {
            return Error.Conflict("project_closed", "Projeto encerrado: o aceite não é desfeito. Use um aditivo.");
        }

        var active = await db.ProjectAcceptances.FirstOrDefaultAsync(a => a.ProjectId == id && a.RevertedAt == null, cancellationToken);
        if (active is null && project.Status == ProjectStatus.Delivered
            && await db.ProjectAcceptances.AnyAsync(a => a.ProjectId == id && a.RevertedAt != null, cancellationToken))
        {
            // Pedido já cumprido (o mesmo desfazer de novo): devolve o projeto atual.
            return await projects.GetAsync(id, cancellationToken);
        }

        if (await CheckAsync(project, input.Version, input.ExpectedStatus, ServiceAction.RevertAcceptance, cancellationToken) is { } refused)
        {
            return refused;
        }

        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > ProjectAcceptance.ReasonMaxLength)
        {
            return QuoteService.Invalid("reason", "Informe o motivo, com até 200 caracteres.");
        }

        if (await LockedReasonAsync(project, active!, cancellationToken) is { } locked)
        {
            return Error.Conflict("acceptance_locked", locked);
        }

        origin.Label = "Aceite desfeito: " + reason;
        active!.RevertedAt = clock.GetUtcNow();
        active.RevertedBy = currentUser.UserId;
        active.RevertReason = reason;
        project.AcceptedOn = null;
        events.Publish(new TechnicalAcceptanceReverted(project.Id, project.ContractId, active.OccurredOn, active.Version));
        return await SaveAsync(project, cancellationToken);
    }

    /// <summary>Motivo do 409 <c>acceptance_locked</c> ou null: parcelas de gatilho "aceite" (contrato e aditivos aprovados) e as NF do projeto.</summary>
    private async Task<string?> LockedReasonAsync(Project project, ProjectAcceptance acceptance, CancellationToken ct)
    {
        var own = await db.ContractMilestones.AsNoTracking().Where(m => m.ContractId == project.ContractId && m.TriggerEvent == TriggerEvent.TechnicalAcceptance)
            .Select(m => m.Id).ToListAsync(ct);
        var amended = await (from m in db.ContractAmendmentMilestones.AsNoTracking()
                             join a in db.ContractAmendments.AsNoTracking() on m.AmendmentId equals a.Id
                             where a.ContractId == project.ContractId && a.Status == AmendmentStatus.Approved && m.TriggerEvent == TriggerEvent.TechnicalAcceptance
                             select m.Id).ToListAsync(ct);
        var milestones = own.Concat(amended).ToHashSet();
        if (milestones.Count > 0)
        {
            var parcels = (await receivables.ForContractAsync(project.ContractId, ct)).Where(r => milestones.Contains(r.MilestoneId) && r.Status != ReceivableStatus.Cancelled);
            if (parcels.Any(r => r.HasSettlement || r.HasBillingSlip || r.Status is ReceivableStatus.PartiallyPaid or ReceivableStatus.Paid))
            {
                return AcceptanceLockedReceivable;
            }
        }

        var notes = await invoices.ByProjectAsync(project.Id, ct);
        return notes.Any(n => n.Status == "Authorized" && n.IssuedOn is { } issued && issued >= acceptance.OccurredOn) ? AcceptanceLockedInvoice : null;
    }

    /// <summary>Projeto do tenant (404 se não existe), só de serviço (409) e com o recurso por perfil (403 <c>module_not_released</c>, CA 4 e 22).</summary>
    private async Task<Result<Project>> LoadAsync(Guid id, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
        {
            return ProjectService.NotFound();
        }

        if (!projects.ServiceExecutionReleased)
        {
            return Error.Forbidden(Error.Codes.ModuleNotReleased, "Funcionalidade não liberada nessa versão do sistema.");
        }

        if (project.ExecutionKind != ExecutionKind.Service)
        {
            return Error.Conflict("not_a_service_project", "Este passo é do projeto de serviço. No projeto de fabricação, a entrega segue a fabricação e a instalação.");
        }

        return project;
    }

    /// <summary>Versão e situação esperada (CA 23), tabela de transições e contrato ativo; null quando pode seguir.</summary>
    private async Task<Error?> CheckAsync(Project project, uint version, ProjectStatus? expected, ServiceAction action, CancellationToken ct)
    {
        if (expected is null)
        {
            return QuoteService.Invalid("expectedStatus", "Informe a situação do projeto que você viu na tela.");
        }

        if (project.Version != version || project.Status != expected)
        {
            return Error.Conflict("concurrency_conflict", "O projeto mudou desde que você abriu. Recarregue.");
        }

        var accepted = project.AcceptedOn is not null || await db.ProjectAcceptances.AnyAsync(a => a.ProjectId == project.Id && a.RevertedAt == null, ct);
        if (!ServiceTransitions.Allows(project.Status, action, accepted))
        {
            return Error.Conflict("invalid_project_transition", ServiceTransitions.Refusal(project.Status, accepted, action));
        }

        if (await db.Contracts.Where(c => c.Id == project.ContractId).Select(c => c.Status).FirstAsync(ct) != ContractStatus.Active)
        {
            return Error.Conflict("contract_not_active", "O contrato precisa estar ativo para registrar este passo.");
        }

        db.Entry(project).Property(p => p.Version).OriginalValue = version;
        return null;
    }

    /// <summary>
    /// Data do fato (CA 24): futura, 422; antes de hoje, motivo obrigatório (até 200); mais de 30 dias, também <c>finance.approve</c> (403).
    /// </summary>
    private async Task<Error?> DateErrorAsync(DateOnly date, string? backdateReason, CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        if (date > today)
        {
            return QuoteService.Invalid("date", "A data não pode ser depois de hoje.");
        }

        if (date == today)
        {
            return null;
        }

        var reason = backdateReason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > ProjectAcceptance.ReasonMaxLength)
        {
            return QuoteService.Invalid("backdateReason", "Data retroativa: informe o motivo, com até 200 caracteres.");
        }

        return date < today.AddDays(-BackdateDays) && !await permissions.HasPermissionAsync("finance", "approve", ct)
            ? Error.Forbidden(Error.Codes.Forbidden, "Data com mais de 30 dias exige a permissão de aprovar no financeiro.")
            : null;
    }

    /// <summary>A auditoria mostra a data do fato, a diferença em dias para o registro e o motivo (CA 24).</summary>
    private void Backdate(DateOnly date, string? backdateReason)
    {
        var days = BusinessTime.Today(clock).DayNumber - date.DayNumber;
        if (days > 0)
        {
            origin.Label = $"Data do fato {date.ToString("dd/MM/yyyy", PtBr)}, {days} {(days == 1 ? "dia" : "dias")} antes do registro · Motivo: {backdateReason?.Trim()}";
        }
    }

    private static string? Reason(string? backdateReason) => string.IsNullOrWhiteSpace(backdateReason) ? null : backdateReason.Trim();

    private static Error? NameError(string? name, string? role)
    {
        if (string.IsNullOrEmpty(name) || name.Length > ProjectAcceptance.NameMaxLength)
        {
            return QuoteService.Invalid("acceptedByName", "Informe quem aceitou, com até 120 caracteres.");
        }

        return role is { Length: > ProjectAcceptance.RoleMaxLength } ? QuoteService.Invalid("acceptedByRole", "O cargo ou papel vai até 80 caracteres.") : null;
    }

    private static Error SegregationError() =>
        Error.Conflict("acceptance_segregation", "Quem registrou a entrega não pode registrar o aceite. Peça a outra pessoa.");

    private async Task<int> NextVersionAsync(Guid projectId, CancellationToken ct) =>
        (await db.ProjectAcceptances.Where(a => a.ProjectId == projectId).MaxAsync(a => (int?)a.Version, ct) ?? 0) + 1;

    /// <summary>IDOR do anexo (CA 25): do tenant (banco do tenant), dono <c>ProjectAcceptance</c> com o projeto da rota e limpo no antivírus.</summary>
    private async Task<bool> AttachmentOkAsync(Guid attachmentId, Guid projectId, CancellationToken ct) =>
        await attachments.FindAsync(attachmentId, ct) is { } file && file.OwnerType == AcceptanceAttachmentOwner && file.OwnerId == projectId
            && file.ScanStatus == ScanStatus.Clean && file.ContentRemovedAt is null;

    /// <summary>Grava; versão do projeto mudada ou aceite vigente gravado em paralelo (índice parcial) = 409 (CA 23, duplo clique).</summary>
    private async Task<Result<ProjectDetail>> SaveAsync(Project project, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency_conflict", "O projeto mudou desde que você abriu. Recarregue.");
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Error.Conflict("concurrency_conflict", "O projeto mudou desde que você abriu. Recarregue.");
        }

        return await projects.GetAsync(project.Id, ct);
    }
}
