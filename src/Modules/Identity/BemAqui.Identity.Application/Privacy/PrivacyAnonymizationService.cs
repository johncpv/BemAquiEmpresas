using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Identity.Application.Privacy;

/// <summary>Argumentos da anonimização (CA 4, §4.3 da revisão): só ids, nunca dado do titular.</summary>
public sealed record PrivacyAnonymizationArgs(Guid PrivacyRequestId, Guid RequestedBy);

/// <summary>
/// Anonimização do titular (LGPD-01b, ADR 0018 e 0037): orquestra os módulos pelo contrato <see cref="IPersonalDataProvider"/>, sem ler nem gravar
/// tabela de outro módulo. Só o Proprietário (403 nos demais), com a identidade conferida, o nome do titular digitado (padrão 7) e o 2FA de novo
/// (D6, 403 <c>mfa_step_up_required</c>). A anonimização roda num job com o <c>tenantId</c>, <b>uma transação por módulo</b> (um escopo novo por
/// módulo, com a auditoria em modo <c>Anonymized</c>), e grava o resultado de cada etapa no pedido; o módulo dono do cadastro roda por último,
/// para uma retentativa ainda achar o titular. Falhou um módulo: "parcial", e "Tentar de novo" refaz só o que falta. Irreversível.
/// </summary>
public sealed partial class PrivacyAnonymizationService(
    IIdentityDb db,
    IEnumerable<IPersonalDataProvider> providers,
    PrivacyRequestService requests,
    RetentionRuleService retention,
    IPermissionService permissions,
    IStepUpTwoFactor stepUp,
    ICurrentUser currentUser,
    INotificationService notifications,
    IEventPublisher events,
    AuditOrigin origin,
    IBackgroundJobClient jobs,
    ITenantContext tenant,
    ITenantScopeFactory scopes,
    TimeProvider clock,
    ILogger<PrivacyAnonymizationService> logger)
{
    public const string RunningCode = "privacy_anonymization_running";
    public const string StepUpMessage = "Confirme com o código do aplicativo autenticador para anonimizar.";
    public const string DoneMessage = "Dados anonimizados";

    /// <summary>"Prever anonimização" (CA 2 e 3): cada módulo responde pelo contrato; nada é gravado além do registro da prévia no histórico.</summary>
    public async Task<Result<PrivacyAnonymizationPreview>> PreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await requests.OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        var request = await db.PrivacyRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        if (Check(request) is { } invalid)
        {
            return invalid;
        }

        var subject = await requests.ResolveAsync(request.SubjectType, request.SubjectId!.Value, cancellationToken);
        if (subject is null)
        {
            return Error.Conflict("privacy_subject_not_found", "O cadastro do titular não existe mais.");
        }

        var context = await ContextAsync(subject, request.Number, providers, cancellationToken);
        var plans = new List<PersonalDataAnonymizationPlan>();
        foreach (var provider in Ordered(providers, subject))
        {
            if (await provider.PlanAnonymizationAsync(context, cancellationToken) is { } plan && (plan.Items.Count > 0 || plan.Blocker is not null))
            {
                plans.Add(plan);
            }
        }

        var blockers = Blockers(plans, subject);
        RecordAudit(request.Id, AuditActions.AnonymizationPreviewed, Summary(plans));
        await db.SaveChangesAsync(cancellationToken);
        return new PrivacyAnonymizationPreview(request.Id, request.Number, request.SubjectName, context.Marker, plans, blockers,
            request.Kind == PrivacyRequestKind.Deletion ? PrivacyRequestRules.DeletionNotice : null, PrivacyRequestRules.BackupNotice, request.Version);
    }

    /// <summary>
    /// "Anonimizar" e "Tentar de novo" (CA 4, 5 e 8): o nome do titular digitado igual ao do pedido, o 2FA de novo conferido antes de qualquer
    /// alteração e nenhum impedimento na prévia; o pedido fica "Anonimizando" e o job (só ids) roda os módulos que faltam.
    /// </summary>
    public async Task<Result<PrivacyAnonymizationStarted>> StartAsync(Guid id, AnonymizePrivacySubjectInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await requests.OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        var request = await db.PrivacyRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return NotFound();
        }

        if (Check(request) is { } invalid)
        {
            return invalid;
        }

        if (!string.Equals((input.Confirmation ?? string.Empty).Trim(), request.SubjectName.Trim(), StringComparison.Ordinal))
        {
            const string message = "Digite o nome do titular exatamente como aparece no pedido para confirmar.";
            return Error.Validation("validation", message, new Dictionary<string, string[]> { ["confirmation"] = [message] });
        }

        var subject = await requests.ResolveAsync(request.SubjectType, request.SubjectId!.Value, cancellationToken);
        if (subject is null)
        {
            return Error.Conflict("privacy_subject_not_found", "O cadastro do titular não existe mais.");
        }

        var context = await ContextAsync(subject, request.Number, providers, cancellationToken);
        var plans = new List<PersonalDataAnonymizationPlan>();
        foreach (var provider in Ordered(providers, subject))
        {
            if (await provider.PlanAnonymizationAsync(context, cancellationToken) is { } plan)
            {
                plans.Add(plan);
            }
        }

        if (Blockers(plans, subject) is { Count: > 0 } blockers)
        {
            return Error.Conflict("privacy_anonymization_blocked", blockers[0]);
        }

        // 2FA de novo (D6) antes de mexer em entidade rastreada: o IStepUpTwoFactor grava no mesmo contexto.
        if (currentUser.UserId is not { } me || !await stepUp.VerifyAsync(me, input.TwoFactorCode, cancellationToken))
        {
            return Error.Forbidden("mfa_step_up_required", StepUpMessage);
        }

        var tracked = await db.PrivacyRequests.FirstAsync(r => r.Id == id, cancellationToken);
        db.Entry(tracked).Property(r => r.Version).OriginalValue = input.Version;
        var steps = PrivacyAnonymizationJson.Steps(tracked.AnonymizationSteps);
        if (steps.Count == 0)
        {
            steps = Ordered(providers, subject).Select(p => new PrivacyAnonymizationStep(p.Module, p.Title, PrivacyAnonymizationStepStatus.Pending, null, null, [])).ToList();
        }

        var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Anonimização do titular fora do escopo de tenant.");
        tracked.AnonymizationStatus = PrivacyAnonymizationStatus.Running;
        tracked.AnonymizationSteps = PrivacyAnonymizationJson.Write(steps);
        tracked.AnonymizedBy = me;
        await db.SaveChangesAsync(cancellationToken);
        jobs.Create<PrivacyAnonymizationJob>(j => j.ExecuteAsync(tenantId, new PrivacyAnonymizationArgs(id, me), CancellationToken.None), new EnqueuedState(JobQueues.Default));
        return new PrivacyAnonymizationStarted(await requests.DetailAsync(tracked, cancellationToken), "Anonimizando… Avisaremos no sino quando terminar.");
    }

    /// <summary>
    /// O job (CA 4 e 5): confere que quem pediu continua no Proprietário, monta o contexto com a retenção e as datas base, e roda cada módulo que
    /// falta num escopo próprio, numa transação; grava a etapa no pedido. Tudo certo: "Dados anonimizados", o nome do titular mascarado nos
    /// pedidos dele, o <see cref="SubjectAnonymized"/> no outbox e o aviso no sino. Idempotente: etapa feita não roda de novo.
    /// </summary>
    public async Task RunAsync(Guid requestId, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        var request = await db.PrivacyRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null || request.AnonymizationStatus != PrivacyAnonymizationStatus.Running || request.SubjectId is not { } subjectId)
        {
            return;
        }

        var info = tenant.Tenant ?? throw new InvalidOperationException("Anonimização do titular fora do escopo de tenant.");
        if (!(await permissions.GetAsync(requestedBy, cancellationToken)).IsOwner)
        {
            await FinishPartialAsync(request, requestedBy, PrivacyAnonymizationJson.Steps(request.AnonymizationSteps), "Quem pediu não é mais do grupo Proprietário.", cancellationToken);
            return;
        }

        var subject = await requests.ResolveAsync(request.SubjectType, subjectId, cancellationToken);
        if (subject is null)
        {
            await FinishPartialAsync(request, requestedBy, PrivacyAnonymizationJson.Steps(request.AnonymizationSteps), "O cadastro do titular não existe mais.", cancellationToken);
            return;
        }

        var context = await ContextAsync(subject, request.Number, providers, cancellationToken);
        var steps = PrivacyAnonymizationJson.Steps(request.AnonymizationSteps);
        var failed = false;
        foreach (var provider in Ordered(providers, subject))
        {
            var index = steps.FindIndex(s => s.Module == provider.Module);
            if (index >= 0 && steps[index].Status == PrivacyAnonymizationStepStatus.Done)
            {
                continue;
            }

            // O dono do cadastro só roda com os outros módulos feitos: a retentativa ainda precisa achar o titular por ele.
            if (failed)
            {
                continue;
            }

            var step = await RunStepAsync(info, provider.Module, provider.Title, context, cancellationToken);
            if (index >= 0)
            {
                steps[index] = step;
            }
            else
            {
                steps.Add(step);
            }

            failed = step.Status == PrivacyAnonymizationStepStatus.Failed;
        }

        // O pedido pode ter mudado enquanto os módulos rodavam (xmin): recarrega antes de gravar.
        await db.Entry(request).ReloadAsync(cancellationToken);
        request.AnonymizationSteps = PrivacyAnonymizationJson.Write(steps);
        if (steps.Any(s => s.Status != PrivacyAnonymizationStepStatus.Done))
        {
            await FinishPartialAsync(request, requestedBy, steps, null, cancellationToken);
            return;
        }

        var summary = PrivacyAnonymizationJson.Summarize(steps);
        request.AnonymizationStatus = PrivacyAnonymizationStatus.Done;
        request.AnonymizationSummary = PrivacyAnonymizationJson.Write(summary);
        request.AnonymizedAt = clock.GetUtcNow();
        request.AnonymizedBy = requestedBy;
        foreach (var other in await db.PrivacyRequests.Where(r => r.SubjectType == request.SubjectType && r.SubjectId == subjectId).ToListAsync(cancellationToken))
        {
            MaskSubject(other, request.Number);
        }

        var identityRemoved = !context.Decide(context.IdentityGroup).Retained;
        events.Publish(new SubjectAnonymized(subject.Kind, subject.Id, subject.PartyId, subject.EmployeeId, request.Number, identityRemoved));
        notifications.Notify(requestedBy, PrivacyRequestService.NotificationType, $"{DoneMessage}: pedido {request.Number}.", PrivacyRequestService.Link(request.Id));
        RecordAudit(request.Id, AuditActions.Anonymized, summary, requestedBy);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Uma etapa: escopo novo do tenant (contexto limpo), auditoria em modo anonimização e uma transação só para o módulo.</summary>
    private async Task<PrivacyAnonymizationStep> RunStepAsync(TenantInfo info, string module, string title, PersonalDataAnonymizationContext context, CancellationToken ct)
    {
        try
        {
            await using var scope = await scopes.CreateScopeAsync(info, ct);
            var services = scope.ServiceProvider;
            var audit = services.GetRequiredService<AuditOrigin>();
            audit.Label = "Anonimização " + context.RequestNumber;
            audit.AnonymizationRequest = context.RequestNumber;
            var provider = services.GetServices<IPersonalDataProvider>().First(p => p.Module == module);
            var moduleDb = services.GetRequiredService<BemAquiDbContext>();
            await using var transaction = await moduleDb.Database.BeginTransactionAsync(ct);
            var result = await provider.AnonymizeAsync(context, ct);
            await moduleDb.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new PrivacyAnonymizationStep(module, title, PrivacyAnonymizationStepStatus.Done, clock.GetUtcNow(), null, result?.Items ?? []);
        }
#pragma warning disable CA1031 // A falha de um módulo vira a etapa "com erro" no pedido, sem derrubar o job (CA 5).
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogStepFailed(logger, ex, module, context.RequestNumber);
            return new PrivacyAnonymizationStep(module, title, PrivacyAnonymizationStepStatus.Failed, clock.GetUtcNow(), $"Não foi possível anonimizar os dados do módulo {title}.", []);
        }
    }

    private async Task FinishPartialAsync(PrivacyRequest request, Guid requestedBy, List<PrivacyAnonymizationStep> steps, string? reason, CancellationToken ct)
    {
        request.AnonymizationStatus = PrivacyAnonymizationStatus.Partial;
        if (reason is not null)
        {
            steps = steps.Select(s => s.Status == PrivacyAnonymizationStepStatus.Done ? s : s with { Status = PrivacyAnonymizationStepStatus.Failed, Error = reason, At = clock.GetUtcNow() }).ToList();
        }

        request.AnonymizationSteps = PrivacyAnonymizationJson.Write(steps);
        var ok = steps.Where(s => s.Status == PrivacyAnonymizationStepStatus.Done).Select(s => s.Title);
        var bad = steps.Where(s => s.Status != PrivacyAnonymizationStepStatus.Done).Select(s => s.Title);
        notifications.Notify(requestedBy, PrivacyRequestService.NotificationType,
            $"Anonimização parcial do pedido {request.Number}: {string.Join(", ", ok)} / {string.Join(", ", bad)}.", PrivacyRequestService.Link(request.Id));
        RecordAudit(request.Id, AuditActions.Anonymized, PrivacyAnonymizationJson.Summarize(steps), requestedBy, partial: true);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Contexto do titular: a tabela de retenção e as datas base de todos os módulos (a maior de cada chave).</summary>
    private async Task<PersonalDataAnonymizationContext> ContextAsync(PersonalDataSubject subject, string number, IEnumerable<IPersonalDataProvider> list, CancellationToken ct)
    {
        var anchors = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var provider in list)
        {
            foreach (var (key, date) in await provider.RetentionAnchorsAsync(subject, ct))
            {
                anchors[key] = anchors.TryGetValue(key, out var current) && current > date ? current : date;
            }
        }

        return new PersonalDataAnonymizationContext(subject, number, BusinessTime.Today(clock), await retention.RetentionAsync(ct), anchors);
    }

    /// <summary>Os outros módulos em ordem alfabética e, por último, o dono do cadastro do titular.</summary>
    private static List<IPersonalDataProvider> Ordered(IEnumerable<IPersonalDataProvider> list, PersonalDataSubject subject)
    {
        var owner = subject.Kind switch
        {
            PersonalDataSubjectKind.Party or PersonalDataSubjectKind.Contact => "parties",
            PersonalDataSubjectKind.Employee => "hr",
            _ => "identity",
        };
        return list.OrderBy(p => p.Module == owner ? 1 : 0).ThenBy(p => p.Module, StringComparer.Ordinal).ToList();
    }

    private List<string> Blockers(IEnumerable<PersonalDataAnonymizationPlan> plans, PersonalDataSubject subject)
    {
        var blockers = plans.Where(p => p.Blocker is not null).Select(p => p.Blocker!).ToList();
        if (subject.Kind == PersonalDataSubjectKind.User && subject.UserId == currentUser.UserId)
        {
            blockers.Insert(0, "Você não pode anonimizar o próprio usuário. Peça a outro membro do Proprietário.");
        }

        return blockers;
    }

    /// <summary>Pedido que pode anonimizar: Anonimização ou Eliminação, aberto, de um cadastro, com a identidade conferida e sem rodar agora.</summary>
    private static Error? Check(PrivacyRequest request)
    {
        if (request.Status != PrivacyRequestStatus.Open)
        {
            return Error.Conflict("privacy_request_closed", "O pedido já foi concluído ou recusado.");
        }

        if (!PrivacyRequestRules.Anonymizes(request.Kind))
        {
            return Error.Conflict("privacy_request_not_anonymization", "Só os pedidos de anonimização e de eliminação anonimizam os dados.");
        }

        if (request.SubjectId is null || request.SubjectType == PersonalDataSubjectKind.Unknown)
        {
            return Error.Conflict("privacy_subject_not_found", "O titular não tem cadastro no sistema: registre a resposta e conclua o pedido.");
        }

        if (!request.IsIdentityVerified)
        {
            return Error.Conflict(PrivacyRequestRules.IdentityNotVerifiedCode, "Confira a identidade do titular antes de anonimizar.");
        }

        return request.AnonymizationStatus switch
        {
            PrivacyAnonymizationStatus.Running => Error.Conflict(RunningCode, "A anonimização está em andamento. Aguarde o aviso no sino."),
            PrivacyAnonymizationStatus.Done => Error.Conflict("privacy_anonymization_done", "Os dados deste titular já foram anonimizados. A anonimização não pode ser desfeita."),
            _ => null,
        };
    }

    /// <summary>Nome do titular nos pedidos dele depois da anonimização: "Maria S." (o pedido é a prova do atendimento); contato e textos livres saem.</summary>
    private void MaskSubject(PrivacyRequest request, string number)
    {
        var entry = db.Entry(request);
        entry.Property(r => r.SubjectName).CurrentValue = request.SubjectName.Contains('@', StringComparison.Ordinal)
            ? PersonalDataMask.Email(request.SubjectName) ?? "—"
            : PrivacyRequestRules.MaskName(request.SubjectName);
        entry.Property(r => r.SubjectContact).CurrentValue = null;
        entry.Property(r => r.Notes).CurrentValue = request.Notes is null ? null : PersonalDataMarker.Text(number);
        request.IdentityNotes = request.IdentityNotes is null ? null : PersonalDataMarker.Text(number);
    }

    private static PrivacyAnonymizationSummary Summary(IEnumerable<PersonalDataAnonymizationPlan> plans) =>
        PrivacyAnonymizationJson.Summarize(plans.Select(p => new PrivacyAnonymizationStep(p.Module, p.Title, PrivacyAnonymizationStepStatus.Pending, null, null, p.Items)));

    /// <summary>Histórico do pedido (CA 11): quem, quando e o resumo (só contagens e a data da retenção), sem dado pessoal.</summary>
    private void RecordAudit(Guid id, string action, PrivacyAnonymizationSummary summary, Guid? userId = null, bool partial = false)
    {
        var changes = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["anonymized"] = new { old = (int?)null, @new = summary.Anonymized },
            ["retained"] = new { old = (int?)null, @new = summary.Retained },
            ["kept"] = new { old = (int?)null, @new = summary.Kept },
            ["partial"] = new { old = (bool?)null, @new = partial },
        });
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = nameof(PrivacyRequest), EntityId = id.ToString(), Action = action, UserId = currentUser.UserId ?? userId,
            At = clock.GetUtcNow(), Ip = currentUser.IpAddress, Origin = origin.Label, Changes = changes,
        });
    }

    private static Error NotFound() => Error.NotFound("privacy_request_not_found", "Pedido do titular não encontrado.");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anonimização do módulo {Module} falhou no pedido {RequestNumber}.")]
    private static partial void LogStepFailed(ILogger logger, Exception exception, string module, string requestNumber);
}
