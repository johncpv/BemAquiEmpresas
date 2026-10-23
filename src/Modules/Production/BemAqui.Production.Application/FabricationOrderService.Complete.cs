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
    /// "Concluir etapa" do supervisor (PRD-02 CA 8 e 9, D10; PRD-03a CA 20 e 21): só a etapa atual de OS não concluída nem cancelada,
    /// fora a de qualidade (PRD-05), com motivo. Na mesma transação: os apontamentos abertos da etapa fecham com "Encerrado pelo
    /// supervisor" (<see cref="LaborEntryClosed"/> para cada um), a etapa vai a <c>Done</c> com a marca "Concluída pelo supervisor",
    /// a próxima fica <c>Ready</c> e, se a OS estava <c>Open</c>, ela sai de Aberta e publica <see cref="ProductionStepStarted"/>. Quem
    /// apontou na etapa não conclui, salvo sem outro aprovador elegível ("Autoaprovado"). A OS é conferida pela versão (xmin): duas
    /// conclusões juntas, só uma grava. O aviso do quadro sai depois do commit.
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

        var scope = await labor.LoadAsync(stepId, id, cancellationToken);
        var currentIndex = scope?.Steps.FindIndex(s => s.Status != ProductionStepStatus.Done) ?? -1;
        if (scope is null || currentIndex < 0 || scope.Steps[currentIndex].Id != stepId)
        {
            return NotCurrentStep();
        }

        if (scope.Step.IsQualityStep)
        {
            return Error.Conflict("quality_step", "A etapa de qualidade é decidida na inspeção, não no quadro.");
        }

        // ENG-02 CA 16 (D12): concluir pelo supervisor uma etapa depois da primeira que ainda não começou exige a BOM liberada.
        if (BomGateRules.Applies(currentIndex, scope.Step.Status != ProductionStepStatus.Ready) && !await bom.IsReleasedAsync(id, cancellationToken))
        {
            return BomGateRules.NotReleased();
        }

        if (order.Version != input.Version)
        {
            return InstallationOrderService.Stale();
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        var segregation = await labor.SegregationAsync(await labor.InvolvedUsersAsync(stepId, cancellationToken), scope, cancellationToken);
        if (segregation.IsFailure)
        {
            return segregation.Error!;
        }

        var now = clock.GetUtcNow();
        foreach (var open in await db.LaborEntries.Where(e => e.OrderStepId == stepId && e.EndedAt == null).ToListAsync(cancellationToken))
        {
            await labor.CloseAsync(scope, open, now, LaborCloseKind.Supervisor, cancellationToken);
        }

        labor.Complete(scope, now, bySupervisor: true, reason, segregation.Value);

        // A OS muda de versão mesmo quando só a etapa mudou: a outra conclusão com a versão antiga recebe 409.
        await labor.SettleAsync(scope, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }
}