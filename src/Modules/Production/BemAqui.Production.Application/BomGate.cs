using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Engineering.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Application;

/// <summary>
/// Bloqueio por BOM no início de etapa (PRD-03a CA 9) e na "Concluir etapa" do supervisor (ENG-02 CA 16, D12): a primeira etapa inicia
/// sem BOM; as seguintes exigem a BOM da OS liberada. A implementação real é o <see cref="EngineeringBomGate"/> (ENG-02).
/// </summary>
public interface IBomGate
{
    Task<bool> IsReleasedAsync(Guid productionOrderId, CancellationToken cancellationToken = default);
}

/// <summary>Nunca bloqueia (o Marco 3.1, antes da ENG-02). Fica para os testes que não tratam de BOM.</summary>
public sealed class NoBomGate : IBomGate
{
    public Task<bool> IsReleasedAsync(Guid productionOrderId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

/// <summary>
/// ENG-02 CA 16: pergunta ao <see cref="IBoms"/> se a OS tem BOM liberada. Com o módulo <c>engineering</c> não liberado no tenant,
/// ninguém consegue liberar a BOM, então o bloqueio não vale (a OS não fica presa sem saída).
/// </summary>
public sealed class EngineeringBomGate(IBoms boms, TenantContextHolder tenant) : IBomGate
{
    public async Task<bool> IsReleasedAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
        tenant.Resolved?.IsReleased("engineering") != true || await boms.IsReleasedAsync(productionOrderId, cancellationToken);
}

/// <summary>Regra do bloqueio por BOM (D12, ENG-02 CA 16), sem banco.</summary>
public static class BomGateRules
{
    public const string Code = "bom_not_released";

    public const string Message = "A BOM desta OS ainda não foi liberada. Fale com a Engenharia.";

    /// <summary>
    /// Pede a BOM: etapa depois da primeira (<paramref name="sequenceIndex"/> &gt; 0) que ainda não começou. A etapa já em andamento ou
    /// pausada no deploy da ENG-02 não é interrompida (outro operador entra e o supervisor conclui sem BOM).
    /// </summary>
    public static bool Applies(int sequenceIndex, bool alreadyStarted) => LaborRules.NeedsBom(sequenceIndex) && !alreadyStarted;

    public static Error NotReleased() => Error.Conflict(Code, Message);
}
