using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class FabricationOrderService
{
    public const int MaxCompletionReason = 200;

    public static Error NotCurrentStep() => Error.Conflict("step_not_current", "Esta etapa não é a etapa atual da OS.");

    /// <summary>
    /// "Concluir etapa" do supervisor (PRD-02 CA 8 e 9, D10): só a etapa atual de OS aberta ou em andamento, fora a de qualidade
    /// (PRD-05), com motivo. Na mesma transação: a etapa vai a <c>Done</c> com a marca "Concluída pelo supervisor", a próxima fica
    /// <c>Ready</c> e, se a OS estava <c>Open</c>, ela vai a <c>InProgress</c> e publica <see cref="ProductionStepStarted"/>. A OS é
    /// conferida pela versão (xmin): duas conclusões juntas, só uma grava. O aviso do quadro sai depois do commit.
    /// </summary>
    public async Task<Result<FabricationOrderDetail>> CompleteStepAsync(Guid id, Guid stepId, CompleteStepInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = input.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxCompletionReason)
        {
            return InstallationTemplateService.Invalid("reason", $"Informe o motivo, com até {MaxCompletionReason} caracteres.");
        }

        var order = await Fabrication(db.ProductionOrders).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return InstallationOrderService.NotFound();
        }

        if (order.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
        {
            return NotCurrentStep();
        }

        var steps = await db.ProductionOrderSteps.Where(s => s.OrderId == id).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var currentIndex = steps.FindIndex(s => s.Status != ProductionStepStatus.Done);
        if (currentIndex < 0 || steps[currentIndex].Id != stepId)
        {
            return NotCurrentStep();
        }

        var step = steps[currentIndex];
        if (step.IsQualityStep)
        {
            return Error.Conflict("quality_step", "A etapa de qualidade é decidida na inspeção, não no quadro.");
        }

        if (order.Version != input.Version)
        {
            return InstallationOrderService.Stale();
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        var now = clock.GetUtcNow();
        step.ActualStart ??= now;
        step.ActualEnd = now;
        step.Status = ProductionStepStatus.Done;
        step.CompletedAt = now;
        step.CompletedBy = currentUser.UserId;
        step.CompletedBySupervisor = true;
        step.CompletionReason = reason;
        if (currentIndex + 1 < steps.Count && steps[currentIndex + 1].Status == ProductionStepStatus.Waiting)
        {
            steps[currentIndex + 1].Status = ProductionStepStatus.Ready;
        }

        if (order.Status == ProductionOrderStatus.Open)
        {
            order.Status = ProductionOrderStatus.InProgress;
            order.ActualStart ??= now;
            events.Publish(new ProductionStepStarted(order.Id, order.ProjectId, step.Id, now));
        }

        // A OS muda de versão mesmo quando só a etapa mudou: a outra conclusão com a versão antiga recebe 409.
        db.Entry(order).Property(o => o.Priority).IsModified = true;
        await board.ChangedAsync(order.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }
}
