using System.Globalization;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Autoaprovação da etapa (EXE-03b CA 5), com a codificação e as regras da FIN-10a (<c>PaymentOrderRules.CanDecide</c>): quem não concluiu
/// aprova; quem concluiu, só se o modo deixar (Qualquer aprovador; Só o Proprietário, se for do Proprietário; Nunca, só o Proprietário único).
/// </summary>
public static class StageApprovalRules
{
    public const string SelfRefusal = "Você concluiu esta etapa: outra pessoa com permissão de aprovar projetos aprova.";

    public static (bool Allowed, bool Self) CanApprove(bool isCompleter, bool isOwner, int ownerMembers, SelfApprovalMode mode)
    {
        if (!isCompleter)
        {
            return (true, false);
        }

        var allowed = mode switch
        {
            SelfApprovalMode.AnyApprover => true,
            SelfApprovalMode.OwnerOnly => isOwner,
            _ => isOwner && ownerMembers == 1,
        };
        return (allowed, allowed);
    }
}

public sealed partial class ProjectStageService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// "Iniciar etapa" (CA 1): <c>Ready → InProgress</c>. Com o projeto <c>Planned</c>, a mesma transação chama o
    /// <see cref="ProjectExecutionService.StartAsync"/> (mesmas regras, o <c>ProjectStarted</c>).
    /// </summary>
    public async Task<Result<ProjectStagesView>> StartStageAsync(Guid id, Guid stageId, ProjectStageStartInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var loaded = await CommandAsync(id, stageId, input.Version, StageCommand.Start, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (project, stage) = loaded.Value;
        stage.Status = ProjectStageStatus.InProgress;
        stage.StartedAt = Now;
        if (project.Status == ProjectStatus.Planned)
        {
            var started = await execution.StartAsync(id, new ProjectStartInput(input.Date ?? BusinessTime.Today(clock), input.Version, input.ExpectedStatus, input.BackdateReason),
                cancellationToken);
            if (started.IsFailure)
            {
                return started.Error!;
            }
        }

        return await CommitAsync(transaction, project, cancellationToken);
    }

    /// <summary>Marcar ou desmarcar um item do checklist (CA 2), com a etapa em andamento; item de outra etapa ou projeto, 404 (IDOR).</summary>
    public async Task<Result<ProjectStagesView>> MarkAsync(Guid id, Guid stageId, int sequence, ProjectStageCheckInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Result is { } asked && !Enum.IsDefined(asked))
        {
            return QuoteService.Invalid("result", "Escolha Feito ou Não se aplica.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var loaded = await CommandAsync(id, stageId, input.Version, StageCommand.Mark, cancellationToken, checkFirst: sequence);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (project, stage) = loaded.Value;
        var check = await db.ProjectServiceStageChecks.FirstAsync(c => c.StageId == stage.Id && c.Sequence == sequence, cancellationToken);
        check.Result = input.Result;
        check.MarkedAt = Now;
        check.MarkedBy = currentUser.UserId;
        return await CommitAsync(transaction, project, cancellationToken);
    }

    /// <summary>
    /// "Concluir etapa" (CA 3 a 7): checklist completo; permissão pelo papel e pelo marco; data até hoje (Entrega, Aceite e marco com as regras
    /// da EXE-01a). Com "Exige aprovação", vai a <c>AwaitingApproval</c>, salvo quando quem conclui pode se autoaprovar (selo "Autoaprovada").
    /// </summary>
    public async Task<Result<ProjectStagesView>> CompleteStageAsync(Guid id, Guid stageId, ProjectStageCompleteInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var loaded = await CommandAsync(id, stageId, input.Version, StageCommand.Complete, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (project, stage) = loaded.Value;
        var results = await db.ProjectServiceStageChecks.Where(c => c.StageId == stage.Id).Select(c => c.Result).ToListAsync(cancellationToken);
        if (!ServiceStageFlow.ChecklistDone(results))
        {
            return Error.Conflict("stage_checklist_incomplete", ServiceStageFlow.ChecklistIncomplete);
        }

        var hasMilestone = await HasMilestoneAsync(project.Id, stage.Code, cancellationToken);
        if (!await permissions.HasPermissionAsync("projects", ServiceStageFlow.CompletionAction(stage.Role, hasMilestone), cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, ServiceStageFlow.CompletionRefusal(stage.Role, hasMilestone));
        }

        if (await DateErrorAsync(project, stage, input.Date, input.ExpectedStatus, input.BackdateReason, hasMilestone, cancellationToken) is { } dateError)
        {
            return dateError;
        }

        var approvedNow = false;
        var self = false;
        if (stage.RequiresApproval && await permissions.HasPermissionAsync("projects", "approve", cancellationToken))
        {
            (approvedNow, self) = await SelfApprovalAsync(isCompleter: true, cancellationToken);
        }

        stage.CompletedOn = input.Date;
        stage.CompletedAt = Now;
        stage.CompletedBy = currentUser.UserId;
        if (ServiceStageFlow.AfterComplete(stage.RequiresApproval, approvedNow) == ProjectStageStatus.AwaitingApproval)
        {
            stage.Status = ProjectStageStatus.AwaitingApproval;
            return await CommitAsync(transaction, project, cancellationToken);
        }

        if (stage.RequiresApproval)
        {
            stage.ApprovedBy = currentUser.UserId;
            stage.SelfApproved = self;
        }

        var finished = await FinishAsync(project, stage, new Effect(input.Date, input.ExpectedStatus, input.BackdateReason, input.AcceptedByName, input.AcceptedByRole, input.AttachmentId),
            hasMilestone, cancellationToken);
        return finished ?? await CommitAsync(transaction, project, cancellationToken);
    }

    /// <summary>
    /// "Aprovar etapa" (CA 5): <c>AwaitingApproval → Done</c> com <c>projects.approve</c> (na rota). Quem concluiu só aprova se o parâmetro
    /// <c>projects.stage_self_approval</c> deixar; aí ganha o selo "Autoaprovada". A data é a da conclusão.
    /// </summary>
    public async Task<Result<ProjectStagesView>> ApproveStageAsync(Guid id, Guid stageId, ProjectStageApproveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var loaded = await CommandAsync(id, stageId, input.Version, StageCommand.Approve, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (project, stage) = loaded.Value;
        var (allowed, self) = await SelfApprovalAsync(stage.CompletedBy is { } by && by == currentUser.UserId, cancellationToken);
        if (!allowed)
        {
            return Error.Forbidden(Error.Codes.Forbidden, StageApprovalRules.SelfRefusal);
        }

        var hasMilestone = await HasMilestoneAsync(project.Id, stage.Code, cancellationToken);
        var date = stage.CompletedOn ?? BusinessTime.Today(clock);
        // Aceite exige também a permissão própria de quem registra o aceite (EXE-01a), mesmo aprovando.
        if (stage.Role == StageRole.Acceptance && !await permissions.HasPermissionAsync("projects", "register_acceptance", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, ServiceStageFlow.AcceptanceRefusal);
        }

        if (await DateErrorAsync(project, stage, date, input.ExpectedStatus, input.BackdateReason, hasMilestone, cancellationToken) is { } dateError)
        {
            return dateError;
        }

        stage.ApprovedBy = currentUser.UserId;
        stage.SelfApproved = self;
        var finished = await FinishAsync(project, stage, new Effect(date, input.ExpectedStatus, input.BackdateReason, input.AcceptedByName, input.AcceptedByRole, input.AttachmentId),
            hasMilestone, cancellationToken);
        return finished ?? await CommitAsync(transaction, project, cancellationToken);
    }

    private sealed record Effect(DateOnly Date, ProjectStatus? ExpectedStatus, string? BackdateReason, string? AcceptedByName, string? AcceptedByRole, Guid? AttachmentId);

    /// <summary>
    /// Etapa a <c>Done</c> (CA 3, 6 e 7): <c>completion_version</c> + 1, a próxima <c>Waiting</c> fica <c>Ready</c> e publica o
    /// <see cref="ProjectStageCompleted"/>. Entrega e Aceite passam pelos serviços da EXE-01a na mesma transação (mesmos eventos); projeto já entregue
    /// (ou já aceito) não registra de novo. Null quando pode gravar.
    /// </summary>
    private async Task<Error?> FinishAsync(Project project, ProjectServiceStage stage, Effect effect, bool hasMilestone, CancellationToken ct)
    {
        stage.Status = ProjectStageStatus.Done;
        stage.CompletionVersion++;
        var next = await db.ProjectServiceStages.Where(s => s.ProjectId == project.Id && s.Sequence > stage.Sequence).OrderBy(s => s.Sequence).FirstOrDefaultAsync(ct);
        if (next is { Status: ProjectStageStatus.Waiting })
        {
            next.Status = ProjectStageStatus.Ready;
        }

        events.Publish(new ProjectStageCompleted(project.Id, project.ContractId, stage.Code, stage.CompletionVersion, effect.Date));
        if (hasMilestone && stage.Role == StageRole.Execution)
        {
            execution.Backdate(effect.Date, effect.BackdateReason);
        }

        if (stage.Role == StageRole.Delivery && project.Status is ProjectStatus.Planned or ProjectStatus.InProgress)
        {
            var delivered = await execution.RegisterDeliveryAsync(project.Id, new ProjectDeliveryInput(effect.Date, project.Version, effect.ExpectedStatus, effect.BackdateReason), ct);
            return delivered.IsFailure ? delivered.Error : null;
        }

        if (stage.Role == StageRole.Acceptance && project.AcceptedOn is null)
        {
            var accepted = await execution.RegisterAcceptanceAsync(project.Id,
                new ProjectAcceptanceInput(effect.Date, project.Version, effect.AcceptedByName, effect.ExpectedStatus, effect.AcceptedByRole, effect.AttachmentId, effect.BackdateReason), ct);
            return accepted.IsFailure ? accepted.Error : null;
        }

        return null;
    }

    /// <summary>
    /// Data do fato (CA 3 e 4): depois de hoje, 422; antes do início da etapa, 422. Entrega, Aceite e marco: <c>expectedStatus</c> obrigatório e
    /// igual ao do projeto (409) e a regra retroativa da EXE-01a (motivo; acima de 30 dias, <c>finance.approve</c>).
    /// </summary>
    private async Task<Error?> DateErrorAsync(Project project, ProjectServiceStage stage, DateOnly date, ProjectStatus? expected, string? backdateReason, bool hasMilestone,
        CancellationToken ct)
    {
        if (date > BusinessTime.Today(clock))
        {
            return QuoteService.Invalid("date", "A data não pode ser depois de hoje.");
        }

        if (stage.StartedAt is { } startedAt && DateOnly.FromDateTime(BusinessTime.ToBusiness(startedAt).DateTime) is var startedOn && date < startedOn)
        {
            return QuoteService.Invalid("date", $"A data não pode ser antes do início da etapa ({startedOn.ToString("dd/MM/yyyy", PtBr)}).");
        }

        if (!ServiceStageFlow.Guarded(stage.Role, hasMilestone))
        {
            return null;
        }

        if (expected is null)
        {
            return QuoteService.Invalid("expectedStatus", "Informe a situação do projeto que você viu na tela.");
        }

        return expected != project.Status ? Stale() : await execution.DateErrorAsync(date, backdateReason, ct);
    }

    private async Task<(bool Allowed, bool Self)> SelfApprovalAsync(bool isCompleter, CancellationToken ct)
    {
        if (!isCompleter)
        {
            return (true, false);
        }

        var owners = await users.OwnerIdsAsync(ct);
        var mode = await parameters.StageSelfApprovalAsync(ct);
        return StageApprovalRules.CanApprove(true, currentUser.UserId is { } me && owners.Contains(me), owners.Count, mode);
    }

    private async Task<bool> HasMilestoneAsync(Guid projectId, string code, CancellationToken ct) =>
        (await milestoneGuard.MilestonesForAsync(projectId, [code], ct)).Count > 0;

    /// <summary>
    /// Todo comando de etapa (CA 9): trava a linha do projeto (<c>SELECT … FOR UPDATE</c>), confere recurso, tipo, versão, situação do projeto,
    /// etapa do projeto (404, IDOR) e o item do checklist (404), e a máquina da etapa (409). Quem chama grava a versão nova do projeto.
    /// </summary>
    private async Task<Result<(Project Project, ProjectServiceStage Stage)>> CommandAsync(Guid id, Guid stageId, uint version, StageCommand command, CancellationToken ct,
        int? checkFirst = null)
    {
        // Trava a linha (FOR UPDATE) e já a carrega rastreada, com o xmin de depois da trava; o LoadAsync abaixo devolve a mesma instância.
        await db.Projects.FromSql($"SELECT p.*, p.xmin FROM commercial.projects p WHERE p.id = {id} FOR UPDATE").FirstOrDefaultAsync(ct);
        var loaded = await LoadAsync(id, tracking: true, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var project = loaded.Value;
        var stage = await db.ProjectServiceStages.FirstOrDefaultAsync(s => s.Id == stageId && s.ProjectId == id, ct);
        if (stage is null)
        {
            return StageNotFound();
        }

        if (checkFirst is { } sequence && !await db.ProjectServiceStageChecks.AnyAsync(c => c.StageId == stage.Id && c.Sequence == sequence, ct))
        {
            return Error.NotFound("project_stage_check_not_found", "Item do checklist não encontrado nesta etapa.");
        }

        if (project.Version != version)
        {
            return Stale();
        }

        if (!ServiceStagePlan.AcceptsStages(project.Status))
        {
            return Error.Conflict("project_stages_locked", ServiceStagePlan.ClosedRefusal);
        }

        if (!ServiceStageFlow.Allows(stage.Status, command))
        {
            return Error.Conflict("invalid_stage_transition", ServiceStageFlow.Refusal(command));
        }

        // A versão do projeto (xmin) sobe na mesma gravação: o comando paralelo que leu a mesma versão recebe 409.
        project.StagesChangedAt = Now;
        db.Entry(project).Property(p => p.Version).OriginalValue = version;
        return (project, stage);
    }

    private async Task<Result<ProjectStagesView>> CommitAsync(IDbContextTransaction transaction, Project project, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || DbErrors.IsUniqueViolation(ex))
        {
            return Stale();
        }

        await transaction.CommitAsync(ct);
        var reloaded = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == project.Id, ct);
        return await ViewAsync(reloaded, ct);
    }
}
