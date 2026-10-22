using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class QualityService
{
    /// <summary>
    /// "Reprovar qualidade" (CA 6 e 7), numa transação: nasce a NC <c>Open</c> com o número <c>NC-AAAA-0001</c> e o responsável (padrão:
    /// quem reprovou, ponto 77), a etapa de qualidade vai a <c>Rejected</c>, a de retorno fica pronta e as seguintes voltam a esperar
    /// (sem apagar apontamentos), as etapas de qualidade da faixa ganham a tentativa nova com o checklist zerado, a OS volta a
    /// <c>InProgress</c>, <see cref="QualityRejected"/> vai para o outbox e o responsável e os supervisores são avisados. Sem segregação.
    /// </summary>
    public async Task<Result<QualityDecisionResult>> RejectAsync(Guid stepId, QualityRejectInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var description = QualityRules.Text(input.Description, "description", QualityRules.MaxDescription, "a descrição da não conformidade", required: true);
        var cause = QualityRules.Text(input.Cause, "cause", QualityRules.MaxCause, "a causa", required: false);
        var action = QualityRules.Text(input.Action, "action", QualityRules.MaxAction, "a ação corretiva", required: false);
        if (new[] { description, cause, action }.FirstOrDefault(t => t.IsFailure) is { } invalid)
        {
            return invalid.Error!;
        }

        var loaded = await LoadAsync(stepId, cancellationToken, input.Version);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var scope = loaded.Value;
        var (order, step) = (scope.Order, scope.Step);
        var states = scope.Steps.Select(s => new QualityStepState(s.Status, s.IsQualityStep)).ToList();
        var returnIndex = input.ReturnToStepId is { } chosen ? scope.Steps.FindIndex(s => s.Id == chosen) : QualityRules.SuggestedReturn(states, scope.Index);
        if (returnIndex < 0 || !QualityRules.ReturnOptions(states, scope.Index).Contains(returnIndex))
        {
            return Error.Validation("return_step_invalid", "Escolha uma etapa anterior desta OS para o retorno.",
                new Dictionary<string, string[]> { ["returnToStepId"] = ["Escolha uma etapa anterior desta OS para o retorno."] });
        }

        var me = currentUser.UserId!.Value;
        var responsible = input.ResponsibleUserId ?? me;
        if (!(await users.UsersWithPermissionAsync("production.view", cancellationToken)).Contains(responsible))
        {
            return LaborRules.Invalid("responsibleUserId", "Escolha um usuário ativo com acesso à Produção.");
        }

        var now = clock.GetUtcNow();
        var plan = QualityRules.PlanRejection(states, scope.Index, returnIndex);
        var returnStep = scope.Steps[returnIndex];
        var nc = new NonConformity
        {
            Number = await numbers.NextAsync("NC", cancellationToken), OrderId = order.Id, OrderStepId = step.Id, Attempt = order.QualityAttempt, Description = description.Value!,
            Cause = cause.Value, Action = action.Value, ResponsibleUserId = responsible, ReturnToStepId = returnStep.Id, OpenedBy = me, OpenedAt = now,
        };
        db.NonConformities.Add(nc);
        db.QualityDecisions.Add(new QualityDecision { OrderId = order.Id, StepId = step.Id, Attempt = order.QualityAttempt, Result = QualityResult.Rejected, DecidedBy = me, DecidedAt = now, NonConformityId = nc.Id });
        await CloseInspectionAsync(scope, now, cancellationToken);

        Reopen(returnStep, ProductionStepStatus.Ready);
        foreach (var i in plan.BackToWaiting)
        {
            Reopen(scope.Steps[i], ProductionStepStatus.Waiting);
        }

        step.Status = ProductionStepStatus.Rejected;
        step.DecidedBy = me;
        step.DecidedAt = now;
        step.SelfInspection = false;
        order.QualityAttempt++;
        order.QualityAttemptStartedAt = now;
        await ResetChecklistsAsync(scope, returnIndex, cancellationToken);
        await UncheckReworkAsync(scope, returnIndex, cancellationToken);
        events.Publish(new QualityRejected(nc.Id, order.Id, order.ProjectId, returnStep.Id));

        var text = $"NC {nc.Number} na {order.Number}: {QualityRules.Excerpt(nc.Description)}. Voltou para {returnStep.Name}.";
        foreach (var user in (await users.UsersWithPermissionAsync("production.approve", cancellationToken)).Append(responsible).Distinct())
        {
            notifications.Notify(user, RejectedType, text, NonConformityLink + nc.Id);
        }

        await SettleAsync(scope, cancellationToken);
        if (!await TrySaveDecisionAsync(cancellationToken))
        {
            return NotPending();
        }

        return new QualityDecisionResult(
            order.Id, order.Number, QualityResult.Rejected, nc.Attempt, false, returnStep.Name, returnStep.Id, false, false, nc.Id, nc.Number, returnStep.Name,
            $"NC {nc.Number} aberta");
    }

    /// <summary>
    /// O checklist das etapas produtivas que serão refeitas volta desmarcado: o operador confere de novo no retrabalho (o histórico da
    /// OS guarda quem tinha marcado e quando).
    /// </summary>
    private async Task UncheckReworkAsync(LaborScope scope, int returnIndex, CancellationToken ct)
    {
        var ids = scope.Steps.Skip(returnIndex).Take(scope.Index - returnIndex).Where(s => !s.IsQualityStep).Select(s => s.Id).ToList();
        foreach (var item in await db.ProductionOrderChecklist.Where(c => c.StepId != null && ids.Contains(c.StepId.Value) && c.Result != null).ToListAsync(ct))
        {
            item.Result = null;
            item.MarkedAt = null;
            item.MarkedBy = null;
        }
    }

    /// <summary>A etapa volta para ser refeita: situação nova e as marcas da conclusão anterior limpas (o histórico guarda os valores).</summary>
    private static void Reopen(ProductionOrderStep step, ProductionStepStatus status)
    {
        step.Status = status;
        step.ActualEnd = null;
        step.CompletedAt = null;
        step.CompletedBy = null;
        step.CompletedBySupervisor = false;
        step.CompletionReason = null;
        step.AwaitingApprovalSince = null;
        step.SelfApproved = false;
    }

    /// <summary>
    /// Checklist zerado nas etapas de qualidade a partir da de retorno (CA 7): com item marcado, os itens da tentativa nova são cópias e
    /// os antigos ficam guardados; sem marca, os mesmos itens passam à tentativa nova. Toda etapa de qualidade decidível fica na
    /// tentativa da OS.
    /// </summary>
    private async Task ResetChecklistsAsync(LaborScope scope, int returnIndex, CancellationToken ct)
    {
        var attempt = scope.Order.QualityAttempt;
        var quality = scope.Steps.Skip(returnIndex).Where(s => s.IsQualityStep).ToList();
        var ids = quality.Select(s => s.Id).ToList();
        var rows = (await db.ProductionOrderChecklist.Where(c => c.StepId != null && ids.Contains(c.StepId.Value)).ToListAsync(ct)).ToLookup(c => c.StepId!.Value);
        foreach (var step in quality)
        {
            var current = rows[step.Id].Where(c => c.Attempt == step.Attempt).OrderBy(c => c.Sequence).ToList();
            if (current.Any(c => c.Result is not null))
            {
                foreach (var item in current)
                {
                    db.ProductionOrderChecklist.Add(new ProductionOrderChecklistItem { OrderId = item.OrderId, StepId = item.StepId, Sequence = item.Sequence, Text = item.Text, Attempt = attempt });
                }
            }
            else
            {
                current.ForEach(c => c.Attempt = attempt);
            }

            step.Attempt = attempt;
        }
    }
}